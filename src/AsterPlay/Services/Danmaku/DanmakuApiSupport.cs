using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using AsterPlay.Models.Danmaku;

namespace AsterPlay.Services.Danmaku;

internal static class DanmakuApiSupport
{
    public static bool IsEpisode(DanmakuContext context) =>
        string.Equals(
            context.ItemType,
            "Episode",
            StringComparison.OrdinalIgnoreCase) ||
        (!string.IsNullOrWhiteSpace(context.SeriesName) &&
         context.EpisodeNumber is > 0);

    public static string BuildMatchFileName(DanmakuContext context)
    {
        if (!string.IsNullOrWhiteSpace(context.FileName))
            return context.FileName.Trim();

        if (!string.IsNullOrWhiteSpace(context.MediaPath))
        {
            try
            {
                var path = context.MediaPath.Trim();
                if (Uri.TryCreate(path, UriKind.Absolute, out var uri) &&
                    !string.IsNullOrWhiteSpace(uri.LocalPath))
                {
                    path = uri.LocalPath;
                }

                var name = Path.GetFileNameWithoutExtension(path);
                if (!string.IsNullOrWhiteSpace(name))
                    return name;
            }
            catch
            {
            }
        }

        if (IsEpisode(context) &&
            !string.IsNullOrWhiteSpace(context.SeriesName) &&
            context.EpisodeNumber is > 0)
        {
            var season = context.SeasonNumber is > 0
                ? context.SeasonNumber.Value
                : 1;

            return $"{context.SeriesName} S{season:00}E{context.EpisodeNumber.Value:00}";
        }

        return context.Title.Trim();
    }

    public static string BuildSearchSubject(DanmakuContext context)
    {
        if (IsEpisode(context) &&
            !string.IsNullOrWhiteSpace(context.SeriesName))
        {
            var subject = context.SeriesName.Trim();
            if (context.SeasonNumber is > 1 &&
                ExtractSeasonNumber(subject) <= 0)
            {
                subject += $" S{context.SeasonNumber.Value:00}";
            }

            return subject;
        }

        if (!string.IsNullOrWhiteSpace(context.OriginalTitle))
            return context.OriginalTitle.Trim();

        return context.Title.Trim();
    }

    public static string BuildUrl(
        string baseUrl,
        string accessToken,
        string apiPath,
        IReadOnlyDictionary<string, string?>? query = null)
    {
        var builder = new UriBuilder(baseUrl.Trim());
        var path = builder.Path.TrimEnd('/');

        var token = accessToken.Trim();
        if (!string.IsNullOrWhiteSpace(token))
        {
            var lastSegment = path
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .LastOrDefault();

            if (!string.Equals(
                    lastSegment,
                    token,
                    StringComparison.Ordinal))
            {
                path += "/" + Uri.EscapeDataString(token);
            }
        }

        path += "/" + apiPath.TrimStart('/');
        builder.Path = path;

        if (query is null || query.Count == 0)
        {
            builder.Query = "";
            return builder.Uri.ToString();
        }

        builder.Query = string.Join(
            "&",
            query
                .Where(item => item.Value is not null)
                .Select(item =>
                    $"{Uri.EscapeDataString(item.Key)}={Uri.EscapeDataString(item.Value!)}"));

        return builder.Uri.ToString();
    }

    public static IReadOnlyList<DanmakuMatchCandidate> ParseMatchCandidates(
        JsonElement root,
        DanmakuContext context,
        string queryText)
    {
        var result = new List<DanmakuMatchCandidate>();
        var matched = BoolField(root, "isMatched");

        if (!TryGetArray(root, "matches", out var matches))
            return result;

        foreach (var value in matches.EnumerateArray())
        {
            var episodeId = LongField(
                value,
                "episodeId",
                "id",
                "episodeID");
            if (episodeId <= 0)
                continue;

            var animeTitle = StringField(
                value,
                "animeTitle",
                "animeName",
                "title");
            var episodeTitle = StringField(
                value,
                "episodeTitle",
                "name");

            var candidate = new DanmakuMatchCandidate(
                episodeId,
                animeTitle,
                episodeTitle,
                ExtractSeasonNumber(animeTitle),
                ExtractEpisodeNumber(episodeTitle),
                0,
                matched ? "filename" : "match-candidate");

            candidate = candidate with
            {
                Score = ScoreCandidate(
                    context,
                    candidate,
                    queryText) + (matched ? 10 : 0)
            };

            result.Add(candidate);
        }

        return Deduplicate(result);
    }

    public static IReadOnlyList<DanmakuSeriesMatchCandidate> ParseSeriesSearchCandidates(
        JsonElement root,
        DanmakuContext context,
        string queryText)
    {
        var result = new List<DanmakuSeriesMatchCandidate>();

        if (!TryGetArray(root, "animes", out var animes))
            return result;

        foreach (var animeValue in animes.EnumerateArray())
        {
            var animeId = LongField(
                animeValue,
                "animeId",
                "id",
                "animeID");
            var animeTitle = StringField(
                animeValue,
                "animeTitle",
                "title",
                "name");

            if (string.IsNullOrWhiteSpace(animeTitle))
                continue;

            var animeSeason = ExtractSeasonNumber(animeTitle);
            var episodes = new List<DanmakuMatchCandidate>();

            if (TryGetArray(animeValue, "episodes", out var episodeArray))
            {
                foreach (var episodeValue in episodeArray.EnumerateArray())
                {
                    var episodeId = LongField(
                        episodeValue,
                        "episodeId",
                        "id",
                        "episodeID");
                    if (episodeId <= 0)
                        continue;

                    var episodeTitle = StringField(
                        episodeValue,
                        "episodeTitle",
                        "title",
                        "name");

                    var episodeNumber = IntField(
                        episodeValue,
                        -1,
                        "episodeNumber",
                        "episode",
                        "sort");

                    if (episodeNumber <= 0)
                        episodeNumber = ExtractEpisodeNumber(episodeTitle);

                    var candidate = new DanmakuMatchCandidate(
                        episodeId,
                        animeTitle,
                        episodeTitle,
                        animeSeason,
                        episodeNumber,
                        0,
                        "manual-series");

                    candidate = candidate with
                    {
                        Score = ScoreCandidate(
                            context,
                            candidate,
                            queryText)
                    };

                    episodes.Add(candidate);
                }
            }

            if (episodes.Count == 0)
                continue;

            var titleScore =
                TitleScore(
                    DanmakuApiSupport.IsEpisode(context)
                        ? context.SeriesName
                        : context.Title,
                    animeTitle) * 80d;

            var seasonScore = 0d;
            if (context.SeasonNumber is > 0)
            {
                if (animeSeason == context.SeasonNumber.Value)
                    seasonScore = 24d;
                else if (animeSeason > 0)
                    seasonScore = -30d;
                else if (context.SeasonNumber.Value == 1)
                    seasonScore = 5d;
            }

            var episodeScore = episodes.Max(item => item.Score) * 0.2d;
            var score = titleScore + seasonScore + episodeScore;

            result.Add(new DanmakuSeriesMatchCandidate(
                animeId,
                animeTitle,
                animeSeason,
                score,
                episodes
                    .OrderBy(item =>
                        item.EpisodeNumber > 0
                            ? item.EpisodeNumber
                            : int.MaxValue)
                    .ThenBy(item => item.EpisodeTitle)
                    .ToArray()));
        }

        return result
            .GroupBy(item =>
                item.AnimeId > 0
                    ? $"id:{item.AnimeId}"
                    : $"title:{item.AnimeTitle}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
                group.OrderByDescending(item => item.Score).First())
            .OrderByDescending(item => item.Score)
            .ToArray();
    }

    public static IReadOnlyList<DanmakuMatchCandidate> ParseEpisodeSearchCandidates(
        JsonElement root,
        DanmakuContext context,
        string queryText)
    {
        var result = new List<DanmakuMatchCandidate>();

        if (!TryGetArray(root, "animes", out var animes))
            return result;

        foreach (var animeValue in animes.EnumerateArray())
        {
            var animeTitle = StringField(
                animeValue,
                "animeTitle",
                "title",
                "name");
            var animeSeason = ExtractSeasonNumber(animeTitle);

            if (!TryGetArray(animeValue, "episodes", out var episodes))
                continue;

            foreach (var episodeValue in episodes.EnumerateArray())
            {
                var episodeId = LongField(
                    episodeValue,
                    "episodeId",
                    "id",
                    "episodeID");
                if (episodeId <= 0)
                    continue;

                var episodeTitle = StringField(
                    episodeValue,
                    "episodeTitle",
                    "title",
                    "name");

                var episodeNumber = IntField(
                    episodeValue,
                    -1,
                    "episodeNumber",
                    "episode",
                    "sort");

                if (episodeNumber <= 0)
                    episodeNumber = ExtractEpisodeNumber(episodeTitle);

                if (episodeNumber <= 0 &&
                    context.EpisodeNumber is > 0)
                {
                    episodeNumber = context.EpisodeNumber.Value;
                }

                var candidate = new DanmakuMatchCandidate(
                    episodeId,
                    animeTitle,
                    episodeTitle,
                    animeSeason,
                    episodeNumber,
                    0,
                    "search");

                if (IsEpisode(context))
                {
                    if (context.EpisodeNumber is > 0 &&
                        candidate.EpisodeNumber > 0 &&
                        candidate.EpisodeNumber != context.EpisodeNumber.Value)
                    {
                        continue;
                    }

                    if (context.SeasonNumber is > 0 &&
                        candidate.SeasonNumber > 0 &&
                        candidate.SeasonNumber != context.SeasonNumber.Value)
                    {
                        continue;
                    }
                }

                candidate = candidate with
                {
                    Score = ScoreCandidate(
                        context,
                        candidate,
                        queryText)
                };

                result.Add(candidate);
            }
        }

        return Deduplicate(result);
    }

    public static DanmakuMatchCandidate? SelectBestCandidate(
        IEnumerable<DanmakuMatchCandidate> candidates,
        DanmakuContext context)
    {
        var best = candidates
            .OrderByDescending(candidate => candidate.Score)
            .FirstOrDefault();

        if (best is null)
            return null;

        var threshold = IsEpisode(context) ? 72d : 62d;
        return best.Score >= threshold ? best : null;
    }

    public static IReadOnlyList<DanmakuComment> ParseComments(
        JsonElement root,
        string idPrefix)
    {
        if (!TryGetArray(root, "comments", out var comments))
            return Array.Empty<DanmakuComment>();

        var result = new List<DanmakuComment>();
        var index = 0;

        foreach (var value in comments.EnumerateArray())
        {
            var text = StringField(value, "m", "text", "content");
            if (string.IsNullOrWhiteSpace(text))
            {
                index++;
                continue;
            }

            var timeSeconds = -1d;
            var mode = DanmakuMode.Scroll;
            var colorArgb = 0xFFFFFFFFu;

            var p = StringField(value, "p");
            if (!string.IsNullOrWhiteSpace(p))
            {
                var parts = p.Split(
                    ',',
                    StringSplitOptions.None);

                if (parts.Length > 0)
                {
                    _ = double.TryParse(
                        parts[0],
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out timeSeconds);
                }

                if (parts.Length > 1 &&
                    int.TryParse(
                        parts[1],
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var parsedMode))
                {
                    mode = MapMode(parsedMode);
                }

                if (parts.Length > 2)
                    colorArgb = ParseColor(parts[2]);
            }
            else
            {
                if (TryDoubleField(value, out var t, "t"))
                {
                    timeSeconds = t;
                }
                else if (TryDoubleField(value, out var rawTime, "time"))
                {
                    timeSeconds = rawTime > 0 && rawTime < 1000
                        ? rawTime
                        : rawTime / 1000d;
                }

                mode = MapMode(
                    IntField(
                        value,
                        1,
                        "mode",
                        "positionType"));

                colorArgb = ParseColor(
                    StringField(value, "color"));
            }

            if (!double.IsFinite(timeSeconds) ||
                timeSeconds < 0)
            {
                index++;
                continue;
            }

            var cid = LongField(value, "cid", "id");
            var id = cid > 0
                ? $"{idPrefix}-{cid}"
                : $"{idPrefix}-{index}-{timeSeconds:0.###}";

            result.Add(new DanmakuComment(
                id,
                timeSeconds,
                text.Trim(),
                mode,
                colorArgb));

            index++;
        }

        return result;
    }

    public static void EnsureSuccessfulResponse(
        JsonElement root,
        string operation)
    {
        if (!root.TryGetProperty(
                "success",
                out var successValue) ||
            successValue.ValueKind != JsonValueKind.False)
        {
            return;
        }

        var message = StringField(
            root,
            "errorMessage",
            "message",
            "error");

        throw new InvalidOperationException(
            $"{operation}: {(string.IsNullOrWhiteSpace(message) ? "request rejected" : message)}");
    }

    public static int ExtractSeasonNumber(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        var patterns = new[]
        {
            @"(?:^|[^A-Za-z0-9])S\s*0*(\d{1,2})(?:[^A-Za-z0-9]|$)",
            @"第\s*0*(\d{1,2})\s*[季部期]",
            @"(?:Season\s*|)(\d{1,2})(?:st|nd|rd|th)?\s*(?:Season|期)"
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(
                text,
                pattern,
                RegexOptions.IgnoreCase);

            if (match.Success &&
                int.TryParse(match.Groups[1].Value, out var value))
            {
                return value;
            }
        }

        return 0;
    }

    public static int ExtractEpisodeNumber(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        var patterns = new[]
        {
            @"(?:^|[^A-Za-z0-9])S\s*0*\d{1,2}\s*E\s*0*(\d{1,4})(?:[^A-Za-z0-9]|$)",
            @"第\s*0*(\d{1,4})\s*[话話集期]",
            @"(?:^|[^A-Za-z0-9])(?:EP?|Episode)\s*[._-]?\s*0*(\d{1,4})(?:[^A-Za-z0-9]|$)"
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(
                text,
                pattern,
                RegexOptions.IgnoreCase);

            if (match.Success &&
                int.TryParse(match.Groups[1].Value, out var value))
            {
                return value;
            }
        }

        return int.TryParse(text.Trim(), out var direct)
            ? direct
            : 0;
    }

    private static double ScoreCandidate(
        DanmakuContext context,
        DanmakuMatchCandidate candidate,
        string queryText)
    {
        var subject = IsEpisode(context)
            ? context.SeriesName
            : context.Title;

        var candidateSubject =
            !string.IsNullOrWhiteSpace(candidate.AnimeTitle)
                ? candidate.AnimeTitle
                : candidate.EpisodeTitle;

        var score =
            TitleScore(subject, candidateSubject) * 55d +
            TitleScore(queryText, candidateSubject) * 20d +
            TitleScore(context.Title, candidate.EpisodeTitle) * 8d;

        if (IsEpisode(context) &&
            context.EpisodeNumber is > 0 &&
            candidate.EpisodeNumber == context.EpisodeNumber.Value)
        {
            score += 24d;
        }

        if (IsEpisode(context) &&
            context.SeasonNumber is > 0)
        {
            if (candidate.SeasonNumber == context.SeasonNumber.Value)
            {
                score += 24d;
            }
            else if (candidate.SeasonNumber > 0)
            {
                score -= 35d;
            }
            else if (context.SeasonNumber.Value == 1)
            {
                score += 5d;
            }
        }

        return score;
    }

    private static double TitleScore(string lhs, string rhs)
    {
        var left = NormalizeTitle(lhs);
        var right = NormalizeTitle(rhs);

        if (left.Length == 0 || right.Length == 0)
            return 0;

        if (left == right)
            return 1;

        if (left.Contains(right) || right.Contains(left))
            return 0.82;

        if (left.Length < 2 || right.Length < 2)
            return 0;

        var leftPairs = BuildPairs(left);
        var rightPairs = BuildPairs(right);
        var common = leftPairs.Count(pair => rightPairs.Contains(pair));

        return (2d * common) /
               (leftPairs.Count + rightPairs.Count);
    }

    private static string NormalizeTitle(string value) =>
        new(
            value
                .Trim()
                .ToLowerInvariant()
                .Where(char.IsLetterOrDigit)
                .ToArray());

    private static HashSet<string> BuildPairs(string value)
    {
        var pairs = new HashSet<string>(
            StringComparer.Ordinal);

        for (var index = 0;
             index + 1 < value.Length;
             index++)
        {
            pairs.Add(value.Substring(index, 2));
        }

        return pairs;
    }

    private static IReadOnlyList<DanmakuMatchCandidate> Deduplicate(
        IEnumerable<DanmakuMatchCandidate> candidates) =>
        candidates
            .GroupBy(candidate => candidate.EpisodeId)
            .Select(group =>
                group.OrderByDescending(item => item.Score).First())
            .OrderByDescending(item => item.Score)
            .ToArray();

    private static DanmakuMode MapMode(int mode) =>
        mode switch
        {
            4 => DanmakuMode.Bottom,
            5 => DanmakuMode.Top,
            _ => DanmakuMode.Scroll
        };

    private static uint ParseColor(string value)
    {
        if (uint.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var rgb))
        {
            return 0xFF000000u | (rgb & 0x00FFFFFFu);
        }

        if (value.StartsWith("#", StringComparison.Ordinal) &&
            uint.TryParse(
                value.AsSpan(1),
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out rgb))
        {
            return 0xFF000000u | (rgb & 0x00FFFFFFu);
        }

        return 0xFFFFFFFFu;
    }

    private static bool TryGetArray(
        JsonElement root,
        string name,
        out JsonElement array)
    {
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty(name, out array) &&
            array.ValueKind == JsonValueKind.Array)
        {
            return true;
        }

        array = default;
        return false;
    }

    private static string StringField(
        JsonElement root,
        params string[] keys)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return "";

        foreach (var key in keys)
        {
            if (!root.TryGetProperty(key, out var value) ||
                value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                continue;
            }

            var text = value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : value.ToString();

            if (!string.IsNullOrWhiteSpace(text))
                return text.Trim();
        }

        return "";
    }

    private static int IntField(
        JsonElement root,
        int fallback,
        params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!TryLongField(root, out var value, key))
                continue;

            if (value >= int.MinValue &&
                value <= int.MaxValue)
            {
                return (int)value;
            }
        }

        return fallback;
    }

    private static long LongField(
        JsonElement root,
        params string[] keys) =>
        TryLongField(root, out var value, keys)
            ? value
            : 0;

    private static bool TryLongField(
        JsonElement root,
        out long value,
        params string[] keys)
    {
        value = 0;

        if (root.ValueKind != JsonValueKind.Object)
            return false;

        foreach (var key in keys)
        {
            if (!root.TryGetProperty(key, out var element))
                continue;

            if (element.ValueKind == JsonValueKind.Number &&
                element.TryGetInt64(out value))
            {
                return true;
            }

            if (long.TryParse(
                    element.ToString(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out value))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryDoubleField(
        JsonElement root,
        out double value,
        params string[] keys)
    {
        value = 0;

        if (root.ValueKind != JsonValueKind.Object)
            return false;

        foreach (var key in keys)
        {
            if (!root.TryGetProperty(key, out var element))
                continue;

            if (element.ValueKind == JsonValueKind.Number &&
                element.TryGetDouble(out value))
            {
                return true;
            }

            if (double.TryParse(
                    element.ToString(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out value))
            {
                return true;
            }
        }

        return false;
    }

    private static bool BoolField(
        JsonElement root,
        string key)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty(key, out var value))
        {
            return false;
        }

        if (value.ValueKind == JsonValueKind.True)
            return true;
        if (value.ValueKind == JsonValueKind.False)
            return false;

        return bool.TryParse(value.ToString(), out var parsed) &&
               parsed;
    }
}

public sealed record DanmakuSeriesMatchCandidate(
    long AnimeId,
    string AnimeTitle,
    int SeasonNumber,
    double Score,
    IReadOnlyList<DanmakuMatchCandidate> Episodes)
{
    public string DisplayTitle => AnimeTitle;

    public string DetailText
    {
        get
        {
            var parts = new List<string>();

            if (SeasonNumber > 0)
                parts.Add($"S{SeasonNumber:00}");

            parts.Add($"{Episodes.Count} 集");
            parts.Add($"评分 {Score:0.#}");

            if (AnimeId > 0)
                parts.Add($"animeId {AnimeId}");

            return string.Join(" · ", parts);
        }
    }
}

public sealed record DanmakuMatchCandidate(
    long EpisodeId,
    string AnimeTitle,
    string EpisodeTitle,
    int SeasonNumber,
    int EpisodeNumber,
    double Score,
    string MatchReason)
{
    public string DisplayTitle =>
        string.IsNullOrWhiteSpace(AnimeTitle)
            ? EpisodeTitle
            : string.IsNullOrWhiteSpace(EpisodeTitle)
                ? AnimeTitle
                : $"{AnimeTitle} · {EpisodeTitle}";

    public string DetailText
    {
        get
        {
            var parts = new List<string>();

            if (SeasonNumber > 0)
                parts.Add($"S{SeasonNumber:00}");
            if (EpisodeNumber > 0)
                parts.Add($"E{EpisodeNumber:00}");

            parts.Add($"episodeId {EpisodeId}");
            parts.Add($"评分 {Score:0.#}");

            return string.Join(" · ", parts);
        }
    }
}
