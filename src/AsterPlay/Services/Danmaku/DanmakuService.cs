using AsterPlay.Models.Danmaku;

namespace AsterPlay.Services.Danmaku;

public sealed class DanmakuService
{
    public async Task<DanmakuDocument> LoadAsync(
        DanmakuContext context,
        DanmakuSourceSettings sourceSettings,
        CancellationToken cancellationToken = default)
    {
        var source = CreateSource(sourceSettings);
        var comments = await source.LoadAsync(context, cancellationToken);

        PlaybackLog.Write(
            "Danmaku",
            $"Loaded source={source.Name}, itemId={context.ItemId}, comments={comments.Count}, duration={context.DurationSeconds:0.###}");

        return new DanmakuDocument(source.Name, comments);
    }

    private static IDanmakuSource CreateSource(
        DanmakuSourceSettings settings) =>
        settings.SourceKind switch
        {
            DanmakuSourceKind.DandanPlay =>
                new DandanPlayDanmakuSource(
                    settings.DandanPlayAppId,
                    settings.DandanPlayAppSecret,
                    settings.DandanPlayWithRelated),

            DanmakuSourceKind.LogVar =>
                new LogVarDanmakuSource(settings.LogVarBaseUrl),

            _ => new BuiltInDanmakuSource()
        };
}
