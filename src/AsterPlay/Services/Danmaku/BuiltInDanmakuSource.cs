using AsterPlay.Models.Danmaku;

namespace AsterPlay.Services.Danmaku;

public sealed class BuiltInDanmakuSource : IDanmakuSource
{
    private static readonly string[] ScrollMessages =
    [
        "AsterPlay · 正式弹幕调度",
        "滚动弹幕 · timeline driven",
        "Render API + WPF Overlay",
        "Seek 时隐藏，恢复播放后再显示",
        "速度 / 暂停 / 全屏同步测试",
        "Step 13 · DanmakuRenderer"
    ];

    public string Name => "AsterPlay 内置测试源";

    public Task<IReadOnlyList<DanmakuComment>> LoadAsync(
        DanmakuContext context,
        CancellationToken cancellationToken)
    {
        var duration = Math.Clamp(
            context.DurationSeconds > 0 ? context.DurationSeconds : 30 * 60,
            60,
            4 * 60 * 60);

        var comments = new List<DanmakuComment>();
        var sequence = 0;

        for (var time = 1.0; time < duration; time += 0.9)
        {
            cancellationToken.ThrowIfCancellationRequested();

            uint colorArgb = (sequence % 4) switch
            {
                0 => 0xFFFFFFFFu,
                1 => 0xFFFFD966u,
                2 => 0xFF6EC6FFu,
                _ => 0xFFA8F0B0u
            };

            comments.Add(new DanmakuComment(
                $"scroll-{sequence}",
                time,
                ScrollMessages[sequence % ScrollMessages.Length],
                DanmakuMode.Scroll,
                colorArgb));

            sequence++;
        }

        for (var time = 5.0; time < duration; time += 12.0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            comments.Add(new DanmakuComment(
                $"top-{time:0.0}",
                time,
                $"顶部弹幕 · {FormatTime(time)}",
                DanmakuMode.Top,
                0xFFFFFFFFu));
        }

        for (var time = 9.0; time < duration; time += 17.0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            comments.Add(new DanmakuComment(
                $"bottom-{time:0.0}",
                time,
                $"底部弹幕 · {FormatTime(time)}",
                DanmakuMode.Bottom,
                0xFFFFE38Au));
        }

        IReadOnlyList<DanmakuComment> result = comments
            .OrderBy(comment => comment.TimeSeconds)
            .ToArray();

        return Task.FromResult(result);
    }

    private static string FormatTime(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{time.Minutes:00}:{time.Seconds:00}";
    }
}
