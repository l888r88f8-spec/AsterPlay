using AsterPlay.Models.Danmaku;

namespace AsterPlay.Services.Danmaku;

public sealed class DanmakuService
{
    public async Task<DanmakuDocument> LoadAsync(
        DanmakuContext context,
        DanmakuSourceSettings sourceSettings,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceSettings.LogVarBaseUrl))
        {
            PlaybackLog.Write(
                "Danmaku",
                $"LogVar not configured: itemId={context.ItemId}");

            return new DanmakuDocument(
                "LogVar（未配置）",
                Array.Empty<DanmakuComment>());
        }

        var source = CreateSource(sourceSettings);

        if (DanmakuMatchOverrideStore.TryGet(
                sourceSettings.LogVarBaseUrl,
                context.ItemId,
                out var manualCandidate) &&
            manualCandidate is not null)
        {
            var manualComments = await source.LoadCandidateAsync(
                manualCandidate,
                cancellationToken);

            PlaybackLog.Write(
                "Danmaku",
                $"Loaded manual match: itemId={context.ItemId}, episodeId={manualCandidate.EpisodeId}, comments={manualComments.Count}");

            return new DanmakuDocument(
                "LogVar · 手动匹配",
                manualComments);
        }

        var comments = await source.LoadAsync(
            context,
            cancellationToken);

        PlaybackLog.Write(
            "Danmaku",
            $"Loaded source={source.Name}, itemId={context.ItemId}, comments={comments.Count}, duration={context.DurationSeconds:0.###}");

        return new DanmakuDocument(
            source.Name,
            comments);
    }

    public Task<IReadOnlyList<DanmakuMatchCandidate>> SearchCandidatesAsync(
        DanmakuContext context,
        DanmakuSourceSettings sourceSettings,
        string? keyword,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceSettings.LogVarBaseUrl))
        {
            return Task.FromResult<IReadOnlyList<DanmakuMatchCandidate>>(
                Array.Empty<DanmakuMatchCandidate>());
        }

        return CreateSource(sourceSettings).SearchCandidatesAsync(
            context,
            keyword,
            cancellationToken);
    }

    public DanmakuMatchCandidate? GetManualMatch(
        string itemId,
        DanmakuSourceSettings sourceSettings)
    {
        return DanmakuMatchOverrideStore.TryGet(
            sourceSettings.LogVarBaseUrl,
            itemId,
            out var candidate)
            ? candidate
            : null;
    }

    public void SetManualMatch(
        string itemId,
        DanmakuSourceSettings sourceSettings,
        DanmakuMatchCandidate candidate)
    {
        DanmakuMatchOverrideStore.Save(
            sourceSettings.LogVarBaseUrl,
            itemId,
            candidate);

        PlaybackLog.Write(
            "Danmaku",
            $"Manual match saved: itemId={itemId}, episodeId={candidate.EpisodeId}, anime={candidate.AnimeTitle}, episode={candidate.EpisodeTitle}");
    }

    public bool ClearManualMatch(
        string itemId,
        DanmakuSourceSettings sourceSettings)
    {
        var removed = DanmakuMatchOverrideStore.Remove(
            sourceSettings.LogVarBaseUrl,
            itemId);

        PlaybackLog.Write(
            "Danmaku",
            $"Manual match cleared: itemId={itemId}, removed={removed}");

        return removed;
    }

    private static LogVarDanmakuSource CreateSource(
        DanmakuSourceSettings sourceSettings) =>
        new(
            sourceSettings.LogVarBaseUrl,
            sourceSettings.LogVarAccessToken);
}
