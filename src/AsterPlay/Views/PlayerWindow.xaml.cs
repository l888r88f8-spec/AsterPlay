using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using AsterPlay.Models;
using AsterPlay.Services;
using AsterPlay.Services.Mpv;
using AsterPlay.Services.Danmaku;
using AsterPlay.Models.Danmaku;
using OpenTK.Wpf;

namespace AsterPlay.Views;

public partial class PlayerWindow : Window
{
    private static readonly double[] Speeds = [0.5, 0.75, 1.0, 1.25, 1.5, 2.0];

    private readonly EmbyClient _client;
    private PlaybackLaunch _launch;
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _controlsTimer;
    private readonly DanmakuService _danmakuService = new();
    private readonly CancellationTokenSource _danmakuLoadCts = new();
    private DanmakuSettings _danmakuSettings = DanmakuSettingsStore.Load();
    private DanmakuSourceSettings _danmakuSourceSettings =
        DanmakuSourceSettingsStore.Load();
    private int _danmakuLoadVersion;
    private double _timelineOffsetSeconds;

    private MpvClient? _mpv;
    private MpvRenderContext? _renderContext;
    private DateTime _lastControlsActivityUtc;
    private int _renderInvalidationQueued;
    private int _reportSeconds;
    private int _stateLogSeconds;
    private int _danmakuLogSeconds;
    private int _seekRequestVersion;
    private double? _serverSeekUiTargetSeconds;
    private double? _danmakuSeekTargetSeconds;
    private double? _danmakuSeekLastTimelineSeconds;
    private int _danmakuSeekResumeSamples;
    private int _lastFramebuffer;
    private int _lastFramebufferWidth;
    private int _lastFramebufferHeight;
    private bool _videoSurfaceStarted;
    private bool _playbackLoaded;
    private bool _updatingUi;
    private bool _fullscreen;
    private bool _startReportSent;
    private bool _stopHandled;
    private bool _renderFailureShown;
    private bool _sourceFailureShown;
    private bool _trackMenuOpen;
    private bool _diagnosticsVisible;
    private bool _danmakuVisible;
    private bool _positionSliderPointerDown;
    private bool _positionSliderDragging;
    private ContextMenu? _activeTrackMenu;
    private ContextMenu? _activeDanmakuSettingsMenu;
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

        DanmakuOverlay.SetSettings(_danmakuSettings);

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
            _mpv.PlaybackEnded += Mpv_PlaybackEnded;
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
            _ = LoadDanmakuAsync();
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
                $"{UserError.GetMessage(ex, "初始化播放器")}\n\n日志：{PlaybackLog.LogPath}",
                "player",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Close();
        }
    }

    private void Mpv_PlaybackEnded(object? sender, MpvPlaybackEndedEventArgs e)
    {
        if (!e.IsError || _stopHandled || _sourceFailureShown)
            return;

        _sourceFailureShown = true;
        PlaybackLog.Write(
            "PlayerSource",
            $"Playback source failed: playMethod={_launch.PlayMethod}, reason={e.ReasonName}, error={e.Error} ({e.ErrorText})");

        Dispatcher.BeginInvoke(
            DispatcherPriority.Normal,
            new Action(() =>
            {
                if (_stopHandled)
                    return;

                var isTranscode = string.Equals(
                    _launch.PlayMethod,
                    "Transcode",
                    StringComparison.OrdinalIgnoreCase);

                StatusBlock.Text = isTranscode ? "转码流播放失败" : "视频源播放失败";

                var message = isTranscode
                    ? "Emby 转码流播放失败。请检查服务端转码任务、磁盘空间和媒体源状态。"
                    : "视频源已失效、不可访问，或 mpv 无法打开该媒体流。";

                MessageBox.Show(
                    $"{message}\n\nmpv：{e.ErrorText}\n日志：{PlaybackLog.LogPath}",
                    isTranscode ? "Transcode failed" : "Playback source failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }));
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

            var framebuffer = VideoSurface.Framebuffer;
            var framebufferWidth = VideoSurface.FrameBufferWidth;
            var framebufferHeight = VideoSurface.FrameBufferHeight;

            if (framebuffer != _lastFramebuffer ||
                framebufferWidth != _lastFramebufferWidth ||
                framebufferHeight != _lastFramebufferHeight)
            {
                PlaybackLog.Write(
                    "mpv-render",
                    $"Framebuffer changed: fbo={framebuffer}, size={framebufferWidth}x{framebufferHeight}, " +
                    "libmpvFlipY=0, wpfCompensation=ScaleY(-1)");

                _lastFramebuffer = framebuffer;
                _lastFramebufferWidth = framebufferWidth;
                _lastFramebufferHeight = framebufferHeight;
            }

            _renderContext.Render(
                framebuffer,
                framebufferWidth,
                framebufferHeight);
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

        _stateLogSeconds++;
        if (_stateLogSeconds >= 5)
        {
            _stateLogSeconds = 0;
            PlaybackLog.Write("PlayerState",
                $"mpvPos={mpvPosition:0.###}, absolutePos={position:0.###}, offset={_timelineOffsetSeconds:0.###}, " +
                $"duration={duration:0.###}, paused={_mpv.IsPaused}, buffering={_mpv.IsBuffering}, " +
                $"volume={_mpv.Volume:0.##}, speed={_mpv.Speed:0.##} | {_mpv.DiagnosticState}");
        }

        UpdateControls(
            position,
            duration,
            _mpv.IsPaused,
            _mpv.IsBuffering,
            _mpv.Volume,
            _mpv.Speed);

        RefreshTrackButtons();

        if (_diagnosticsVisible)
            RefreshDiagnosticsPanel();

        if (_danmakuVisible)
        {
            _danmakuLogSeconds++;
            if (_danmakuLogSeconds >= 5)
            {
                _danmakuLogSeconds = 0;
                var metrics = DanmakuOverlay.GetMetrics();
                PlaybackLog.Write(
                    "Danmaku",
                    $"timeline={metrics.TimelineSeconds:0.###}, input={metrics.InputCount}, loaded={metrics.LoadedCount}, " +
                    $"documentDropped={metrics.DocumentDroppedCount}, visible={metrics.VisibleCount}, active={metrics.ActiveCount}, " +
                    $"layoutDropped={metrics.LayoutDroppedCount}, " +
                    $"frame={metrics.LastFrameMilliseconds:0.###}ms, peak={metrics.PeakFrameMilliseconds:0.###}ms, " +
                    $"surface={metrics.Width:0}x{metrics.Height:0}, dpi={metrics.DpiX:0}x{metrics.DpiY:0}");
            }
        }

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

                if (_serverSeekUiTargetSeconds is null &&
                    !_positionSliderPointerDown &&
                    !_positionSliderDragging)
                {
                    PositionSlider.Value = Math.Clamp(position, 0, duration);
                }
            }

            if (_serverSeekUiTargetSeconds is null &&
                !_positionSliderPointerDown &&
                !_positionSliderDragging)
            {
                CurrentTimeBlock.Text = FormatTime(position);
            }

            DurationBlock.Text = duration > 0 ? FormatTime(duration) : "--:--";
            PauseButton.Content = paused ? "播放" : "暂停";
            MuteButton.Content = volume <= 0.01 ? "取消静音" : "静音";
            StatusBlock.Text = _serverSeekUiTargetSeconds is double seekTarget
                ? $"跳转至 {FormatTime(seekTarget)}…"
                : buffering ? "缓冲中…" : paused ? "已暂停" : "";

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
        SyncDanmaku();

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
        SyncDanmaku();
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

        var currentAbsolute =
            _timelineOffsetSeconds + Math.Max(0, _mpv.PositionSeconds);
        var targetAbsolute = Math.Clamp(
            currentAbsolute + seconds,
            0,
            PositionSlider.Maximum > 0
                ? PositionSlider.Maximum
                : Math.Max(0, currentAbsolute + seconds));

        if (_launch.RequiresServerSeek)
        {
            SeekAbsoluteFromTimeline(targetAbsolute);
            return;
        }

        BeginDanmakuSeekSuppression(targetAbsolute);
        _mpv.Seek(seconds);
        SyncDanmaku();
        ShowControls();
    }

    private void PositionSlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_updatingUi)
            return;

        _positionSliderPointerDown = true;
        ShowControls();
    }

    private void PositionSlider_DragStarted(object sender, DragStartedEventArgs e)
    {
        _positionSliderPointerDown = true;
        _positionSliderDragging = true;

        PlaybackLog.Write(
            "PlayerSeek",
            $"Slider drag started: displayed={PositionSlider.Value:0.###}, offset={_timelineOffsetSeconds:0.###}");
        ShowControls();
    }

    private void PositionSlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingUi)
            return;

        if (_positionSliderPointerDown || _positionSliderDragging)
            CurrentTimeBlock.Text = FormatTime(e.NewValue);
    }

    private void PositionSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_updatingUi || _positionSliderDragging)
            return;

        var requestedAbsolute = PositionSlider.Value;
        _positionSliderPointerDown = false;

        PlaybackLog.Write(
            "PlayerSeek",
            $"Slider click committed: target={requestedAbsolute:0.###}");

        SeekAbsoluteFromTimeline(requestedAbsolute);
    }

    private void PositionSlider_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (_updatingUi)
            return;

        var requestedAbsolute = PositionSlider.Value;
        _positionSliderDragging = false;
        _positionSliderPointerDown = false;

        PlaybackLog.Write(
            "PlayerSeek",
            $"Slider drag committed: target={requestedAbsolute:0.###}");

        SeekAbsoluteFromTimeline(requestedAbsolute);
    }

    private void PositionSlider_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_positionSliderDragging)
            return;

        _positionSliderPointerDown = false;
    }

    private void SeekAbsoluteFromTimeline(double requestedAbsolute) =>
        _ = SeekAbsoluteFromTimelineAsync(requestedAbsolute);

    private async Task SeekAbsoluteFromTimelineAsync(double requestedAbsolute)
    {
        var mpv = _mpv;
        if (mpv is null)
            return;

        requestedAbsolute = Math.Clamp(
            requestedAbsolute,
            0,
            PositionSlider.Maximum > 0 ? PositionSlider.Maximum : requestedAbsolute);

        BeginDanmakuSeekSuppression(requestedAbsolute);

        if (_launch.RequiresServerSeek)
        {
            await ReopenServerSeekStreamAsync(requestedAbsolute);
            return;
        }

        var localTarget = Math.Max(0, requestedAbsolute - _timelineOffsetSeconds);
        PlaybackLog.Write(
            "PlayerSeek",
            $"Client seek: absolute={requestedAbsolute:0.###}, local={localTarget:0.###}, offset={_timelineOffsetSeconds:0.###}");

        mpv.SeekAbsolute(localTarget);
        CurrentTimeBlock.Text = FormatTime(requestedAbsolute);
        SyncDanmaku();
        ShowControls();
    }

    private async Task ReopenServerSeekStreamAsync(double requestedAbsolute)
    {
        var mpv = _mpv;
        if (mpv is null)
            return;

        var requestVersion = ++_seekRequestVersion;
        var oldLaunch = _launch;
        var targetTicks = (long)Math.Max(
            0,
            Math.Round(requestedAbsolute * 10_000_000d));

        var wasPaused = mpv.IsPaused;
        var volume = mpv.Volume;
        var speed = mpv.Speed;

        _serverSeekUiTargetSeconds = requestedAbsolute;

        PlaybackLog.Write(
            "PlayerSeek",
            $"Server seek negotiation begin: absolute={requestedAbsolute:0.###}, ticks={targetTicks}, " +
            $"mediaSourceId={oldLaunch.MediaSourceId}, currentPlaySessionId={oldLaunch.PlaySessionId}");

        StatusBlock.Text = $"跳转至 {FormatTime(requestedAbsolute)}…";
        CurrentTimeBlock.Text = FormatTime(requestedAbsolute);
        PositionSlider.Value = requestedAbsolute;
        ShowControls();

        try
        {
            var replacement = await _client.ReopenPlayableStreamAsync(
                oldLaunch,
                targetTicks);

            if (_stopHandled ||
                requestVersion != _seekRequestVersion ||
                _mpv is null)
            {
                PlaybackLog.Write(
                    "PlayerSeek",
                    $"Ignoring stale server seek response: requestVersion={requestVersion}, latest={_seekRequestVersion}");
                return;
            }

            if (!string.Equals(
                    replacement.ItemId,
                    oldLaunch.ItemId,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Emby returned a different item while seeking.");
            }

            _launch = replacement;
            _timelineOffsetSeconds = replacement.UsesServerStartOffset
                ? Math.Max(0, replacement.ResumePositionTicks / 10_000_000d)
                : 0;
            _lastPositionTicks = targetTicks;
            _startReportSent = false;
            _reportSeconds = 0;

            PlaybackLog.Write(
                "PlayerSeek",
                $"Server seek negotiation complete: oldSession={oldLaunch.PlaySessionId}, " +
                $"newSession={replacement.PlaySessionId}, serverOffset={replacement.UsesServerStartOffset}, " +
                $"requiresServerSeek={replacement.RequiresServerSeek}, url={PlaybackLog.Redact(replacement.Url)}");

            _mpv.Load(replacement.Url);
            _mpv.SetVolume(volume);
            _mpv.SetSpeed(speed);
            _mpv.SetPaused(wasPaused);

            _serverSeekUiTargetSeconds = null;
            CurrentTimeBlock.Text = FormatTime(requestedAbsolute);
            PositionSlider.Value = requestedAbsolute;
            SyncDanmaku();
            ShowControls();
        }
        catch (Exception ex)
        {
            if (requestVersion != _seekRequestVersion)
                return;

            PlaybackLog.Error("PlayerSeek", ex);
            _serverSeekUiTargetSeconds = null;
            CancelDanmakuSeekSuppression();
            StatusBlock.Text = "跳转失败，继续当前播放";

            var currentAbsolute =
                _timelineOffsetSeconds + Math.Max(0, mpv.PositionSeconds);
            CurrentTimeBlock.Text = FormatTime(currentAbsolute);
            PositionSlider.Value = Math.Clamp(
                currentAbsolute,
                PositionSlider.Minimum,
                PositionSlider.Maximum);

            ShowControls();
        }
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
        SyncDanmaku();
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

    private DanmakuContext CreateDanmakuContext()
    {
        var durationSeconds = _launch.RunTimeTicks is > 0
            ? _launch.RunTimeTicks.Value / 10_000_000d
            : 0;

        return new DanmakuContext(
            _launch.ItemId,
            _launch.Title,
            durationSeconds,
            _launch.SeriesId,
            _launch.SeriesName,
            _launch.SeasonNumber,
            _launch.EpisodeNumber,
            _launch.OriginalTitle,
            _launch.ItemType,
            _launch.SourcePath,
            _launch.SourceFileName);
    }

    private async Task LoadDanmakuAsync()
    {
        try
        {
            var loadVersion = ++_danmakuLoadVersion;
            var document = await _danmakuService.LoadAsync(
                CreateDanmakuContext(),
                _danmakuSourceSettings,
                _danmakuLoadCts.Token);

            if (_stopHandled ||
                _danmakuLoadCts.IsCancellationRequested ||
                loadVersion != _danmakuLoadVersion)
            {
                return;
            }

            DanmakuOverlay.SetDocument(document);

            PlaybackLog.Write(
                "Danmaku",
                $"Document ready: source={document.SourceName}, comments={document.Comments.Count}");

            if (document.Comments.Count == 0)
                StatusBlock.Text = $"弹幕：{document.SourceName} 未匹配到弹幕";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            PlaybackLog.Error("DanmakuLoad", ex);

            if (!_stopHandled)
                StatusBlock.Text = $"LogVar 弹幕加载失败：{ex.Message}";
        }
    }

    private void Danmaku_Click(object sender, RoutedEventArgs e) =>
        ToggleDanmaku();

    private void DanmakuSettings_Click(object sender, RoutedEventArgs e) =>
        OpenDanmakuSettingsMenu();

    private void DanmakuSource_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new DanmakuSourceSettingsWindow(
            _danmakuSourceSettings)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true || dialog.Result is null)
            return;

        _danmakuSourceSettings = dialog.Result;
        DanmakuSourceSettingsStore.Save(_danmakuSourceSettings);

        StatusBlock.Text = "LogVar：正在重新匹配弹幕…";

        DanmakuOverlay.SetDocument(
            new DanmakuDocument(
                "LogVar",
                Array.Empty<DanmakuComment>()));

        _ = LoadDanmakuAsync();
        ShowControls();
    }

    private void DanmakuMatch_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(
                _danmakuSourceSettings.LogVarBaseUrl))
        {
            StatusBlock.Text = "请先配置 LogVar 服务器地址。";
            ShowControls();
            return;
        }

        var context = CreateDanmakuContext();
        var currentBinding = _danmakuService.GetManualSeriesMatch(
            context,
            _danmakuSourceSettings);

        var dialog = new DanmakuMatchWindow(
            _danmakuService,
            context,
            _danmakuSourceSettings,
            currentBinding)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
            return;

        if (dialog.UseAutomaticMatch)
        {
            _danmakuService.ClearManualSeriesMatch(
                context,
                _danmakuSourceSettings);

            StatusBlock.Text =
                "LogVar：已恢复当前剧集自动匹配，正在重新加载…";
        }
        else if (
            dialog.SelectedSeries is DanmakuSeriesMatchCandidate series &&
            dialog.SelectedEpisode is DanmakuMatchCandidate episode)
        {
            var binding = _danmakuService.SetManualSeriesMatch(
                context,
                _danmakuSourceSettings,
                series,
                episode);

            var offsetText = binding.EpisodeOffset == 0
                ? "集数一一对应"
                : binding.EpisodeOffset > 0
                    ? $"集数偏移 +{binding.EpisodeOffset}"
                    : $"集数偏移 {binding.EpisodeOffset}";

            StatusBlock.Text =
                $"LogVar：已绑定 {series.AnimeTitle}（{offsetText}），正在重新加载…";
        }
        else
        {
            return;
        }

        DanmakuOverlay.SetDocument(
            new DanmakuDocument(
                "LogVar",
                Array.Empty<DanmakuComment>()));

        _ = LoadDanmakuAsync();
        ShowControls();
    }

    private void OpenDanmakuSettingsMenu()
    {
        if (_activeDanmakuSettingsMenu?.IsOpen == true)
            _activeDanmakuSettingsMenu.IsOpen = false;

        var menu = new ContextMenu
        {
            PlacementTarget = DanmakuSettingsButton,
            Placement = PlacementMode.Top,
            StaysOpen = false
        };

        menu.Items.Add(CreateDanmakuSettingGroup(
            "字号",
            [
                ("小 · 18", 18d),
                ("标准 · 22", 22d),
                ("大 · 28", 28d),
                ("特大 · 34", 34d)
            ],
            _danmakuSettings.FontSize,
            value => ApplyDanmakuSettings(
                _danmakuSettings with { FontSize = value },
                $"字号 {value:0}")));

        menu.Items.Add(CreateDanmakuSettingGroup(
            "速度",
            [
                ("慢 · 0.75×", 0.75d),
                ("标准 · 1.0×", 1d),
                ("快 · 1.25×", 1.25d),
                ("很快 · 1.5×", 1.5d)
            ],
            _danmakuSettings.Speed,
            value => ApplyDanmakuSettings(
                _danmakuSettings with { Speed = value },
                $"速度 {value:0.##}×")));

        menu.Items.Add(CreateDanmakuSettingGroup(
            "透明度",
            [
                ("50%", 0.50d),
                ("70%", 0.70d),
                ("85%", 0.85d),
                ("100%", 1.00d)
            ],
            _danmakuSettings.Opacity,
            value => ApplyDanmakuSettings(
                _danmakuSettings with { Opacity = value },
                $"透明度 {value:P0}")));

        menu.Items.Add(CreateDanmakuSettingGroup(
            "显示区域",
            [
                ("上半屏 · 50%", 0.50d),
                ("默认 · 72%", 0.72d),
                ("全屏 · 100%", 1.00d)
            ],
            _danmakuSettings.ScreenHeightRatio,
            value => ApplyDanmakuSettings(
                _danmakuSettings with { ScreenHeightRatio = value },
                $"显示区域 {value:P0}")));

        menu.Items.Add(CreateDanmakuSettingGroup(
            "密度",
            [
                ("低 · 35%", 0.35d),
                ("中 · 65%", 0.65d),
                ("高 · 100%", 1.00d)
            ],
            _danmakuSettings.DensityRatio,
            value => ApplyDanmakuSettings(
                _danmakuSettings with { DensityRatio = value },
                $"密度 {value:P0}")));

        menu.Items.Add(CreateDanmakuIntSettingGroup(
            "同时显示上限",
            [
                ("省资源 · 40", 40),
                ("标准 · 80", 80),
                ("高 · 140", 140)
            ],
            _danmakuSettings.MaxActiveComments,
            value => ApplyDanmakuSettings(
                _danmakuSettings with { MaxActiveComments = value },
                $"同时显示上限 {value}")));

        var overlapItem = new MenuItem
        {
            Header = "防重叠",
            IsCheckable = true,
            IsChecked = _danmakuSettings.AvoidOverlap
        };
        overlapItem.Click += (_, _) =>
            ApplyDanmakuSettings(
                _danmakuSettings with
                {
                    AvoidOverlap = overlapItem.IsChecked
                },
                overlapItem.IsChecked
                    ? "已开启防重叠"
                    : "已关闭防重叠");
        menu.Items.Add(overlapItem);

        var filterItem = new MenuItem
        {
            Header =
                $"过滤设置… ({_danmakuSettings.BlockedWords.Count} 词 / {_danmakuSettings.BlockedUsers.Count} 用户)"
        };
        filterItem.Click += (_, _) =>
            OpenDanmakuFilterWindow();
        menu.Items.Add(filterItem);

        menu.Items.Add(new Separator());

        var resetItem = new MenuItem { Header = "恢复默认设置" };
        resetItem.Click += (_, _) =>
            ApplyDanmakuSettings(new DanmakuSettings(), "已恢复默认设置");
        menu.Items.Add(resetItem);

        menu.Closed += (_, _) =>
        {
            _trackMenuOpen = false;
            if (ReferenceEquals(_activeDanmakuSettingsMenu, menu))
                _activeDanmakuSettingsMenu = null;
            ShowControls();
        };

        _activeDanmakuSettingsMenu = menu;
        _trackMenuOpen = true;
        ShowControls();
        menu.IsOpen = true;
    }

    private static MenuItem CreateDanmakuSettingGroup(
        string header,
        IReadOnlyList<(string Label, double Value)> options,
        double currentValue,
        Action<double> apply)
    {
        var group = new MenuItem { Header = header };

        foreach (var option in options)
        {
            var item = new MenuItem
            {
                Header = option.Label,
                IsCheckable = true,
                IsChecked = Math.Abs(option.Value - currentValue) < 0.001
            };

            var value = option.Value;
            item.Click += (_, _) => apply(value);
            group.Items.Add(item);
        }

        return group;
    }

    private static MenuItem CreateDanmakuIntSettingGroup(
        string header,
        IReadOnlyList<(string Label, int Value)> options,
        int currentValue,
        Action<int> apply)
    {
        var group = new MenuItem { Header = header };

        foreach (var option in options)
        {
            var item = new MenuItem
            {
                Header = option.Label,
                IsCheckable = true,
                IsChecked = option.Value == currentValue
            };

            var value = option.Value;
            item.Click += (_, _) => apply(value);
            group.Items.Add(item);
        }

        return group;
    }

    private void OpenDanmakuFilterWindow()
    {
        var dialog = new DanmakuFilterWindow(
            _danmakuSettings)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true ||
            dialog.Result is null)
        {
            return;
        }

        ApplyDanmakuSettings(
            dialog.Result,
            $"过滤已更新：{dialog.Result.BlockedWords.Count} 词 / {dialog.Result.BlockedUsers.Count} 用户");
    }

    private void ApplyDanmakuSettings(
        DanmakuSettings settings,
        string statusText)
    {
        _danmakuSettings = settings;
        DanmakuOverlay.SetSettings(_danmakuSettings);
        DanmakuSettingsStore.Save(_danmakuSettings);

        StatusBlock.Text = $"弹幕：{statusText}";
        PlaybackLog.Write(
            "DanmakuSettings",
            $"font={_danmakuSettings.FontSize:0.##}, speed={_danmakuSettings.Speed:0.##}, " +
            $"opacity={_danmakuSettings.Opacity:0.##}, area={_danmakuSettings.ScreenHeightRatio:0.##}, " +
            $"density={_danmakuSettings.DensityRatio:0.##}, cap={_danmakuSettings.MaxActiveComments}, " +
            $"avoidOverlap={_danmakuSettings.AvoidOverlap}, blockedWords={_danmakuSettings.BlockedWords.Count}, " +
            $"blockedUsers={_danmakuSettings.BlockedUsers.Count}");

        if (_diagnosticsVisible)
            RefreshDiagnosticsPanel();

        ShowControls();
    }

    private void ToggleDanmaku()
    {
        _danmakuVisible = !_danmakuVisible;
        DanmakuOverlay.SetActive(_danmakuVisible);
        DanmakuButton.Content = _danmakuVisible ? "弹幕 ✓" : "弹幕";
        _danmakuLogSeconds = 0;

        if (_danmakuVisible)
            SyncDanmaku();

        var metrics = DanmakuOverlay.GetMetrics();
        PlaybackLog.Write(
            "Danmaku",
            _danmakuVisible
                ? $"Enabled: source={metrics.SourceName}, input={metrics.InputCount}, loaded={metrics.LoadedCount}, " +
                  $"documentDropped={metrics.DocumentDroppedCount}, visible={metrics.VisibleCount}, " +
                  $"layoutDropped={metrics.LayoutDroppedCount}, cap={_danmakuSettings.MaxActiveComments}, " +
                  $"mouseThrough={!DanmakuOverlay.IsHitTestVisible}, surface={metrics.Width:0}x{metrics.Height:0}, " +
                  $"dpi={metrics.DpiX:0}x{metrics.DpiY:0}"
                : $"Disabled: source={metrics.SourceName}, input={metrics.InputCount}, loaded={metrics.LoadedCount}, " +
                  $"documentDropped={metrics.DocumentDroppedCount}, visible={metrics.VisibleCount}, " +
                  $"peakFrame={metrics.PeakFrameMilliseconds:0.###}ms");

        if (_diagnosticsVisible)
            RefreshDiagnosticsPanel();

        ShowControls();
    }

    private void BeginDanmakuSeekSuppression(double targetSeconds)
    {
        _danmakuSeekTargetSeconds = targetSeconds;
        _danmakuSeekLastTimelineSeconds = null;
        _danmakuSeekResumeSamples = 0;

        if (!_danmakuVisible)
            return;

        DanmakuOverlay.SetSuppressed(true);
        PlaybackLog.Write(
            "Danmaku",
            $"Hidden for seek: target={targetSeconds:0.###}");
    }

    private void CancelDanmakuSeekSuppression()
    {
        _danmakuSeekTargetSeconds = null;
        _danmakuSeekLastTimelineSeconds = null;
        _danmakuSeekResumeSamples = 0;

        if (!_danmakuVisible || _mpv is null || !_playbackLoaded)
        {
            DanmakuOverlay.SetSuppressed(false);
            return;
        }

        var timelineSeconds =
            _timelineOffsetSeconds + Math.Max(0, _mpv.PositionSeconds);

        DanmakuOverlay.Synchronize(
            timelineSeconds,
            _mpv.IsPaused || _mpv.IsBuffering,
            _mpv.Speed);
        DanmakuOverlay.SetSuppressed(false);
    }

    private void SyncDanmaku()
    {
        if (!_danmakuVisible || _mpv is null || !_playbackLoaded)
            return;

        var mpvPosition = Math.Max(0, _mpv.PositionSeconds);
        var timelineSeconds = _timelineOffsetSeconds + mpvPosition;

        if (_danmakuSeekTargetSeconds is double seekTarget)
        {
            DanmakuOverlay.SetSuppressed(true);

            if (_mpv.IsPaused || _mpv.IsBuffering)
            {
                _danmakuSeekLastTimelineSeconds = timelineSeconds;
                _danmakuSeekResumeSamples = 0;
                return;
            }

            var nearTarget = Math.Abs(timelineSeconds - seekTarget) <= 1.5;
            var advancing =
                _danmakuSeekLastTimelineSeconds is double previousTimeline &&
                timelineSeconds - previousTimeline >= 0.03;

            _danmakuSeekLastTimelineSeconds = timelineSeconds;

            if (nearTarget && advancing)
                _danmakuSeekResumeSamples++;
            else
                _danmakuSeekResumeSamples = 0;

            // Require two consecutive advancing samples near the seek target.
            // At the 250 ms sync cadence this keeps the barrage hidden until
            // actual playback has resumed, so no intermediate clock alignment
            // is ever visible to the user.
            if (_danmakuSeekResumeSamples < 2)
                return;

            PlaybackLog.Write(
                "Danmaku",
                $"Shown after seek playback resumed: target={seekTarget:0.###}, timeline={timelineSeconds:0.###}, " +
                $"offset={_timelineOffsetSeconds:0.###}, mpvPos={mpvPosition:0.###}");

            _danmakuSeekTargetSeconds = null;
            _danmakuSeekLastTimelineSeconds = null;
            _danmakuSeekResumeSamples = 0;

            DanmakuOverlay.Synchronize(
                timelineSeconds,
                paused: false,
                _mpv.Speed);
            DanmakuOverlay.SetSuppressed(false);
            return;
        }

        DanmakuOverlay.SetSuppressed(false);
        DanmakuOverlay.Synchronize(
            timelineSeconds,
            _mpv.IsPaused || _mpv.IsBuffering,
            _mpv.Speed);
    }

    private void Diagnostics_Click(object sender, RoutedEventArgs e) =>
        ToggleDiagnostics();

    private void ToggleDiagnostics()
    {
        _diagnosticsVisible = !_diagnosticsVisible;
        DiagnosticsPanel.Visibility = _diagnosticsVisible
            ? Visibility.Visible
            : Visibility.Collapsed;

        DiagnosticsButton.Content = _diagnosticsVisible ? "信息 ✓" : "信息";

        if (_diagnosticsVisible)
        {
            RefreshDiagnosticsPanel();
            PlaybackLog.Write(
                "PlayerDiagnostics",
                $"Opened: playMethod={_launch.PlayMethod}, source={_launch.SourceContainer}, " +
                $"negotiated={_launch.NegotiatedContainer}/{_launch.NegotiatedProtocol}, " +
                $"mediaSourceId={_launch.MediaSourceId}, reason={_launch.DecisionReason}");
        }

        ShowControls();
    }

    private void RefreshDiagnosticsPanel()
    {
        if (_mpv is null || !_playbackLoaded)
            return;

        var snapshot = _mpv.GetDiagnosticSnapshot();
        var tracks = _mpv.GetTracks();

        var selectedAudio = tracks.FirstOrDefault(track =>
            string.Equals(track.Type, "audio", StringComparison.Ordinal) &&
            track.Selected);
        var selectedSubtitle = tracks.FirstOrDefault(track =>
            string.Equals(track.Type, "sub", StringComparison.Ordinal) &&
            track.Selected);

        var routeParts = new List<string> { _launch.PlayMethod };
        if (!string.IsNullOrWhiteSpace(_launch.NegotiatedContainer))
            routeParts.Add(_launch.NegotiatedContainer.ToUpperInvariant());
        if (!string.IsNullOrWhiteSpace(_launch.NegotiatedProtocol))
            routeParts.Add(_launch.NegotiatedProtocol.ToUpperInvariant());

        DiagnosticsMethodBlock.Text = string.Join(" · ", routeParts);
        DiagnosticsDecisionBlock.Text = string.IsNullOrWhiteSpace(_launch.DecisionReason)
            ? "—"
            : _launch.DecisionReason;

        var sourceParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(_launch.SourceContainer))
            sourceParts.Add(_launch.SourceContainer.ToUpperInvariant());
        if (!string.IsNullOrWhiteSpace(_launch.SourceVideoCodec))
            sourceParts.Add(_launch.SourceVideoCodec.ToUpperInvariant());
        if (!string.IsNullOrWhiteSpace(_launch.SourceAudioCodec))
            sourceParts.Add(_launch.SourceAudioCodec.ToUpperInvariant());
        if (_launch.SourceWidth is > 0 && _launch.SourceHeight is > 0)
            sourceParts.Add($"{_launch.SourceWidth}×{_launch.SourceHeight}");
        DiagnosticsSourceBlock.Text = sourceParts.Count == 0
            ? "—"
            : string.Join(" · ", sourceParts);

        var actualVideo = new List<string>();
        if (!string.IsNullOrWhiteSpace(snapshot.VideoCodec))
            actualVideo.Add(snapshot.VideoCodec.ToUpperInvariant());
        if (snapshot.Width is > 0 && snapshot.Height is > 0)
            actualVideo.Add($"{snapshot.Width}×{snapshot.Height}");
        if (snapshot.Fps is > 0)
            actualVideo.Add($"{snapshot.Fps:0.##} fps");
        DiagnosticsVideoBlock.Text = actualVideo.Count == 0
            ? "等待视频…"
            : string.Join(" · ", actualVideo);

        var actualAudio = new List<string>();
        if (!string.IsNullOrWhiteSpace(snapshot.AudioCodec))
            actualAudio.Add(snapshot.AudioCodec.ToUpperInvariant());
        if (selectedAudio is not null)
            actualAudio.Add(FormatTrackMenuLabel(selectedAudio));
        DiagnosticsAudioBlock.Text = actualAudio.Count == 0
            ? "等待音频…"
            : string.Join(" · ", actualAudio.Distinct(StringComparer.OrdinalIgnoreCase));

        DiagnosticsHwdecBlock.Text =
            string.IsNullOrWhiteSpace(snapshot.HwdecCurrent) ||
            string.Equals(snapshot.HwdecCurrent, "no", StringComparison.OrdinalIgnoreCase)
                ? "软件解码"
                : $"{snapshot.HwdecCurrent}（硬件解码）";

        DiagnosticsVoBlock.Text = string.IsNullOrWhiteSpace(snapshot.VideoOutput)
            ? "—"
            : snapshot.VideoOutput;

        var bitrateParts = new List<string>();
        if (snapshot.VideoBitrate is > 0)
            bitrateParts.Add($"视频 {FormatBitrate(snapshot.VideoBitrate.Value)}");
        if (snapshot.AudioBitrate is > 0)
            bitrateParts.Add($"音频 {FormatBitrate(snapshot.AudioBitrate.Value)}");
        DiagnosticsBitrateBlock.Text = bitrateParts.Count == 0
            ? "—"
            : string.Join(" · ", bitrateParts);

        var audioLabel = selectedAudio is null
            ? $"aid {snapshot.AudioTrackId}"
            : FormatTrackMenuLabel(selectedAudio);
        var subtitleLabel = selectedSubtitle is null
            ? "字幕关闭"
            : FormatTrackMenuLabel(selectedSubtitle);
        DiagnosticsTracksBlock.Text = $"{audioLabel} / {subtitleLabel}";

        var cacheParts = new List<string>();
        if (snapshot.CacheSeconds is >= 0)
            cacheParts.Add($"{snapshot.CacheSeconds:0.0}s");
        if (snapshot.CacheBufferingState is >= 0)
            cacheParts.Add($"{snapshot.CacheBufferingState:0}%");
        cacheParts.Add(snapshot.Buffering ? "缓冲中" : "正常");
        DiagnosticsCacheBlock.Text = string.Join(" · ", cacheParts);

        DiagnosticsOffsetBlock.Text = _launch.UsesServerStartOffset
            ? $"{_timelineOffsetSeconds:0.###}s（服务器续播偏移）"
            : "0s";
        DiagnosticsMediaSourceBlock.Text = string.IsNullOrWhiteSpace(_launch.MediaSourceId)
            ? "—"
            : _launch.MediaSourceId;

        if (_danmakuVisible)
        {
            var danmaku = DanmakuOverlay.GetMetrics();
            DiagnosticsDanmakuBlock.Text =
                $"{danmaku.SourceName} · {danmaku.InputCount} 原始 / {danmaku.LoadedCount} 保留 / " +
                $"{danmaku.VisibleCount} 过滤后 / {danmaku.ActiveCount} 活动 · " +
                $"{danmaku.DocumentDroppedCount} 文档保护丢弃 / {danmaku.LayoutDroppedCount} 防重叠丢弃 · " +
                $"字号 {_danmakuSettings.FontSize:0} / 速度 {_danmakuSettings.Speed:0.##}× / " +
                $"透明度 {_danmakuSettings.Opacity:P0} / 密度 {_danmakuSettings.DensityRatio:P0} / " +
                $"上限 {_danmakuSettings.MaxActiveComments} · " +
                $"{danmaku.LastFrameMilliseconds:0.00} ms / 峰值 {danmaku.PeakFrameMilliseconds:0.00} ms · " +
                $"{danmaku.Width:0}×{danmaku.Height:0} · DPI {danmaku.DpiX:0}×{danmaku.DpiY:0}";
        }
        else
        {
            DiagnosticsDanmakuBlock.Text = "关闭";
        }
    }

    private static string FormatBitrate(double bitsPerSecond)
    {
        if (bitsPerSecond >= 1_000_000)
            return $"{bitsPerSecond / 1_000_000d:0.##} Mbps";

        if (bitsPerSecond >= 1_000)
            return $"{bitsPerSecond / 1_000d:0.#} Kbps";

        return $"{bitsPerSecond:0} bps";
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

            case Key.D:
                ToggleDanmaku();
                e.Handled = true;
                break;

            case Key.I:
                ToggleDiagnostics();
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
        _danmakuLoadCts.Cancel();
        DanmakuOverlay.SetActive(false);

        if (_activeTrackMenu?.IsOpen == true)
            _activeTrackMenu.IsOpen = false;
        if (_activeDanmakuSettingsMenu?.IsOpen == true)
            _activeDanmakuSettingsMenu.IsOpen = false;

        _activeTrackMenu = null;
        _activeDanmakuSettingsMenu = null;
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

        mpv.PlaybackEnded -= Mpv_PlaybackEnded;

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
