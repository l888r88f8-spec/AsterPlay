using Microsoft.UI.Xaml;

namespace AsterPlay.WinUI;

public sealed partial class MainWindow
{
    private bool _startupCoverPresentedRaised;
    private bool _startupCoverRevealStarted;
    private bool _startupCoverLoaded;
    private bool _startupCoverImageReady;
    private bool _startupCoverPresentationStarted;

    internal event EventHandler? StartupCoverPresented;

    private void StartupCover_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        _startupCoverLoaded = true;
        TryPresentStartupCover();
    }

    private void StartupCoverImage_ImageOpened(
        object sender,
        RoutedEventArgs e)
    {
        _startupCoverImageReady = true;
        StartupDiagnostics.Write(
            "In-window startup cover icon decoded");
        TryPresentStartupCover();
    }

    private void StartupCoverImage_ImageFailed(
        object sender,
        ExceptionRoutedEventArgs e)
    {
        StartupDiagnostics.Write(
            $"In-window startup cover icon failed: {e.ErrorMessage}");
        // Keep the native splash visible. The final startup-ready fallback will
        // fade it directly into Home instead of exposing an iconless cover.
    }

    private void TryPresentStartupCover()
    {
        if (!_startupCoverLoaded ||
            !_startupCoverImageReady ||
            _startupCoverPresentationStarted ||
            _startupCoverPresentedRaised)
        {
            return;
        }

        _startupCoverPresentationStarted = true;
        _ = PresentStartupCoverAsync();
    }

    private async Task PresentStartupCoverAsync()
    {
        try
        {
            // ImageOpened guarantees the large source PNG has decoded. Wait for
            // subsequent composition frames as well, so the native splash is
            // never removed over a background-only in-window cover.
            var frames = await WaitForStartupCoverFramesAsync(2, 700);

            var flush = Task.Run(DwmFlush);
            await Task.WhenAny(
                flush,
                Task.Delay(700));

            if (_startupCoverPresentedRaised ||
                StartupCover.Visibility != Visibility.Visible)
            {
                return;
            }

            _startupCoverPresentedRaised = true;
            StartupDiagnostics.Write(
                $"In-window startup cover presented with decoded icon; frames={frames}, " +
                $"size={StartupCover.ActualWidth:0}x{StartupCover.ActualHeight:0}");
            StartupCoverPresented?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            StartupDiagnostics.WriteException(
                "PresentStartupCoverAsync",
                ex);
            // Leave the native splash in place. MainWindow_StartupVisualReady
            // owns the safe direct-to-Home fallback.
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

            const int durationMilliseconds = 560;
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
