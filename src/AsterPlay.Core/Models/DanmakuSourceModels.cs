namespace AsterPlay.Models.Danmaku;

public sealed record DanmakuSourceSettings
{
    public string LogVarBaseUrl { get; init; } = "";
    public string LogVarAccessToken { get; init; } = "";
}
