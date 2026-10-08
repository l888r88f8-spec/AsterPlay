using Microsoft.UI.Xaml;

namespace AsterPlay.WinUI;

public sealed partial class MainWindow
{
    private bool _startupCoverRevealStarted;
    private bool _startupCoverImageReady;

    private void StartupCoverImage_ImageOpened(
        object sender,
        RoutedEventArgs e)
    {
        _startupCoverImageReady = true;
        StartupDiagnostics.Write(
            "In-window startup cover icon decoded");
    }

    private void StartupCoverImage_ImageFailed(
        object sender,
        ExceptionRoutedEventArgs e)
    {
        StartupDiagnostics.Write(
            $"In-window startup cover icon failed: {e.ErrorMessage}");
    }

    // Keep the in-window cover visible during the native -> WinUI handoff for
    // first-run/empty Home. It is painted by WinUI and shields any delayed
    // first visible compositor frame after the native HWND is withdrawn.
    internal async Task PrepareEmptyHomeCoverHandoffAsync()
    {
        WriteStartupVisualState("PrepareEmptyHomeCoverHandoff.before");

        StartupCover.Visibility = Visibility.Visible;
        StartupCover.IsHitTestVisible = true;
        StartupCover.Opacity = 0.999;
        StartupCover.UpdateLayout();

        var frames = await WaitForStartupCoverFramesAsync(3, 1000);
        var flush = Task.Run(DwmFlush);
        var completed = await Task.WhenAny(flush, Task.Delay(1000));

        StartupDiagnostics.Write(
            $"Empty-home native-to-XAML cover handoff ready; " +
            $"coverImageReady={_startupCoverImageReady}; frames={frames}; " +
            $"dwmFlushed={ReferenceEquals(completed, flush)}");
        WriteStartupVisualState("PrepareEmptyHomeCoverHandoff.after");
    }

    internal async Task PrepareHomeBehindNativeSplashAsync()
    {
        WriteStartupVisualState(
            "PrepareHomeBehindNativeSplash.beforeCollapse");
        // The native splash remains the only visible startup surface. Remove
        // the XAML cover behind it, then require fresh composition frames and a
        // DWM fence before the native surface begins fading.
        StartupCover.IsHitTestVisible = false;
        StartupCover.Opacity = 0;
        StartupCover.Visibility = Visibility.Collapsed;

        var frames = await WaitForStartupCoverFramesAsync(3, 1000);
        var flush = Task.Run(DwmFlush);
        var completed = await Task.WhenAny(
            flush,
            Task.Delay(1000));

        StartupDiagnostics.Write(
            $"Home prepared behind native splash; frames={frames}, " +
            $"dwmFlushed={ReferenceEquals(completed, flush)}");
        WriteStartupVisualState(
            "PrepareHomeBehindNativeSplash.afterFence");
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
            WriteStartupVisualState(
                "RevealStartupCover.beforeFade");
            StartupDiagnostics.Write(
                $"Home visual ready; fading in-window startup cover; iconReady={_startupCoverImageReady}");

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
            WriteStartupVisualState(
                "RevealStartupCover.afterCollapse");
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
