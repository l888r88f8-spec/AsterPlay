namespace AsterPlay.Services.Mpv;

public sealed record MpvDiagnosticSnapshot(
    string VideoCodec,
    string AudioCodec,
    int? Width,
    int? Height,
    double? Fps,
    double? VideoBitrate,
    double? AudioBitrate,
    string HwdecCurrent,
    string VideoOutput,
    string AudioTrackId,
    string SubtitleTrackId,
    double? CacheSeconds,
    double? CacheBufferingState,
    bool Buffering,
    double? AvSync);
