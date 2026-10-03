using System.Text.Json.Serialization;

namespace AsterPlay.Models;

public sealed class EmbySession
{
    public string ServerUrl { get; set; } = "";
    public string UserId { get; set; } = "";
    public string AccessToken { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string UserName { get; set; } = "";
}

public sealed class AuthResponse
{
    [JsonPropertyName("AccessToken")]
    public string AccessToken { get; set; } = "";

    [JsonPropertyName("User")]
    public EmbyUser? User { get; set; }
}

public sealed class EmbyUser
{
    [JsonPropertyName("Id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("Name")]
    public string Name { get; set; } = "";
}

public sealed class EmbyItemsResponse
{
    [JsonPropertyName("Items")]
    public List<EmbyItem> Items { get; set; } = [];
}

public sealed class EmbyItem
{
    [JsonPropertyName("Id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("Name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("Type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("CollectionType")]
    public string CollectionType { get; set; } = "";

    [JsonPropertyName("Overview")]
    public string Overview { get; set; } = "";

    [JsonPropertyName("ProductionYear")]
    public int? ProductionYear { get; set; }

    [JsonPropertyName("CommunityRating")]
    public double? CommunityRating { get; set; }

    [JsonPropertyName("RunTimeTicks")]
    public long? RunTimeTicks { get; set; }

    [JsonPropertyName("ParentIndexNumber")]
    public int? ParentIndexNumber { get; set; }

    [JsonPropertyName("IndexNumber")]
    public int? IndexNumber { get; set; }

    [JsonPropertyName("Genres")]
    public List<string> Genres { get; set; } = [];

    [JsonPropertyName("ImageTags")]
    public Dictionary<string, string> ImageTags { get; set; } = [];

    [JsonPropertyName("BackdropImageTags")]
    public List<string> BackdropImageTags { get; set; } = [];

    [JsonPropertyName("UserData")]
    public EmbyUserData? UserData { get; set; }
}

public sealed class EmbyUserData
{
    [JsonPropertyName("IsFavorite")]
    public bool IsFavorite { get; set; }

    [JsonPropertyName("PlayedPercentage")]
    public double? PlayedPercentage { get; set; }

    [JsonPropertyName("PlaybackPositionTicks")]
    public long PlaybackPositionTicks { get; set; }
}

public sealed class PlaybackInfoResponse
{
    [JsonPropertyName("PlaySessionId")]
    public string PlaySessionId { get; set; } = "";

    [JsonPropertyName("MediaSources")]
    public List<MediaSource> MediaSources { get; set; } = [];
}

public sealed class PlaybackLaunch
{
    public string Url { get; set; } = "";
    public string Title { get; set; } = "";
    public string ItemId { get; set; } = "";
    public string MediaSourceId { get; set; } = "";
    public string PlaySessionId { get; set; } = "";
    public long ResumePositionTicks { get; set; }
    public long? RunTimeTicks { get; set; }
}

public sealed class MediaSource
{
    [JsonPropertyName("Id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("Container")]
    public string Container { get; set; } = "";

    [JsonPropertyName("DirectStreamUrl")]
    public string DirectStreamUrl { get; set; } = "";

    [JsonPropertyName("SupportsDirectPlay")]
    public bool SupportsDirectPlay { get; set; }
}
