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
            "Single-window startup cover icon decoded");
    }

    private void StartupCoverImage_ImageFailed(
        object sender,
        ExceptionRoutedEventArgs e)
    {
        StartupDiagnostics.Write(
            $"Single-window startup cover icon failed: {e.ErrorMessage}");
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
                $"Home visual ready; fading single-window startup cover; iconReady={_startupCoverImageReady}");

            // Home and the cover share one compositor tree. Wait for two fresh
            // frames after the ready signal, then fade the cover without any
            // top-level window activation or destruction.
            var frames = await WaitForStartupCoverFramesAsync(2, 700);
            StartupDiagnostics.Write(
                $"Single-window cover fade fence completed; frames={frames}");

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
                "Single-window startup cover removed; Home is directly visible");
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
