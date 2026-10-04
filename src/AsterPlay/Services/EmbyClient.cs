using System.Text;
using System.Net.Http.Json;
using System.Text.Json;
using AsterPlay.Models;

namespace AsterPlay.Services;

public sealed class EmbyClient
{
    private const string DetailListFields =
        "Genres,MediaStreams,Overview,ParentId,Path,People,ProviderIds," +
        "PrimaryImageAspectRatio,Studios,Taglines";

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
            $"/Users/{Esc(UserId)}/Items/{Esc(itemId)}");

    public async Task<List<EmbyItem>> GetSeasonsAsync(string seriesId)
    {
        var path =
            $"/Shows/{Esc(seriesId)}/Seasons" +
            $"?UserId={Esc(UserId)}" +
            $"&Fields={Esc(DetailListFields)}" +
            "&EnableImages=true&EnableUserData=true" +
            "&ImageTypeLimit=1&EnableImageTypes=Primary,Backdrop,Thumb";

        var result = await GetAsync<EmbyItemsResponse>(path);
        return result.Items
            .OrderBy(item => item.IndexNumber ?? int.MaxValue)
            .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task<List<EmbyItem>> GetEpisodesAsync(string seriesId, string seasonId)
    {
        var path =
            $"/Shows/{Esc(seriesId)}/Episodes" +
            $"?UserId={Esc(UserId)}" +
            $"&SeasonId={Esc(seasonId)}" +
            $"&Fields={Esc(DetailListFields)}" +
            "&EnableImages=true&EnableUserData=true" +
            "&ImageTypeLimit=1&EnableImageTypes=Primary,Backdrop,Thumb";

        var result = await GetAsync<EmbyItemsResponse>(path);
        return result.Items
            .OrderBy(item => item.ParentIndexNumber ?? int.MaxValue)
            .ThenBy(item => item.IndexNumber ?? int.MaxValue)
            .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
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

    public string BuildPersonPrimaryUrl(EmbyPerson person, int maxWidth = 400) =>
        string.IsNullOrWhiteSpace(person.Id)
            ? ""
            : WithToken($"/Items/{Esc(person.Id)}/Images/Primary?maxWidth={maxWidth}&quality=90");

    public async Task<PlaybackLaunch> GetPlayableStreamAsync(EmbyItem source, bool restart = false)
    {
        PlaybackLog.Write("Emby",
            $"Resolve playback: sourceId={source.Id}, type={source.Type}, name={source.Name}, restart={restart}");

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
        var resumeTicks = restart
            ? 0
            : Math.Max(0, playable.UserData?.PlaybackPositionTicks ?? 0);
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

        PlaybackLog.Write("Emby",
            $"Playback session: playSessionId={playSessionId} (shared by stream/start/progress/stop)");

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
                playMethod = "DirectStream";
                PlaybackLog.Write("Emby",
                    "Resume strategy: dynamic stream with StartTimeTicks (stream copy/direct stream)");
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
        var payload = new Dictionary<string, object?>
        {
            ["QueueableMediaTypes"] = new[] { "Audio", "Video", "Photo" },
            ["CanSeek"] = true,
            ["ItemId"] = launch.ItemId,
            ["MediaSourceId"] = launch.MediaSourceId,
            ["PlaySessionId"] = launch.PlaySessionId,
            ["PositionTicks"] = Math.Max(0, positionTicks),
            ["IsPaused"] = isPaused,
            ["IsMuted"] = volume <= 0.01,
            ["VolumeLevel"] = (int)Math.Clamp(Math.Round(volume), 0, 100),
            ["PlayMethod"] = launch.PlayMethod,
            ["PlaybackRate"] = 1.0,
            ["Shuffle"] = false,
            ["RepeatMode"] = "RepeatNone",
            ["PlaylistIndex"] = 0,
            ["PlaylistLength"] = 1
        };

        if (long.TryParse(launch.ItemId, out var queueItemId))
        {
            payload["NowPlayingQueue"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["Id"] = queueItemId,
                    ["PlaylistItemId"] = "0"
                }
            };
        }

        if (launch.RunTimeTicks is > 0)
            payload["RunTimeTicks"] = launch.RunTimeTicks.Value;

        if (!string.IsNullOrWhiteSpace(eventName))
            payload["EventName"] = eventName;

        var payloadJson = JsonSerializer.Serialize(payload, _json);
        PlaybackLog.Write("EmbyReportPayload", $"path={path}, body={payloadJson}");

        using var req = CreatePlaybackReportRequest(HttpMethod.Post, path);
        req.Content = new StringContent(payloadJson, Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(req);
        PlaybackLog.Write("EmbyReport",
            $"{eventName ?? Path.GetFileName(path)}: status={(int)response.StatusCode}, itemId={launch.ItemId}, " +
            $"positionTicks={positionTicks}, paused={isPaused}, volume={volume:0.##}, playSessionId={launch.PlaySessionId}, playMethod={launch.PlayMethod}");
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

    private HttpRequestMessage CreatePlaybackReportRequest(HttpMethod method, string path)
    {
        // Match the identity parameters used by current official Emby apps for
        // playback check-ins. These establish the device/session auth context
        // that /Sessions/Playing* relies on.
        var url = Combine(path);
        url = AppendQueryParameter(url, "X-Emby-Client", "AsterPlay");
        url = AppendQueryParameter(url, "X-Emby-Device-Name", "Windows");
        url = AppendQueryParameter(url, "X-Emby-Device-Id", DeviceId);
        url = AppendQueryParameter(url, "X-Emby-Client-Version", "2.0.0");
        url = AppendQueryParameter(url, "X-Emby-Token", AccessToken);
        url = AppendQueryParameter(url, "reqformat", "json");

        return CreateRequest(method, url);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path, bool includeToken = true)
    {
        var request = new HttpRequestMessage(method, Combine(path));

        string authorization;
        if (includeToken &&
            !string.IsNullOrWhiteSpace(UserId) &&
            !string.IsNullOrWhiteSpace(AccessToken))
        {
            authorization =
                $"Emby UserId=\"{UserId}\", Client=\"AsterPlay\", Device=\"Windows\", " +
                $"DeviceId=\"{DeviceId}\", Version=\"2.0.0\", Token=\"{AccessToken}\"";
        }
        else
        {
            authorization =
                $"Emby Client=\"AsterPlay\", Device=\"Windows\", DeviceId=\"{DeviceId}\", Version=\"2.0.0\"";
        }

        request.Headers.TryAddWithoutValidation("X-Emby-Authorization", authorization);

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
