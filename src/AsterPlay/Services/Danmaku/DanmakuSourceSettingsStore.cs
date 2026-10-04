using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AsterPlay.Models.Danmaku;

namespace AsterPlay.Services.Danmaku;

public static class DanmakuSourceSettingsStore
{
    private static readonly byte[] Entropy =
        Encoding.UTF8.GetBytes("AsterPlay.DanmakuSource.v1");

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

            var secret = "";
            if (!string.IsNullOrWhiteSpace(stored.DandanPlayAppSecretProtected))
            {
                var protectedBytes = Convert.FromBase64String(
                    stored.DandanPlayAppSecretProtected);
                var bytes = ProtectedData.Unprotect(
                    protectedBytes,
                    Entropy,
                    DataProtectionScope.CurrentUser);
                secret = Encoding.UTF8.GetString(bytes);
            }

            return Normalize(new DanmakuSourceSettings
            {
                SourceKind = stored.SourceKind,
                LogVarBaseUrl = stored.LogVarBaseUrl ?? "",
                DandanPlayAppId = stored.DandanPlayAppId ?? "",
                DandanPlayAppSecret = secret,
                DandanPlayWithRelated = stored.DandanPlayWithRelated
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

            string? protectedSecret = null;
            if (!string.IsNullOrWhiteSpace(settings.DandanPlayAppSecret))
            {
                var protectedBytes = ProtectedData.Protect(
                    Encoding.UTF8.GetBytes(settings.DandanPlayAppSecret),
                    Entropy,
                    DataProtectionScope.CurrentUser);
                protectedSecret = Convert.ToBase64String(protectedBytes);
            }

            var stored = new StoredDanmakuSourceSettings
            {
                SourceKind = settings.SourceKind,
                LogVarBaseUrl = settings.LogVarBaseUrl,
                DandanPlayAppId = settings.DandanPlayAppId,
                DandanPlayAppSecretProtected = protectedSecret,
                DandanPlayWithRelated = settings.DandanPlayWithRelated
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

    private static DanmakuSourceSettings Normalize(
        DanmakuSourceSettings settings) =>
        settings with
        {
            LogVarBaseUrl = settings.LogVarBaseUrl.Trim().TrimEnd('/'),
            DandanPlayAppId = settings.DandanPlayAppId.Trim()
        };

    private sealed class StoredDanmakuSourceSettings
    {
        public DanmakuSourceKind SourceKind { get; set; }
        public string? LogVarBaseUrl { get; set; }
        public string? DandanPlayAppId { get; set; }
        public string? DandanPlayAppSecretProtected { get; set; }
        public bool DandanPlayWithRelated { get; set; } = true;
    }
}
