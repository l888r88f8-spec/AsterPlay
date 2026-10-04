using System.Globalization;
using System.Text.Json.Serialization;
using AsterPlay.Models.Danmaku;

namespace AsterPlay.Services.Danmaku;

internal static class DanmakuApiSupport
{
    public static string BuildMatchFileName(DanmakuContext context)
    {
        if (!string.IsNullOrWhiteSpace(context.SeriesName) &&
            context.EpisodeNumber is > 0)
        {
            var season = context.SeasonNumber is > 0
                ? context.SeasonNumber.Value
                : 1;

            return $"{context.SeriesName} S{season:00}E{context.EpisodeNumber.Value:00}";
        }

        return context.Title;
    }

    public static IReadOnlyList<DanmakuComment> ParseComments(
        IReadOnlyList<ApiComment>? comments,
        string idPrefix,
        double shiftSeconds)
    {
        if (comments is null || comments.Count == 0)
            return Array.Empty<DanmakuComment>();

        var result = new List<DanmakuComment>(comments.Count);

        for (var index = 0; index < comments.Count; index++)
        {
            var item = comments[index];
            if (string.IsNullOrWhiteSpace(item.M))
                continue;

            if (!TryParseParameters(
                    item,
                    out var timeSeconds,
                    out var mode,
                    out var colorArgb))
            {
                continue;
            }

            timeSeconds += shiftSeconds;
            if (!double.IsFinite(timeSeconds) || timeSeconds < 0)
                continue;

            var id = item.Cid > 0
                ? $"{idPrefix}-{item.Cid}"
                : $"{idPrefix}-{index}-{timeSeconds:0.###}";

            result.Add(new DanmakuComment(
                id,
                timeSeconds,
                item.M.Trim(),
                mode,
                colorArgb));
        }

        return result;
    }

    private static bool TryParseParameters(
        ApiComment item,
        out double timeSeconds,
        out DanmakuMode mode,
        out uint colorArgb)
    {
        timeSeconds = item.T ?? -1;
        mode = DanmakuMode.Scroll;
        colorArgb = 0xFFFFFFFFu;

        if (string.IsNullOrWhiteSpace(item.P))
            return timeSeconds >= 0;

        var parts = item.P.Split(',');

        if (parts.Length > 0 &&
            double.TryParse(
                parts[0],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsedTime))
        {
            timeSeconds = parsedTime;
        }

        if (parts.Length > 1 &&
            int.TryParse(
                parts[1],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsedMode))
        {
            mode = parsedMode switch
            {
                4 => DanmakuMode.Bottom,
                5 => DanmakuMode.Top,
                _ => DanmakuMode.Scroll
            };
        }

        if (parts.Length > 2 &&
            uint.TryParse(
                parts[2],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var rgb))
        {
            colorArgb = 0xFF000000u | (rgb & 0x00FFFFFFu);
        }

        return timeSeconds >= 0;
    }

    public static string CombineBaseAndPath(string baseUrl, string path) =>
        $"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}";
}

internal sealed class ApiMatchResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; } = true;

    [JsonPropertyName("errorMessage")]
    public string ErrorMessage { get; set; } = "";

    [JsonPropertyName("isMatched")]
    public bool IsMatched { get; set; }

    [JsonPropertyName("matches")]
    public List<ApiMatchResult> Matches { get; set; } = [];
}

internal sealed class ApiMatchResult
{
    [JsonPropertyName("episodeId")]
    public long EpisodeId { get; set; }

    [JsonPropertyName("animeId")]
    public long AnimeId { get; set; }

    [JsonPropertyName("animeTitle")]
    public string AnimeTitle { get; set; } = "";

    [JsonPropertyName("episodeTitle")]
    public string EpisodeTitle { get; set; } = "";

    [JsonPropertyName("shift")]
    public double Shift { get; set; }
}

internal sealed class ApiCommentResponse
{
    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("comments")]
    public List<ApiComment> Comments { get; set; } = [];
}

internal sealed class ApiComment
{
    [JsonPropertyName("cid")]
    public long Cid { get; set; }

    [JsonPropertyName("p")]
    public string P { get; set; } = "";

    [JsonPropertyName("m")]
    public string M { get; set; } = "";

    [JsonPropertyName("t")]
    public double? T { get; set; }
}
