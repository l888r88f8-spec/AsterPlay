using System.Text.Json.Serialization;
using AsterPlay.Models;

namespace AsterPlay.Services.Playback;

public sealed class PlaybackInfoRequestPayload
{
    [JsonPropertyName("UserId")]
    public string UserId { get; init; } = "";

    [JsonPropertyName("StartTimeTicks")]
    public long StartTimeTicks { get; init; }

    [JsonPropertyName("IsPlayback")]
    public bool IsPlayback { get; init; } = true;

    [JsonPropertyName("EnableDirectPlay")]
    public bool EnableDirectPlay { get; init; } = true;

    [JsonPropertyName("EnableDirectStream")]
    public bool EnableDirectStream { get; init; } = true;

    [JsonPropertyName("EnableTranscoding")]
    public bool EnableTranscoding { get; init; } = true;

    [JsonPropertyName("AllowVideoStreamCopy")]
    public bool AllowVideoStreamCopy { get; init; } = true;

    [JsonPropertyName("AllowAudioStreamCopy")]
    public bool AllowAudioStreamCopy { get; init; } = true;

    [JsonPropertyName("AllowInterlacedVideoStreamCopy")]
    public bool AllowInterlacedVideoStreamCopy { get; init; } = true;

    [JsonPropertyName("DeviceProfile")]
    public PlaybackDeviceProfile DeviceProfile { get; init; } = PlaybackProfile.CreateDeviceProfile();
}

public sealed class PlaybackDeviceProfile
{
    [JsonPropertyName("Name")]
    public string Name { get; init; } = "AsterPlay mpv";

    [JsonPropertyName("SupportedMediaTypes")]
    public string SupportedMediaTypes { get; init; } = "Video";

    [JsonPropertyName("MaxStreamingBitrate")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? MaxStreamingBitrate { get; init; }

    [JsonPropertyName("DirectPlayProfiles")]
    public List<PlaybackDirectPlayProfile> DirectPlayProfiles { get; init; } = [];

    [JsonPropertyName("TranscodingProfiles")]
    public List<PlaybackTranscodingProfile> TranscodingProfiles { get; init; } = [];

    [JsonPropertyName("ContainerProfiles")]
    public List<object> ContainerProfiles { get; init; } = [];

    [JsonPropertyName("CodecProfiles")]
    public List<object> CodecProfiles { get; init; } = [];

    [JsonPropertyName("ResponseProfiles")]
    public List<object> ResponseProfiles { get; init; } = [];

    [JsonPropertyName("SubtitleProfiles")]
    public List<PlaybackSubtitleProfile> SubtitleProfiles { get; init; } = [];
}

public sealed class PlaybackDirectPlayProfile
{
    [JsonPropertyName("Container")]
    public string Container { get; init; } = "";

    [JsonPropertyName("AudioCodec")]
    public string AudioCodec { get; init; } = "";

    [JsonPropertyName("VideoCodec")]
    public string VideoCodec { get; init; } = "";

    [JsonPropertyName("Type")]
    public string Type { get; init; } = "Video";
}

public sealed class PlaybackTranscodingProfile
{
    [JsonPropertyName("Container")]
    public string Container { get; init; } = "ts";

    [JsonPropertyName("Type")]
    public string Type { get; init; } = "Video";

    [JsonPropertyName("VideoCodec")]
    public string VideoCodec { get; init; } = "h264";

    [JsonPropertyName("AudioCodec")]
    public string AudioCodec { get; init; } = "aac,ac3";

    [JsonPropertyName("Protocol")]
    public string Protocol { get; init; } = "hls";

    [JsonPropertyName("Context")]
    public string Context { get; init; } = "Streaming";

    [JsonPropertyName("MaxAudioChannels")]
    public string MaxAudioChannels { get; init; } = "8";

    [JsonPropertyName("CopyTimestamps")]
    public bool CopyTimestamps { get; init; } = true;
}

public sealed class PlaybackSubtitleProfile
{
    [JsonPropertyName("Format")]
    public string Format { get; init; } = "";

    [JsonPropertyName("Method")]
    public string Method { get; init; } = "Embed";
}

public static class PlaybackProfile
{
    private const string BroadAudioCodecs =
        "aac,ac3,eac3,truehd,dts,dca,mp2,mp3,opus,vorbis,flac,alac,pcm_s16le,pcm_s24le";

    public static PlaybackDeviceProfile CreateDeviceProfile() => new()
    {
        // Do not invent an implicit network bitrate cap. mpv can direct play
        // high-bitrate local content; a future user setting can populate this.
        MaxStreamingBitrate = null,

        DirectPlayProfiles =
        [
            new PlaybackDirectPlayProfile
            {
                Container = "mkv,matroska,webm",
                VideoCodec = "h264,hevc,h265,av1,vp9,vp8,mpeg2video,mpeg4,vc1",
                AudioCodec = BroadAudioCodecs
            },
            new PlaybackDirectPlayProfile
            {
                Container = "mp4,m4v,mov",
                VideoCodec = "h264,hevc,h265,av1,mpeg4",
                AudioCodec = "aac,ac3,eac3,mp3,opus,flac,alac"
            },
            new PlaybackDirectPlayProfile
            {
                Container = "ts,mpegts,m2ts,mts",
                VideoCodec = "h264,hevc,h265,mpeg2video,vc1",
                AudioCodec = "aac,ac3,eac3,truehd,dts,dca,mp2"
            },
            new PlaybackDirectPlayProfile
            {
                Container = "avi",
                VideoCodec = "h264,mpeg4,msmpeg4v3,vc1",
                AudioCodec = "mp3,ac3,dts,dca,pcm_s16le"
            },
            new PlaybackDirectPlayProfile
            {
                Container = "mpg,mpeg,vob",
                VideoCodec = "mpeg1video,mpeg2video",
                AudioCodec = "mp2,mp3,ac3,dts,dca"
            },
            new PlaybackDirectPlayProfile
            {
                Container = "ogg,ogv",
                VideoCodec = "theora,vp8,vp9",
                AudioCodec = "vorbis,opus"
            }
        ],

        TranscodingProfiles =
        [
            new PlaybackTranscodingProfile
            {
                Container = "ts",
                Protocol = "hls",
                VideoCodec = "h264",
                AudioCodec = "aac,ac3",
                MaxAudioChannels = "8",
                Context = "Streaming",
                CopyTimestamps = true
            }
        ],

        // AsterPlay currently consumes subtitle tracks from the media stream
        // itself. Do not claim External sidecar delivery until DeliveryUrl is
        // explicitly attached to mpv via sub-add.
        SubtitleProfiles =
        [
            new PlaybackSubtitleProfile { Format = "srt", Method = "Embed" },
            new PlaybackSubtitleProfile { Format = "subrip", Method = "Embed" },
            new PlaybackSubtitleProfile { Format = "ass", Method = "Embed" },
            new PlaybackSubtitleProfile { Format = "ssa", Method = "Embed" },
            new PlaybackSubtitleProfile { Format = "webvtt", Method = "Embed" },
            new PlaybackSubtitleProfile { Format = "vtt", Method = "Embed" },
            new PlaybackSubtitleProfile { Format = "sub", Method = "Embed" },
            new PlaybackSubtitleProfile { Format = "pgssub", Method = "Embed" },
            new PlaybackSubtitleProfile { Format = "pgs", Method = "Embed" },
            new PlaybackSubtitleProfile { Format = "dvdsub", Method = "Embed" },
            new PlaybackSubtitleProfile { Format = "dvbsub", Method = "Embed" }
        ]
    };
}

public enum PlaybackDecisionKind
{
    DirectPlay,
    DirectStream,
    Transcode
}

public sealed record PlaybackDecision(
    MediaSource MediaSource,
    PlaybackDecisionKind Kind,
    string Reason)
{
    public string PlayMethod => Kind switch
    {
        PlaybackDecisionKind.DirectPlay => "DirectPlay",
        PlaybackDecisionKind.DirectStream => "DirectStream",
        _ => "Transcode"
    };
}

public static class PlaybackDecisionSelector
{
    public static PlaybackDecision Select(
        IReadOnlyList<MediaSource> sources,
        bool hasStartPosition)
    {
        if (sources.Count == 0)
            throw new InvalidOperationException("No media source is available.");

        // Emby's documented direct-streaming path is a static HTTP stream.
        // That path is client-seekable, so a resume position must not force us
        // into a dynamic server-offset stream.
        var directPlay = sources.FirstOrDefault(source => source.SupportsDirectPlay);
        if (directPlay is not null)
        {
            return new PlaybackDecision(
                directPlay,
                PlaybackDecisionKind.DirectPlay,
                hasStartPosition
                    ? "server marked source as DirectPlay compatible; resume will use client-side seek"
                    : "server marked source as DirectPlay compatible");
        }

        var directStream = sources.FirstOrDefault(source => source.SupportsDirectStream);
        if (directStream is not null)
        {
            return new PlaybackDecision(
                directStream,
                PlaybackDecisionKind.DirectStream,
                hasStartPosition
                    ? "server marked source as DirectStream compatible; static HTTP stream will use client-side seek"
                    : "server marked source as DirectStream compatible; using static HTTP stream");
        }

        var transcode = sources.FirstOrDefault(source =>
            source.SupportsTranscoding &&
            !string.IsNullOrWhiteSpace(source.TranscodingUrl));

        if (transcode is not null)
        {
            return new PlaybackDecision(
                transcode,
                PlaybackDecisionKind.Transcode,
                hasStartPosition
                    ? "DirectPlay/DirectStream unavailable; transcoding will use server-side StartTimeTicks"
                    : "DirectPlay/DirectStream unavailable; using negotiated TranscodingUrl");
        }

        // Compatibility fallback for older Emby servers that return URLs but do
        // not populate all Supports* flags consistently.
        var urlTranscode = sources.FirstOrDefault(source =>
            !string.IsNullOrWhiteSpace(source.TranscodingUrl));

        if (urlTranscode is not null)
        {
            return new PlaybackDecision(
                urlTranscode,
                PlaybackDecisionKind.Transcode,
                "compatibility fallback: TranscodingUrl present without capability flags");
        }

        throw new InvalidOperationException(
            "Emby did not return a playable DirectPlay, DirectStream, or Transcode source.");
    }
}
