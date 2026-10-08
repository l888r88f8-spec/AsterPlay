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

    // The native splash stays on screen while the REAL startup page is
    // composed. This applies to both the no-server Home and authenticated
    // Home, avoiding an intermediate cover-to-page transition.
    internal async Task<bool> PrepareHomeBehindNativeSplashAsync()
    {
        WriteStartupVisualState("PrepareHomeBehindNativeSplash.beforeCollapse");

        // The native splash still covers the real window. Submit the final
        // content without the in-window cover, and wait for an actual DWM S_OK
        // before fading the native HWND into that content.
        StartupCover.IsHitTestVisible = false;
        StartupCover.Opacity = 0;
        StartupCover.Visibility = Visibility.Collapsed;

        var ready = await WaitForVerifiedStartupPresentationAsync(
            "content after XAML cover removal", 3);
        if (ready)
            WriteStartupVisualState("PrepareHomeBehindNativeSplash.verified");

        return ready;
    }

    internal async Task<bool> RevealStartupCoverAsync()
    {
        if (_startupCoverRevealStarted)
            return false;

        if (StartupCover.Visibility != Visibility.Visible)
        {
            StartupDiagnostics.Write(
                "In-window startup cover is already collapsed; no fade needed");
            return true;
        }

        _startupCoverRevealStarted = true;
        try
        {
            WriteStartupVisualState("RevealStartupCover.beforeFence");

            // Do not equate a delayed or timed-out fence with success.
            // This check runs AFTER the native splash is removed, with the
            // WinUI HWND actually unoccluded and presenting its own frames.
            if (!await WaitForVerifiedStartupPresentationAsync(
                    "visible WinUI window before XAML cover fade", 3))
            {
                return false;
            }

            StartupDiagnostics.Write(
                $"Visible WinUI frames confirmed; fading XAML cover; iconReady={_startupCoverImageReady}");

            const int durationMilliseconds = 560;
            var start = DateTime.UtcNow;

            while (!_startupWindowClosed)
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

            if (_startupWindowClosed)
                return false;

            StartupCover.Opacity = 0;
            StartupCover.IsHitTestVisible = false;
            StartupCover.Visibility = Visibility.Collapsed;

            StartupDiagnostics.Write(
                "In-window startup cover removed after verified presentation");
            WriteStartupVisualState("RevealStartupCover.afterCollapse");
            return true;
        }
        finally
        {
            _startupCoverRevealStarted = false;
        }
    }

    // DwmFlush is a rendering fence, not a proof that XAML text/images were
    // loaded. The page-geometry and InitialVisualReady guards are separately
    // checked before and after each fence.
    private Task<int>? _pendingStartupDwmFlush;

    private async Task<bool> WaitForStartupDwmFenceAsync(
        string phase, int timeoutMilliseconds)
    {
        if (_startupWindowClosed)
            return false;

        // Do not spawn overlapping blocked DwmFlush threads on remote or
        // headless desktops. Retry the outstanding fence instead.
        if (_pendingStartupDwmFlush is null ||
            _pendingStartupDwmFlush.IsCompleted)
        {
            _pendingStartupDwmFlush = Task.Run(DwmFlush);
        }

        var pending = _pendingStartupDwmFlush!;
        var completed = await Task.WhenAny(
            pending, Task.Delay(timeoutMilliseconds));

        if (!ReferenceEquals(completed, pending))
        {
            StartupDiagnostics.Write(
                $"Startup DWM fence pending; phase={phase}; timeout={timeoutMilliseconds}ms");
            return false;
        }

        try
        {
            var hr = await pending;
            if (hr == 0)
            {
                StartupDiagnostics.Write(
                    $"Startup DWM fence confirmed; phase={phase}; hr=S_OK");
                return true;
            }

            StartupDiagnostics.Write(
                $"Startup DWM fence rejected; phase={phase}; hr=0x{(uint)hr:X8}");
        }
        catch (Exception ex)
        {
            StartupDiagnostics.WriteException(
                $"Startup DWM fence failure; phase={phase}", ex);
        }

        return false;
    }

    private async Task<bool> WaitForVerifiedStartupPresentationAsync(
        string phase, int targetFrames)
    {
        var targetPage = PageHost.Content;
        var attempt = 0;

        while (!_startupWindowClosed &&
               ReferenceEquals(PageHost.Content, targetPage))
        {
            attempt++;

            if (!IsStartupPageReady(targetPage))
            {
                StartupDiagnostics.Write(
                    $"Startup presentation waiting for page; phase={phase}; attempt={attempt}");
            }
            else
            {
                var frames = await WaitForStartupCoverFramesAsync(
                    targetFrames, 1200);

                if (frames >= targetFrames &&
                    IsStartupPageReady(targetPage) &&
                    await WaitForStartupDwmFenceAsync(phase, 1800) &&
                    IsStartupPageReady(targetPage))
                {
                    StartupDiagnostics.Write(
                        $"Startup presentation confirmed; phase={phase}; " +
                        $"attempt={attempt}; frames={frames}/{targetFrames}");
                    return true;
                }

                StartupDiagnostics.Write(
                    $"Startup presentation not confirmed; phase={phase}; " +
                    $"attempt={attempt}; frames={frames}/{targetFrames}");
            }

            // This is a retry backoff, not a deadline that forces dismissal.
            // No deadline may silently turn failed presentation into ready.
            await Task.Delay(200);
        }

        StartupDiagnostics.Write(
            $"Startup presentation abandoned; phase={phase}; " +
            $"windowClosed={_startupWindowClosed}; pageChanged=" +
            $"{!ReferenceEquals(PageHost.Content, targetPage)}");
        return false;
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
