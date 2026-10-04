namespace AsterPlay.Models.Danmaku;

public enum DanmakuMode
{
    Scroll,
    Top,
    Bottom
}

public sealed record DanmakuComment(
    string Id,
    double TimeSeconds,
    string Text,
    DanmakuMode Mode = DanmakuMode.Scroll,
    uint ColorArgb = 0xFFFFFFFF,
    string Sender = "");

public sealed record DanmakuDocument(
    string SourceName,
    IReadOnlyList<DanmakuComment> Comments);

public sealed record DanmakuContext(
    string ItemId,
    string Title,
    double DurationSeconds,
    string SeriesId = "",
    string SeriesName = "",
    int? SeasonNumber = null,
    int? EpisodeNumber = null,
    string OriginalTitle = "",
    string ItemType = "",
    string MediaPath = "",
    string FileName = "");

public sealed record DanmakuSeriesMatchBinding(
    long AnimeId,
    string AnimeTitle,
    int LogVarSeasonNumber,
    int EpisodeOffset);

public sealed record DanmakuSettings
{
    public double FontSize { get; init; } = 22;
    public double ScrollDurationSeconds { get; init; } = 7;
    public double FixedDurationSeconds { get; init; } = 4;
    public double Opacity { get; init; } = 0.95;
    public double Speed { get; init; } = 1;
    public double ScreenHeightRatio { get; init; } = 0.72;
    public double DensityRatio { get; init; } = 1;
    public int MaxActiveComments { get; init; } = 80;
    public bool AvoidOverlap { get; init; } = true;
    public List<string> BlockedWords { get; init; } = [];
    public List<string> BlockedUsers { get; init; } = [];
}
