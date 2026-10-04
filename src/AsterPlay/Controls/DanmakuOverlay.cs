using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using AsterPlay.Models.Danmaku;

namespace AsterPlay.Controls;

public sealed class DanmakuOverlay : FrameworkElement
{
    private const double TopPadding = 24;
    private const double BottomPadding = 26;
    private const double LaneGap = 6;

    private static readonly Typeface Typeface = new(
        new FontFamily("Segoe UI"),
        FontStyles.Normal,
        FontWeights.SemiBold,
        FontStretches.Normal);

    private readonly Dictionary<string, FormattedText> _textCache = [];
    private IReadOnlyList<DanmakuComment> _comments = Array.Empty<DanmakuComment>();
    private DanmakuSettings _settings = new();

    private bool _active;
    private bool _suppressed;
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
    private string _sourceName = "未加载";

    public DanmakuOverlay()
    {
        IsHitTestVisible = false;
        Focusable = false;
        ClipToBounds = true;
        Visibility = Visibility.Collapsed;
        SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
    }

    public void SetDocument(DanmakuDocument document)
    {
        _sourceName = document.SourceName;
        _comments = document.Comments
            .Where(comment =>
                double.IsFinite(comment.TimeSeconds) &&
                comment.TimeSeconds >= 0 &&
                !string.IsNullOrWhiteSpace(comment.Text))
            .OrderBy(comment => comment.TimeSeconds)
            .ToArray();

        _textCache.Clear();
        InvalidateVisual();
    }

    public void SetSettings(DanmakuSettings settings)
    {
        _settings = settings with
        {
            FontSize = Math.Clamp(settings.FontSize, 12, 48),
            ScrollDurationSeconds = Math.Clamp(settings.ScrollDurationSeconds, 2, 20),
            FixedDurationSeconds = Math.Clamp(settings.FixedDurationSeconds, 1, 10),
            Opacity = Math.Clamp(settings.Opacity, 0.1, 1),
            Speed = Math.Clamp(settings.Speed, 0.25, 4),
            ScreenHeightRatio = Math.Clamp(settings.ScreenHeightRatio, 0.25, 1)
        };

        _textCache.Clear();
        InvalidateVisual();
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
            Visibility = _suppressed
                ? Visibility.Collapsed
                : Visibility.Visible;
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

    public void SetSuppressed(bool suppressed)
    {
        if (_suppressed == suppressed)
            return;

        _suppressed = suppressed;
        Visibility = _active && !_suppressed
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (_suppressed)
            _activeCount = 0;

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

    public DanmakuMetrics GetMetrics()
    {
        var dpi = VisualTreeHelper.GetDpi(this);

        return new DanmakuMetrics(
            _comments.Count,
            _activeCount,
            _lastFrameMilliseconds,
            _peakFrameMilliseconds,
            ActualWidth,
            ActualHeight,
            dpi.PixelsPerInchX,
            dpi.PixelsPerInchY,
            _lastTimelineSeconds,
            _sourceName);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        if (!_active ||
            _suppressed ||
            _comments.Count == 0 ||
            RenderSize.Width <= 0 ||
            RenderSize.Height <= 0)
        {
            return;
        }

        var frameStarted = Stopwatch.GetTimestamp();
        var timelineSeconds = GetInterpolatedTimelineSeconds();
        _lastTimelineSeconds = timelineSeconds;

        var dpi = VisualTreeHelper.GetDpi(this);
        if (Math.Abs(_cachedPixelsPerDip - dpi.PixelsPerDip) > 0.001)
        {
            _cachedPixelsPerDip = dpi.PixelsPerDip;
            _textCache.Clear();
        }

        var maxLifetime = Math.Max(
            _settings.ScrollDurationSeconds / _settings.Speed,
            _settings.FixedDurationSeconds);
        var firstIndex = LowerBound(timelineSeconds - maxLifetime);
        var lastIndex = UpperBound(timelineSeconds);

        var laneHeight = Math.Max(
            _settings.FontSize + LaneGap,
            22);
        var usableHeight = Math.Max(
            laneHeight,
            RenderSize.Height * _settings.ScreenHeightRatio);
        var scrollLaneCount = Math.Max(
            1,
            Math.Min(
                18,
                (int)Math.Floor(
                    Math.Max(laneHeight, usableHeight - TopPadding) / laneHeight)));
        var fixedLaneCount = Math.Max(
            1,
            Math.Min(4, scrollLaneCount / 3));

        var activeCount = 0;

        drawingContext.PushOpacity(_settings.Opacity);
        try
        {
            for (var index = firstIndex; index < lastIndex; index++)
            {
                var comment = _comments[index];
                var age = timelineSeconds - comment.TimeSeconds;
                if (age < 0)
                    continue;

                var text = GetOrCreateText(comment, dpi.PixelsPerDip);

                switch (comment.Mode)
                {
                    case DanmakuMode.Top:
                        if (age <= _settings.FixedDurationSeconds)
                        {
                            DrawTop(
                                drawingContext,
                                text,
                                comment,
                                laneHeight,
                                fixedLaneCount);
                            activeCount++;
                        }
                        break;

                    case DanmakuMode.Bottom:
                        if (age <= _settings.FixedDurationSeconds)
                        {
                            DrawBottom(
                                drawingContext,
                                text,
                                comment,
                                laneHeight,
                                fixedLaneCount);
                            activeCount++;
                        }
                        break;

                    default:
                        var scrollDuration =
                            _settings.ScrollDurationSeconds / _settings.Speed;
                        if (age <= scrollDuration)
                        {
                            DrawScroll(
                                drawingContext,
                                text,
                                comment,
                                age,
                                scrollDuration,
                                laneHeight,
                                scrollLaneCount);
                            activeCount++;
                        }
                        break;
                }
            }
        }
        finally
        {
            drawingContext.Pop();
        }

        _activeCount = activeCount;
        _renderCount++;

        if ((_renderCount % 180) == 0 && _textCache.Count > 512)
            _textCache.Clear();

        _lastFrameMilliseconds = ElapsedMilliseconds(frameStarted);
        if (_lastFrameMilliseconds > _peakFrameMilliseconds)
            _peakFrameMilliseconds = _lastFrameMilliseconds;
    }

    private void DrawScroll(
        DrawingContext drawingContext,
        FormattedText text,
        DanmakuComment comment,
        double age,
        double duration,
        double laneHeight,
        int laneCount)
    {
        var progress = Math.Clamp(age / duration, 0, 1);
        var startX = RenderSize.Width + 32;
        var endX = -text.WidthIncludingTrailingWhitespace - 32;
        var x = startX + ((endX - startX) * progress);
        var lane = StableLane(comment.Id, laneCount);
        var y = TopPadding + (lane * laneHeight);

        drawingContext.DrawText(text, new Point(x, y));
    }

    private void DrawTop(
        DrawingContext drawingContext,
        FormattedText text,
        DanmakuComment comment,
        double laneHeight,
        int laneCount)
    {
        var lane = StableLane(comment.Id, laneCount);
        var x = Math.Max(
            8,
            (RenderSize.Width - text.WidthIncludingTrailingWhitespace) / 2);
        var y = TopPadding + (lane * laneHeight);

        drawingContext.DrawText(text, new Point(x, y));
    }

    private void DrawBottom(
        DrawingContext drawingContext,
        FormattedText text,
        DanmakuComment comment,
        double laneHeight,
        int laneCount)
    {
        var lane = StableLane(comment.Id, laneCount);
        var x = Math.Max(
            8,
            (RenderSize.Width - text.WidthIncludingTrailingWhitespace) / 2);
        var y = Math.Max(
            TopPadding,
            RenderSize.Height - BottomPadding - ((lane + 1) * laneHeight));

        drawingContext.DrawText(text, new Point(x, y));
    }

    private FormattedText GetOrCreateText(
        DanmakuComment comment,
        double pixelsPerDip)
    {
        if (_textCache.TryGetValue(comment.Id, out var cached))
            return cached;

        var color = Color.FromArgb(
            (byte)(comment.ColorArgb >> 24),
            (byte)(comment.ColorArgb >> 16),
            (byte)(comment.ColorArgb >> 8),
            (byte)comment.ColorArgb);

        var formatted = new FormattedText(
            comment.Text,
            CultureInfo.GetCultureInfo("zh-CN"),
            FlowDirection.LeftToRight,
            Typeface,
            _settings.FontSize,
            new SolidColorBrush(color),
            pixelsPerDip);

        _textCache[comment.Id] = formatted;
        return formatted;
    }

    private void CompositionTarget_Rendering(object? sender, EventArgs e)
    {
        if (_active && !_suppressed)
            InvalidateVisual();
    }

    private double GetInterpolatedTimelineSeconds()
    {
        if (_paused)
            return _sampleTimelineSeconds;

        var elapsedSeconds =
            (Stopwatch.GetTimestamp() - _sampleTimestamp) /
            (double)Stopwatch.Frequency;

        return Math.Max(
            0,
            _sampleTimelineSeconds + (elapsedSeconds * _speed));
    }

    private int LowerBound(double timeSeconds)
    {
        var low = 0;
        var high = _comments.Count;

        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            if (_comments[middle].TimeSeconds < timeSeconds)
                low = middle + 1;
            else
                high = middle;
        }

        return low;
    }

    private int UpperBound(double timeSeconds)
    {
        var low = 0;
        var high = _comments.Count;

        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            if (_comments[middle].TimeSeconds <= timeSeconds)
                low = middle + 1;
            else
                high = middle;
        }

        return low;
    }

    private static int StableLane(string id, int laneCount)
    {
        if (laneCount <= 1)
            return 0;

        unchecked
        {
            var hash = 17;
            foreach (var ch in id)
                hash = (hash * 31) + ch;

            return (hash & int.MaxValue) % laneCount;
        }
    }

    private static double ElapsedMilliseconds(long startedAt) =>
        (Stopwatch.GetTimestamp() - startedAt) *
        1000d /
        Stopwatch.Frequency;
}

public readonly record struct DanmakuMetrics(
    int LoadedCount,
    int ActiveCount,
    double LastFrameMilliseconds,
    double PeakFrameMilliseconds,
    double Width,
    double Height,
    double DpiX,
    double DpiY,
    double TimelineSeconds,
    string SourceName);
