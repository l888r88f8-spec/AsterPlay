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
    private const double HorizontalGap = 24;
    private const int MaxDocumentComments = 200_000;

    private static readonly Typeface Typeface = new(
        new FontFamily("Segoe UI"),
        FontStyles.Normal,
        FontWeights.SemiBold,
        FontStretches.Normal);

    private readonly Dictionary<string, FormattedText> _textCache = [];
    private readonly Dictionary<string, int> _laneAssignments = [];

    private IReadOnlyList<DanmakuComment> _sourceComments =
        Array.Empty<DanmakuComment>();
    private IReadOnlyList<DanmakuComment> _comments =
        Array.Empty<DanmakuComment>();
    private DanmakuSettings _settings = new();

    private bool _active;
    private bool _suppressed;
    private bool _paused = true;
    private bool _layoutValid;
    private double _sampleTimelineSeconds;
    private double _speed = 1;
    private long _sampleTimestamp = Stopwatch.GetTimestamp();
    private double _cachedPixelsPerDip;
    private double _layoutWidth = -1;
    private double _layoutHeight = -1;
    private double _layoutPixelsPerDip = -1;
    private int _inputCount;
    private int _documentDroppedCount;
    private int _activeCount;
    private int _layoutDroppedCount;
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
        TextOptions.SetTextFormattingMode(
            this,
            TextFormattingMode.Display);
    }

    public void SetDocument(DanmakuDocument document)
    {
        _sourceName = document.SourceName;

        var validComments = document.Comments
            .Where(comment =>
                double.IsFinite(comment.TimeSeconds) &&
                comment.TimeSeconds >= 0 &&
                !string.IsNullOrWhiteSpace(comment.Text))
            .OrderBy(comment => comment.TimeSeconds)
            .ToArray();

        _inputCount = validComments.Length;

        if (validComments.Length > MaxDocumentComments)
        {
            _sourceComments = SampleEvenly(
                validComments,
                MaxDocumentComments);
            _documentDroppedCount =
                validComments.Length -
                _sourceComments.Count;

            PlaybackLog.Write(
                "Danmaku",
                $"Document guard applied: source={document.SourceName}, input={validComments.Length}, " +
                $"retained={_sourceComments.Count}, dropped={_documentDroppedCount}");
        }
        else
        {
            _sourceComments = validComments;
            _documentDroppedCount = 0;
        }

        RebuildVisibleComments();
    }

    public void SetSettings(DanmakuSettings settings)
    {
        _settings = settings with
        {
            FontSize = Math.Clamp(settings.FontSize, 12, 48),
            ScrollDurationSeconds = Math.Clamp(
                settings.ScrollDurationSeconds,
                2,
                20),
            FixedDurationSeconds = Math.Clamp(
                settings.FixedDurationSeconds,
                1,
                10),
            Opacity = Math.Clamp(settings.Opacity, 0.1, 1),
            Speed = Math.Clamp(settings.Speed, 0.25, 4),
            ScreenHeightRatio = Math.Clamp(
                settings.ScreenHeightRatio,
                0.25,
                1),
            DensityRatio = Math.Clamp(
                settings.DensityRatio,
                0.25,
                1),
            MaxActiveComments = Math.Clamp(
                settings.MaxActiveComments,
                10,
                300),
            BlockedWords = NormalizeList(
                settings.BlockedWords),
            BlockedUsers = NormalizeList(
                settings.BlockedUsers)
        };

        RebuildVisibleComments();
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
            CompositionTarget.Rendering +=
                CompositionTarget_Rendering;
        }
        else
        {
            CompositionTarget.Rendering -=
                CompositionTarget_Rendering;
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

    public void Synchronize(
        double timelineSeconds,
        bool paused,
        double speed)
    {
        if (!double.IsFinite(timelineSeconds))
            timelineSeconds = 0;

        if (!double.IsFinite(speed))
            speed = 1;

        _sampleTimelineSeconds =
            Math.Max(0, timelineSeconds);
        _paused = paused;
        _speed = Math.Clamp(speed, 0.25, 4);
        _sampleTimestamp = Stopwatch.GetTimestamp();
    }

    public DanmakuMetrics GetMetrics()
    {
        var dpi = VisualTreeHelper.GetDpi(this);

        return new DanmakuMetrics(
            _inputCount,
            _sourceComments.Count,
            _documentDroppedCount,
            _comments.Count,
            _activeCount,
            _layoutDroppedCount,
            _lastFrameMilliseconds,
            _peakFrameMilliseconds,
            ActualWidth,
            ActualHeight,
            dpi.PixelsPerInchX,
            dpi.PixelsPerInchY,
            _lastTimelineSeconds,
            _sourceName);
    }

    protected override void OnRender(
        DrawingContext drawingContext)
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
        var timelineSeconds =
            GetInterpolatedTimelineSeconds();
        _lastTimelineSeconds = timelineSeconds;

        var dpi = VisualTreeHelper.GetDpi(this);
        if (Math.Abs(
                _cachedPixelsPerDip -
                dpi.PixelsPerDip) > 0.001)
        {
            _cachedPixelsPerDip = dpi.PixelsPerDip;
            _textCache.Clear();
            InvalidateLayoutSchedule();
        }

        EnsureLayoutSchedule(dpi.PixelsPerDip);

        var maxLifetime = Math.Max(
            _settings.ScrollDurationSeconds /
            _settings.Speed,
            _settings.FixedDurationSeconds);
        var firstIndex = LowerBound(
            timelineSeconds - maxLifetime);
        var lastIndex = UpperBound(timelineSeconds);

        var laneHeight = Math.Max(
            _settings.FontSize + LaneGap,
            22);
        var usableHeight = Math.Max(
            laneHeight,
            RenderSize.Height *
            _settings.ScreenHeightRatio);
        var scrollLaneCount = Math.Max(
            1,
            Math.Min(
                18,
                (int)Math.Floor(
                    Math.Max(
                        laneHeight,
                        usableHeight - TopPadding) /
                    laneHeight)));
        var fixedLaneCount = Math.Max(
            1,
            Math.Min(
                4,
                scrollLaneCount / 3));

        var activeCount = 0;

        drawingContext.PushOpacity(
            _settings.Opacity);
        try
        {
            for (var index = firstIndex;
                 index < lastIndex;
                 index++)
            {
                if (activeCount >=
                    _settings.MaxActiveComments)
                {
                    break;
                }

                var comment = _comments[index];
                var age =
                    timelineSeconds -
                    comment.TimeSeconds;

                if (age < 0)
                    continue;

                var text = GetOrCreateText(
                    comment,
                    dpi.PixelsPerDip);

                switch (comment.Mode)
                {
                    case DanmakuMode.Top:
                    {
                        if (age >
                            _settings.FixedDurationSeconds)
                        {
                            break;
                        }

                        var lane = ResolveLane(
                            comment,
                            fixedLaneCount);

                        if (lane < 0)
                            break;

                        DrawTop(
                            drawingContext,
                            text,
                            lane,
                            laneHeight);
                        activeCount++;
                        break;
                    }

                    case DanmakuMode.Bottom:
                    {
                        if (age >
                            _settings.FixedDurationSeconds)
                        {
                            break;
                        }

                        var lane = ResolveLane(
                            comment,
                            fixedLaneCount);

                        if (lane < 0)
                            break;

                        DrawBottom(
                            drawingContext,
                            text,
                            lane,
                            laneHeight);
                        activeCount++;
                        break;
                    }

                    default:
                    {
                        var scrollDuration =
                            _settings.ScrollDurationSeconds /
                            _settings.Speed;

                        if (age > scrollDuration)
                            break;

                        var lane = ResolveLane(
                            comment,
                            scrollLaneCount);

                        if (lane < 0)
                            break;

                        DrawScroll(
                            drawingContext,
                            text,
                            age,
                            scrollDuration,
                            lane,
                            laneHeight);
                        activeCount++;
                        break;
                    }
                }
            }
        }
        finally
        {
            drawingContext.Pop();
        }

        _activeCount = activeCount;
        _renderCount++;

        if ((_renderCount % 180) == 0 &&
            _textCache.Count > 1024)
        {
            _textCache.Clear();
        }

        _lastFrameMilliseconds =
            ElapsedMilliseconds(frameStarted);

        if (_lastFrameMilliseconds >
            _peakFrameMilliseconds)
        {
            _peakFrameMilliseconds =
                _lastFrameMilliseconds;
        }
    }

    private void RebuildVisibleComments()
    {
        var blockedWords = new HashSet<string>(
            _settings.BlockedWords,
            StringComparer.OrdinalIgnoreCase);
        var blockedUsers = new HashSet<string>(
            _settings.BlockedUsers,
            StringComparer.OrdinalIgnoreCase);

        _comments = _sourceComments
            .Where(comment =>
                !IsBlockedByWord(
                    comment.Text,
                    blockedWords))
            .Where(comment =>
                string.IsNullOrWhiteSpace(
                    comment.Sender) ||
                !blockedUsers.Contains(
                    comment.Sender.Trim()))
            .Where(comment =>
                KeepByDensity(
                    comment.Id,
                    _settings.DensityRatio))
            .ToArray();

        _textCache.Clear();
        InvalidateLayoutSchedule();
        InvalidateVisual();
    }

    private void EnsureLayoutSchedule(
        double pixelsPerDip)
    {
        if (_layoutValid &&
            Math.Abs(
                _layoutWidth -
                RenderSize.Width) < 0.5 &&
            Math.Abs(
                _layoutHeight -
                RenderSize.Height) < 0.5 &&
            Math.Abs(
                _layoutPixelsPerDip -
                pixelsPerDip) < 0.001)
        {
            return;
        }

        _laneAssignments.Clear();
        _layoutDroppedCount = 0;
        _layoutWidth = RenderSize.Width;
        _layoutHeight = RenderSize.Height;
        _layoutPixelsPerDip = pixelsPerDip;
        _layoutValid = true;

        if (!_settings.AvoidOverlap ||
            _comments.Count == 0)
        {
            return;
        }

        var laneHeight = Math.Max(
            _settings.FontSize + LaneGap,
            22);
        var usableHeight = Math.Max(
            laneHeight,
            RenderSize.Height *
            _settings.ScreenHeightRatio);
        var scrollLaneCount = Math.Max(
            1,
            Math.Min(
                18,
                (int)Math.Floor(
                    Math.Max(
                        laneHeight,
                        usableHeight - TopPadding) /
                    laneHeight)));
        var fixedLaneCount = Math.Max(
            1,
            Math.Min(
                4,
                scrollLaneCount / 3));

        var scrollStates =
            new ScrollLaneState?[scrollLaneCount];
        var topEnds =
            Enumerable.Repeat(
                double.NegativeInfinity,
                fixedLaneCount)
            .ToArray();
        var bottomEnds =
            Enumerable.Repeat(
                double.NegativeInfinity,
                fixedLaneCount)
            .ToArray();

        var scrollDuration =
            _settings.ScrollDurationSeconds /
            _settings.Speed;

        foreach (var comment in _comments)
        {
            var lane = comment.Mode switch
            {
                DanmakuMode.Top =>
                    AssignFixedLane(
                        comment,
                        topEnds,
                        _settings.FixedDurationSeconds),

                DanmakuMode.Bottom =>
                    AssignFixedLane(
                        comment,
                        bottomEnds,
                        _settings.FixedDurationSeconds),

                _ =>
                    AssignScrollLane(
                        comment,
                        scrollStates,
                        scrollDuration)
            };

            _laneAssignments[comment.Id] = lane;

            if (lane < 0)
                _layoutDroppedCount++;
        }
    }

    private int AssignFixedLane(
        DanmakuComment comment,
        double[] laneEnds,
        double duration)
    {
        var preferred = StableLane(
            comment.Id,
            laneEnds.Length);

        for (var offset = 0;
             offset < laneEnds.Length;
             offset++)
        {
            var lane =
                (preferred + offset) %
                laneEnds.Length;

            if (laneEnds[lane] >
                comment.TimeSeconds + 0.001)
            {
                continue;
            }

            laneEnds[lane] =
                comment.TimeSeconds + duration;
            return lane;
        }

        return -1;
    }

    private int AssignScrollLane(
        DanmakuComment comment,
        ScrollLaneState?[] states,
        double duration)
    {
        var width = EstimateTextWidth(
            comment.Text);
        var preferred = StableLane(
            comment.Id,
            states.Length);

        for (var offset = 0;
             offset < states.Length;
             offset++)
        {
            var lane =
                (preferred + offset) %
                states.Length;
            var previous = states[lane];

            if (previous is not null &&
                !CanShareScrollLane(
                    previous.Value,
                    comment.TimeSeconds,
                    width,
                    duration))
            {
                continue;
            }

            states[lane] = new ScrollLaneState(
                comment.TimeSeconds,
                width);
            return lane;
        }

        return -1;
    }

    private bool CanShareScrollLane(
        ScrollLaneState previous,
        double nextStart,
        double nextWidth,
        double duration)
    {
        var delta =
            nextStart - previous.StartSeconds;

        if (delta < 0)
            return false;

        var width = Math.Max(
            1,
            RenderSize.Width);
        var previousSpeed =
            (width +
             previous.Width +
             64) /
            duration;
        var nextSpeed =
            (width +
             nextWidth +
             64) /
            duration;

        var entryGap =
            (previous.Width +
             HorizontalGap) /
            previousSpeed;

        var exitGap =
            duration -
            ((width +
              64 -
              HorizontalGap) /
             nextSpeed);

        var minimumGap = Math.Max(
            entryGap,
            exitGap);

        return delta >= minimumGap;
    }

    private int ResolveLane(
        DanmakuComment comment,
        int laneCount)
    {
        if (!_settings.AvoidOverlap)
            return StableLane(
                comment.Id,
                laneCount);

        return _laneAssignments.TryGetValue(
                comment.Id,
                out var lane)
            ? lane
            : -1;
    }

    private void DrawScroll(
        DrawingContext drawingContext,
        FormattedText text,
        double age,
        double duration,
        int lane,
        double laneHeight)
    {
        var progress = Math.Clamp(
            age / duration,
            0,
            1);
        var startX =
            RenderSize.Width + 32;
        var endX =
            -text.WidthIncludingTrailingWhitespace -
            32;
        var x =
            startX +
            ((endX - startX) *
             progress);
        var y =
            TopPadding +
            (lane * laneHeight);

        drawingContext.DrawText(
            text,
            new Point(x, y));
    }

    private void DrawTop(
        DrawingContext drawingContext,
        FormattedText text,
        int lane,
        double laneHeight)
    {
        var x = Math.Max(
            8,
            (RenderSize.Width -
             text.WidthIncludingTrailingWhitespace) /
            2);
        var y =
            TopPadding +
            (lane * laneHeight);

        drawingContext.DrawText(
            text,
            new Point(x, y));
    }

    private void DrawBottom(
        DrawingContext drawingContext,
        FormattedText text,
        int lane,
        double laneHeight)
    {
        var x = Math.Max(
            8,
            (RenderSize.Width -
             text.WidthIncludingTrailingWhitespace) /
            2);
        var y = Math.Max(
            TopPadding,
            RenderSize.Height -
            BottomPadding -
            ((lane + 1) * laneHeight));

        drawingContext.DrawText(
            text,
            new Point(x, y));
    }

    private FormattedText GetOrCreateText(
        DanmakuComment comment,
        double pixelsPerDip)
    {
        if (_textCache.TryGetValue(
                comment.Id,
                out var cached))
        {
            return cached;
        }

        var color = Color.FromArgb(
            (byte)(comment.ColorArgb >> 24),
            (byte)(comment.ColorArgb >> 16),
            (byte)(comment.ColorArgb >> 8),
            (byte)comment.ColorArgb);

        var brush = new SolidColorBrush(color);
        brush.Freeze();

        var formatted = new FormattedText(
            comment.Text,
            CultureInfo.GetCultureInfo("zh-CN"),
            FlowDirection.LeftToRight,
            Typeface,
            _settings.FontSize,
            brush,
            pixelsPerDip);

        _textCache[comment.Id] = formatted;
        return formatted;
    }

    private void CompositionTarget_Rendering(
        object? sender,
        EventArgs e)
    {
        if (_active && !_suppressed)
            InvalidateVisual();
    }

    private double GetInterpolatedTimelineSeconds()
    {
        if (_paused)
            return _sampleTimelineSeconds;

        var elapsedSeconds =
            (Stopwatch.GetTimestamp() -
             _sampleTimestamp) /
            (double)Stopwatch.Frequency;

        return Math.Max(
            0,
            _sampleTimelineSeconds +
            (elapsedSeconds * _speed));
    }

    private int LowerBound(double timeSeconds)
    {
        var low = 0;
        var high = _comments.Count;

        while (low < high)
        {
            var middle =
                low +
                ((high - low) / 2);

            if (_comments[middle]
                    .TimeSeconds <
                timeSeconds)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    private int UpperBound(double timeSeconds)
    {
        var low = 0;
        var high = _comments.Count;

        while (low < high)
        {
            var middle =
                low +
                ((high - low) / 2);

            if (_comments[middle]
                    .TimeSeconds <=
                timeSeconds)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    private void InvalidateLayoutSchedule()
    {
        _layoutValid = false;
        _laneAssignments.Clear();
        _layoutDroppedCount = 0;
    }

    private double EstimateTextWidth(
        string text)
    {
        var units = 0d;

        foreach (var ch in text)
        {
            units += ch <= 0x007F
                ? 0.58
                : 1.0;
        }

        return Math.Max(
            _settings.FontSize,
            (units *
             _settings.FontSize) +
            6);
    }

    private static IReadOnlyList<DanmakuComment> SampleEvenly(
        IReadOnlyList<DanmakuComment> comments,
        int maxCount)
    {
        if (comments.Count <= maxCount)
            return comments.ToArray();

        var sampled = new DanmakuComment[maxCount];

        for (var index = 0;
             index < maxCount;
             index++)
        {
            var sourceIndex =
                (int)(((long)index *
                       comments.Count) /
                      maxCount);

            sampled[index] =
                comments[sourceIndex];
        }

        return sampled;
    }

    private static bool IsBlockedByWord(
        string text,
        IReadOnlyCollection<string> blockedWords)
    {
        foreach (var word in blockedWords)
        {
            if (text.Contains(
                    word,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool KeepByDensity(
        string id,
        double densityRatio)
    {
        if (densityRatio >= 0.999)
            return true;

        var bucket =
            StableHash(id) % 10_000u;
        var threshold =
            (uint)Math.Round(
                densityRatio * 10_000d);

        return bucket < threshold;
    }

    private static List<string> NormalizeList(
        IEnumerable<string>? values) =>
        (values ?? Array.Empty<string>())
            .Select(value =>
                value?.Trim() ?? "")
            .Where(value =>
                value.Length > 0)
            .Distinct(
                StringComparer.OrdinalIgnoreCase)
            .Take(200)
            .ToList();

    private static int StableLane(
        string id,
        int laneCount)
    {
        if (laneCount <= 1)
            return 0;

        return (int)(
            StableHash(id) %
            (uint)laneCount);
    }

    private static uint StableHash(string id)
    {
        unchecked
        {
            var hash = 2166136261u;

            foreach (var ch in id)
            {
                hash ^= ch;
                hash *= 16777619u;
            }

            return hash;
        }
    }

    private static double ElapsedMilliseconds(
        long startedAt) =>
        (Stopwatch.GetTimestamp() -
         startedAt) *
        1000d /
        Stopwatch.Frequency;

    private readonly record struct ScrollLaneState(
        double StartSeconds,
        double Width);
}

public readonly record struct DanmakuMetrics(
    int InputCount,
    int LoadedCount,
    int DocumentDroppedCount,
    int VisibleCount,
    int ActiveCount,
    int LayoutDroppedCount,
    double LastFrameMilliseconds,
    double PeakFrameMilliseconds,
    double Width,
    double Height,
    double DpiX,
    double DpiY,
    double TimelineSeconds,
    string SourceName);
