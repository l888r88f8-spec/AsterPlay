using System.Text;
using System.Text.Json;

namespace AsterPlay.Services;

public sealed record AppSettings
{
    public bool RestoreSessionOnStartup { get; init; } = true;
    public int PlayerControlsAutoHideSeconds { get; init; } = 3;
}

public static class AppSettingsStore
{
    private static readonly string DirectoryPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AsterPlay");

    private static readonly string FilePath = Path.Combine(DirectoryPath, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new AppSettings();

            return JsonSerializer.Deserialize<AppSettings>(
                       File.ReadAllText(FilePath),
                       JsonOptions)
                   ?? new AppSettings();
        }
        catch (Exception ex)
        {
            PlaybackLog.Error("AppSettingsLoad", ex);
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions), Encoding.UTF8);
            File.Move(temp, FilePath, true);
        }
        catch (Exception ex)
        {
            PlaybackLog.Error("AppSettingsSave", ex);
        }
    }
}
