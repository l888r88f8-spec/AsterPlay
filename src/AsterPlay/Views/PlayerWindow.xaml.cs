using System.ComponentModel;
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
    private int _reportSeconds;
    private bool _updatingUi;
    private bool _fullscreen;
    private long _lastPositionTicks;
    private readonly double _timelineOffsetSeconds;
    private bool _stopHandled;

    public PlayerWindow(EmbyClient client, PlaybackLaunch launch)
    {
        _client = client;
        _launch = launch;
        _timelineOffsetSeconds = launch.UsesServerStartOffset
            ? Math.Max(0, launch.ResumePositionTicks / 10_000_000d)
            : 0;

        InitializeComponent();

        Title = $"AsterPlay · {launch.Title}";
        TitleBlock.Text = launch.Title;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += PlayerTimer_Tick;

        Loaded += PlayerWindow_Loaded;
        Closing += PlayerWindow_Closing;
    }

    private async void PlayerWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            PlaybackLog.Write("Player",
                $"Window loaded: title={_launch.Title}, itemId={_launch.ItemId}, mediaSourceId={_launch.MediaSourceId}, " +
                $"resumeTicks={_launch.ResumePositionTicks}, serverOffset={_launch.UsesServerStartOffset}, " +
                $"timelineOffset={_timelineOffsetSeconds:0.###}, log={PlaybackLog.LogPath}");
            PlaybackLog.Write("Player", "mpv loads the server stream from local position 0; no initial mpv seek.");
            PlayerHost.Load(_launch.Url);
            VolumeSlider.Value = 100;
            _lastPositionTicks = _launch.ResumePositionTicks;
            _timer.Start();

            await SafeReportAsync(() =>
                _client.ReportPlaybackStartAsync(
                    _launch,
                    _launch.ResumePositionTicks,
                    false,
                    VolumeSlider.Value));
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

    private void PlayerTimer_Tick(object? sender, EventArgs e)
    {
        var mpvPosition = Math.Max(0, PlayerHost.PositionSeconds);
        var position = _timelineOffsetSeconds + mpvPosition;

        var duration = _launch.RunTimeTicks is > 0
            ? _launch.RunTimeTicks.Value / 10_000_000d
            : _timelineOffsetSeconds + Math.Max(0, PlayerHost.DurationSeconds);

        _lastPositionTicks = (long)(position * 10_000_000d);

        PlaybackLog.Write("PlayerState",
            $"mpvPos={mpvPosition:0.###}, absolutePos={position:0.###}, offset={_timelineOffsetSeconds:0.###}, " +
            $"duration={duration:0.###}, paused={PlayerHost.IsPaused}, buffering={PlayerHost.IsBuffering}, " +
            $"volume={PlayerHost.Volume:0.##} | {PlayerHost.DiagnosticState}");

        _updatingUi = true;
        try
        {
            if (duration > 0)
            {
                PositionSlider.Maximum = duration;
                PositionSlider.Value = Math.Clamp(position, 0, duration);
            }

            CurrentTimeBlock.Text = FormatTime(position);
            DurationBlock.Text = duration > 0 ? FormatTime(duration) : "--:--";

            StatusBlock.Text = PlayerHost.IsBuffering
                ? "缓冲中…"
                : PlayerHost.IsPaused ? "已暂停" : "";
        }
        finally
        {
            _updatingUi = false;
        }

        _reportSeconds++;
        if (_reportSeconds >= 5)
        {
            _reportSeconds = 0;
            _ = ReportProgressAsync();
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
            // Playback must continue even if the server temporarily rejects a progress update.
        }
    }

    private void PlayerWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_stopHandled)
            return;

        _stopHandled = true;
        _timer.Stop();

        // Snapshot state BEFORE stopping mpv. qEmby follows the same order:
        // final progress -> stopped report, while the player itself is stopped immediately.
        var finalTicks = _lastPositionTicks;
        var finalVolume = PlayerHost.Volume;
        PlaybackLog.Write("Player", $"Closing playback window: finalTicks={finalTicks}");

        _ = FinalizePlaybackAsync(finalTicks, finalVolume);

        // Do not wait for the network before silencing playback.
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

    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        PlaybackLog.Write("Player", "Pause button clicked");
        var wasPaused = PlayerHost.IsPaused;
        PlayerHost.TogglePause();
        _ = ReportProgressAsync(wasPaused ? "Unpause" : "Pause");
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        PlaybackLog.Write("Player", "Seek -10");
        PlayerHost.Seek(-10);
    }

    private void Forward_Click(object sender, RoutedEventArgs e)
    {
        PlaybackLog.Write("Player", "Seek +10");
        PlayerHost.Seek(10);
    }

    private void PositionSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_updatingUi)
        {
            var requestedAbsolute = PositionSlider.Value;
            var localTarget = Math.Max(0, requestedAbsolute - _timelineOffsetSeconds);
            PlaybackLog.Write("Player",
                $"Timeline seek: absolute={requestedAbsolute:0.###}, local={localTarget:0.###}, offset={_timelineOffsetSeconds:0.###}");
            PlayerHost.SeekAbsolute(localTarget);
        }
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_updatingUi)
            PlayerHost.SetVolume(e.NewValue);
    }

    private void Audio_Click(object sender, RoutedEventArgs e)
    {
        PlayerHost.CycleAudio();
        StatusBlock.Text = "已切换音轨";
    }

    private void Subtitle_Click(object sender, RoutedEventArgs e)
    {
        PlayerHost.CycleSubtitle();
        StatusBlock.Text = "已切换字幕";
    }

    private void Fullscreen_Click(object sender, RoutedEventArgs e) => ToggleFullscreen();

    private void ToggleFullscreen()
    {
        if (!_fullscreen)
        {
            _fullscreen = true;
            ControlsPanel.Visibility = Visibility.Collapsed;
            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Maximized;
        }
        else
        {
            _fullscreen = false;
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.SingleBorderWindow;
            ControlsPanel.Visibility = Visibility.Visible;
        }
    }

    private void PlayerWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Space:
                PlayerHost.TogglePause();
                e.Handled = true;
                break;
            case Key.Left:
                PlayerHost.Seek(-10);
                e.Handled = true;
                break;
            case Key.Right:
                PlayerHost.Seek(10);
                e.Handled = true;
                break;
            case Key.Up:
                VolumeSlider.Value = Math.Min(100, VolumeSlider.Value + 5);
                e.Handled = true;
                break;
            case Key.Down:
                VolumeSlider.Value = Math.Max(0, VolumeSlider.Value - 5);
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

    private static string FormatTime(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
            seconds = 0;

        var time = TimeSpan.FromSeconds(seconds);
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{time.Minutes:00}:{time.Seconds:00}";
    }
}
