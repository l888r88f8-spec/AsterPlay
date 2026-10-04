using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AsterPlay.Models.Danmaku;

namespace AsterPlay.Services.Danmaku;

public static class DanmakuSourceSettingsStore
{
    private static readonly byte[] Entropy =
        Encoding.UTF8.GetBytes("AsterPlay.DanmakuSource.v2");

    private static readonly string DirectoryPath =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AsterPlay");

    private static readonly string SettingsPath =
        Path.Combine(DirectoryPath, "danmaku-source.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static DanmakuSourceSettings Load()
    {
        if (!File.Exists(SettingsPath))
            return new DanmakuSourceSettings();

        try
        {
            var json = File.ReadAllText(SettingsPath);
            var stored = JsonSerializer.Deserialize<StoredDanmakuSourceSettings>(
                json,
                JsonOptions);

            if (stored is null)
                return new DanmakuSourceSettings();

            return Normalize(new DanmakuSourceSettings
            {
                SourceKind = stored.SourceKind,
                DandanPlayBaseUrl = string.IsNullOrWhiteSpace(stored.DandanPlayBaseUrl)
                    ? "https://api.dandanplay.net"
                    : stored.DandanPlayBaseUrl,
                DandanPlayAppId = stored.DandanPlayAppId ?? "",
                DandanPlayAppSecret = Unprotect(stored.DandanPlayAppSecretProtected),
                DandanPlayWithRelated = stored.DandanPlayWithRelated,
                LogVarBaseUrl = stored.LogVarBaseUrl ?? "",
                LogVarAccessToken = Unprotect(stored.LogVarAccessTokenProtected)
            });
        }
        catch (Exception ex)
        {
            PlaybackLog.Error("DanmakuSourceSettingsLoad", ex);
            return new DanmakuSourceSettings();
        }
    }

    public static void Save(DanmakuSourceSettings settings)
    {
        try
        {
            settings = Normalize(settings);
            Directory.CreateDirectory(DirectoryPath);

            var stored = new StoredDanmakuSourceSettings
            {
                SourceKind = settings.SourceKind,
                DandanPlayBaseUrl = settings.DandanPlayBaseUrl,
                DandanPlayAppId = settings.DandanPlayAppId,
                DandanPlayAppSecretProtected = Protect(
                    settings.DandanPlayAppSecret),
                DandanPlayWithRelated = settings.DandanPlayWithRelated,
                LogVarBaseUrl = settings.LogVarBaseUrl,
                LogVarAccessTokenProtected = Protect(
                    settings.LogVarAccessToken)
            };

            var tempPath = SettingsPath + ".tmp";
            File.WriteAllText(
                tempPath,
                JsonSerializer.Serialize(stored, JsonOptions));
            File.Move(tempPath, SettingsPath, overwrite: true);
        }
        catch (Exception ex)
        {
            PlaybackLog.Error("DanmakuSourceSettingsSave", ex);
        }
    }

    private static string? Protect(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var protectedBytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(value),
            Entropy,
            DataProtectionScope.CurrentUser);

        return Convert.ToBase64String(protectedBytes);
    }

    private static string Unprotect(string? protectedValue)
    {
        if (string.IsNullOrWhiteSpace(protectedValue))
            return "";

        try
        {
            var protectedBytes = Convert.FromBase64String(protectedValue);
            var bytes = ProtectedData.Unprotect(
                protectedBytes,
                Entropy,
                DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            // v1 stored only the DandanPlay secret with different entropy.
            // Keep startup resilient and let the user re-enter credentials.
            return "";
        }
    }

    private static DanmakuSourceSettings Normalize(
        DanmakuSourceSettings settings) =>
        settings with
        {
            DandanPlayBaseUrl = NormalizeBaseUrl(
                string.IsNullOrWhiteSpace(settings.DandanPlayBaseUrl)
                    ? "https://api.dandanplay.net"
                    : settings.DandanPlayBaseUrl),
            DandanPlayAppId = settings.DandanPlayAppId.Trim(),
            LogVarBaseUrl = NormalizeBaseUrl(settings.LogVarBaseUrl),
            LogVarAccessToken = settings.LogVarAccessToken.Trim()
        };

    private static string NormalizeBaseUrl(string value) =>
        value.Trim().TrimEnd('/');

    private sealed class StoredDanmakuSourceSettings
    {
        public DanmakuSourceKind SourceKind { get; set; }
        public string? DandanPlayBaseUrl { get; set; }
        public string? DandanPlayAppId { get; set; }
        public string? DandanPlayAppSecretProtected { get; set; }
        public bool DandanPlayWithRelated { get; set; } = true;
        public string? LogVarBaseUrl { get; set; }
        public string? LogVarAccessTokenProtected { get; set; }
    }
}
