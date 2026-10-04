using System.Text;
using System.Net.Http.Json;
using System.Text.Json;
using AsterPlay.Models;
using AsterPlay.Services.Playback;

namespace AsterPlay.Services;

public sealed class PlaybackStateChangedEventArgs : EventArgs
{
    public required string ItemId { get; init; }
    public long PositionTicks { get; init; }
    public long? RunTimeTicks { get; init; }
    public string EventName { get; init; } = "";
    public bool IsPaused { get; init; }
}

public sealed class EmbyClient
{
    public event EventHandler<PlaybackStateChangedEventArgs>? PlaybackStateChanged;
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
            "&SortBy=DatePlayed&SortOrder=Descending" +
            "&Fields=Overview,Genres,ProductionYear,CommunityRating,RunTimeTicks,UserData" +
            "&EnableImageTypes=Primary,Backdrop,Thumb&ImageTypeLimit=1";

        var items = (await GetAsync<EmbyItemsResponse>(path)).Items;

        // DatePlayed is an official Items sort field. Keep the server order as a
        // stable fallback when LastPlayedDate is missing or identical.
        return items
            .Select((item, index) => new { Item = item, Index = index })
            .OrderByDescending(x => x.Item.UserData?.LastPlayedDate ?? DateTimeOffset.MinValue)
            .ThenBy(x => x.Index)
            .Select(x => x.Item)
            .ToList();
    }

    public async Task<EmbyItemsResponse> GetLibraryItemsAsync(
        string? parentId,
        string? searchTerm,
        string includeItemTypes,
        int? year,
        string sortBy,
        string sortOrder,
        bool favoriteOnly,
        int startIndex,
        int limit)
    {
        var query = new List<string>
        {
            "Recursive=true",
            $"StartIndex={Math.Max(0, startIndex)}",
            $"Limit={Math.Clamp(limit, 1, 100)}",
            $"IncludeItemTypes={Esc(string.IsNullOrWhiteSpace(includeItemTypes) ? "Movie,Series" : includeItemTypes)}",
            $"SortBy={Esc(string.IsNullOrWhiteSpace(sortBy) ? "SortName" : sortBy)}",
            $"SortOrder={Esc(string.Equals(sortOrder, "Ascending", StringComparison.OrdinalIgnoreCase) ? "Ascending" : "Descending")}",
            "Fields=Overview,Genres,ProductionYear,CommunityRating,RunTimeTicks,UserData",
            "EnableImages=true",
            "EnableUserData=true",
            "ImageTypeLimit=1",
            "EnableImageTypes=Primary,Backdrop"
        };

        if (!string.IsNullOrWhiteSpace(parentId))
            query.Add($"ParentId={Esc(parentId)}");

        if (!string.IsNullOrWhiteSpace(searchTerm))
            query.Add($"SearchTerm={Esc(searchTerm.Trim())}");

        if (year is > 0)
            query.Add($"Years={year.Value}");

        if (favoriteOnly)
            query.Add("IsFavorite=true");

        var path =
            $"/Users/{Esc(UserId)}/Items?" +
            string.Join("&", query);

        return await GetAsync<EmbyItemsResponse>(path);
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

    public async Task<PlaybackInfoResponse> GetMediaInfoAsync(string itemId)
    {
        using var req = CreateRequest(
            HttpMethod.Post,
            $"/Items/{Esc(itemId)}/PlaybackInfo" +
            $"?UserId={Esc(UserId)}" +
            "&StartTimeTicks=0" +
            "&IsPlayback=false" +
            "&AutoOpenLiveStream=false");

        req.Content = JsonContent.Create(new
        {
            UserId,
            StartTimeTicks = 0L,
            IsPlayback = false,
            AutoOpenLiveStream = false
        });

        using var response = await _http.SendAsync(req);
        PlaybackLog.Write(
            "DetailsMedia",
            $"PlaybackInfo details: itemId={itemId}, status={(int)response.StatusCode} {response.ReasonPhrase}");

        await EnsureSuccess(response, "Media info request failed");

        var info = await response.Content.ReadFromJsonAsync<PlaybackInfoResponse>(_json)
                   ?? throw new InvalidOperationException("Media info response was empty.");

        PlaybackLog.Write(
            "DetailsMedia",
            $"itemId={itemId}, mediaSources={info.MediaSources.Count}, " +
            $"mediaStreams={info.MediaSources.Sum(source => source.MediaStreams.Count)}");

        return info;
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

    public async Task<PlaybackLaunch> GetPlayableStreamAsync(
        EmbyItem source,
        bool restart = false,
        long? startTimeTicksOverride = null,
        string? preferredMediaSourceId = null,
        string? currentPlaySessionId = null)
    {
        PlaybackLog.Write("Emby",
            $"Resolve playback: sourceId={source.Id}, type={source.Type}, name={source.Name}, restart={restart}, " +
            $"startOverride={startTimeTicksOverride?.ToString() ?? "-"}, preferredMediaSourceId={preferredMediaSourceId ?? "-"}, " +
            $"currentPlaySessionId={currentPlaySessionId ?? "-"}");

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
        var resumeTicks = startTimeTicksOverride.HasValue
            ? Math.Max(0, startTimeTicksOverride.Value)
            : restart
                ? 0
                : Math.Max(0, playable.UserData?.PlaybackPositionTicks ?? 0);
        PlaybackLog.Write("Emby",
            $"Fresh UserData: itemId={playable.Id}, resumeTicks={resumeTicks}, runtimeTicks={playable.RunTimeTicks ?? 0}");

        var playbackInfoPath =
            $"/Items/{Esc(playable.Id)}/PlaybackInfo?UserId={Esc(UserId)}" +
            $"&StartTimeTicks={resumeTicks}&IsPlayback=true";

        if (!string.IsNullOrWhiteSpace(preferredMediaSourceId))
            playbackInfoPath += $"&MediaSourceId={Esc(preferredMediaSourceId)}";

        if (!string.IsNullOrWhiteSpace(currentPlaySessionId))
            playbackInfoPath += $"&CurrentPlaySessionId={Esc(currentPlaySessionId)}";

        using var req = CreateRequest(HttpMethod.Post, playbackInfoPath);

        var playbackRequest = new PlaybackInfoRequestPayload
        {
            UserId = UserId,
            StartTimeTicks = resumeTicks,
            MediaSourceId = string.IsNullOrWhiteSpace(preferredMediaSourceId)
                ? null
                : preferredMediaSourceId,
            CurrentPlaySessionId = string.IsNullOrWhiteSpace(currentPlaySessionId)
                ? null
                : currentPlaySessionId,
            IsPlayback = true,
            EnableDirectPlay = true,
            EnableDirectStream = true,
            EnableTranscoding = true,
            AllowVideoStreamCopy = true,
            AllowAudioStreamCopy = true,
            AllowInterlacedVideoStreamCopy = true,
            DeviceProfile = PlaybackProfile.CreateDeviceProfile()
        };

        PlaybackLog.Write(
            "PlaybackProfile",
            $"Request profile: directPlayProfiles={playbackRequest.DeviceProfile.DirectPlayProfiles.Count}, " +
            $"transcodingProfiles={playbackRequest.DeviceProfile.TranscodingProfiles.Count}, " +
            $"subtitleProfiles={playbackRequest.DeviceProfile.SubtitleProfiles.Count}, bitrateCap=none");

        req.Content = JsonContent.Create(playbackRequest, options: _json);

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
                $"transcode={candidate.SupportsTranscoding}, " +
                $"directStreamUrl={PlaybackLog.Redact(candidate.DirectStreamUrl)}, " +
                $"transcodingUrl={PlaybackLog.Redact(candidate.TranscodingUrl)}, " +
                $"transcodingContainer={candidate.TranscodingContainer}, subProtocol={candidate.TranscodingSubProtocol}");
        }

        IReadOnlyList<MediaSource> decisionSources = info.MediaSources;

        if (!string.IsNullOrWhiteSpace(preferredMediaSourceId))
        {
            var preferredSources = info.MediaSources
                .Where(source => string.Equals(
                    source.Id,
                    preferredMediaSourceId,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (preferredSources.Count > 0)
            {
                decisionSources = preferredSources;
            }
            else
            {
                PlaybackLog.Write(
                    "PlaybackDecision",
                    $"Preferred media source {preferredMediaSourceId} was not returned; falling back to all sources.");
            }
        }

        var decision = PlaybackDecisionSelector.Select(
            decisionSources,
            requiresServerStartOffset: resumeTicks > 0);

        var media = decision.MediaSource;

        var playSessionId = string.IsNullOrWhiteSpace(info.PlaySessionId)
            ? Guid.NewGuid().ToString("N")
            : info.PlaySessionId;

        PlaybackLog.Write("PlaybackDecision",
            $"kind={decision.Kind}, playMethod={decision.PlayMethod}, mediaSourceId={media.Id}, " +
            $"container={media.Container}, reason={decision.Reason}");

        PlaybackLog.Write("Emby",
            $"Playback session: playSessionId={playSessionId} (shared by stream/start/progress/stop)");

        string url;
        bool usesServerStartOffset;
        bool requiresServerSeek;
        var playMethod = decision.PlayMethod;

        switch (decision.Kind)
        {
            case PlaybackDecisionKind.DirectPlay:
                usesServerStartOffset = false;
                requiresServerSeek = false;

                var directPlayContainer = media.Container.Trim().TrimStart('.');
                if (string.IsNullOrWhiteSpace(directPlayContainer))
                    directPlayContainer = "mp4";

                url = WithToken(
                    $"/Videos/{Esc(playable.Id)}/stream.{Esc(directPlayContainer)}?static=true" +
                    $"&MediaSourceId={Esc(media.Id)}" +
                    $"&PlaySessionId={Esc(playSessionId)}" +
                    $"&DeviceId={Esc(DeviceId)}");

                PlaybackLog.Write(
                    "PlaybackDecision",
                    $"URL strategy: static DirectPlay stream with explicit container .{directPlayContainer}");
                break;

            case PlaybackDecisionKind.DirectStream:
                usesServerStartOffset = resumeTicks > 0;
                requiresServerSeek = true;

                if (!string.IsNullOrWhiteSpace(media.DirectStreamUrl))
                {
                    url = Combine(media.DirectStreamUrl);

                    if (resumeTicks > 0)
                    {
                        url = AppendQueryParameter(
                            url,
                            "StartTimeTicks",
                            resumeTicks.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    }

                    url = AppendToken(url);
                    PlaybackLog.Write(
                        "PlaybackDecision",
                        "URL strategy: negotiated DirectStreamUrl");
                }
                else
                {
                    var container = media.Container.Trim().TrimStart('.');
                    if (string.IsNullOrWhiteSpace(container))
                        container = "mp4";

                    var directStreamPath =
                        $"/Videos/{Esc(playable.Id)}/stream.{Esc(container)}" +
                        $"?MediaSourceId={Esc(media.Id)}" +
                        $"&PlaySessionId={Esc(playSessionId)}" +
                        $"&DeviceId={Esc(DeviceId)}";

                    if (resumeTicks > 0)
                        directStreamPath += $"&StartTimeTicks={resumeTicks}";

                    url = WithToken(directStreamPath);
                    PlaybackLog.Write(
                        "PlaybackDecision",
                        "URL strategy: dynamic DirectStream endpoint");
                }
                break;

            case PlaybackDecisionKind.Transcode:
                requiresServerSeek = true;

                if (string.IsNullOrWhiteSpace(media.TranscodingUrl))
                {
                    throw new InvalidOperationException(
                        "Emby selected transcoding but did not return a TranscodingUrl.");
                }

                usesServerStartOffset = resumeTicks > 0;
                url = Combine(media.TranscodingUrl);

                if (resumeTicks > 0)
                {
                    url = AppendQueryParameter(
                        url,
                        "StartTimeTicks",
                        resumeTicks.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }

                url = AppendToken(url);
                PlaybackLog.Write(
                    "PlaybackDecision",
                    $"URL strategy: negotiated TranscodingUrl ({media.TranscodingContainer}/{media.TranscodingSubProtocol})");
                break;

            default:
                throw new InvalidOperationException("Unsupported playback decision.");
        }

        PlaybackLog.Write("Emby",
            $"Selected source: itemId={playable.Id}, mediaSourceId={media.Id}, " +
            $"resumeTicks={resumeTicks}, serverOffset={usesServerStartOffset}, playMethod={playMethod}, url={url}");

        var title = playable.IndexNumber is > 0
            ? playable.ParentIndexNumber is > 0
                ? $"S{playable.ParentIndexNumber:00}E{playable.IndexNumber:00} · {playable.Name}"
                : $"E{playable.IndexNumber:00} · {playable.Name}"
            : playable.Name;

        var sourceVideo = media.MediaStreams.FirstOrDefault(stream =>
            string.Equals(stream.Type, "Video", StringComparison.OrdinalIgnoreCase));

        var sourceAudio = media.MediaStreams.FirstOrDefault(stream =>
                              string.Equals(stream.Type, "Audio", StringComparison.OrdinalIgnoreCase) &&
                              stream.IsDefault)
                          ?? media.MediaStreams.FirstOrDefault(stream =>
                              string.Equals(stream.Type, "Audio", StringComparison.OrdinalIgnoreCase));

        var negotiatedContainer = decision.Kind == PlaybackDecisionKind.Transcode &&
                                  !string.IsNullOrWhiteSpace(media.TranscodingContainer)
            ? media.TranscodingContainer
            : media.Container;

        var negotiatedProtocol = decision.Kind == PlaybackDecisionKind.Transcode &&
                                 !string.IsNullOrWhiteSpace(media.TranscodingSubProtocol)
            ? media.TranscodingSubProtocol
            : media.Protocol;

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
            RequiresServerSeek = requiresServerSeek,
            PlayMethod = playMethod,
            DecisionReason = decision.Reason,
            SourceContainer = media.Container,
            SourceProtocol = media.Protocol,
            NegotiatedContainer = negotiatedContainer,
            NegotiatedProtocol = negotiatedProtocol,
            SourceVideoCodec = sourceVideo?.Codec ?? "",
            SourceAudioCodec = sourceAudio?.Codec ?? "",
            SourceWidth = sourceVideo?.Width,
            SourceHeight = sourceVideo?.Height
        };
    }

    public Task<PlaybackLaunch> ReopenPlayableStreamAsync(
        PlaybackLaunch current,
        long startTimeTicks) =>
        GetPlayableStreamAsync(
            new EmbyItem { Id = current.ItemId },
            restart: false,
            startTimeTicksOverride: Math.Max(0, startTimeTicks),
            preferredMediaSourceId: current.MediaSourceId,
            currentPlaySessionId: current.PlaySessionId);

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

        try
        {
            PlaybackStateChanged?.Invoke(
                this,
                new PlaybackStateChangedEventArgs
                {
                    ItemId = launch.ItemId,
                    PositionTicks = Math.Max(0, positionTicks),
                    RunTimeTicks = launch.RunTimeTicks,
                    EventName = eventName ?? Path.GetFileName(path),
                    IsPaused = isPaused
                });
        }
        catch (Exception ex)
        {
            // UI refresh listeners must never make a successful Emby playback
            // report look like a playback-report failure.
            PlaybackLog.Error("PlaybackStateChanged", ex);
        }
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
