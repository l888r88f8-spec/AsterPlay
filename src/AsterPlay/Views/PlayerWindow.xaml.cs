using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using AsterPlay.Models;
using AsterPlay.Services;
using AsterPlay.Services.Mpv;
using OpenTK.Wpf;

namespace AsterPlay.Views;

public partial class PlayerWindow : Window
{
    private static readonly double[] Speeds = [0.5, 0.75, 1.0, 1.25, 1.5, 2.0];

    private readonly EmbyClient _client;
    private readonly PlaybackLaunch _launch;
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _controlsTimer;
    private readonly double _timelineOffsetSeconds;

    private MpvClient? _mpv;
    private MpvRenderContext? _renderContext;
    private DateTime _lastControlsActivityUtc;
    private int _renderInvalidationQueued;
    private int _reportSeconds;
    private bool _videoSurfaceStarted;
    private bool _playbackLoaded;
    private bool _updatingUi;
    private bool _fullscreen;
    private bool _startReportSent;
    private bool _stopHandled;
    private bool _renderFailureShown;
    private bool _trackMenuOpen;
    private ContextMenu? _activeTrackMenu;
    private long _lastPositionTicks;
    private double _lastAudibleVolume = 100;
    private WindowState _windowedState = WindowState.Normal;
    private WindowStyle _windowedStyle = WindowStyle.SingleBorderWindow;

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

        SpeedComboBox.ItemsSource = Speeds.Select(x => $"{x:0.##}×").ToArray();
        SpeedComboBox.SelectedIndex = 2;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += PlayerTimer_Tick;

        _controlsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _controlsTimer.Tick += ControlsTimer_Tick;

        Loaded += PlayerWindow_Loaded;
        Closing += PlayerWindow_Closing;
    }

    private void PlayerWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            PlaybackLog.Write("Player",
                $"Window loaded: title={_launch.Title}, itemId={_launch.ItemId}, mediaSourceId={_launch.MediaSourceId}, " +
                $"resumeTicks={_launch.ResumePositionTicks}, serverOffset={_launch.UsesServerStartOffset}, " +
                $"timelineOffset={_timelineOffsetSeconds:0.###}, log={PlaybackLog.LogPath}");
            PlaybackLog.Write("Player",
                "Render API mode: mpv renders into the WPF OpenGL framebuffer; no wid/HwndHost is used.");

            _mpv = new MpvClient();
            _lastAudibleVolume = 100;
            _lastPositionTicks = _launch.ResumePositionTicks;

            var settings = new GLWpfControlSettings
            {
                MajorVersion = 3,
                MinorVersion = 3,
                // Let GLWpfControl render on WPF's normal composition cadence.
                // This avoids flooding DispatcherPriority.Render and starving mouse input.
                RenderContinuously = true,
                UseDeviceDpi = true,
                Samples = 0
            };

            VideoSurface.Start(settings);
            _videoSurfaceStarted = true;
            VideoSurface.InvalidateVisual();

            ShowControls();
            _timer.Start();
            _controlsTimer.Start();
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
            MessageBox.Show(
                $"{ex.Message}\n\n日志：{PlaybackLog.LogPath}",
                "player",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Close();
        }
    }

    private void VideoSurface_OnRender(TimeSpan delta)
    {
        Interlocked.Exchange(ref _renderInvalidationQueued, 0);

        if (_stopHandled || _mpv is null)
            return;

        try
        {
            if (_renderContext is null)
            {
                _renderContext = new MpvRenderContext(_mpv, RequestVideoRender);
                _renderContext.Initialize();

                PlaybackLog.Write(
                    "mpv-render",
                    $"WPF framebuffer ready: fbo={VideoSurface.Framebuffer}, " +
                    $"size={VideoSurface.FrameBufferWidth}x{VideoSurface.FrameBufferHeight}");

                _mpv.SetVolume(100);
                _mpv.Load(_launch.Url);
                _playbackLoaded = true;
            }

            _renderContext.Render(
                VideoSurface.Framebuffer,
                VideoSurface.FrameBufferWidth,
                VideoSurface.FrameBufferHeight);
        }
        catch (Exception ex)
        {
            PlaybackLog.Error("mpv-render", ex);

            if (_renderFailureShown)
                return;

            _renderFailureShown = true;
            Dispatcher.BeginInvoke(
                DispatcherPriority.Normal,
                new Action(() =>
                {
                    if (_stopHandled)
                        return;

                    MessageBox.Show(
                        $"视频渲染初始化失败：{ex.Message}\n\n日志：{PlaybackLog.LogPath}",
                        "libmpv Render API",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    Close();
                }));
        }
    }

    private void RequestVideoRender()
    {
        if (_stopHandled)
            return;

        if (Interlocked.Exchange(ref _renderInvalidationQueued, 1) != 0)
            return;

        try
        {
            Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() =>
                {
                    Interlocked.Exchange(ref _renderInvalidationQueued, 0);

                    if (!_stopHandled && _videoSurfaceStarted)
                        VideoSurface.InvalidateVisual();
                }));
        }
        catch (InvalidOperationException)
        {
            Interlocked.Exchange(ref _renderInvalidationQueued, 0);
        }
    }

    private void PlayerTimer_Tick(object? sender, EventArgs e)
    {
        if (_mpv is null || !_playbackLoaded)
            return;

        var mpvPosition = Math.Max(0, _mpv.PositionSeconds);
        var position = _timelineOffsetSeconds + mpvPosition;

        var duration = _launch.RunTimeTicks is > 0
            ? _launch.RunTimeTicks.Value / 10_000_000d
            : _timelineOffsetSeconds + Math.Max(0, _mpv.DurationSeconds);

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
                    _mpv.IsPaused,
                    _mpv.Volume));
        }

        PlaybackLog.Write("PlayerState",
            $"mpvPos={mpvPosition:0.###}, absolutePos={position:0.###}, offset={_timelineOffsetSeconds:0.###}, " +
            $"duration={duration:0.###}, paused={_mpv.IsPaused}, buffering={_mpv.IsBuffering}, " +
            $"volume={_mpv.Volume:0.##}, speed={_mpv.Speed:0.##} | {_mpv.DiagnosticState}");

        UpdateControls(
            position,
            duration,
            _mpv.IsPaused,
            _mpv.IsBuffering,
            _mpv.Volume,
            _mpv.Speed);

        RefreshTrackButtons();

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

    private void UpdateControls(
        double position,
        double duration,
        bool paused,
        bool buffering,
        double volume,
        double speed)
    {
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
            PauseButton.Content = paused ? "播放" : "暂停";
            MuteButton.Content = volume <= 0.01 ? "取消静音" : "静音";
            StatusBlock.Text = buffering ? "缓冲中…" : paused ? "已暂停" : "";

            VolumeSlider.Value = Math.Clamp(volume, 0, 100);

            var speedIndex = Array.FindIndex(Speeds, x => Math.Abs(x - speed) < 0.01);
            if (speedIndex >= 0 && SpeedComboBox.SelectedIndex != speedIndex)
                SpeedComboBox.SelectedIndex = speedIndex;
        }
        finally
        {
            _updatingUi = false;
        }
    }

    private void ControlsTimer_Tick(object? sender, EventArgs e)
    {
        if (ControlsPanel.Visibility != Visibility.Visible ||
            ControlsPanel.IsMouseOver ||
            _trackMenuOpen)
        {
            return;
        }

        if (DateTime.UtcNow - _lastControlsActivityUtc >= TimeSpan.FromSeconds(3))
            HideControls();
    }

    private void PlayerRoot_MouseMove(object sender, MouseEventArgs e) => ShowControls();

    private void PlayerRoot_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        ShowControls();

        PlaybackLog.Write(
            "PlayerInput",
            $"MouseLeftDown clicks={e.ClickCount}, overControls={ControlsPanel.IsMouseOver}");

        if (e.ClickCount == 2 && !ControlsPanel.IsMouseOver)
        {
            PlaybackLog.Write("PlayerInput", "WPF video double-click -> toggle fullscreen");
            ToggleFullscreen();
            e.Handled = true;
        }
    }

    private void ShowControls()
    {
        if (_stopHandled)
            return;

        _lastControlsActivityUtc = DateTime.UtcNow;
        ControlsPanel.Visibility = Visibility.Visible;
        PlayerRoot.Cursor = Cursors.Arrow;
    }

    private void HideControls()
    {
        if (ControlsPanel.IsMouseOver)
            return;

        ControlsPanel.Visibility = Visibility.Collapsed;
        PlayerRoot.Cursor = Cursors.None;
    }

    private async Task ReportProgressAsync(string eventName = "TimeUpdate")
    {
        var mpv = _mpv;
        if (mpv is null)
            return;

        var paused = mpv.IsPaused;
        var volume = mpv.Volume;

        await SafeReportAsync(() =>
            _client.ReportPlaybackProgressAsync(
                _launch,
                _lastPositionTicks,
                paused,
                volume,
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

    private void Pause_Click(object sender, RoutedEventArgs e) => TogglePauseAndReport();

    private void TogglePauseAndReport()
    {
        if (_mpv is null)
            return;

        PlaybackLog.Write("Player", "Pause toggled");
        var wasPaused = _mpv.IsPaused;
        _mpv.TogglePause();
        _ = ReportProgressAsync(wasPaused ? "Unpause" : "Pause");
        ShowControls();
    }

    private void Back_Click(object sender, RoutedEventArgs e) => SeekRelative(-10);

    private void Forward_Click(object sender, RoutedEventArgs e) => SeekRelative(10);

    private void SeekRelative(double seconds)
    {
        if (_mpv is null)
            return;

        PlaybackLog.Write("Player", $"Seek {(seconds >= 0 ? "+" : "")}{seconds:0.###}");
        _mpv.Seek(seconds);
        ShowControls();
    }

    private void PositionSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_updatingUi)
            return;

        SeekAbsoluteFromTimeline(PositionSlider.Value);
    }

    private void SeekAbsoluteFromTimeline(double requestedAbsolute)
    {
        if (_mpv is null)
            return;

        var localTarget = Math.Max(0, requestedAbsolute - _timelineOffsetSeconds);
        PlaybackLog.Write("Player",
            $"Timeline seek: absolute={requestedAbsolute:0.###}, local={localTarget:0.###}, offset={_timelineOffsetSeconds:0.###}");
        _mpv.SeekAbsolute(localTarget);
        ShowControls();
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_updatingUi)
            SetVolume(e.NewValue);
    }

    private void SetVolume(double volume)
    {
        if (_mpv is null)
            return;

        if (volume > 0.01)
            _lastAudibleVolume = volume;

        _mpv.SetVolume(volume);
        ShowControls();
    }

    private void Mute_Click(object sender, RoutedEventArgs e) => ToggleMute();

    private void ToggleMute()
    {
        if (_mpv is null)
            return;

        var volume = _mpv.Volume;
        if (volume > 0.01)
        {
            _lastAudibleVolume = volume;
            _mpv.SetVolume(0);
        }
        else
        {
            _mpv.SetVolume(_lastAudibleVolume > 0.01 ? _lastAudibleVolume : 100);
        }

        ShowControls();
    }

    private void SpeedComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingUi ||
            _mpv is null ||
            SpeedComboBox.SelectedIndex < 0 ||
            SpeedComboBox.SelectedIndex >= Speeds.Length)
        {
            return;
        }

        SetSpeed(Speeds[SpeedComboBox.SelectedIndex]);
    }

    private void SetSpeed(double speed)
    {
        if (_mpv is null)
            return;

        PlaybackLog.Write("Player", $"Playback speed={speed:0.##}");
        _mpv.SetSpeed(speed);
        ShowControls();
    }

    private void Audio_Click(object sender, RoutedEventArgs e)
    {
        if (_mpv is null)
            return;

        var tracks = _mpv.GetTracks()
            .Where(track => string.Equals(track.Type, "audio", StringComparison.Ordinal))
            .ToArray();

        OpenTrackMenu(AudioButton, tracks, isSubtitleMenu: false);
    }

    private void Subtitle_Click(object sender, RoutedEventArgs e)
    {
        if (_mpv is null)
            return;

        var tracks = _mpv.GetTracks()
            .Where(track => string.Equals(track.Type, "sub", StringComparison.Ordinal))
            .ToArray();

        OpenTrackMenu(SubtitleButton, tracks, isSubtitleMenu: true);
    }

    private void OpenTrackMenu(
        Button anchor,
        IReadOnlyList<PlayerTrack> tracks,
        bool isSubtitleMenu)
    {
        if (_activeTrackMenu?.IsOpen == true)
            _activeTrackMenu.IsOpen = false;

        var menu = new ContextMenu
        {
            PlacementTarget = anchor,
            Placement = PlacementMode.Top,
            StaysOpen = false
        };

        if (isSubtitleMenu)
        {
            var subtitlesOff = new MenuItem
            {
                Header = "关闭字幕",
                IsCheckable = true,
                IsChecked = tracks.All(track => !track.Selected)
            };
            subtitlesOff.Click += (_, _) => SelectSubtitleTrack(null);
            menu.Items.Add(subtitlesOff);

            if (tracks.Count > 0)
                menu.Items.Add(new Separator());
        }

        if (tracks.Count == 0)
        {
            menu.Items.Add(new MenuItem
            {
                Header = isSubtitleMenu ? "没有可用字幕" : "没有可用音轨",
                IsEnabled = false
            });
        }
        else
        {
            foreach (var track in tracks)
            {
                var item = new MenuItem
                {
                    Header = FormatTrackMenuLabel(track),
                    IsCheckable = true,
                    IsChecked = track.Selected
                };

                if (isSubtitleMenu)
                {
                    item.Click += (_, _) => SelectSubtitleTrack(track);
                }
                else
                {
                    item.Click += (_, _) => SelectAudioTrack(track);
                }

                menu.Items.Add(item);
            }
        }

        menu.Closed += (_, _) =>
        {
            _trackMenuOpen = false;
            if (ReferenceEquals(_activeTrackMenu, menu))
                _activeTrackMenu = null;
            ShowControls();
        };

        _activeTrackMenu = menu;
        _trackMenuOpen = true;
        ShowControls();
        menu.IsOpen = true;
    }

    private void SelectAudioTrack(PlayerTrack track)
    {
        if (_mpv is null)
            return;

        _mpv.SetAudioTrack(track.Id);
        AudioButton.Content = $"音轨 · {FormatTrackButtonLabel(track)}";
        StatusBlock.Text = $"音轨：{FormatTrackMenuLabel(track)}";
        PlaybackLog.Write("PlayerTrack", $"Audio selected: {FormatTrackMenuLabel(track)}");
        _ = ReportProgressAsync("AudioTrackChange");
        ShowControls();
    }

    private void SelectSubtitleTrack(PlayerTrack? track)
    {
        if (_mpv is null)
            return;

        _mpv.SetSubtitleTrack(track?.Id);

        if (track is null)
        {
            SubtitleButton.Content = "字幕 · 关闭";
            StatusBlock.Text = "字幕已关闭";
            PlaybackLog.Write("PlayerTrack", "Subtitle disabled");
        }
        else
        {
            SubtitleButton.Content = $"字幕 · {FormatTrackButtonLabel(track)}";
            StatusBlock.Text = $"字幕：{FormatTrackMenuLabel(track)}";
            PlaybackLog.Write("PlayerTrack", $"Subtitle selected: {FormatTrackMenuLabel(track)}");
        }

        _ = ReportProgressAsync("SubtitleTrackChange");
        ShowControls();
    }

    private void RefreshTrackButtons()
    {
        if (_mpv is null || !_playbackLoaded)
            return;

        var tracks = _mpv.GetTracks();
        var selectedAudio = tracks.FirstOrDefault(track =>
            string.Equals(track.Type, "audio", StringComparison.Ordinal) &&
            track.Selected);
        var selectedSubtitle = tracks.FirstOrDefault(track =>
            string.Equals(track.Type, "sub", StringComparison.Ordinal) &&
            track.Selected);

        AudioButton.Content = selectedAudio is null
            ? "音轨"
            : $"音轨 · {FormatTrackButtonLabel(selectedAudio)}";

        SubtitleButton.Content = selectedSubtitle is null
            ? "字幕 · 关闭"
            : $"字幕 · {FormatTrackButtonLabel(selectedSubtitle)}";
    }

    private static string FormatTrackButtonLabel(PlayerTrack track)
    {
        if (!string.IsNullOrWhiteSpace(track.Language))
            return track.Language.ToUpperInvariant();

        if (!string.IsNullOrWhiteSpace(track.Title))
            return track.Title;

        if (!string.IsNullOrWhiteSpace(track.Codec))
            return track.Codec.ToUpperInvariant();

        return $"#{track.Id}";
    }

    private static string FormatTrackMenuLabel(PlayerTrack track)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(track.Language))
            parts.Add(track.Language.ToUpperInvariant());

        if (!string.IsNullOrWhiteSpace(track.Title) &&
            !parts.Contains(track.Title, StringComparer.OrdinalIgnoreCase))
        {
            parts.Add(track.Title);
        }

        if (!string.IsNullOrWhiteSpace(track.Codec))
            parts.Add(track.Codec.ToUpperInvariant());

        if (string.Equals(track.Type, "audio", StringComparison.Ordinal))
        {
            if (!string.IsNullOrWhiteSpace(track.Channels))
                parts.Add(track.Channels);
            else if (track.ChannelCount is > 0)
                parts.Add($"{track.ChannelCount}ch");
        }

        if (track.External)
            parts.Add("外挂");

        if (track.Forced)
            parts.Add("强制");

        if (track.Default)
            parts.Add("默认");

        if (parts.Count == 0)
            parts.Add($"Track {track.Id}");

        return string.Join(" · ", parts);
    }

    private void Fullscreen_Click(object sender, RoutedEventArgs e)
    {
        PlaybackLog.Write("PlayerInput", "Fullscreen button clicked");
        ToggleFullscreen();
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
        VideoSurface.InvalidateVisual();
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

            case Key.Up when _mpv is not null:
                SetVolume(Math.Min(100, _mpv.Volume + 5));
                e.Handled = true;
                break;

            case Key.Down when _mpv is not null:
                SetVolume(Math.Max(0, _mpv.Volume - 5));
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

        if (_activeTrackMenu?.IsOpen == true)
            _activeTrackMenu.IsOpen = false;
        _activeTrackMenu = null;
        _trackMenuOpen = false;

        var finalTicks = _lastPositionTicks;
        var finalVolume = _mpv?.Volume ?? 100;
        PlaybackLog.Write("Player", $"Closing playback window: finalTicks={finalTicks}");

        _ = FinalizePlaybackAsync(finalTicks, finalVolume);

        PlayerRoot.Cursor = Cursors.Arrow;
        ShutdownPlayback();

        PlaybackLog.Write("Player", "mpv Render API playback stopped and disposed on window close");
    }

    private void ShutdownPlayback()
    {
        var mpv = _mpv;
        _mpv = null;

        if (mpv is null)
            return;

        try
        {
            mpv.Stop();
        }
        catch (Exception ex)
        {
            PlaybackLog.Error("mpv-stop", ex);
        }

        try
        {
            if (_videoSurfaceStarted)
                VideoSurface.Context?.MakeCurrent();

            _renderContext?.Dispose();
        }
        catch (Exception ex)
        {
            PlaybackLog.Error("mpv-render-dispose", ex);
        }
        finally
        {
            _renderContext = null;
        }

        try
        {
            mpv.Dispose();
        }
        catch (Exception ex)
        {
            PlaybackLog.Error("mpv-dispose", ex);
        }

        if (_videoSurfaceStarted)
        {
            try
            {
                VideoSurface.Dispose();
            }
            catch (Exception ex)
            {
                PlaybackLog.Error("video-surface-dispose", ex);
            }

            _videoSurfaceStarted = false;
        }
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
