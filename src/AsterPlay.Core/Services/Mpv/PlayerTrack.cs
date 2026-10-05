namespace AsterPlay.Services.Mpv;

public sealed record PlayerTrack(
    string Id,
    string Type,
    string? Language,
    string? Title,
    string? Codec,
    bool Selected,
    bool External,
    bool Default,
    bool Forced,
    int? ChannelCount,
    string? Channels);
