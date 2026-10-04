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

    public Task<EmbyItem> GetItemAsync(string itemId) =>
        GetAsync<EmbyItem>(
            $"/Users/{Esc(UserId)}/Items/{Esc(itemId)}" +
            "?Fields=Overview,Genres,ProductionYear,CommunityRating,RunTimeTicks,UserData");

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
        PlaybackLog.Write("Emby",
            $"Resolve playback: sourceId={source.Id}, type={source.Type}, name={source.Name}");

        var playable = source;
        if (string.Equals(source.Type, "Series", StringComparison.OrdinalIgnoreCase))
        {
            var next = await GetAsync<EmbyItemsResponse>(
                $"/Shows/NextUp?UserId={Esc(UserId)}&SeriesId={Esc(source.Id)}&Limit=1" +
                "&Fields=Overview,Genres,ProductionYear,CommunityRating,RunTimeTicks,UserData");
            playable = next.Items.FirstOrDefault()
                       ?? throw new InvalidOperationException("No playable next episode was found for this series.");
            PlaybackLog.Write("Emby",
                $"Series resolved to episode: itemId={playable.Id}, name={playable.Name}");
        }

        // Do not trust the home-card UserData. Resume position can change on another
        // device, so always resolve the latest item immediately before playback.
        playable = await GetItemAsync(playable.Id);
        var resumeTicks = Math.Max(0, playable.UserData?.PlaybackPositionTicks ?? 0);
        PlaybackLog.Write("Emby",
            $"Fresh UserData: itemId={playable.Id}, resumeTicks={resumeTicks}, runtimeTicks={playable.RunTimeTicks ?? 0}");

        using var req = CreateRequest(
            HttpMethod.Post,
            $"/Items/{Esc(playable.Id)}/PlaybackInfo?UserId={Esc(UserId)}");
        req.Content = JsonContent.Create(new
        {
            UserId,
            StartTimeTicks = resumeTicks,
            IsPlayback = true,
            EnableDirectPlay = true,
            EnableDirectStream = true,
            EnableTranscoding = true
        });

        using var response = await _http.SendAsync(req);
        PlaybackLog.Write("Emby",
            $"PlaybackInfo response: status={(int)response.StatusCode} {response.ReasonPhrase}, startTimeTicks={resumeTicks}");
        await EnsureSuccess(response, "PlaybackInfo request failed");

        var info = await response.Content.ReadFromJsonAsync<PlaybackInfoResponse>(_json)
                   ?? throw new InvalidOperationException("PlaybackInfo was empty.");

        PlaybackLog.Write("Emby",
            $"PlaybackInfo: playSessionId={info.PlaySessionId}, mediaSources={info.MediaSources.Count}");
        for (var i = 0; i < info.MediaSources.Count; i++)
        {
            var candidate = info.MediaSources[i];
            PlaybackLog.Write("Emby",
                $"MediaSource[{i}]: id={candidate.Id}, container={candidate.Container}, " +
                $"directPlay={candidate.SupportsDirectPlay}, directStream={candidate.SupportsDirectStream}, " +
                $"transcode={candidate.SupportsTranscoding}, transcodingUrl={candidate.TranscodingUrl}");
        }

        var media = info.MediaSources.FirstOrDefault()
                    ?? throw new InvalidOperationException("No media source is available.");

        var playSessionId = string.IsNullOrWhiteSpace(info.PlaySessionId)
            ? Guid.NewGuid().ToString("N")
            : info.PlaySessionId;

        // qEmby's Emby branch deliberately uses a client-generated UUID for
        // /Sessions/Playing* reporting instead of PlaybackInfo's session id.
        var reportPlaySessionId = Guid.NewGuid().ToString("N");
        PlaybackLog.Write("Emby",
            $"Playback sessions: streamSessionId={playSessionId}, reportSessionId={reportPlaySessionId}");

        string url;
        bool usesServerStartOffset;
        string playMethod;

        if (resumeTicks > 0)
        {
            // Resume is performed by Emby, not by mpv. The returned stream starts at
            // the requested point, while mpv's local timeline starts from zero.
            usesServerStartOffset = true;

            if (!string.IsNullOrWhiteSpace(media.TranscodingUrl))
            {
                url = AppendToken(Combine(media.TranscodingUrl));
                playMethod = "Transcode";
                PlaybackLog.Write("Emby", "Resume strategy: negotiated TranscodingUrl");
            }
            else
            {
                var container = media.Container.Trim().TrimStart('.');
                if (string.IsNullOrWhiteSpace(container))
                    container = "mp4";

                url = WithToken(
                    $"/Videos/{Esc(playable.Id)}/stream.{Esc(container)}" +
                    $"?MediaSourceId={Esc(media.Id)}" +
                    $"&PlaySessionId={Esc(playSessionId)}" +
                    $"&StartTimeTicks={resumeTicks}" +
                    $"&DeviceId={Esc(DeviceId)}");
                playMethod = "Transcode";
                PlaybackLog.Write("Emby", "Resume strategy: dynamic stream with StartTimeTicks");
            }
        }
        else
        {
            usesServerStartOffset = false;
            playMethod = "DirectPlay";
            url = WithToken(
                $"/Videos/{Esc(playable.Id)}/stream?static=true" +
                $"&MediaSourceId={Esc(media.Id)}" +
                $"&PlaySessionId={Esc(playSessionId)}");
            PlaybackLog.Write("Emby", "Playback strategy: static direct stream");
        }

        PlaybackLog.Write("Emby",
            $"Selected source: itemId={playable.Id}, mediaSourceId={media.Id}, " +
            $"resumeTicks={resumeTicks}, serverOffset={usesServerStartOffset}, playMethod={playMethod}, url={url}");

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
            PlaySessionId = playSessionId,
            ReportPlaySessionId = reportPlaySessionId,
            ResumePositionTicks = resumeTicks,
            RunTimeTicks = playable.RunTimeTicks ?? media.RunTimeTicks,
            UsesServerStartOffset = usesServerStartOffset,
            PlayMethod = playMethod
        };
    }

    public Task ReportPlaybackStartAsync(
        PlaybackLaunch launch, long positionTicks, bool isPaused, double volume) =>
        SendPlaybackReportAsync("/Sessions/Playing", launch, positionTicks, isPaused, volume, null);

    public Task ReportPlaybackProgressAsync(
        PlaybackLaunch launch, long positionTicks, bool isPaused, double volume, string eventName = "TimeUpdate") =>
        SendPlaybackReportAsync("/Sessions/Playing/Progress", launch, positionTicks, isPaused, volume, eventName);

    public Task ReportPlaybackStoppedAsync(
        PlaybackLaunch launch, long positionTicks, bool isPaused, double volume) =>
        SendPlaybackReportAsync("/Sessions/Playing/Stopped", launch, positionTicks, isPaused, volume, null);

    private async Task SendPlaybackReportAsync(
        string path,
        PlaybackLaunch launch,
        long positionTicks,
        bool isPaused,
        double volume,
        string? eventName)
    {
        // Match the payload shape used by the proven qEmby implementation.
        // Keep this deliberately small: Emby only needs the playback identity,
        // absolute position and state for resume synchronization.
        var payload = new Dictionary<string, object?>
        {
            ["ItemId"] = launch.ItemId,
            ["MediaSourceId"] = launch.MediaSourceId,
            ["PositionTicks"] = Math.Max(0, positionTicks),
            ["PlayMethod"] = "DirectPlay",
            ["IsPaused"] = isPaused,
            ["IsMuted"] = volume <= 0.01,
            ["CanSeek"] = true,
            ["PlaySessionId"] = launch.ReportPlaySessionId,
            ["QueueableMediaTypes"] = new[] { "Video" }
        };

        if (!string.IsNullOrWhiteSpace(eventName))
            payload["EventName"] = eventName.ToLowerInvariant();

        using var req = CreateRequest(HttpMethod.Post, path);
        req.Content = JsonContent.Create(payload);

        using var response = await _http.SendAsync(req);
        PlaybackLog.Write("EmbyReport",
            $"{eventName ?? Path.GetFileName(path)}: status={(int)response.StatusCode}, itemId={launch.ItemId}, " +
            $"positionTicks={positionTicks}, paused={isPaused}, volume={volume:0.##}, reportSessionId={launch.ReportPlaySessionId}");
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
        var authorization =
            $"MediaBrowser Client=\"AsterPlay\", Device=\"Windows\", DeviceId=\"{DeviceId}\", Version=\"2.0.0\"";

        if (includeToken && !string.IsNullOrWhiteSpace(AccessToken))
            authorization += $", Token=\"{AccessToken}\"";

        request.Headers.TryAddWithoutValidation("X-Emby-Authorization", authorization);

        // Keep the dedicated token header as well for compatibility with servers
        // and reverse proxies that explicitly inspect it.
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
