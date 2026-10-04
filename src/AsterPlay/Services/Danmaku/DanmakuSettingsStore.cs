using System.Text.Json;
using AsterPlay.Models.Danmaku;

namespace AsterPlay.Services.Danmaku;

public static class DanmakuSettingsStore
{
    private static readonly string DirectoryPath =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AsterPlay");

    private static readonly string SettingsPath =
        Path.Combine(DirectoryPath, "danmaku-settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static DanmakuSettings Load()
    {
        if (!File.Exists(SettingsPath))
            return new DanmakuSettings();

        try
        {
            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<DanmakuSettings>(
                json,
                JsonOptions);

            return Normalize(settings ?? new DanmakuSettings());
        }
        catch (Exception ex)
        {
            PlaybackLog.Error("DanmakuSettingsLoad", ex);
            return new DanmakuSettings();
        }
    }

    public static void Save(DanmakuSettings settings)
    {
        try
        {
            var normalized = Normalize(settings);
            Directory.CreateDirectory(DirectoryPath);

            var tempPath = SettingsPath + ".tmp";
            File.WriteAllText(
                tempPath,
                JsonSerializer.Serialize(normalized, JsonOptions));

            File.Move(tempPath, SettingsPath, overwrite: true);
        }
        catch (Exception ex)
        {
            PlaybackLog.Error("DanmakuSettingsSave", ex);
        }
    }

    private static DanmakuSettings Normalize(DanmakuSettings settings) =>
        settings with
        {
            FontSize = Math.Clamp(settings.FontSize, 12, 48),
            ScrollDurationSeconds = Math.Clamp(
                settings.ScrollDurationSeconds,
                2,
                20),
            FixedDurationSeconds = Math.Clamp(
                settings.FixedDurationSeconds,
                1,
                10),
            Opacity = Math.Clamp(settings.Opacity, 0.1, 1),
            Speed = Math.Clamp(settings.Speed, 0.25, 4),
            ScreenHeightRatio = Math.Clamp(
                settings.ScreenHeightRatio,
                0.25,
                1)
        };
}
