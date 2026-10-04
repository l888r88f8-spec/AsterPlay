using AsterPlay.Models.Danmaku;

namespace AsterPlay.Services.Danmaku;

public sealed class DanmakuService
{
    private readonly IReadOnlyList<IDanmakuSource> _sources;

    public DanmakuService()
        : this([new BuiltInDanmakuSource()])
    {
    }

    public DanmakuService(IReadOnlyList<IDanmakuSource> sources)
    {
        _sources = sources;
    }

    public async Task<DanmakuDocument> LoadAsync(
        DanmakuContext context,
        CancellationToken cancellationToken = default)
    {
        if (_sources.Count == 0)
            return new DanmakuDocument("无数据源", Array.Empty<DanmakuComment>());

        foreach (var source in _sources)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var comments = await source.LoadAsync(context, cancellationToken);
            if (comments.Count == 0)
                continue;

            PlaybackLog.Write(
                "Danmaku",
                $"Loaded source={source.Name}, itemId={context.ItemId}, comments={comments.Count}, duration={context.DurationSeconds:0.###}");

            return new DanmakuDocument(source.Name, comments);
        }

        return new DanmakuDocument(
            string.Join(" / ", _sources.Select(source => source.Name)),
            Array.Empty<DanmakuComment>());
    }
}
