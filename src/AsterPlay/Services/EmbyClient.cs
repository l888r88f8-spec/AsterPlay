using System.Net.Http.Json;
using System.Text.Json;
using AsterPlay.Models;

namespace AsterPlay.Services;

public sealed class EmbyClient
{
    private readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(25)
    };

    private readonly JsonSerializerOptions _json = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public string ServerUrl { get; private set; } = "";
    public string UserId { get; private set; } = "";
    public string AccessToken { get; private set; } = "";
    public string UserName { get; private set; } = "";
    public string DeviceId { get; private set; } = Guid.NewGuid().ToString("N");

    public bool IsAuthenticated =>
        !string.IsNullOrWhiteSpace(ServerUrl) &&
        !string.IsNullOrWhiteSpace(UserId) &&
        !string.IsNullOrWhiteSpace(AccessToken);

    public async Task<EmbySession> AuthenticateAsync(string serverUrl, string userName, string password)
    {
        ServerUrl = NormalizeServerUrl(serverUrl);
        UserName = userName.Trim();

        var request = CreateRequest(HttpMethod.Post, "/Users/AuthenticateByName", includeToken: false);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Username"] = UserName,
            ["Pw"] = password
        });

        using var response = await _http.SendAsync(request);
        await EnsureSuccess(response, "Login failed");

        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(_json)
                   ?? throw new InvalidOperationException("The server returned an empty login response.");

        AccessToken = auth.AccessToken;
        UserId = auth.User?.Id ?? "";
        UserName = auth.User?.Name ?? UserName;

        if (string.IsNullOrWhiteSpace(AccessToken) || string.IsNullOrWhiteSpace(UserId))
            throw new InvalidOperationException("The server did not return a valid access token/user id.");

        var session = ExportSession();
        AppStateStore.Save(session);
        return session;
    }

    public void Restore(EmbySession session)
    {
        ServerUrl = NormalizeServerUrl(session.ServerUrl);
        UserId = session.UserId;
        AccessToken = session.AccessToken;
        DeviceId = string.IsNullOrWhiteSpace(session.DeviceId)
            ? Guid.NewGuid().ToString("N")
            : session.DeviceId;
        UserName = session.UserName;
    }

    public void Reset()
    {
        ServerUrl = "";
        UserId = "";
        AccessToken = "";
        UserName = "";
        DeviceId = Guid.NewGuid().ToString("N");
    }

    public EmbySession ExportSession() => new()
    {
        ServerUrl = ServerUrl,
        UserId = UserId,
        AccessToken = AccessToken,
        DeviceId = DeviceId,
        UserName = UserName
    };

    public async Task<List<EmbyItem>> GetViewsAsync()
    {
        var result = await GetAsync<EmbyItemsResponse>($"/Users/{Esc(UserId)}/Views");
        return result.Items;
    }

    public async Task<List<EmbyItem>> GetLibraryLatestAsync(string parentId, int limit = 8)
    {
        var path =
            $"/Users/{Esc(UserId)}/Items?ParentId={Esc(parentId)}&Recursive=true" +
            $"&SortBy=DateCreated&SortOrder=Descending&IncludeItemTypes=Movie,Series&Limit={limit}" +
            "&Fields=Overview,Genres,ProductionYear,CommunityRating,RunTimeTicks,UserData" +
            "&EnableImageTypes=Primary,Backdrop&ImageTypeLimit=1";
        return (await GetAsync<EmbyItemsResponse>(path)).Items;
    }

    public async Task<List<EmbyItem>> GetLatestAsync(int limit = 16)
    {
        var path =
            $"/Users/{Esc(UserId)}/Items?Recursive=true&SortBy=DateCreated&SortOrder=Descending" +
            $"&IncludeItemTypes=Movie,Series&Limit={limit}" +
            "&Fields=Overview,Genres,ProductionYear,CommunityRating,RunTimeTicks,UserData" +
            "&EnableImageTypes=Primary,Backdrop&ImageTypeLimit=1";
        return (await GetAsync<EmbyItemsResponse>(path)).Items;
    }

    public async Task<List<EmbyItem>> GetResumeAsync(int limit = 16)
    {
        var path =
            $"/Users/{Esc(UserId)}/Items/Resume?Recursive=true&MediaTypes=Video&Limit={limit}" +
            "&Fields=Overview,Genres,ProductionYear,CommunityRating,RunTimeTicks,UserData" +
            "&EnableImageTypes=Primary,Backdrop,Thumb&ImageTypeLimit=1";
        return (await GetAsync<EmbyItemsResponse>(path)).Items;
    }

    public async Task<bool> SetFavoriteAsync(string itemId, bool favorite)
    {
        var method = favorite ? HttpMethod.Post : HttpMethod.Delete;
        using var req = CreateRequest(method, $"/Users/{Esc(UserId)}/FavoriteItems/{Esc(itemId)}");
        if (favorite)
            req.Content = JsonContent.Create(new { });

        using var response = await _http.SendAsync(req);
        await EnsureSuccess(response, "Failed to update favorite");
        return favorite;
    }

    public string BuildBackdropUrl(EmbyItem item, int maxWidth = 1920)
    {
        if (item.BackdropImageTags.Count > 0)
            return WithToken($"/Items/{Esc(item.Id)}/Images/Backdrop/0?maxWidth={maxWidth}&quality=90");

        return BuildPrimaryUrl(item, maxWidth);
    }

    public string BuildPrimaryUrl(EmbyItem item, int maxWidth = 500) =>
        WithToken($"/Items/{Esc(item.Id)}/Images/Primary?maxWidth={maxWidth}&quality=90");

    public async Task<PlaybackLaunch> GetPlayableStreamAsync(EmbyItem source)
    {
        PlaybackLog.Write("Emby", $"Resolve playback: sourceId={source.Id}, type={source.Type}, name={source.Name}, resumeTicks={source.UserData?.PlaybackPositionTicks ?? 0}");

        var playable = source;
        if (string.Equals(source.Type, "Series", StringComparison.OrdinalIgnoreCase))
        {
            var next = await GetAsync<EmbyItemsResponse>(
                $"/Shows/NextUp?UserId={Esc(UserId)}&SeriesId={Esc(source.Id)}&Limit=1" +
                "&Fields=Overview,Genres,ProductionYear,CommunityRating,RunTimeTicks,UserData");
            playable = next.Items.FirstOrDefault()
                       ?? throw new InvalidOperationException("No playable next episode was found for this series.");
            PlaybackLog.Write("Emby", $"Series resolved to episode: itemId={playable.Id}, name={playable.Name}");
        }

        using var req = CreateRequest(
            HttpMethod.Post,
            $"/Items/{Esc(playable.Id)}/PlaybackInfo?UserId={Esc(UserId)}");
        req.Content = JsonContent.Create(new
        {
            UserId
        });

        using var response = await _http.SendAsync(req);
        PlaybackLog.Write("Emby", $"PlaybackInfo response: status={(int)response.StatusCode} {response.ReasonPhrase}");
        await EnsureSuccess(response, "PlaybackInfo request failed");

        var info = await response.Content.ReadFromJsonAsync<PlaybackInfoResponse>(_json)
                   ?? throw new InvalidOperationException("PlaybackInfo was empty.");

        PlaybackLog.Write("Emby", $"PlaybackInfo: playSessionId={info.PlaySessionId}, mediaSources={info.MediaSources.Count}");
        for (var i = 0; i < info.MediaSources.Count; i++)
        {
            var candidate = info.MediaSources[i];
            PlaybackLog.Write("Emby",
                $"MediaSource[{i}]: id={candidate.Id}, container={candidate.Container}, supportsDirectPlay={candidate.SupportsDirectPlay}, directStreamUrl={candidate.DirectStreamUrl}");
        }

        var media = info.MediaSources.FirstOrDefault()
                    ?? throw new InvalidOperationException("No media source is available.");

        // Keep the same playback strategy that has already proven reliable in qEmby:
        // use Emby's static video stream endpoint instead of preferring DirectStreamUrl.
        var url = WithToken(
            $"/Videos/{Esc(playable.Id)}/stream?static=true&mediaSourceId={Esc(media.Id)}");
        PlaybackLog.Write("Emby", $"Selected source: itemId={playable.Id}, mediaSourceId={media.Id}, url={url}");

        var title = playable.IndexNumber is > 0
            ? playable.ParentIndexNumber is > 0
                ? $"S{playable.ParentIndexNumber:00}E{playable.IndexNumber:00} · {playable.Name}"
                : $"E{playable.IndexNumber:00} · {playable.Name}"
            : playable.Name;

        return new PlaybackLaunch
        {
            Url = url,
            Title = title,
            ItemId = playable.Id,
            MediaSourceId = media.Id,
            PlaySessionId = string.IsNullOrWhiteSpace(info.PlaySessionId)
                ? Guid.NewGuid().ToString("N")
                : info.PlaySessionId,
            ResumePositionTicks = Math.Max(0, playable.UserData?.PlaybackPositionTicks ?? 0),
            RunTimeTicks = playable.RunTimeTicks
        };
    }

    public Task ReportPlaybackStartAsync(
        PlaybackLaunch launch, long positionTicks, bool isPaused, double volume) =>
        SendPlaybackReportAsync("/Sessions/Playing", launch, positionTicks, isPaused, volume, "play");

    public Task ReportPlaybackProgressAsync(
        PlaybackLaunch launch, long positionTicks, bool isPaused, double volume) =>
        SendPlaybackReportAsync("/Sessions/Playing/Progress", launch, positionTicks, isPaused, volume, "timeupdate");

    public Task ReportPlaybackStoppedAsync(
        PlaybackLaunch launch, long positionTicks, bool isPaused, double volume) =>
        SendPlaybackReportAsync("/Sessions/Playing/Stopped", launch, positionTicks, isPaused, volume, "stop");

    private async Task SendPlaybackReportAsync(
        string path,
        PlaybackLaunch launch,
        long positionTicks,
        bool isPaused,
        double volume,
        string eventName)
    {
        using var req = CreateRequest(HttpMethod.Post, path);
        req.Content = JsonContent.Create(new
        {
            launch.ItemId,
            launch.MediaSourceId,
            launch.PlaySessionId,
            PositionTicks = Math.Max(0, positionTicks),
            IsPaused = isPaused,
            IsMuted = volume <= 0.01,
            VolumeLevel = (int)Math.Clamp(Math.Round(volume), 0, 100),
            PlayMethod = "DirectPlay",
            CanSeek = true,
            EventName = eventName
        });

        using var response = await _http.SendAsync(req);
        PlaybackLog.Write("EmbyReport",
            $"{eventName}: status={(int)response.StatusCode}, itemId={launch.ItemId}, positionTicks={positionTicks}, paused={isPaused}, volume={volume:0.##}");
        await EnsureSuccess(response, "Playback session update failed");
    }

    private async Task<T> GetAsync<T>(string path)
    {
        using var req = CreateRequest(HttpMethod.Get, path);
        using var response = await _http.SendAsync(req);
        await EnsureSuccess(response, "Emby request failed");
        return await response.Content.ReadFromJsonAsync<T>(_json)
               ?? throw new InvalidOperationException("The server returned an empty response.");
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path, bool includeToken = true)
    {
        var request = new HttpRequestMessage(method, Combine(path));
        request.Headers.TryAddWithoutValidation(
            "X-Emby-Authorization",
            $"MediaBrowser Client=\"AsterPlay\", Device=\"Windows\", DeviceId=\"{DeviceId}\", Version=\"2.0.0\"");

        if (includeToken && !string.IsNullOrWhiteSpace(AccessToken))
            request.Headers.TryAddWithoutValidation("X-Emby-Token", AccessToken);

        return request;
    }

    private string Combine(string path)
    {
        if (Uri.TryCreate(path, UriKind.Absolute, out var absolute))
            return absolute.ToString();

        return ServerUrl.TrimEnd('/') + "/" + path.TrimStart('/');
    }

    private string WithToken(string path) => AppendToken(Combine(path));

    private static string AppendQueryParameter(string url, string name, string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            url.Contains(name + "=", StringComparison.OrdinalIgnoreCase))
            return url;

        return url + (url.Contains('?') ? "&" : "?") +
               Uri.EscapeDataString(name) + "=" + Uri.EscapeDataString(value);
    }

    private string AppendToken(string url)
    {
        if (string.IsNullOrWhiteSpace(AccessToken) ||
            url.Contains("api_key=", StringComparison.OrdinalIgnoreCase))
            return url;

        return url + (url.Contains('?') ? "&" : "?") + "api_key=" + Uri.EscapeDataString(AccessToken);
    }

    private static string NormalizeServerUrl(string value)
    {
        value = value.Trim().TrimEnd('/');
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("Enter a valid http:// or https:// Emby server address.");

        return value;
    }

    private static string Esc(string value) => Uri.EscapeDataString(value ?? "");

    private static async Task EnsureSuccess(HttpResponseMessage response, string prefix)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync();
        if (body.Length > 300)
            body = body[..300];

        throw new HttpRequestException($"{prefix}: HTTP {(int)response.StatusCode} {response.ReasonPhrase}. {body}");
    }
}
