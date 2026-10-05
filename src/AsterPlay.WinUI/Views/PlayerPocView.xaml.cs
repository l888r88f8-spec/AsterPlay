using System.Globalization;
using AsterPlay.Models;
using AsterPlay.Models.Danmaku;
using AsterPlay.Services;
using AsterPlay.Services.Danmaku;
using AsterPlay.Services.Mpv;
using AsterPlay.WinUI.Interop;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace AsterPlay.WinUI.Views;

public sealed partial class PlayerPocView : UserControl
{
    private readonly EmbyClient _client;
    private PlaybackLaunch _launch;
    private readonly SwapChainPanelInterop _swapChainPanel = new();

    private readonly DispatcherTimer _uiTimer =
        new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly DispatcherTimer _reportTimer =
        new() { Interval = TimeSpan.FromSeconds(10) };
    private readonly DispatcherTimer _controlsTimer =
        new() { Interval = TimeSpan.FromMilliseconds(500) };

    private readonly AppSettings _appSettings = AppSettingsStore.Load();
    private readonly DanmakuService _danmakuService = new();

    private DanmakuSettings _danmakuSettings = DanmakuSettingsStore.Load();
    private DanmakuSourceSettings _danmakuSourceSettings =
        DanmakuSourceSettingsStore.Load();
    private CancellationTokenSource _danmakuLoadCts = new();

    private MpvClient? _mpv;
    private IReadOnlyList<EmbyItem>? _seasonEpisodes;
    private bool _loaded;
    private bool _disposed;
    private bool _updatingSlider;
    private bool _sliderSeeking;
    private bool _danmakuVisible;
    private bool _isFullscreen;
    private bool _diagnosticsVisible;
    private bool _serverSeekPending;
    private bool _pointerOverInteractiveOverlay;
    private int _seekVersion;
    private double _timelineOffsetSeconds;
    private double _lastAudibleVolume = 100;
    private long _lastPositionTicks;
    private DateTime _lastControlsActivityUtc = DateTime.UtcNow;

    public event EventHandler? BackRequested;
    public event EventHandler<EmbyItem>? EpisodeRequested;
    public event EventHandler<bool>? FullscreenRequested;

    public PlayerPocView(EmbyClient client, PlaybackLaunch launch)
    {
        _client = client;
        _launch = launch;

        InitializeComponent();

        TitleBlock.Text = string.IsNullOrWhiteSpace(launch.SeriesName)
            ? launch.Title
            : launch.SeriesName;
        EpisodeTitleBlock.Text = BuildEpisodeSubtitle(launch);

        PreviousEpisodeButton.IsEnabled = HasEpisodeNavigation(launch);
        NextEpisodeButton.IsEnabled = HasEpisodeNavigation(launch);
        EpisodeListButton.IsEnabled = HasEpisodeNavigation(launch);

        SpeedComboBox.SelectedIndex = 2;

        _uiTimer.Tick += UiTimer_Tick;
        _reportTimer.Tick += ReportTimer_Tick;
        _controlsTimer.Tick += ControlsTimer_Tick;

        DanmakuOverlay.ApplySettings(_danmakuSettings);
    }

    private async void PlayerPocView_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loaded || _disposed)
            return;

        _loaded = true;
        Focus(FocusState.Programmatic);
        ShowControls();

        try
        {
            StatusBlock.Text = "正在创建 libmpv D3D11 composition 输出…";

            _mpv = new MpvClient(MpvVideoOutputMode.D3D11Composition);
            _mpv.PlaybackEnded += Mpv_PlaybackEnded;

            UpdateCompositionSize();

            var resumeSeconds = _launch.ResumePositionTicks / 10_000_000d;
            _timelineOffsetSeconds = _launch.UsesServerStartOffset
                ? resumeSeconds
                : 0;
            var mpvStartSeconds = _launch.UsesServerStartOffset
                ? 0
                : resumeSeconds;

            _mpv.Load(_launch.Url, mpvStartSeconds);
            VolumeSlider.Value = Math.Clamp(_mpv.Volume, 0, 100);
            _lastAudibleVolume = VolumeSlider.Value > 0
                ? VolumeSlider.Value
                : 100;

            _uiTimer.Start();
            _reportTimer.Start();
            _controlsTimer.Start();

            await SafeReportAsync(() =>
                _client.ReportPlaybackStartAsync(
                    _launch,
                    _launch.ResumePositionTicks,
                    false,
                    _mpv.Volume));

            StatusBlock.Text = "等待视频输出…";

            if (!string.IsNullOrWhiteSpace(_danmakuSourceSettings.LogVarBaseUrl))
                _ = LoadDanmakuAsync();
        }
        catch (Exception ex)
        {
            PlaybackLog.Error("WinUIPlayerInit", ex);
            StatusBlock.Text = UserError.GetMessage(ex, "播放");
        }
    }

    private void UiTimer_Tick(object? sender, object e)
    {
        if (_mpv is null || _disposed)
            return;

        try
        {
            var swapChain = _mpv.GetDisplaySwapchain();
            if (swapChain != IntPtr.Zero &&
                swapChain != _swapChainPanel.AttachedSwapChain)
            {
                _swapChainPanel.Attach(VideoPanel, swapChain);
                PlaybackLog.Write(
                    "WinUIPlayer",
                    $"Attached mpv display-swapchain=0x{swapChain.ToInt64():X}");
                StatusBlock.Text = "";
            }

            var duration = ResolveDurationSeconds();
            var position = ResolveTimelinePositionSeconds();
            var snapshot = _mpv.GetDiagnosticSnapshot();

            _lastPositionTicks = (long)Math.Max(
                0,
                position * 10_000_000d);

            _updatingSlider = true;
            try
            {
                PositionSlider.Maximum = Math.Max(1, duration);
                BufferedProgressBar.Maximum = Math.Max(1, duration);

                if (!_sliderSeeking && !_serverSeekPending)
                    PositionSlider.Value = Math.Clamp(position, 0, PositionSlider.Maximum);

                var cacheSeconds = Math.Max(0, snapshot.CacheSeconds ?? 0);
                BufferedProgressBar.Value = Math.Clamp(
                    position + cacheSeconds,
                    0,
                    BufferedProgressBar.Maximum);

                if (!_sliderSeeking)
                    CurrentTimeBlock.Text = FormatTime(position);

                DurationBlock.Text = duration > 0
                    ? FormatTime(duration)
                    : "--:--";

                PlayPauseButton.Content = _mpv.IsPaused ? "▶" : "⏸";
                MuteButton.Content = _mpv.Volume <= 0.01 ? "🔇" : "🔊";
                NetworkSpeedBlock.Text = FormatNetworkSpeed(
                    snapshot.NetworkSpeedBytesPerSecond);
                ResolutionButton.Content = FormatResolutionLabel(
                    snapshot.Width,
                    snapshot.Height);
            }
            finally
            {
                _updatingSlider = false;
            }

            if (_danmakuVisible)
                DanmakuOverlay.Sync(position, _mpv.IsPaused);

            if (_diagnosticsVisible)
                RefreshDiagnostics(snapshot);

            if (_serverSeekPending)
                StatusBlock.Text = $"跳转至 {FormatTime(PositionSlider.Value)}…";
            else if (_mpv.IsBuffering)
                StatusBlock.Text = "缓冲中…";
            else if (string.Equals(StatusBlock.Text, "缓冲中…", StringComparison.Ordinal))
                StatusBlock.Text = "";
        }
        catch (Exception ex)
        {
            PlaybackLog.Error("WinUIPlayerTick", ex);
        }
    }

    private async void ReportTimer_Tick(object? sender, object e)
    {
        if (_mpv is null || _disposed)
            return;

        await SafeReportAsync(() =>
            _client.ReportPlaybackProgressAsync(
                _launch,
                _lastPositionTicks,
                _mpv.IsPaused,
                _mpv.Volume));
    }

    private void ControlsTimer_Tick(object? sender, object e)
    {
        if (ControlsPanel.Visibility != Visibility.Visible)
            return;

        if (_pointerOverInteractiveOverlay)
            return;

        if (DateTime.UtcNow - _lastControlsActivityUtc >=
            TimeSpan.FromSeconds(_appSettings.PlayerControlsAutoHideSeconds))
        {
            HideControls();
        }
    }

    private void PlayerRoot_PointerMoved(object sender, PointerRoutedEventArgs e) =>
        ShowControls();

    private void InteractiveOverlay_PointerEntered(
        object sender,
        PointerRoutedEventArgs e)
    {
        _pointerOverInteractiveOverlay = true;
        ShowControls();
    }

    private void InteractiveOverlay_PointerExited(
        object sender,
        PointerRoutedEventArgs e)
    {
        _pointerOverInteractiveOverlay = false;
        _lastControlsActivityUtc = DateTime.UtcNow;
    }

    private void PlayerRoot_DoubleTapped(
        object sender,
        DoubleTappedRoutedEventArgs e)
    {
        ToggleFullscreen();
        e.Handled = true;
    }

    private void ShowControls()
    {
        if (_disposed)
            return;

        _lastControlsActivityUtc = DateTime.UtcNow;
        TopInfoPanel.Visibility = Visibility.Visible;
        ControlsPanel.Visibility = Visibility.Visible;
        StatusBlock.Visibility = Visibility.Visible;
    }

    private void HideControls()
    {
        if (_pointerOverInteractiveOverlay)
            return;

        TopInfoPanel.Visibility = Visibility.Collapsed;
        ControlsPanel.Visibility = Visibility.Collapsed;
        MoreSettingsPanel.Visibility = Visibility.Collapsed;

        if (string.IsNullOrWhiteSpace(StatusBlock.Text))
            StatusBlock.Visibility = Visibility.Collapsed;
    }

    private void VideoPanel_SizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateCompositionSize();

    private void UpdateCompositionSize()
    {
        if (_mpv is null || VideoPanel.XamlRoot is null)
            return;

        var scale = VideoPanel.XamlRoot.RasterizationScale;
        var width = Math.Max(
            1,
            (int)Math.Round(VideoPanel.ActualWidth * scale));
        var height = Math.Max(
            1,
            (int)Math.Round(VideoPanel.ActualHeight * scale));

        _mpv.SetD3D11CompositionSize(width, height);
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (_mpv is null)
            return;

        _mpv.TogglePause();
        ShowControls();
    }

    private void SeekBack_Click(object sender, RoutedEventArgs e) =>
        SeekRelative(-10);

    private void SeekForward_Click(object sender, RoutedEventArgs e) =>
        SeekRelative(10);

    private void SeekRelative(double deltaSeconds)
    {
        if (_mpv is null)
            return;

        var target = Math.Max(
            0,
            ResolveTimelinePositionSeconds() + deltaSeconds);

        _ = SeekTimelineAsync(target);
        ShowControls();
    }

    private void PositionSlider_PointerPressed(
        object sender,
        PointerRoutedEventArgs e)
    {
        if (_updatingSlider)
            return;

        _sliderSeeking = true;
        ShowControls();
    }

    private void PositionSlider_PointerReleased(
        object sender,
        PointerRoutedEventArgs e)
    {
        if (!_sliderSeeking || _mpv is null)
            return;

        _sliderSeeking = false;
        _ = SeekTimelineAsync(PositionSlider.Value);
        ShowControls();
    }

    private void PositionSlider_ValueChanged(
        object sender,
        RangeBaseValueChangedEventArgs e)
    {
        if (_updatingSlider)
            return;

        if (_sliderSeeking)
            CurrentTimeBlock.Text = FormatTime(e.NewValue);
    }

    private async Task SeekTimelineAsync(double targetSeconds)
    {
        var mpv = _mpv;
        if (mpv is null)
            return;

        targetSeconds = Math.Clamp(
            targetSeconds,
            0,
            Math.Max(targetSeconds, PositionSlider.Maximum));

        DanmakuOverlay.Reset(targetSeconds);

        if (!_launch.RequiresServerSeek)
        {
            var localTarget = Math.Max(
                0,
                targetSeconds - _timelineOffsetSeconds);
            mpv.SeekAbsolute(localTarget);
            _lastPositionTicks =
                (long)(targetSeconds * 10_000_000d);
            return;
        }

        var requestVersion = ++_seekVersion;
        var targetTicks =
            (long)Math.Round(targetSeconds * 10_000_000d);
        var wasPaused = mpv.IsPaused;
        var volume = mpv.Volume;
        var speed = mpv.Speed;

        _serverSeekPending = true;
        StatusBlock.Text = $"跳转至 {FormatTime(targetSeconds)}…";

        try
        {
            var replacement = await _client.ReopenPlayableStreamAsync(
                _launch,
                targetTicks);

            if (_disposed ||
                requestVersion != _seekVersion ||
                _mpv is null)
            {
                return;
            }

            _launch = replacement;
            _timelineOffsetSeconds = replacement.UsesServerStartOffset
                ? Math.Max(
                    0,
                    replacement.ResumePositionTicks / 10_000_000d)
                : 0;
            _lastPositionTicks = targetTicks;

            _mpv.Load(replacement.Url);
            _mpv.SetVolume(volume);
            _mpv.SetSpeed(speed);
            _mpv.SetPaused(wasPaused);

            await SafeReportAsync(() =>
                _client.ReportPlaybackStartAsync(
                    replacement,
                    targetTicks,
                    wasPaused,
                    volume));

            StatusBlock.Text = "";
        }
        catch (Exception ex)
        {
            PlaybackLog.Error("WinUIServerSeek", ex);
            StatusBlock.Text = "跳转失败，继续当前播放";
        }
        finally
        {
            if (requestVersion == _seekVersion)
                _serverSeekPending = false;
        }
    }

    private void VolumeSlider_ValueChanged(
        object sender,
        RangeBaseValueChangedEventArgs e)
    {
        if (_mpv is null)
            return;

        if (e.NewValue > 0.01)
            _lastAudibleVolume = e.NewValue;

        _mpv.SetVolume(e.NewValue);
        ShowControls();
    }

    private void Mute_Click(object sender, RoutedEventArgs e) =>
        ToggleMute();

    private void ToggleMute()
    {
        if (_mpv is null)
            return;

        if (_mpv.Volume > 0.01)
        {
            _lastAudibleVolume = _mpv.Volume;
            _mpv.SetVolume(0);
        }
        else
        {
            _mpv.SetVolume(
                _lastAudibleVolume > 0.01
                    ? _lastAudibleVolume
                    : 100);
        }

        ShowControls();
    }

    private void SpeedComboBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_mpv is null ||
            SpeedComboBox.SelectedItem is not ComboBoxItem item ||
            !double.TryParse(
                item.Tag?.ToString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var speed))
        {
            return;
        }

        _mpv.SetSpeed(speed);
        StatusBlock.Text = $"播放速度 {speed:0.##}×";
        ShowControls();
    }

    private void Audio_Click(object sender, RoutedEventArgs e)
    {
        if (_mpv is null)
            return;

        var tracks = _mpv.GetTracks()
            .Where(track => string.Equals(
                track.Type,
                "audio",
                StringComparison.OrdinalIgnoreCase))
            .ToArray();

        ShowTrackMenu(AudioButton, tracks, false);
    }

    private void Subtitle_Click(object sender, RoutedEventArgs e)
    {
        if (_mpv is null)
            return;

        var tracks = _mpv.GetTracks()
            .Where(track => string.Equals(
                track.Type,
                "sub",
                StringComparison.OrdinalIgnoreCase))
            .ToArray();

        ShowTrackMenu(SubtitleButton, tracks, true);
    }

    private void ShowTrackMenu(
        Button anchor,
        IReadOnlyList<PlayerTrack> tracks,
        bool subtitle)
    {
        var menu = new MenuFlyout();

        if (subtitle)
        {
            var off = new MenuFlyoutItem { Text = "关闭字幕" };
            off.Click += (_, _) =>
            {
                _mpv?.SetSubtitleTrack(null);
                StatusBlock.Text = "字幕已关闭";
            };
            menu.Items.Add(off);
            if (tracks.Count > 0)
                menu.Items.Add(new MenuFlyoutSeparator());
        }

        if (tracks.Count == 0)
        {
            menu.Items.Add(new MenuFlyoutItem
            {
                Text = subtitle ? "没有可用字幕" : "没有可用音轨",
                IsEnabled = false
            });
        }
        else
        {
            foreach (var track in tracks)
            {
                var item = new MenuFlyoutItem
                {
                    Text = $"{(track.Selected ? "✓  " : "")}{FormatTrackLabel(track)}"
                };

                item.Click += (_, _) =>
                {
                    if (_mpv is null)
                        return;

                    if (subtitle)
                    {
                        _mpv.SetSubtitleTrack(track.Id);
                        StatusBlock.Text =
                            $"字幕：{FormatTrackLabel(track)}";
                    }
                    else
                    {
                        _mpv.SetAudioTrack(track.Id);
                        StatusBlock.Text =
                            $"音轨：{FormatTrackLabel(track)}";
                    }

                    ShowControls();
                };

                menu.Items.Add(item);
            }
        }

        menu.ShowAt(anchor);
        ShowControls();
    }

    private void MoreSettings_Click(object sender, RoutedEventArgs e)
    {
        MoreSettingsPanel.Visibility =
            MoreSettingsPanel.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
        ShowControls();
    }

    private void Diagnostics_Click(object sender, RoutedEventArgs e) =>
        ToggleDiagnostics();

    private void ToggleDiagnostics()
    {
        _diagnosticsVisible = !_diagnosticsVisible;
        DiagnosticsPanel.Visibility = _diagnosticsVisible
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (_diagnosticsVisible && _mpv is not null)
            RefreshDiagnostics(_mpv.GetDiagnosticSnapshot());

        ShowControls();
    }

    private void RefreshDiagnostics(MpvDiagnosticSnapshot snapshot)
    {
        DiagnosticsMethodBlock.Text =
            $"{_launch.PlayMethod} · {_launch.DecisionReason}";

        var video = new List<string>();
        if (!string.IsNullOrWhiteSpace(snapshot.VideoCodec))
            video.Add(snapshot.VideoCodec.ToUpperInvariant());
        if (snapshot.Width is > 0 && snapshot.Height is > 0)
            video.Add($"{snapshot.Width}×{snapshot.Height}");
        if (snapshot.Fps is > 0)
            video.Add($"{snapshot.Fps:0.##} fps");
        if (!string.IsNullOrWhiteSpace(snapshot.HwdecCurrent))
            video.Add(snapshot.HwdecCurrent);

        DiagnosticsVideoBlock.Text =
            video.Count == 0 ? "--" : string.Join(" · ", video);

        var audio = new List<string>();
        if (!string.IsNullOrWhiteSpace(snapshot.AudioCodec))
            audio.Add(snapshot.AudioCodec.ToUpperInvariant());
        if (snapshot.AudioBitrate is > 0)
            audio.Add($"{snapshot.AudioBitrate.Value / 1000d:0} kbps");
        if (!string.IsNullOrWhiteSpace(snapshot.AudioTrackId))
            audio.Add($"track {snapshot.AudioTrackId}");

        DiagnosticsAudioBlock.Text =
            audio.Count == 0 ? "--" : string.Join(" · ", audio);

        DiagnosticsCacheBlock.Text =
            $"{Math.Max(0, snapshot.CacheSeconds ?? 0):0.0}s · " +
            $"{FormatNetworkSpeed(snapshot.NetworkSpeedBytesPerSecond)}" +
            (snapshot.Buffering ? " · buffering" : "");

        DiagnosticsDanmakuBlock.Text =
            _danmakuVisible
                ? $"{DanmakuOverlay.LoadedCount} 条 · 活跃 {DanmakuOverlay.ActiveCount}"
                : "关闭";
    }

    private async void EpisodeList_Click(object sender, RoutedEventArgs e)
    {
        var episodes = await EnsureSeasonEpisodesAsync();
        if (episodes.Count == 0)
            return;

        var menu = new MenuFlyout();

        foreach (var episode in episodes)
        {
            var code = BuildEpisodeCode(
                episode.ParentIndexNumber,
                episode.IndexNumber);
            var label = string.IsNullOrWhiteSpace(code)
                ? episode.Name
                : $"{code} · {episode.Name}";

            var item = new MenuFlyoutItem
            {
                Text =
                    $"{(string.Equals(episode.Id, _launch.ItemId, StringComparison.OrdinalIgnoreCase) ? "✓  " : "")}{label}"
            };

            item.Click += (_, _) =>
                EpisodeRequested?.Invoke(this, episode);

            menu.Items.Add(item);
        }

        menu.ShowAt(EpisodeListButton);
        ShowControls();
    }

    private async void PreviousEpisode_Click(
        object sender,
        RoutedEventArgs e) =>
        await NavigateEpisodeAsync(-1);

    private async void NextEpisode_Click(
        object sender,
        RoutedEventArgs e) =>
        await NavigateEpisodeAsync(1);

    private async Task NavigateEpisodeAsync(int offset)
    {
        var episodes = await EnsureSeasonEpisodesAsync();
        if (episodes.Count == 0)
            return;

        var currentIndex = -1;
        for (var index = 0; index < episodes.Count; index++)
        {
            if (string.Equals(
                    episodes[index].Id,
                    _launch.ItemId,
                    StringComparison.OrdinalIgnoreCase))
            {
                currentIndex = index;
                break;
            }
        }

        if (currentIndex < 0 && _launch.EpisodeNumber is int episodeNumber)
        {
            for (var index = 0; index < episodes.Count; index++)
            {
                if (episodes[index].IndexNumber == episodeNumber)
                {
                    currentIndex = index;
                    break;
                }
            }
        }

        if (currentIndex < 0)
            return;

        var target = currentIndex + offset;
        if (target < 0 || target >= episodes.Count)
            return;

        EpisodeRequested?.Invoke(this, episodes[target]);
    }

    private async Task<IReadOnlyList<EmbyItem>> EnsureSeasonEpisodesAsync()
    {
        if (_seasonEpisodes is not null)
            return _seasonEpisodes;

        if (!HasEpisodeNavigation(_launch))
            return Array.Empty<EmbyItem>();

        try
        {
            var seasons = await _client.GetSeasonsAsync(_launch.SeriesId);
            var season = seasons.FirstOrDefault(item =>
                item.IndexNumber == _launch.SeasonNumber)
                ?? seasons.FirstOrDefault();

            if (season is null)
                return Array.Empty<EmbyItem>();

            _seasonEpisodes = await _client.GetEpisodesAsync(
                _launch.SeriesId,
                season.Id);

            UpdateEpisodeNavigationButtons();
            return _seasonEpisodes;
        }
        catch (Exception ex)
        {
            PlaybackLog.Error("WinUIEpisodeList", ex);
            StatusBlock.Text = UserError.GetMessage(ex, "加载选集");
            return Array.Empty<EmbyItem>();
        }
    }

    private void UpdateEpisodeNavigationButtons()
    {
        if (_seasonEpisodes is null || _seasonEpisodes.Count == 0)
            return;

        var index = _seasonEpisodes
            .Select((item, i) => new { item, i })
            .FirstOrDefault(x =>
                string.Equals(
                    x.item.Id,
                    _launch.ItemId,
                    StringComparison.OrdinalIgnoreCase))
            ?.i ?? -1;

        PreviousEpisodeButton.IsEnabled = index > 0;
        NextEpisodeButton.IsEnabled =
            index >= 0 && index + 1 < _seasonEpisodes.Count;
    }

    private void Fullscreen_Click(object sender, RoutedEventArgs e) =>
        ToggleFullscreen();

    private void ToggleFullscreen()
    {
        _isFullscreen = !_isFullscreen;
        FullscreenButton.Content = _isFullscreen
            ? "退出全屏"
            : "全屏";
        FullscreenRequested?.Invoke(this, _isFullscreen);
        ShowControls();
    }

    private async void Danmaku_Click(object sender, RoutedEventArgs e) =>
        await ToggleDanmakuAsync();

    private async Task ToggleDanmakuAsync()
    {
        if (!_danmakuVisible &&
            DanmakuOverlay.LoadedCount == 0 &&
            !string.IsNullOrWhiteSpace(_danmakuSourceSettings.LogVarBaseUrl))
        {
            await LoadDanmakuAsync();
        }

        _danmakuVisible = !_danmakuVisible;
        DanmakuOverlay.Visibility = _danmakuVisible
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (_danmakuVisible)
            DanmakuOverlay.Reset(ResolveTimelinePositionSeconds());

        DanmakuButton.Content = _danmakuVisible
            ? "弹幕 ✓"
            : "弹幕";

        StatusBlock.Text = _danmakuVisible
            ? $"弹幕已开启 · {DanmakuOverlay.LoadedCount} 条"
            : "弹幕已关闭";

        ShowControls();
    }

    private async Task LoadDanmakuAsync()
    {
        _danmakuLoadCts.Cancel();
        _danmakuLoadCts.Dispose();
        _danmakuLoadCts = new CancellationTokenSource();

        try
        {
            var document = await _danmakuService.LoadAsync(
                CreateDanmakuContext(),
                _danmakuSourceSettings,
                _danmakuLoadCts.Token);

            if (_disposed)
                return;

            DanmakuOverlay.SetDocument(document);
            DanmakuOverlay.ApplySettings(_danmakuSettings);
            BuildDanmakuHeatmap(document);

            if (document.Comments.Count == 0)
                StatusBlock.Text = $"弹幕：{document.SourceName} 未匹配到弹幕";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            PlaybackLog.Error("WinUIDanmakuLoad", ex);
            StatusBlock.Text = $"弹幕加载失败：{ex.Message}";
        }
    }

    private void BuildDanmakuHeatmap(DanmakuDocument document)
    {
        DanmakuHeatmapCanvas.Children.Clear();

        var duration = ResolveDurationSeconds();
        if (duration <= 0 || document.Comments.Count == 0)
            return;

        const int bins = 96;
        var counts = new int[bins];

        foreach (var comment in document.Comments)
        {
            var index = Math.Clamp(
                (int)(comment.TimeSeconds / duration * bins),
                0,
                bins - 1);
            counts[index]++;
        }

        var max = Math.Max(1, counts.Max());

        void Draw()
        {
            DanmakuHeatmapCanvas.Children.Clear();
            var width = DanmakuHeatmapCanvas.ActualWidth;
            if (width <= 1)
                return;

            var binWidth = width / bins;

            for (var index = 0; index < bins; index++)
            {
                if (counts[index] == 0)
                    continue;

                var height = 3 + 12d * counts[index] / max;
                var rect = new Rectangle
                {
                    Width = Math.Max(1, binWidth),
                    Height = height,
                    Fill = new SolidColorBrush(
                        Windows.UI.Color.FromArgb(
                            150,
                            88,
                            166,
                            255))
                };

                Canvas.SetLeft(rect, index * binWidth);
                Canvas.SetTop(rect, (18 - height) / 2);
                DanmakuHeatmapCanvas.Children.Add(rect);
            }
        }

        Draw();
        DanmakuHeatmapCanvas.SizeChanged += (_, _) => Draw();
    }

    private async void DanmakuSettings_Click(
        object sender,
        RoutedEventArgs e)
    {
        var fontSize = new NumberBox
        {
            Header = "字号",
            Minimum = 12,
            Maximum = 48,
            Value = _danmakuSettings.FontSize
        };
        var speed = new NumberBox
        {
            Header = "速度",
            Minimum = 0.25,
            Maximum = 4,
            SmallChange = 0.25,
            Value = _danmakuSettings.Speed
        };
        var opacity = new NumberBox
        {
            Header = "透明度",
            Minimum = 0.1,
            Maximum = 1,
            SmallChange = 0.05,
            Value = _danmakuSettings.Opacity
        };
        var heightRatio = new NumberBox
        {
            Header = "显示区域",
            Minimum = 0.25,
            Maximum = 1,
            SmallChange = 0.05,
            Value = _danmakuSettings.ScreenHeightRatio
        };
        var density = new NumberBox
        {
            Header = "密度",
            Minimum = 0.25,
            Maximum = 1,
            SmallChange = 0.05,
            Value = _danmakuSettings.DensityRatio
        };
        var maxActive = new NumberBox
        {
            Header = "同时显示上限",
            Minimum = 10,
            Maximum = 300,
            SmallChange = 10,
            Value = _danmakuSettings.MaxActiveComments
        };
        var overlap = new CheckBox
        {
            Content = "防重叠",
            IsChecked = _danmakuSettings.AvoidOverlap
        };
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(fontSize);
        panel.Children.Add(speed);
        panel.Children.Add(opacity);
        panel.Children.Add(heightRatio);
        panel.Children.Add(density);
        panel.Children.Add(maxActive);
        panel.Children.Add(overlap);

        var dialog = new ContentDialog
        {
            XamlRoot = PlayerRoot.XamlRoot,
            Title = "弹幕设置",
            Content = new ScrollViewer
            {
                Content = panel,
                MaxHeight = 560
            },
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        _danmakuSettings = _danmakuSettings with
        {
            FontSize = fontSize.Value,
            Speed = speed.Value,
            Opacity = opacity.Value,
            ScreenHeightRatio = heightRatio.Value,
            DensityRatio = density.Value,
            MaxActiveComments = (int)Math.Round(maxActive.Value),
            AvoidOverlap = overlap.IsChecked == true
        };

        DanmakuSettingsStore.Save(_danmakuSettings);
        DanmakuOverlay.ApplySettings(_danmakuSettings);
        StatusBlock.Text = "弹幕设置已保存";
        ShowControls();
    }

    private async void DanmakuFilter_Click(
        object sender,
        RoutedEventArgs e)
    {
        var dialog = new DanmakuFilterDialog(_danmakuSettings)
        {
            XamlRoot = PlayerRoot.XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary ||
            dialog.Result is null)
        {
            return;
        }

        _danmakuSettings = dialog.Result;
        DanmakuSettingsStore.Save(_danmakuSettings);
        DanmakuOverlay.ApplySettings(_danmakuSettings);

        StatusBlock.Text =
            $"弹幕过滤已更新 · {_danmakuSettings.BlockedWords.Count} 词 / " +
            $"{_danmakuSettings.BlockedUsers.Count} 用户";
        ShowControls();
    }

    private async void DanmakuSource_Click(
        object sender,
        RoutedEventArgs e)
    {
        var dialog = new DanmakuSourceSettingsDialog(
            _danmakuSourceSettings)
        {
            XamlRoot = PlayerRoot.XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary ||
            dialog.Result is null)
        {
            return;
        }

        _danmakuSourceSettings = dialog.Result;
        DanmakuSourceSettingsStore.Save(_danmakuSourceSettings);

        DanmakuOverlay.SetDocument(
            new DanmakuDocument(
                "LogVar",
                Array.Empty<DanmakuComment>()));

        StatusBlock.Text = "LogVar：正在重新匹配弹幕…";
        await LoadDanmakuAsync();

        if (_danmakuVisible)
            DanmakuOverlay.Reset(ResolveTimelinePositionSeconds());

        StatusBlock.Text = "LogVar 设置已保存";
        ShowControls();
    }

    private async void DanmakuMatch_Click(
        object sender,
        RoutedEventArgs e)
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

        var dialog = new DanmakuMatchDialog(
            _danmakuService,
            context,
            _danmakuSourceSettings,
            currentBinding)
        {
            XamlRoot = PlayerRoot.XamlRoot
        };

        await dialog.ShowAsync();

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

        await LoadDanmakuAsync();

        if (_danmakuVisible)
            DanmakuOverlay.Reset(ResolveTimelinePositionSeconds());

        ShowControls();
    }

    private DanmakuContext CreateDanmakuContext()
    {
        var duration = _launch.RunTimeTicks is > 0
            ? _launch.RunTimeTicks.Value / 10_000_000d
            : ResolveDurationSeconds();

        return new DanmakuContext(
            _launch.ItemId,
            _launch.Title,
            duration,
            _launch.SeriesId,
            _launch.SeriesName,
            _launch.SeasonNumber,
            _launch.EpisodeNumber,
            _launch.OriginalTitle,
            _launch.ItemType,
            _launch.SourcePath,
            _launch.SourceFileName);
    }

    private void Back_Click(object sender, RoutedEventArgs e) =>
        BackRequested?.Invoke(this, EventArgs.Empty);

    private void PlayerPocView_KeyDown(
        object sender,
        KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case Windows.System.VirtualKey.Space:
                _mpv?.TogglePause();
                e.Handled = true;
                break;

            case Windows.System.VirtualKey.Left:
                SeekRelative(-10);
                e.Handled = true;
                break;

            case Windows.System.VirtualKey.Right:
                SeekRelative(10);
                e.Handled = true;
                break;

            case Windows.System.VirtualKey.Up when _mpv is not null:
                VolumeSlider.Value = Math.Min(100, _mpv.Volume + 5);
                e.Handled = true;
                break;

            case Windows.System.VirtualKey.Down when _mpv is not null:
                VolumeSlider.Value = Math.Max(0, _mpv.Volume - 5);
                e.Handled = true;
                break;

            case Windows.System.VirtualKey.M:
                ToggleMute();
                e.Handled = true;
                break;

            case Windows.System.VirtualKey.D:
                _ = ToggleDanmakuAsync();
                e.Handled = true;
                break;

            case Windows.System.VirtualKey.I:
                ToggleDiagnostics();
                e.Handled = true;
                break;

            case Windows.System.VirtualKey.F:
            case Windows.System.VirtualKey.F11:
                ToggleFullscreen();
                e.Handled = true;
                break;

            case Windows.System.VirtualKey.Escape when _isFullscreen:
                ToggleFullscreen();
                e.Handled = true;
                break;
        }

        ShowControls();
    }

    private void Mpv_PlaybackEnded(
        object? sender,
        MpvPlaybackEndedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_disposed)
                return;

            StatusBlock.Text = e.IsError
                ? $"播放源结束：{e.ReasonName} / {e.ErrorText}"
                : "播放结束";
            ShowControls();
        });
    }

    private async void PlayerPocView_Unloaded(
        object sender,
        RoutedEventArgs e)
    {
        await ShutdownAsync();
    }

    private async Task ShutdownAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        _uiTimer.Stop();
        _reportTimer.Stop();
        _controlsTimer.Stop();

        _danmakuLoadCts.Cancel();
        _danmakuLoadCts.Dispose();

        if (_isFullscreen)
        {
            _isFullscreen = false;
            FullscreenRequested?.Invoke(this, false);
        }

        var mpv = _mpv;
        _mpv = null;

        if (mpv is not null)
        {
            mpv.PlaybackEnded -= Mpv_PlaybackEnded;

            try
            {
                await SafeReportAsync(() =>
                    _client.ReportPlaybackStoppedAsync(
                        _launch,
                        _lastPositionTicks,
                        mpv.IsPaused,
                        mpv.Volume));
            }
            finally
            {
                try
                {
                    _swapChainPanel.Detach();
                }
                catch (Exception ex)
                {
                    PlaybackLog.Error(
                        "WinUISwapChainDetach",
                        ex);
                }

                try
                {
                    mpv.Stop();
                }
                catch (Exception ex)
                {
                    PlaybackLog.Error(
                        "WinUIMpvStop",
                        ex);
                }

                mpv.Dispose();
            }
        }

        _swapChainPanel.Dispose();
    }

    private double ResolveTimelinePositionSeconds()
    {
        if (_mpv is null)
            return _lastPositionTicks / 10_000_000d;

        return _timelineOffsetSeconds +
               Math.Max(0, _mpv.PositionSeconds);
    }

    private double ResolveDurationSeconds()
    {
        if (_launch.RunTimeTicks is > 0)
            return _launch.RunTimeTicks.Value / 10_000_000d;

        return _timelineOffsetSeconds +
               Math.Max(0, _mpv?.DurationSeconds ?? 0);
    }

    private static bool HasEpisodeNavigation(
        PlaybackLaunch launch) =>
        !string.IsNullOrWhiteSpace(launch.SeriesId) &&
        launch.EpisodeNumber is > 0;

    private static string BuildEpisodeSubtitle(
        PlaybackLaunch launch)
    {
        var code = BuildEpisodeCode(
            launch.SeasonNumber,
            launch.EpisodeNumber);

        var title = launch.Title?.Trim() ?? "";

        if (!string.IsNullOrWhiteSpace(code))
        {
            while (title.StartsWith(
                       code,
                       StringComparison.OrdinalIgnoreCase))
            {
                title = title[code.Length..]
                    .Trim(' ', '·', '-', '—', ':', '：');
            }

            return string.IsNullOrWhiteSpace(title)
                ? code
                : $"{code} · {title}";
        }

        if (!string.IsNullOrWhiteSpace(launch.OriginalTitle) &&
            !string.Equals(
                launch.OriginalTitle,
                launch.Title,
                StringComparison.OrdinalIgnoreCase))
        {
            return launch.OriginalTitle;
        }

        return "";
    }

    private static string BuildEpisodeCode(
        int? season,
        int? episode)
    {
        if (season is > 0 && episode is > 0)
            return $"S{season:00}E{episode:00}";

        if (episode is > 0)
            return $"E{episode:00}";

        return "";
    }

    private static string FormatTrackLabel(PlayerTrack track)
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

        if (string.Equals(
                track.Type,
                "audio",
                StringComparison.OrdinalIgnoreCase))
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

        return parts.Count == 0
            ? $"Track {track.Id}"
            : string.Join(" · ", parts);
    }

    private static string FormatResolutionLabel(
        int? width,
        int? height)
    {
        if (width is not > 0 || height is not > 0)
            return "--";

        if (height >= 2160 || width >= 3840)
            return "4K";
        if (height >= 1440)
            return "1440p";
        if (height >= 1080)
            return "1080p";
        if (height >= 720)
            return "720p";

        return $"{height}p";
    }

    private static string FormatNetworkSpeed(double? bytesPerSecond)
    {
        if (bytesPerSecond is not > 0)
            return "-- MB/s";

        if (bytesPerSecond >= 1024 * 1024)
            return $"{bytesPerSecond.Value / 1024d / 1024d:0.0} MB/s";

        return $"{bytesPerSecond.Value / 1024d:0} KB/s";
    }

    private static string FormatTime(double seconds)
    {
        if (double.IsNaN(seconds) ||
            double.IsInfinity(seconds) ||
            seconds < 0)
        {
            seconds = 0;
        }

        var time = TimeSpan.FromSeconds(seconds);
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{time.Minutes:00}:{time.Seconds:00}";
    }

    private static async Task SafeReportAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            PlaybackLog.Error(
                "WinUIPlaybackReport",
                ex);
        }
    }
}
