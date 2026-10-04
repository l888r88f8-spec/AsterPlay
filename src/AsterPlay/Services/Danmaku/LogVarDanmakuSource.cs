using System.Net.Http.Json;
using System.Text.Json;
using AsterPlay.Models.Danmaku;

namespace AsterPlay.Services.Danmaku;

public sealed class LogVarDanmakuSource : IDanmakuSource
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _baseUrl;

    public LogVarDanmakuSource(string baseUrl)
    {
        _baseUrl = baseUrl.Trim().TrimEnd('/');
    }

    public string Name => "LogVar";

    public async Task<IReadOnlyList<DanmakuComment>> LoadAsync(
        DanmakuContext context,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(_baseUrl, UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttp &&
             baseUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                "LogVar 服务器地址无效。请填写 http:// 或 https:// 开头的基础地址。");
        }

        var fileName = DanmakuApiSupport.BuildMatchFileName(context);
        var matchUrl = DanmakuApiSupport.CombineBaseAndPath(
            _baseUrl,
            "/api/v2/match");

        using var matchRequest = new HttpRequestMessage(
            HttpMethod.Post,
            matchUrl)
        {
            Content = JsonContent.Create(new { fileName })
        };

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
                $"LogVar match empty: itemId={context.ItemId}, matchName={fileName}, message={matched?.ErrorMessage}");
            return Array.Empty<DanmakuComment>();
        }

        var commentUrl = DanmakuApiSupport.CombineBaseAndPath(
            _baseUrl,
            $"/api/v2/comment/{selected.EpisodeId}?format=json&duration=true");

        using var commentResponse = await Http.GetAsync(
            commentUrl,
            cancellationToken);
        commentResponse.EnsureSuccessStatusCode();

        var payload = await commentResponse.Content.ReadFromJsonAsync<ApiCommentResponse>(
            JsonOptions,
            cancellationToken);

        var comments = DanmakuApiSupport.ParseComments(
            payload?.Comments,
            "logvar",
            selected.Shift);

        PlaybackLog.Write(
            "DanmakuSource",
            $"LogVar matched: itemId={context.ItemId}, anime={selected.AnimeTitle}, episode={selected.EpisodeTitle}, episodeId={selected.EpisodeId}, comments={comments.Count}");

        return comments;
    }
}
