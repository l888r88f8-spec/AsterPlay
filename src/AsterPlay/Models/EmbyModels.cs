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

    [JsonPropertyName("TotalRecordCount")]
    public int TotalRecordCount { get; set; }
}

public sealed class EmbyItem
{
    [JsonPropertyName("Id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("Name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("OriginalTitle")]
    public string OriginalTitle { get; set; } = "";

    [JsonPropertyName("Type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("MediaType")]
    public string MediaType { get; set; } = "";

    [JsonPropertyName("CollectionType")]
    public string CollectionType { get; set; } = "";

    [JsonPropertyName("Overview")]
    public string Overview { get; set; } = "";

    [JsonPropertyName("Taglines")]
    public List<string> Taglines { get; set; } = [];

    [JsonPropertyName("Genres")]
    public List<string> Genres { get; set; } = [];

    [JsonPropertyName("Tags")]
    public List<string> Tags { get; set; } = [];

    [JsonPropertyName("ProductionLocations")]
    public List<string> ProductionLocations { get; set; } = [];

    [JsonPropertyName("ProductionYear")]
    public int? ProductionYear { get; set; }

    [JsonPropertyName("PremiereDate")]
    public DateTimeOffset? PremiereDate { get; set; }

    [JsonPropertyName("CommunityRating")]
    public double? CommunityRating { get; set; }

    [JsonPropertyName("CriticRating")]
    public double? CriticRating { get; set; }

    [JsonPropertyName("OfficialRating")]
    public string OfficialRating { get; set; } = "";

    [JsonPropertyName("RunTimeTicks")]
    public long? RunTimeTicks { get; set; }

    [JsonPropertyName("Size")]
    public long? Size { get; set; }

    [JsonPropertyName("Bitrate")]
    public int? Bitrate { get; set; }

    [JsonPropertyName("Container")]
    public string Container { get; set; } = "";

    [JsonPropertyName("VideoCodec")]
    public string VideoCodec { get; set; } = "";

    [JsonPropertyName("AudioCodec")]
    public string AudioCodec { get; set; } = "";

    [JsonPropertyName("Width")]
    public int? Width { get; set; }

    [JsonPropertyName("Height")]
    public int? Height { get; set; }

    [JsonPropertyName("Path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("ParentId")]
    public string ParentId { get; set; } = "";

    [JsonPropertyName("SeriesId")]
    public string SeriesId { get; set; } = "";

    [JsonPropertyName("SeriesName")]
    public string SeriesName { get; set; } = "";

    [JsonPropertyName("SeasonId")]
    public string SeasonId { get; set; } = "";

    [JsonPropertyName("SeasonName")]
    public string SeasonName { get; set; } = "";

    [JsonPropertyName("Status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("SeasonCount")]
    public int? SeasonCount { get; set; }

    [JsonPropertyName("ChildCount")]
    public int? ChildCount { get; set; }

    [JsonPropertyName("ParentIndexNumber")]
    public int? ParentIndexNumber { get; set; }

    [JsonPropertyName("IndexNumber")]
    public int? IndexNumber { get; set; }

    [JsonPropertyName("People")]
    public List<EmbyPerson> People { get; set; } = [];

    [JsonPropertyName("Studios")]
    public List<EmbyNameLongIdPair> Studios { get; set; } = [];

    [JsonPropertyName("ProviderIds")]
    public Dictionary<string, string> ProviderIds { get; set; } = [];

    [JsonPropertyName("MediaSources")]
    public List<MediaSource> MediaSources { get; set; } = [];

    [JsonPropertyName("MediaStreams")]
    public List<EmbyMediaStream> MediaStreams { get; set; } = [];

    [JsonPropertyName("PrimaryImageAspectRatio")]
    public double? PrimaryImageAspectRatio { get; set; }

    [JsonPropertyName("ImageTags")]
    public Dictionary<string, string> ImageTags { get; set; } = [];

    [JsonPropertyName("BackdropImageTags")]
    public List<string> BackdropImageTags { get; set; } = [];

    [JsonPropertyName("ParentBackdropItemId")]
    public string ParentBackdropItemId { get; set; } = "";

    [JsonPropertyName("ParentBackdropImageTags")]
    public List<string> ParentBackdropImageTags { get; set; } = [];

    [JsonPropertyName("UserData")]
    public EmbyUserData? UserData { get; set; }
}

public sealed class EmbyPerson
{
    [JsonPropertyName("Name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("Id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("Role")]
    public string Role { get; set; } = "";

    [JsonPropertyName("Type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("PrimaryImageTag")]
    public string PrimaryImageTag { get; set; } = "";
}

public sealed class EmbyNameLongIdPair
{
    [JsonPropertyName("Name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("Id")]
    public long? Id { get; set; }
}

public sealed class EmbyMediaStream
{
    [JsonPropertyName("Codec")]
    public string Codec { get; set; } = "";

    [JsonPropertyName("Profile")]
    public string Profile { get; set; } = "";

    [JsonPropertyName("Type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("Index")]
    public int Index { get; set; }

    [JsonPropertyName("Language")]
    public string Language { get; set; } = "";

    [JsonPropertyName("Title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("DisplayTitle")]
    public string DisplayTitle { get; set; } = "";

    [JsonPropertyName("DisplayLanguage")]
    public string DisplayLanguage { get; set; } = "";

    [JsonPropertyName("Width")]
    public int? Width { get; set; }

    [JsonPropertyName("Height")]
    public int? Height { get; set; }

    [JsonPropertyName("AspectRatio")]
    public string AspectRatio { get; set; } = "";

    [JsonPropertyName("AverageFrameRate")]
    public double? AverageFrameRate { get; set; }

    [JsonPropertyName("RealFrameRate")]
    public double? RealFrameRate { get; set; }

    [JsonPropertyName("BitRate")]
    public int? BitRate { get; set; }

    [JsonPropertyName("BitDepth")]
    public int? BitDepth { get; set; }

    [JsonPropertyName("Channels")]
    public int? Channels { get; set; }

    [JsonPropertyName("ChannelLayout")]
    public string ChannelLayout { get; set; } = "";

    [JsonPropertyName("SampleRate")]
    public int? SampleRate { get; set; }

    [JsonPropertyName("VideoRange")]
    public string VideoRange { get; set; } = "";

    [JsonPropertyName("ColorTransfer")]
    public string ColorTransfer { get; set; } = "";

    [JsonPropertyName("ColorPrimaries")]
    public string ColorPrimaries { get; set; } = "";

    [JsonPropertyName("ColorSpace")]
    public string ColorSpace { get; set; } = "";

    [JsonPropertyName("IsDefault")]
    public bool IsDefault { get; set; }

    [JsonPropertyName("IsForced")]
    public bool IsForced { get; set; }

    [JsonPropertyName("IsExternal")]
    public bool IsExternal { get; set; }

    [JsonPropertyName("IsInterlaced")]
    public bool IsInterlaced { get; set; }

    [JsonPropertyName("DeliveryMethod")]
    public string DeliveryMethod { get; set; } = "";

    [JsonPropertyName("DeliveryUrl")]
    public string DeliveryUrl { get; set; } = "";
}

public sealed class EmbyUserData
{
    [JsonPropertyName("IsFavorite")]
    public bool IsFavorite { get; set; }

    [JsonPropertyName("Played")]
    public bool Played { get; set; }

    [JsonPropertyName("PlayCount")]
    public int PlayCount { get; set; }

    [JsonPropertyName("PlayedPercentage")]
    public double? PlayedPercentage { get; set; }

    [JsonPropertyName("PlaybackPositionTicks")]
    public long PlaybackPositionTicks { get; set; }

    [JsonPropertyName("LastPlayedDate")]
    public DateTimeOffset? LastPlayedDate { get; set; }
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
    public string OriginalTitle { get; set; } = "";
    public string SeriesName { get; set; } = "";
    public int? SeasonNumber { get; set; }
    public int? EpisodeNumber { get; set; }
    public string ItemType { get; set; } = "";
    public string SourcePath { get; set; } = "";
    public string SourceFileName { get; set; } = "";
    public string ItemId { get; set; } = "";
    public string MediaSourceId { get; set; } = "";
    public string PlaySessionId { get; set; } = "";
    public long ResumePositionTicks { get; set; }
    public long? RunTimeTicks { get; set; }
    public bool UsesServerStartOffset { get; set; }
    public bool RequiresServerSeek { get; set; }
    public string PlayMethod { get; set; } = "DirectPlay";

    // Negotiation metadata used by the player diagnostics panel.
    public string DecisionReason { get; set; } = "";
    public string SourceContainer { get; set; } = "";
    public string SourceProtocol { get; set; } = "";
    public string NegotiatedContainer { get; set; } = "";
    public string NegotiatedProtocol { get; set; } = "";
    public string SourceVideoCodec { get; set; } = "";
    public string SourceAudioCodec { get; set; } = "";
    public int? SourceWidth { get; set; }
    public int? SourceHeight { get; set; }
}

public sealed class MediaSource
{
    [JsonPropertyName("Id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("Name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("Path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("Type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("Protocol")]
    public string Protocol { get; set; } = "";

    [JsonPropertyName("Container")]
    public string Container { get; set; } = "";

    [JsonPropertyName("Size")]
    public long? Size { get; set; }

    [JsonPropertyName("Bitrate")]
    public int? Bitrate { get; set; }

    [JsonPropertyName("DirectStreamUrl")]
    public string DirectStreamUrl { get; set; } = "";

    [JsonPropertyName("AddApiKeyToDirectStreamUrl")]
    public bool AddApiKeyToDirectStreamUrl { get; set; }

    [JsonPropertyName("TranscodingContainer")]
    public string TranscodingContainer { get; set; } = "";

    [JsonPropertyName("TranscodingSubProtocol")]
    public string TranscodingSubProtocol { get; set; } = "";

    [JsonPropertyName("SupportsDirectPlay")]
    public bool SupportsDirectPlay { get; set; }

    [JsonPropertyName("SupportsDirectStream")]
    public bool SupportsDirectStream { get; set; }

    [JsonPropertyName("SupportsTranscoding")]
    public bool SupportsTranscoding { get; set; }

    [JsonPropertyName("TranscodingUrl")]
    public string TranscodingUrl { get; set; } = "";

    [JsonPropertyName("RunTimeTicks")]
    public long? RunTimeTicks { get; set; }

    [JsonPropertyName("DefaultAudioStreamIndex")]
    public int? DefaultAudioStreamIndex { get; set; }

    [JsonPropertyName("DefaultSubtitleStreamIndex")]
    public int? DefaultSubtitleStreamIndex { get; set; }

    [JsonPropertyName("MediaStreams")]
    public List<EmbyMediaStream> MediaStreams { get; set; } = [];
}

