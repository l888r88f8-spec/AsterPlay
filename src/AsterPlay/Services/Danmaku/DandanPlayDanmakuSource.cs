using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AsterPlay.Models.Danmaku;

namespace AsterPlay.Services.Danmaku;

public sealed class DandanPlayDanmakuSource : IDanmakuSource
{
    private const string BaseUrl = "https://api.dandanplay.net";

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _appId;
    private readonly string _appSecret;
    private readonly bool _withRelated;

    public DandanPlayDanmakuSource(
        string appId,
        string appSecret,
        bool withRelated)
    {
        _appId = appId.Trim();
        _appSecret = appSecret;
        _withRelated = withRelated;
    }

    public string Name => "弹弹play开放弹幕网络";

    public async Task<IReadOnlyList<DanmakuComment>> LoadAsync(
        DanmakuContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_appId) ||
            string.IsNullOrWhiteSpace(_appSecret))
        {
            throw new InvalidOperationException(
                "弹弹play 数据源需要配置 AppId 和 AppSecret。");
        }

        var fileName = DanmakuApiSupport.BuildMatchFileName(context);
        const string matchPath = "/api/v2/match";

        using var matchRequest = CreateSignedRequest(
            HttpMethod.Post,
            matchPath);
        matchRequest.Content = JsonContent.Create(new
        {
            fileName,
            videoDuration = context.DurationSeconds > 0
                ? (int)Math.Round(context.DurationSeconds)
                : 0,
            matchMode = "fileNameOnly"
        });

        using var matchResponse = await Http.SendAsync(
            matchRequest,
            cancellationToken);
        matchResponse.EnsureSuccessStatusCode();

        var matched = await matchResponse.Content.ReadFromJsonAsync<ApiMatchResponse>(
            JsonOptions,
            cancellationToken);

        var selected = matched?.Matches
            .FirstOrDefault(item => item.EpisodeId > 0);

        if (selected is null)
        {
            PlaybackLog.Write(
                "DanmakuSource",
                $"DandanPlay match empty: itemId={context.ItemId}, matchName={fileName}, message={matched?.ErrorMessage}");
            return Array.Empty<DanmakuComment>();
        }

        var commentPath = $"/api/v2/comment/{selected.EpisodeId}";
        var query = _withRelated
            ? "?withRelated=true&chConvert=1"
            : "?chConvert=1";

        using var commentRequest = CreateSignedRequest(
            HttpMethod.Get,
            commentPath,
            query);
        using var commentResponse = await Http.SendAsync(
            commentRequest,
            cancellationToken);
        commentResponse.EnsureSuccessStatusCode();

        var payload = await commentResponse.Content.ReadFromJsonAsync<ApiCommentResponse>(
            JsonOptions,
            cancellationToken);

        var comments = DanmakuApiSupport.ParseComments(
            payload?.Comments,
            "dandanplay",
            selected.Shift);

        PlaybackLog.Write(
            "DanmakuSource",
            $"DandanPlay matched: itemId={context.ItemId}, anime={selected.AnimeTitle}, episode={selected.EpisodeTitle}, episodeId={selected.EpisodeId}, comments={comments.Count}, related={_withRelated}");

        return comments;
    }

    private HttpRequestMessage CreateSignedRequest(
        HttpMethod method,
        string path,
        string query = "")
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signingPath = path.ToLowerInvariant();
        var input = $"{_appId}{timestamp}{signingPath}{_appSecret}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        var signature = Convert.ToBase64String(hash);

        var request = new HttpRequestMessage(
            method,
            $"{BaseUrl}{path}{query}");
        request.Headers.TryAddWithoutValidation("X-AppId", _appId);
        request.Headers.TryAddWithoutValidation(
            "X-Timestamp",
            timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation("X-Signature", signature);
        return request;
    }
}
