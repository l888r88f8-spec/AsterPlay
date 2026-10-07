using Microsoft.UI.Xaml;

namespace AsterPlay.WinUI;

public sealed partial class MainWindow
{
    private bool _startupCoverPresentedRaised;
    private bool _startupCoverRevealStarted;

    internal event EventHandler? StartupCoverPresented;

    private async void StartupCover_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        if (_startupCoverPresentedRaised)
            return;

        try
        {
            // The native splash only needs to survive until this lightweight
            // in-window cover has entered MainWindow's own composition tree.
            // Unlike a second top-level HWND, this cover does not occlude the
            // owner window from DWM, so Home/LiquidGlass can genuinely render
            // underneath it during the rest of startup.
            var frames = await WaitForStartupCoverFramesAsync(2, 500);

            var flush = Task.Run(DwmFlush);
            await Task.WhenAny(
                flush,
                Task.Delay(500));

            if (_startupCoverPresentedRaised ||
                StartupCover.Visibility != Visibility.Visible)
            {
                return;
            }

            _startupCoverPresentedRaised = true;
            StartupDiagnostics.Write(
                $"In-window startup cover presented; frames={frames}, " +
                $"size={StartupCover.ActualWidth:0}x{StartupCover.ActualHeight:0}");
            StartupCoverPresented?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            StartupDiagnostics.WriteException(
                "StartupCover_Loaded",
                ex);

            // Do not strand the native splash if the diagnostic barrier itself
            // fails. The in-window cover is already part of the XAML tree.
            if (!_startupCoverPresentedRaised)
            {
                _startupCoverPresentedRaised = true;
                StartupCoverPresented?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    internal async Task RevealStartupCoverAsync()
    {
        if (_startupCoverRevealStarted ||
            StartupCover.Visibility != Visibility.Visible)
        {
            return;
        }

        _startupCoverRevealStarted = true;

        try
        {
            StartupDiagnostics.Write(
                "Home visual ready; fading in-window startup cover");

            // Now that the top-level native splash is gone, these frames are
            // real visible MainWindow composition frames. Give LiquidGlass and
            // decoded Home textures a final chance to settle before revealing.
            await WaitForStartupCoverFramesAsync(3, 700);

            var flush = Task.Run(DwmFlush);
            await Task.WhenAny(
                flush,
                Task.Delay(700));

            const int durationMilliseconds = 420;
            var start = DateTime.UtcNow;

            while (true)
            {
                var progress = Math.Clamp(
                    (DateTime.UtcNow - start).TotalMilliseconds /
                    durationMilliseconds,
                    0.0,
                    1.0);

                var eased = progress * progress * (3.0 - (2.0 * progress));
                StartupCover.Opacity = 0.999 * (1.0 - eased);

                if (progress >= 1.0)
                    break;

                await Task.Delay(16);
            }

            StartupCover.Opacity = 0;
            StartupCover.IsHitTestVisible = false;
            StartupCover.Visibility = Visibility.Collapsed;

            StartupDiagnostics.Write(
                "In-window startup cover removed; Home is now directly visible");
        }
        finally
        {
            _startupCoverRevealStarted = false;
        }
    }

    private static async Task<int> WaitForStartupCoverFramesAsync(
        int targetFrames,
        int timeoutMilliseconds)
    {
        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var frames = 0;

        EventHandler<object>? handler = null;
        handler = (_, _) =>
        {
            frames++;
            if (frames >= targetFrames)
                completion.TrySetResult(true);
        };

        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += handler;
        try
        {
            await Task.WhenAny(
                completion.Task,
                Task.Delay(timeoutMilliseconds));
            return frames;
        }
        finally
        {
            Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= handler;
        }
    }
}
