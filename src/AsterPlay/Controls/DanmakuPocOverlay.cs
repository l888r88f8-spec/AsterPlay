using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace AsterPlay.Controls;

public sealed class DanmakuPocOverlay : FrameworkElement
{
    private const double SpawnIntervalSeconds = 0.14;
    private const double LifetimeSeconds = 7.0;
    private const double TopPadding = 24;
    private const double LaneHeight = 30;
    private const double FontSize = 22;

    private static readonly Typeface Typeface = new(
        new FontFamily("Segoe UI"),
        FontStyles.Normal,
        FontWeights.SemiBold,
        FontStretches.Normal);

    private static readonly Brush[] Palette =
    [
        Brushes.White,
        Brushes.Gold,
        Brushes.DeepSkyBlue,
        Brushes.LightGreen,
        Brushes.Violet
    ];

    private readonly Dictionary<long, FormattedText> _textCache = [];

    private bool _active;
    private bool _paused = true;
    private double _sampleTimelineSeconds;
    private double _speed = 1;
    private long _sampleTimestamp = Stopwatch.GetTimestamp();
    private double _cachedPixelsPerDip;
    private int _activeCount;
    private long _renderCount;
    private double _lastFrameMilliseconds;
    private double _peakFrameMilliseconds;
    private double _lastTimelineSeconds;

    public DanmakuPocOverlay()
    {
        IsHitTestVisible = false;
        Focusable = false;
        ClipToBounds = true;
        Visibility = Visibility.Collapsed;
        SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
    }

    public void SetActive(bool active)
    {
        if (_active == active)
            return;

        _active = active;

        if (active)
        {
            _activeCount = 0;
            _renderCount = 0;
            _lastFrameMilliseconds = 0;
            _peakFrameMilliseconds = 0;
            Visibility = Visibility.Visible;
            CompositionTarget.Rendering += CompositionTarget_Rendering;
        }
        else
        {
            CompositionTarget.Rendering -= CompositionTarget_Rendering;
            Visibility = Visibility.Collapsed;
            _activeCount = 0;
            _textCache.Clear();
        }

        InvalidateVisual();
    }

    public void Synchronize(double timelineSeconds, bool paused, double speed)
    {
        if (!double.IsFinite(timelineSeconds))
            timelineSeconds = 0;

        if (!double.IsFinite(speed))
            speed = 1;

        _sampleTimelineSeconds = Math.Max(0, timelineSeconds);
        _paused = paused;
        _speed = Math.Clamp(speed, 0.25, 4);
        _sampleTimestamp = Stopwatch.GetTimestamp();
    }

    public DanmakuPocMetrics GetMetrics()
    {
        var dpi = VisualTreeHelper.GetDpi(this);

        return new DanmakuPocMetrics(
            _activeCount,
            _lastFrameMilliseconds,
            _peakFrameMilliseconds,
            ActualWidth,
            ActualHeight,
            dpi.PixelsPerInchX,
            dpi.PixelsPerInchY,
            _lastTimelineSeconds);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        if (!_active || RenderSize.Width <= 0 || RenderSize.Height <= 0)
            return;

        var frameStarted = Stopwatch.GetTimestamp();
        var timelineSeconds = GetInterpolatedTimelineSeconds();
        _lastTimelineSeconds = timelineSeconds;

        var dpi = VisualTreeHelper.GetDpi(this);
        if (Math.Abs(_cachedPixelsPerDip - dpi.PixelsPerDip) > 0.001)
        {
            _cachedPixelsPerDip = dpi.PixelsPerDip;
            _textCache.Clear();
        }

        var width = RenderSize.Width;
        var height = RenderSize.Height;
        var usableHeight = Math.Max(LaneHeight, height * 0.72);
        var laneCount = Math.Max(
            1,
            Math.Min(16, (int)Math.Floor(Math.Max(LaneHeight, usableHeight - TopPadding) / LaneHeight)));

        var firstSlot = Math.Max(
            0,
            (long)Math.Floor((timelineSeconds - LifetimeSeconds) / SpawnIntervalSeconds));
        var lastSlot = Math.Max(
            0,
            (long)Math.Floor(timelineSeconds / SpawnIntervalSeconds));

        var activeCount = 0;

        for (var slot = firstSlot; slot <= lastSlot; slot++)
        {
            var bornAt = slot * SpawnIntervalSeconds;
            var age = timelineSeconds - bornAt;
            if (age < 0 || age > LifetimeSeconds)
                continue;

            var text = GetOrCreateText(slot, bornAt, dpi.PixelsPerDip);
            var progress = age / LifetimeSeconds;
            var startX = width + 32;
            var endX = -text.WidthIncludingTrailingWhitespace - 32;
            var x = startX + ((endX - startX) * progress);
            var lane = (int)((slot * 17 + 11) % laneCount);
            var y = TopPadding + (lane * LaneHeight);

            drawingContext.DrawText(text, new Point(x, y));
            activeCount++;
        }

        _activeCount = activeCount;
        _renderCount++;

        if ((_renderCount % 120) == 0 && _textCache.Count > 96)
        {
            var keepFrom = Math.Max(0, firstSlot - 4);
            var keepTo = lastSlot + 4;

            foreach (var key in _textCache.Keys
                         .Where(key => key < keepFrom || key > keepTo)
                         .ToArray())
            {
                _textCache.Remove(key);
            }
        }

        _lastFrameMilliseconds = ElapsedMilliseconds(frameStarted);
        if (_lastFrameMilliseconds > _peakFrameMilliseconds)
            _peakFrameMilliseconds = _lastFrameMilliseconds;
    }

    private void CompositionTarget_Rendering(object? sender, EventArgs e)
    {
        if (_active)
            InvalidateVisual();
    }

    private double GetInterpolatedTimelineSeconds()
    {
        if (_paused)
            return _sampleTimelineSeconds;

        var elapsedSeconds =
            (Stopwatch.GetTimestamp() - _sampleTimestamp) / (double)Stopwatch.Frequency;

        return Math.Max(0, _sampleTimelineSeconds + (elapsedSeconds * _speed));
    }

    private FormattedText GetOrCreateText(long slot, double bornAt, double pixelsPerDip)
    {
        if (_textCache.TryGetValue(slot, out var cached))
            return cached;

        var text = (slot % 5) switch
        {
            0 => $"AsterPlay PoC · {FormatTimeline(bornAt)} · WPF Overlay",
            1 => $"Render API + WPF · #{slot:0000}",
            2 => $"同步测试 · {FormatTimeline(bornAt)} · Seek / Pause / Speed",
            3 => $"DPI / Resize / Fullscreen · #{slot:0000}",
            _ => $"鼠标穿透性能测试 · {FormatTimeline(bornAt)}"
        };

        var brush = Palette[(int)(slot % Palette.Length)];
        var formatted = new FormattedText(
            text,
            CultureInfo.GetCultureInfo("zh-CN"),
            FlowDirection.LeftToRight,
            Typeface,
            FontSize,
            brush,
            pixelsPerDip);

        _textCache[slot] = formatted;
        return formatted;
    }

    private static string FormatTimeline(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}.{time.Milliseconds / 100}"
            : $"{time.Minutes:00}:{time.Seconds:00}.{time.Milliseconds / 100}";
    }

    private static double ElapsedMilliseconds(long startedAt) =>
        (Stopwatch.GetTimestamp() - startedAt) * 1000d / Stopwatch.Frequency;
}

public readonly record struct DanmakuPocMetrics(
    int ActiveCount,
    double LastFrameMilliseconds,
    double PeakFrameMilliseconds,
    double Width,
    double Height,
    double DpiX,
    double DpiY,
    double TimelineSeconds);
