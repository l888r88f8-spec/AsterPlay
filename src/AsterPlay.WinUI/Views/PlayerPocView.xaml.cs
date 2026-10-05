using AsterPlay.Models;
using AsterPlay.Services;
using AsterPlay.Services.Mpv;
using AsterPlay.WinUI.Interop;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace AsterPlay.WinUI.Views;

public sealed partial class PlayerPocView : UserControl
{
    private readonly EmbyClient _client;
    private PlaybackLaunch _launch;
    private readonly SwapChainPanelInterop _swapChainPanel = new();
    private readonly DispatcherTimer _uiTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _reportTimer = new() { Interval = TimeSpan.FromSeconds(10) };

    private MpvClient? _mpv;
    private bool _loaded;
    private bool _disposed;
    private bool _updatingSlider;
    private double _timelineOffsetSeconds;
    private long _lastPositionTicks;

    public event EventHandler? BackRequested;

    public PlayerPocView(EmbyClient client, PlaybackLaunch launch)
    {
        _client = client;
        _launch = launch;

        InitializeComponent();

        TitleBlock.Text = launch.Title;
        RouteBlock.Text = BuildRouteLabel(launch);

        _uiTimer.Tick += UiTimer_Tick;
        _reportTimer.Tick += ReportTimer_Tick;
    }

    private async void PlayerPocView_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loaded || _disposed)
            return;

        _loaded = true;
        Focus(FocusState.Programmatic);

        try
        {
            StatusBlock.Text = "正在创建 libmpv D3D11 composition 输出…";

            _mpv = new MpvClient(MpvVideoOutputMode.D3D11Composition);
            _mpv.PlaybackEnded += Mpv_PlaybackEnded;

            UpdateCompositionSize();

            var resumeSeconds = _launch.ResumePositionTicks / 10_000_000d;
            _timelineOffsetSeconds = _launch.UsesServerStartOffset ? resumeSeconds : 0;
            var mpvStartSeconds = _launch.UsesServerStartOffset ? 0 : resumeSeconds;

            _mpv.Load(_launch.Url, mpvStartSeconds);
            VolumeSlider.Value = Math.Clamp(_mpv.Volume, 0, 100);

            _uiTimer.Start();
            _reportTimer.Start();

            await SafeReportAsync(() =>
                _client.ReportPlaybackStartAsync(
                    _launch,
                    _launch.ResumePositionTicks,
                    false,
                    _mpv.Volume));

            StatusBlock.Text = "等待 mpv 创建 D3D11 composition swap chain…";
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
                StatusBlock.Text = "D3D11 composition swap chain 已连接到 WinUI SwapChainPanel。";
                PlaybackLog.Write(
                    "WinUIPlayer",
                    $"Attached mpv display-swapchain=0x{swapChain.ToInt64():X}");
            }

            var duration = ResolveDurationSeconds();
            var position = ResolveTimelinePositionSeconds();

            _lastPositionTicks = (long)Math.Max(0, position * 10_000_000d);

            _updatingSlider = true;
            PositionSlider.Maximum = Math.Max(1, duration);
            PositionSlider.Value = Math.Clamp(position, 0, PositionSlider.Maximum);
            _updatingSlider = false;

            TimeBlock.Text = $"{FormatTime(position)} / {FormatTime(duration)}";
            PlayPauseButton.Content = _mpv.IsPaused ? "播放" : "暂停";

            if (_mpv.IsBuffering)
                StatusBlock.Text = "缓冲中…";
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

    private void VideoPanel_SizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateCompositionSize();

    private void UpdateCompositionSize()
    {
        if (_mpv is null || VideoPanel.XamlRoot is null)
            return;

        var scale = VideoPanel.XamlRoot.RasterizationScale;
        var width = Math.Max(1, (int)Math.Round(VideoPanel.ActualWidth * scale));
        var height = Math.Max(1, (int)Math.Round(VideoPanel.ActualHeight * scale));

        _mpv.SetD3D11CompositionSize(width, height);
        PlaybackLog.Write("WinUIPlayer", $"composition size={width}x{height}, scale={scale:0.###}");
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        _mpv?.TogglePause();
    }

    private void SeekBack_Click(object sender, RoutedEventArgs e) =>
        SeekRelative(-10);

    private void SeekForward_Click(object sender, RoutedEventArgs e) =>
        SeekRelative(10);

    private void SeekRelative(double deltaSeconds)
    {
        if (_mpv is null)
            return;

        var target = Math.Max(0, ResolveTimelinePositionSeconds() + deltaSeconds);
        SeekTimeline(target);
    }

    private async void PositionSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_updatingSlider ||
            _mpv is null ||
            Math.Abs(e.NewValue - ResolveTimelinePositionSeconds()) < 2)
        {
            return;
        }

        // Slider changes can be frequent while dragging. For the POC, only
        // perform a seek when the difference is meaningful.
        await Task.Delay(1);
        SeekTimeline(e.NewValue);
    }

    private void SeekTimeline(double targetSeconds)
    {
        if (_mpv is null)
            return;

        if (_launch.UsesServerStartOffset)
        {
            var relative = Math.Max(0, targetSeconds - _timelineOffsetSeconds);
            _mpv.SeekAbsolute(relative);
        }
        else
        {
            _mpv.SeekAbsolute(targetSeconds);
        }

        _lastPositionTicks = (long)(targetSeconds * 10_000_000d);
    }

    private void VolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_mpv is null)
            return;

        _mpv.SetVolume(e.NewValue);
    }

    private void Back_Click(object sender, RoutedEventArgs e) =>
        BackRequested?.Invoke(this, EventArgs.Empty);

    private void PlayerPocView_KeyDown(object sender, KeyRoutedEventArgs e)
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
        }
    }

    private void Mpv_PlaybackEnded(object? sender, MpvPlaybackEndedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_disposed)
                return;

            StatusBlock.Text = e.IsError
                ? $"播放源结束：{e.ReasonName} / {e.ErrorText}"
                : "播放结束";
        });
    }

    private async void PlayerPocView_Unloaded(object sender, RoutedEventArgs e)
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
                try { _swapChainPanel.Detach(); }
                catch (Exception ex) { PlaybackLog.Error("WinUISwapChainDetach", ex); }

                try { mpv.Stop(); }
                catch (Exception ex) { PlaybackLog.Error("WinUIMpvStop", ex); }

                mpv.Dispose();
            }
        }

        _swapChainPanel.Dispose();
    }

    private double ResolveTimelinePositionSeconds()
    {
        if (_mpv is null)
            return _lastPositionTicks / 10_000_000d;

        return _timelineOffsetSeconds + Math.Max(0, _mpv.PositionSeconds);
    }

    private double ResolveDurationSeconds()
    {
        if (_launch.RunTimeTicks is > 0)
            return _launch.RunTimeTicks.Value / 10_000_000d;

        return _timelineOffsetSeconds + Math.Max(0, _mpv?.DurationSeconds ?? 0);
    }

    private static string BuildRouteLabel(PlaybackLaunch launch)
    {
        var details = new List<string> { launch.PlayMethod };

        if (!string.IsNullOrWhiteSpace(launch.NegotiatedContainer))
            details.Add(launch.NegotiatedContainer.ToUpperInvariant());

        if (!string.IsNullOrWhiteSpace(launch.SourceVideoCodec))
            details.Add(launch.SourceVideoCodec.ToUpperInvariant());

        return string.Join(" · ", details);
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

    private static async Task SafeReportAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            PlaybackLog.Error("WinUIPlaybackReport", ex);
        }
    }
}
