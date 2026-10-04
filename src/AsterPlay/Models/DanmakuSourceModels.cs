namespace AsterPlay.Models.Danmaku;

public enum DanmakuSourceKind
{
    BuiltIn,
    DandanPlay,
    LogVar
}

public sealed record DanmakuSourceSettings
{
    public DanmakuSourceKind SourceKind { get; init; } = DanmakuSourceKind.BuiltIn;

    public string DandanPlayBaseUrl { get; init; } =
        "https://api.dandanplay.net";
    public string DandanPlayAppId { get; init; } = "";
    public string DandanPlayAppSecret { get; init; } = "";
    public bool DandanPlayWithRelated { get; init; } = true;

    public string LogVarBaseUrl { get; init; } = "";
    public string LogVarAccessToken { get; init; } = "";
}
