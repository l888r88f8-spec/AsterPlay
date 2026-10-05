using AsterPlay.Models.Danmaku;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace AsterPlay.WinUI.Controls;

public sealed class WinUIDanmakuOverlay : Canvas
{
    private DanmakuDocument _document =
        new("None", Array.Empty<DanmakuComment>());

    private DanmakuSettings _settings = new();
    private readonly List<ActiveComment> _active = [];
    private int _nextIndex;
    private int _nextLane;
    private double _lastTimeline = -1;

    public int LoadedCount => _document.Comments.Count;
    public int ActiveCount => _active.Count;

    public void SetDocument(DanmakuDocument document)
    {
        _document = new DanmakuDocument(
            document.SourceName,
            document.Comments
                .OrderBy(comment => comment.TimeSeconds)
                .ToArray());

        Reset(0);
    }

    public void ApplySettings(DanmakuSettings settings)
    {
        _settings = settings;
        Reset(Math.Max(0, _lastTimeline));
    }

    public void Reset(double timelineSeconds)
    {
        Children.Clear();
        _active.Clear();
        _nextLane = 0;
        _lastTimeline = timelineSeconds;

        var comments = _document.Comments;
        var low = 0;
        var high = comments.Count;
        while (low < high)
        {
            var mid = low + ((high - low) / 2);
            if (comments[mid].TimeSeconds < timelineSeconds)
                low = mid + 1;
            else
                high = mid;
        }

        _nextIndex = low;
    }

    public void Sync(double timelineSeconds, bool paused)
    {
        if (Visibility != Visibility.Visible ||
            ActualWidth <= 1 ||
            ActualHeight <= 1)
        {
            _lastTimeline = timelineSeconds;
            return;
        }

        if (_lastTimeline < 0 ||
            timelineSeconds < _lastTimeline - 0.1 ||
            timelineSeconds - _lastTimeline > 1.5)
        {
            Reset(timelineSeconds);
        }

        if (!paused)
            SpawnDueComments(timelineSeconds);

        UpdateActive(timelineSeconds);
        _lastTimeline = timelineSeconds;
    }

    private void SpawnDueComments(double timelineSeconds)
    {
        while (_nextIndex < _document.Comments.Count)
        {
            var comment = _document.Comments[_nextIndex];
            if (comment.TimeSeconds > timelineSeconds + 0.08)
                break;

            _nextIndex++;

            if (comment.TimeSeconds < timelineSeconds - 0.35 ||
                !ShouldDisplay(comment) ||
                _active.Count >= _settings.MaxActiveComments)
            {
                continue;
            }

            AddComment(comment);
        }
    }

    private bool ShouldDisplay(DanmakuComment comment)
    {
        if (_settings.BlockedWords.Any(word =>
                comment.Text.Contains(word, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(comment.Sender) &&
            _settings.BlockedUsers.Any(user =>
                string.Equals(user, comment.Sender, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (_settings.DensityRatio >= 0.999)
            return true;

        var hash = Math.Abs(StringComparer.Ordinal.GetHashCode(comment.Id));
        var sample = (hash % 10_000) / 10_000d;
        return sample <= _settings.DensityRatio;
    }

    private void AddComment(DanmakuComment comment)
    {
        var fontSize = _settings.FontSize;
        var estimatedWidth = Math.Max(44, comment.Text.Length * fontSize * 0.62);
        var laneHeight = fontSize + 8;
        var usableHeight = Math.Max(
            laneHeight,
            ActualHeight * _settings.ScreenHeightRatio);
        var laneCount = Math.Max(1, (int)(usableHeight / laneHeight));
        var lane = _nextLane++ % laneCount;

        var block = new TextBlock
        {
            Text = comment.Text,
            FontSize = fontSize,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(ToColor(comment.ColorArgb)),
            Opacity = _settings.Opacity,
            IsHitTestVisible = false,
            TextWrapping = TextWrapping.NoWrap
        };

        Children.Add(block);

        var duration = comment.Mode == DanmakuMode.Scroll
            ? _settings.ScrollDurationSeconds / Math.Max(0.25, _settings.Speed)
            : _settings.FixedDurationSeconds;

        var top = comment.Mode switch
        {
            DanmakuMode.Bottom => Math.Max(0, usableHeight - laneHeight * (lane + 1)),
            _ => lane * laneHeight
        };

        SetTop(block, top);

        _active.Add(new ActiveComment(
            comment,
            block,
            comment.TimeSeconds,
            Math.Max(0.5, duration),
            estimatedWidth));
    }

    private void UpdateActive(double timelineSeconds)
    {
        for (var index = _active.Count - 1; index >= 0; index--)
        {
            var active = _active[index];
            var elapsed = timelineSeconds - active.StartSeconds;
            var progress = elapsed / active.DurationSeconds;

            if (progress >= 1)
            {
                Children.Remove(active.Element);
                _active.RemoveAt(index);
                continue;
            }

            if (progress < 0)
                continue;

            if (active.Comment.Mode == DanmakuMode.Scroll)
            {
                var x = ActualWidth -
                        progress * (ActualWidth + active.EstimatedWidth);
                SetLeft(active.Element, x);
            }
            else
            {
                SetLeft(
                    active.Element,
                    Math.Max(0, (ActualWidth - active.EstimatedWidth) / 2));
            }
        }
    }

    private static Windows.UI.Color ToColor(uint argb) =>
        Windows.UI.Color.FromArgb(
            (byte)((argb >> 24) & 0xFF),
            (byte)((argb >> 16) & 0xFF),
            (byte)((argb >> 8) & 0xFF),
            (byte)(argb & 0xFF));

    private sealed record ActiveComment(
        DanmakuComment Comment,
        TextBlock Element,
        double StartSeconds,
        double DurationSeconds,
        double EstimatedWidth);
}
