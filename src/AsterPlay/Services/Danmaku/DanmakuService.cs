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

        if (DanmakuSeriesMatchStore.TryGet(
                sourceSettings.LogVarBaseUrl,
                context,
                out var seriesBinding) &&
            seriesBinding is not null)
        {
            var manualCandidate =
                await source.ResolveSeriesBindingAsync(
                    context,
                    seriesBinding,
                    cancellationToken);

            if (manualCandidate is not null)
            {
                var manualComments = await source.LoadCandidateAsync(
                    manualCandidate,
                    cancellationToken);

                PlaybackLog.Write(
                    "Danmaku",
                    $"Loaded manual series match: itemId={context.ItemId}, seriesId={context.SeriesId}, " +
                    $"anime={seriesBinding.AnimeTitle}, episodeId={manualCandidate.EpisodeId}, " +
                    $"episodeOffset={seriesBinding.EpisodeOffset}, comments={manualComments.Count}");

                return new DanmakuDocument(
                    "LogVar · 手动剧集匹配",
                    manualComments);
            }

            PlaybackLog.Write(
                "Danmaku",
                $"Saved series match could not resolve current episode; falling back to auto: " +
                $"itemId={context.ItemId}, seriesId={context.SeriesId}, anime={seriesBinding.AnimeTitle}");
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

    public Task<IReadOnlyList<DanmakuSeriesMatchCandidate>> SearchSeriesCandidatesAsync(
        DanmakuContext context,
        DanmakuSourceSettings sourceSettings,
        string? keyword,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceSettings.LogVarBaseUrl))
        {
            return Task.FromResult<IReadOnlyList<DanmakuSeriesMatchCandidate>>(
                Array.Empty<DanmakuSeriesMatchCandidate>());
        }

        return CreateSource(sourceSettings).SearchSeriesCandidatesAsync(
            context,
            keyword,
            cancellationToken);
    }

    public DanmakuSeriesMatchBinding? GetManualSeriesMatch(
        DanmakuContext context,
        DanmakuSourceSettings sourceSettings)
    {
        return DanmakuSeriesMatchStore.TryGet(
            sourceSettings.LogVarBaseUrl,
            context,
            out var binding)
            ? binding
            : null;
    }

    public DanmakuSeriesMatchBinding SetManualSeriesMatch(
        DanmakuContext context,
        DanmakuSourceSettings sourceSettings,
        DanmakuSeriesMatchCandidate series,
        DanmakuMatchCandidate episode)
    {
        var episodeOffset =
            context.EpisodeNumber is > 0 &&
            episode.EpisodeNumber > 0
                ? episode.EpisodeNumber -
                  context.EpisodeNumber.Value
                : 0;

        var binding = new DanmakuSeriesMatchBinding(
            series.AnimeId,
            series.AnimeTitle,
            series.SeasonNumber,
            episodeOffset);

        DanmakuSeriesMatchStore.Save(
            sourceSettings.LogVarBaseUrl,
            context,
            binding);

        PlaybackLog.Write(
            "Danmaku",
            $"Manual series match saved: itemId={context.ItemId}, seriesId={context.SeriesId}, " +
            $"embySeason={context.SeasonNumber}, embyEpisode={context.EpisodeNumber}, " +
            $"animeId={series.AnimeId}, anime={series.AnimeTitle}, logVarSeason={series.SeasonNumber}, " +
            $"selectedEpisode={episode.EpisodeNumber}, offset={episodeOffset}, episodeId={episode.EpisodeId}");

        return binding;
    }

    public bool ClearManualSeriesMatch(
        DanmakuContext context,
        DanmakuSourceSettings sourceSettings)
    {
        var removed = DanmakuSeriesMatchStore.Remove(
            sourceSettings.LogVarBaseUrl,
            context);

        PlaybackLog.Write(
            "Danmaku",
            $"Manual series match cleared: itemId={context.ItemId}, seriesId={context.SeriesId}, " +
            $"season={context.SeasonNumber}, removed={removed}");

        return removed;
    }

    private static LogVarDanmakuSource CreateSource(
        DanmakuSourceSettings sourceSettings) =>
        new(
            sourceSettings.LogVarBaseUrl,
            sourceSettings.LogVarAccessToken);
}
