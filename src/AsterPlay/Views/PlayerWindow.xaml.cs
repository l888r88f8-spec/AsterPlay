using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Threading;
using AsterPlay.Models;
using AsterPlay.Services;

namespace AsterPlay.Views;

public partial class PlayerWindow : Window
{
    private readonly EmbyClient _client;
    private readonly PlaybackLaunch _launch;
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _controlsTimer;
    private readonly DispatcherTimer _mousePollTimer;
    private readonly double _timelineOffsetSeconds;

    private PlayerControlsWindow? _controlsWindow;
    private DateTime _lastControlsActivityUtc;
    private int _reportSeconds;
    private bool _fullscreen;
    private bool _startReportSent;
    private bool _stopHandled;
    private long _lastPositionTicks;
    private double _lastAudibleVolume = 100;
    private WindowState _windowedState = WindowState.Normal;
    private WindowStyle _windowedStyle = WindowStyle.SingleBorderWindow;
    private int _lastMouseX = int.MinValue;
    private int _lastMouseY = int.MinValue;
    private bool _leftButtonWasDown;
    private long _lastVideoClickTick;
    private int _lastVideoClickX;
    private int _lastVideoClickY;

    public PlayerWindow(EmbyClient client, PlaybackLaunch launch)
    {
        _client = client;
        _launch = launch;
        _timelineOffsetSeconds = launch.UsesServerStartOffset
            ? Math.Max(0, launch.ResumePositionTicks / 10_000_000d)
            : 0;

        InitializeComponent();

        Title = $"AsterPlay · {launch.Title}";

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += PlayerTimer_Tick;

        _controlsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _controlsTimer.Tick += ControlsTimer_Tick;

        // Poll cursor/button state instead of depending on HwndHost mouse events.
        // mpv/gpu-next may create or consume input in native child windows below
        // the WPF HwndHost, so polling is independent of the HWND hierarchy.
        _mousePollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        _mousePollTimer.Tick += MousePollTimer_Tick;

        Loaded += PlayerWindow_Loaded;
        Closing += PlayerWindow_Closing;
        LocationChanged += (_, _) => SyncControlsWindow();
        SizeChanged += (_, _) => SyncControlsWindow();
        StateChanged += (_, _) => Dispatcher.BeginInvoke(SyncControlsWindow, DispatcherPriority.Loaded);
    }

    private void PlayerWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            PlaybackLog.Write("Player",
                $"Window loaded: title={_launch.Title}, itemId={_launch.ItemId}, mediaSourceId={_launch.MediaSourceId}, " +
                $"resumeTicks={_launch.ResumePositionTicks}, serverOffset={_launch.UsesServerStartOffset}, " +
                $"timelineOffset={_timelineOffsetSeconds:0.###}, log={PlaybackLog.LogPath}");
            PlaybackLog.Write("Player", "mpv loads the server stream from local position 0; no initial mpv seek.");

            PlayerHost.Load(_launch.Url);
            PlayerHost.SetVolume(100);
            _lastAudibleVolume = 100;
            _lastPositionTicks = _launch.ResumePositionTicks;

            EnsureControlsWindow();
            ShowControls();

            _timer.Start();
            _controlsTimer.Start();
            _mousePollTimer.Start();
        }
        catch (DllNotFoundException)
        {
            MessageBox.Show(
                "未找到 libmpv-2.dll。请先准备 mpv 运行库后重新构建。",
                "libmpv missing",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Close();
        }
        catch (Exception ex)
        {
            PlaybackLog.Error("Player", ex);
            MessageBox.Show($"{ex.Message}\n\n日志：{PlaybackLog.LogPath}", "mpv", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
        }
    }

    private void EnsureControlsWindow()
    {
        if (_controlsWindow is not null)
            return;

        var controls = new PlayerControlsWindow(_launch.Title)
        {
            Owner = this
        };

        controls.UserActivity += ShowControls;
        controls.TogglePauseRequested += TogglePauseAndReport;
        controls.BackRequested += () => SeekRelative(-10);
        controls.ForwardRequested += () => SeekRelative(10);
        controls.MuteRequested += ToggleMute;
        controls.AudioRequested += CycleAudio;
        controls.SubtitleRequested += CycleSubtitle;
        controls.FullscreenRequested += ToggleFullscreen;
        controls.SeekRequested += SeekAbsoluteFromTimeline;
        controls.VolumeChanged += SetVolume;
        controls.SpeedChanged += SetSpeed;

        _controlsWindow = controls;
        controls.Show();
        Dispatcher.BeginInvoke(SyncControlsWindow, DispatcherPriority.Loaded);
    }

    private void PlayerTimer_Tick(object? sender, EventArgs e)
    {
        var mpvPosition = Math.Max(0, PlayerHost.PositionSeconds);
        var position = _timelineOffsetSeconds + mpvPosition;

        var duration = _launch.RunTimeTicks is > 0
            ? _launch.RunTimeTicks.Value / 10_000_000d
            : _timelineOffsetSeconds + Math.Max(0, PlayerHost.DurationSeconds);

        _lastPositionTicks = (long)(position * 10_000_000d);

        if (!_startReportSent && mpvPosition > 0.05)
        {
            _startReportSent = true;
            PlaybackLog.Write("Player",
                $"mpv playback started; reporting Playing at absoluteTicks={_lastPositionTicks}");
            _ = SafeReportAsync(() =>
                _client.ReportPlaybackStartAsync(
                    _launch,
                    _lastPositionTicks,
                    PlayerHost.IsPaused,
                    PlayerHost.Volume));
        }

        PlaybackLog.Write("PlayerState",
            $"mpvPos={mpvPosition:0.###}, absolutePos={position:0.###}, offset={_timelineOffsetSeconds:0.###}, " +
            $"duration={duration:0.###}, paused={PlayerHost.IsPaused}, buffering={PlayerHost.IsBuffering}, " +
            $"volume={PlayerHost.Volume:0.##}, speed={PlayerHost.Speed:0.##} | {PlayerHost.DiagnosticState}");

        _controlsWindow?.UpdateState(
            position,
            duration,
            PlayerHost.IsPaused,
            PlayerHost.IsBuffering,
            PlayerHost.Volume,
            PlayerHost.Speed);

        if (_startReportSent)
        {
            _reportSeconds++;
            if (_reportSeconds >= 10)
            {
                _reportSeconds = 0;
                _ = ReportProgressAsync();
            }
        }
    }

    private void ControlsTimer_Tick(object? sender, EventArgs e)
    {
        if (_controlsWindow is null ||
            !_controlsWindow.IsVisible ||
            _controlsWindow.IsMouseOver)
            return;

        if (DateTime.UtcNow - _lastControlsActivityUtc >= TimeSpan.FromSeconds(3))
            HideControls();
    }

    private void MousePollTimer_Tick(object? sender, EventArgs e)
    {
        if (_stopHandled || !IsActive || !GetCursorPos(out var point))
            return;

        var overVideo = IsPointOverVideo(point.X, point.Y);
        var moved = point.X != _lastMouseX || point.Y != _lastMouseY;

        if (moved)
        {
            _lastMouseX = point.X;
            _lastMouseY = point.Y;

            if (overVideo)
                ShowControls();
        }

        var leftDown = (GetAsyncKeyState(0x01) & 0x8000) != 0;
        if (leftDown &&
            !_leftButtonWasDown &&
            overVideo &&
            !IsPointOverControls(point.X, point.Y))
        {
            RegisterVideoClick(point.X, point.Y);
        }

        _leftButtonWasDown = leftDown;
    }

    private void RegisterVideoClick(int x, int y)
    {
        var now = Environment.TickCount64;
        var maxDx = Math.Max(1, GetSystemMetrics(36));
        var maxDy = Math.Max(1, GetSystemMetrics(37));

        if (_lastVideoClickTick > 0 &&
            now - _lastVideoClickTick <= GetDoubleClickTime() &&
            Math.Abs(x - _lastVideoClickX) <= maxDx &&
            Math.Abs(y - _lastVideoClickY) <= maxDy)
        {
            _lastVideoClickTick = 0;
            PlaybackLog.Write("PlayerInput", "Polled video double-click -> toggle fullscreen");
            ToggleFullscreen();
            return;
        }

        _lastVideoClickTick = now;
        _lastVideoClickX = x;
        _lastVideoClickY = y;
    }

    private bool IsPointOverVideo(int screenX, int screenY)
    {
        try
        {
            var topLeft = PlayerHost.PointToScreen(new Point(0, 0));
            var bottomRight = PlayerHost.PointToScreen(
                new Point(PlayerHost.ActualWidth, PlayerHost.ActualHeight));

            return screenX >= topLeft.X &&
                   screenX < bottomRight.X &&
                   screenY >= topLeft.Y &&
                   screenY < bottomRight.Y;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private bool IsPointOverControls(int screenX, int screenY)
    {
        if (_controlsWindow is null || !_controlsWindow.IsVisible)
            return false;

        var point = new Point(screenX, screenY);
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is not null)
            point = source.CompositionTarget.TransformFromDevice.Transform(point);

        return point.X >= _controlsWindow.Left &&
               point.X < _controlsWindow.Left + _controlsWindow.ActualWidth &&
               point.Y >= _controlsWindow.Top &&
               point.Y < _controlsWindow.Top + _controlsWindow.ActualHeight;
    }

    private void ShowControls()
    {
        if (_stopHandled)
            return;

        _lastControlsActivityUtc = DateTime.UtcNow;
        PlayerHost.SetCursorHidden(false);

        if (_controlsWindow is null)
        {
            EnsureControlsWindow();
            return;
        }

        if (!_controlsWindow.IsVisible)
            _controlsWindow.Show();

        SyncControlsWindow();
    }

    private void HideControls()
    {
        if (_controlsWindow is null || _controlsWindow.IsMouseOver)
            return;

        _controlsWindow.Hide();
        PlayerHost.SetCursorHidden(true);
    }

    private void SyncControlsWindow()
    {
        if (_controlsWindow is null ||
            !IsLoaded ||
            PlayerHost.ActualWidth <= 1 ||
            PlayerHost.ActualHeight <= 1)
            return;

        try
        {
            var topLeftPx = PlayerHost.PointToScreen(new Point(0, 0));
            var bottomRightPx = PlayerHost.PointToScreen(
                new Point(PlayerHost.ActualWidth, PlayerHost.ActualHeight));

            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget is not null)
            {
                topLeftPx = source.CompositionTarget.TransformFromDevice.Transform(topLeftPx);
                bottomRightPx = source.CompositionTarget.TransformFromDevice.Transform(bottomRightPx);
            }

            var width = Math.Max(1, bottomRightPx.X - topLeftPx.X);
            var height = Math.Min(150, Math.Max(100, bottomRightPx.Y - topLeftPx.Y));

            _controlsWindow.Width = width;
            _controlsWindow.Height = height;
            _controlsWindow.Left = topLeftPx.X;
            _controlsWindow.Top = bottomRightPx.Y - height;
        }
        catch (InvalidOperationException)
        {
            // Window is changing state; StateChanged/SizeChanged will retry.
        }
    }

    private async Task ReportProgressAsync(string eventName = "TimeUpdate")
    {
        await SafeReportAsync(() =>
            _client.ReportPlaybackProgressAsync(
                _launch,
                _lastPositionTicks,
                PlayerHost.IsPaused,
                PlayerHost.Volume,
                eventName));
    }

    private async Task SafeReportAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            PlaybackLog.Error("PlaybackReport", ex);
        }
    }

    private void TogglePauseAndReport()
    {
        PlaybackLog.Write("Player", "Pause toggled");
        var wasPaused = PlayerHost.IsPaused;
        PlayerHost.TogglePause();
        _ = ReportProgressAsync(wasPaused ? "Unpause" : "Pause");
        ShowControls();
    }

    private void SeekRelative(double seconds)
    {
        PlaybackLog.Write("Player", $"Seek {(seconds >= 0 ? "+" : "")}{seconds:0.###}");
        PlayerHost.Seek(seconds);
        ShowControls();
    }

    private void SeekAbsoluteFromTimeline(double requestedAbsolute)
    {
        var localTarget = Math.Max(0, requestedAbsolute - _timelineOffsetSeconds);
        PlaybackLog.Write("Player",
            $"Timeline seek: absolute={requestedAbsolute:0.###}, local={localTarget:0.###}, offset={_timelineOffsetSeconds:0.###}");
        PlayerHost.SeekAbsolute(localTarget);
        ShowControls();
    }

    private void SetVolume(double volume)
    {
        if (volume > 0.01)
            _lastAudibleVolume = volume;

        PlayerHost.SetVolume(volume);
        ShowControls();
    }

    private void ToggleMute()
    {
        var volume = PlayerHost.Volume;
        if (volume > 0.01)
        {
            _lastAudibleVolume = volume;
            PlayerHost.SetVolume(0);
        }
        else
        {
            PlayerHost.SetVolume(_lastAudibleVolume > 0.01 ? _lastAudibleVolume : 100);
        }

        ShowControls();
    }

    private void SetSpeed(double speed)
    {
        PlaybackLog.Write("Player", $"Playback speed={speed:0.##}");
        PlayerHost.SetSpeed(speed);
        ShowControls();
    }

    private void CycleAudio()
    {
        PlayerHost.CycleAudio();
        _controlsWindow?.SetStatus("已切换音轨");
        ShowControls();
    }

    private void CycleSubtitle()
    {
        PlayerHost.CycleSubtitle();
        _controlsWindow?.SetStatus("已切换字幕");
        ShowControls();
    }

    private void ToggleFullscreen()
    {
        if (_stopHandled)
            return;

        if (!_fullscreen)
        {
            _windowedState = WindowState;
            _windowedStyle = WindowStyle;
            _fullscreen = true;

            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Maximized;
        }
        else
        {
            _fullscreen = false;
            WindowState = WindowState.Normal;
            WindowStyle = _windowedStyle;
            WindowState = _windowedState;
        }

        ShowControls();
        Dispatcher.BeginInvoke(SyncControlsWindow, DispatcherPriority.Loaded);
    }

    private void PlayerWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        ShowControls();

        switch (e.Key)
        {
            case Key.Space:
                TogglePauseAndReport();
                e.Handled = true;
                break;

            case Key.Left:
                SeekRelative(-10);
                e.Handled = true;
                break;

            case Key.Right:
                SeekRelative(10);
                e.Handled = true;
                break;

            case Key.Up:
                SetVolume(Math.Min(100, PlayerHost.Volume + 5));
                e.Handled = true;
                break;

            case Key.Down:
                SetVolume(Math.Max(0, PlayerHost.Volume - 5));
                e.Handled = true;
                break;

            case Key.M:
                ToggleMute();
                e.Handled = true;
                break;

            case Key.F11:
                ToggleFullscreen();
                e.Handled = true;
                break;

            case Key.Escape when _fullscreen:
                ToggleFullscreen();
                e.Handled = true;
                break;
        }
    }

    private void PlayerWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_stopHandled)
            return;

        _stopHandled = true;
        _timer.Stop();
        _controlsTimer.Stop();
        _mousePollTimer.Stop();

        var finalTicks = _lastPositionTicks;
        var finalVolume = PlayerHost.Volume;
        PlaybackLog.Write("Player", $"Closing playback window: finalTicks={finalTicks}");

        _ = FinalizePlaybackAsync(finalTicks, finalVolume);

        PlayerHost.SetCursorHidden(false);

        if (_controlsWindow is not null)
        {
            try
            {
                _controlsWindow.Close();
            }
            catch (InvalidOperationException)
            {
                // Owned window may already be closing with its owner.
            }

            _controlsWindow = null;
        }

        PlayerHost.ShutdownPlayback();
        PlaybackLog.Write("Player", "mpv stopped and disposed on window close");
    }

    private async Task FinalizePlaybackAsync(long finalTicks, double finalVolume)
    {
        await SafeReportAsync(() =>
            _client.ReportPlaybackProgressAsync(
                _launch,
                finalTicks,
                true,
                finalVolume,
                "Pause"));

        await SafeReportAsync(() =>
            _client.ReportPlaybackStoppedAsync(
                _launch,
                finalTicks,
                true,
                finalVolume));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}
