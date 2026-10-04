using System.Text.RegularExpressions;

namespace AsterPlay.Services;

public static class PlaybackLog
{
    private static readonly object Sync = new();
    private static readonly string DirectoryPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AsterPlay", "logs");

    private static readonly string SessionPath =
        Path.Combine(DirectoryPath, $"playback-{DateTime.Now:yyyyMMdd-HHmmss}.log");

    public static string LogPath => SessionPath;

    public static void Write(string area, string message)
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{area}] {Redact(message)}{Environment.NewLine}";
            lock (Sync)
                File.AppendAllText(SessionPath, line);
        }
        catch
        {
            // Diagnostics must never break playback.
        }
    }

    public static void Error(string area, Exception ex) =>
        Write(area, $"{ex.GetType().Name}: {ex.Message}{Environment.NewLine}{ex.StackTrace}");

    public static string Redact(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        value = Regex.Replace(
            value,
            @"(?i)(api_key|apikey|access_token|token|x-emby-token)=([^&\s]+)",
            "$1=<redacted>");

        value = Regex.Replace(
            value,
            @"(?i)(X-Emby-Token\s*[:=]\s*)([^,\s]+)",
            "$1<redacted>");

        return value;
    }
}
