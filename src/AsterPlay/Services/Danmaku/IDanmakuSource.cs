using AsterPlay.Models.Danmaku;

namespace AsterPlay.Services.Danmaku;

public interface IDanmakuSource
{
    string Name { get; }

    Task<IReadOnlyList<DanmakuComment>> LoadAsync(
        DanmakuContext context,
        CancellationToken cancellationToken);
}
