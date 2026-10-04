using System.Net.Http.Json;
using System.Text.Json;
using AsterPlay.Models.Danmaku;

namespace AsterPlay.Services.Danmaku;

public sealed class LogVarDanmakuSource : IDanmakuSource
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    private readonly string _baseUrl;
    private readonly string _accessToken;

    public LogVarDanmakuSource(
        string baseUrl,
        string accessToken)
    {
        _baseUrl = baseUrl.Trim().TrimEnd('/');
        _accessToken = accessToken.Trim();
    }

    public string Name => "LogVar";

    public async Task<IReadOnlyList<DanmakuComment>> LoadAsync(
        DanmakuContext context,
        CancellationToken cancellationToken)
    {
        ValidateBaseUrl();

        var candidates = new List<ApiMatchCandidate>();
        var fileName = DanmakuApiSupport.BuildMatchFileName(context);

        if (!string.IsNullOrWhiteSpace(fileName))
        {
            try
            {
                var root = await PostJsonAsync(
                    "/api/v2/match",
                    new { fileName },
                    cancellationToken);

                DanmakuApiSupport.EnsureSuccessfulResponse(
                    root,
                    "LogVar match");

                var filenameCandidates =
                    DanmakuApiSupport.ParseMatchCandidates(
                        root,
                        context,
                        fileName);

                candidates.AddRange(filenameCandidates);

                var matched =
                    root.TryGetProperty("isMatched", out var matchedValue) &&
                    matchedValue.ValueKind == JsonValueKind.True;

                PlaybackLog.Write(
                    "DanmakuSource",
                    $"LogVar filename match: itemId={context.ItemId}, fileName={fileName}, " +
                    $"matched={matched}, candidates={filenameCandidates.Count}, " +
                    $"topScore={filenameCandidates.FirstOrDefault()?.Score:0.##}");

                if (matched)
                {
                    var direct =
                        DanmakuApiSupport.SelectBestCandidate(
                            filenameCandidates,
                            context);

                    if (direct is not null)
                    {
                        return await FetchCommentsAsync(
                            direct,
                            cancellationToken);
                    }
                }
            }
            catch (Exception ex)
            {
                PlaybackLog.Write(
                    "DanmakuSource",
                    $"LogVar filename match failed; fallback to metadata search: " +
                    $"itemId={context.ItemId}, error={ex.Message}");
            }
        }

        var subject = DanmakuApiSupport.BuildSearchSubject(context);
        var episode = DanmakuApiSupport.IsEpisode(context)
            ? context.EpisodeNumber?.ToString(
                System.Globalization.CultureInfo.InvariantCulture) ?? ""
            : "movie";

        if (!string.IsNullOrWhiteSpace(subject))
        {
            try
            {
                var root = await GetJsonAsync(
                    "/api/v2/search/episodes",
                    new Dictionary<string, string?>
                    {
                        ["anime"] = subject,
                        ["episode"] = episode
                    },
                    cancellationToken);

                DanmakuApiSupport.EnsureSuccessfulResponse(
                    root,
                    "LogVar episode search");

                var metadataCandidates =
                    DanmakuApiSupport.ParseEpisodeSearchCandidates(
                        root,
                        context,
                        subject);

                candidates.AddRange(metadataCandidates);

                PlaybackLog.Write(
                    "DanmakuSource",
                    $"LogVar metadata search: itemId={context.ItemId}, anime={subject}, " +
                    $"episode={episode}, candidates={metadataCandidates.Count}, " +
                    $"topScore={metadataCandidates.FirstOrDefault()?.Score:0.##}");
            }
            catch (Exception ex)
            {
                if (candidates.Count == 0)
                    throw;

                PlaybackLog.Write(
                    "DanmakuSource",
                    $"LogVar metadata search failed; keeping filename candidates: " +
                    $"itemId={context.ItemId}, error={ex.Message}");
            }
        }

        var selected = DanmakuApiSupport.SelectBestCandidate(
            candidates,
            context);

        if (selected is null)
        {
            var top = candidates
                .OrderByDescending(candidate => candidate.Score)
                .FirstOrDefault();

            PlaybackLog.Write(
                "DanmakuSource",
                $"LogVar no confident match: itemId={context.ItemId}, fileName={fileName}, " +
                $"subject={subject}, candidates={candidates.Count}, " +
                $"top={top?.AnimeTitle} / {top?.EpisodeTitle}, topScore={top?.Score:0.##}");

            return Array.Empty<DanmakuComment>();
        }

        return await FetchCommentsAsync(
            selected,
            cancellationToken);
    }

    private async Task<IReadOnlyList<DanmakuComment>> FetchCommentsAsync(
        ApiMatchCandidate selected,
        CancellationToken cancellationToken)
    {
        var root = await GetJsonAsync(
            $"/api/v2/comment/{selected.EpisodeId}",
            new Dictionary<string, string?>
            {
                ["format"] = "json",
                ["duration"] = "true"
            },
            cancellationToken);

        DanmakuApiSupport.EnsureSuccessfulResponse(
            root,
            "LogVar comments");

        var comments = DanmakuApiSupport.ParseComments(
            root,
            "logvar");

        PlaybackLog.Write(
            "DanmakuSource",
            $"LogVar selected: episodeId={selected.EpisodeId}, anime={selected.AnimeTitle}, " +
            $"episode={selected.EpisodeTitle}, score={selected.Score:0.##}, " +
            $"reason={selected.MatchReason}, comments={comments.Count}");

        return comments;
    }

    private async Task<JsonElement> PostJsonAsync(
        string path,
        object payload,
        CancellationToken cancellationToken)
    {
        var url = DanmakuApiSupport.BuildUrl(
            _baseUrl,
            _accessToken,
            path);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            url)
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.TryAddWithoutValidation(
            "Accept",
            "application/json");
        request.Headers.TryAddWithoutValidation(
            "User-Agent",
            "AsterPlay/1.0 (Danmaku)");

        using var response = await Http.SendAsync(
            request,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(
            cancellationToken);
        using var document = await JsonDocument.ParseAsync(
            stream,
            cancellationToken: cancellationToken);

        return document.RootElement.Clone();
    }

    private async Task<JsonElement> GetJsonAsync(
        string path,
        IReadOnlyDictionary<string, string?> query,
        CancellationToken cancellationToken)
    {
        var url = DanmakuApiSupport.BuildUrl(
            _baseUrl,
            _accessToken,
            path,
            query);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            url);
        request.Headers.TryAddWithoutValidation(
            "Accept",
            "application/json");
        request.Headers.TryAddWithoutValidation(
            "User-Agent",
            "AsterPlay/1.0 (Danmaku)");

        using var response = await Http.SendAsync(
            request,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(
            cancellationToken);
        using var document = await JsonDocument.ParseAsync(
            stream,
            cancellationToken: cancellationToken);

        return document.RootElement.Clone();
    }

    private void ValidateBaseUrl()
    {
        if (!Uri.TryCreate(
                _baseUrl,
                UriKind.Absolute,
                out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp &&
             uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                "LogVar 服务器地址无效。请填写 http:// 或 https:// 开头的服务器地址。");
        }
    }
}
