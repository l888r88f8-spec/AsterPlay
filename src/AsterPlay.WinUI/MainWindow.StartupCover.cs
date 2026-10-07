using Microsoft.UI.Xaml;

namespace AsterPlay.WinUI;

public sealed partial class MainWindow
{
    private bool _startupCoverRevealStarted;
    private bool _startupCoverImageReady;
    private readonly TaskCompletionSource<bool> _startupCoverImageCompletion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private void StartupCoverImage_ImageOpened(
        object sender,
        RoutedEventArgs e)
    {
        _startupCoverImageReady = true;
        _startupCoverImageCompletion.TrySetResult(true);
        StartupDiagnostics.Write(
            "In-window startup cover icon decoded");
    }

    private void StartupCoverImage_ImageFailed(
        object sender,
        ExceptionRoutedEventArgs e)
    {
        _startupCoverImageCompletion.TrySetResult(false);
        StartupDiagnostics.Write(
            $"In-window startup cover icon failed: {e.ErrorMessage}");
    }

    internal async Task PrepareStartupCoverForNativeHandoffAsync()
    {
        // Keep an opaque WinUI-owned surface directly below the native splash.
        // It is intentionally the same artwork, so destroying the native HWND
        // is visually lossless and does not expose an unpresented owner window.
        StartupCover.Visibility = Visibility.Visible;
        StartupCover.IsHitTestVisible = true;
        StartupCover.Opacity = 0.999;

        WriteStartupVisualState(
            "PrepareStartupCoverForNativeHandoff.beforeFence");

        var imageTask = _startupCoverImageCompletion.Task;
        var imageWait = await Task.WhenAny(
            imageTask,
            Task.Delay(1000));
        var imageReady =
            ReferenceEquals(imageWait, imageTask) &&
            await imageTask;

        var frames = await WaitForStartupCoverFramesAsync(3, 1000);
        var flush = Task.Run(DwmFlush);
        var completed = await Task.WhenAny(
            flush,
            Task.Delay(1000));
        var dwmFlushed = ReferenceEquals(completed, flush);

        StartupDiagnostics.Write(
            $"In-window cover prepared behind native splash; " +
            $"iconReady={imageReady}, frames={frames}, dwmFlushed={dwmFlushed}");
        WriteStartupVisualState(
            "PrepareStartupCoverForNativeHandoff.afterFence");
    }

    internal async Task ConfirmStartupCoverPresentedAsync()
    {
        // These are the first composition frames after the layered native HWND
        // has gone away. Fence them before starting the visible WinUI fade.
        var frames = await WaitForStartupCoverFramesAsync(2, 1000);
        var flush = Task.Run(DwmFlush);
        var completed = await Task.WhenAny(
            flush,
            Task.Delay(1000));

        StartupDiagnostics.Write(
            $"In-window cover confirmed after native handoff; frames={frames}, " +
            $"dwmFlushed={ReferenceEquals(completed, flush)}");
        WriteStartupVisualState(
            "ConfirmStartupCoverPresented.afterFence");
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

            // This animation now runs entirely inside MainWindow's compositor,
            // so every opacity step blends against the already-presented Home.
            await WaitForStartupCoverFramesAsync(2, 700);

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
