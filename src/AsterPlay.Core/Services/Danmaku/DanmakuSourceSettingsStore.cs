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
                LogVarBaseUrl = stored.LogVarBaseUrl ?? "",
                LogVarAccessToken = Unprotect(
                    stored.LogVarAccessTokenProtected)
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
            return "";
        }
    }

    private static DanmakuSourceSettings Normalize(
        DanmakuSourceSettings settings) =>
        settings with
        {
            LogVarBaseUrl = settings.LogVarBaseUrl
                .Trim()
                .TrimEnd('/'),
            LogVarAccessToken = settings.LogVarAccessToken.Trim()
        };

    private sealed class StoredDanmakuSourceSettings
    {
        public string? LogVarBaseUrl { get; set; }
        public string? LogVarAccessTokenProtected { get; set; }
    }
}
