using System.Text.RegularExpressions;

namespace AsterPlay.Services;

public static class PlaybackLog
{
    private static readonly object Sync = new();
    private static readonly string DirectoryPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AsterPlay");

    private static readonly string LogFilePath =
        Path.Combine(DirectoryPath, "playback.log");

    private static readonly string PreviousLogFilePath =
        Path.Combine(DirectoryPath, "playback.previous.log");

    private const long MaxLogBytes = 8L * 1024 * 1024;

    private static readonly string SessionStartedAt =
        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");

    private static bool _sessionHeaderWritten;

    public static string LogPath => LogFilePath;

    public static void Write(string area, string message)
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{area}] {Redact(message)}{Environment.NewLine}";

            lock (Sync)
            {
                RotateIfNeeded();

                if (!_sessionHeaderWritten)
                {
                    File.AppendAllText(
                        LogFilePath,
                        $"{Environment.NewLine}========== AsterPlay session {SessionStartedAt} =========={Environment.NewLine}");
                    _sessionHeaderWritten = true;
                }

                File.AppendAllText(LogFilePath, line);
            }
        }
        catch
        {
            // Diagnostics must never break playback.
        }
    }

    private static void RotateIfNeeded()
    {
        try
        {
            if (!File.Exists(LogFilePath) ||
                new FileInfo(LogFilePath).Length < MaxLogBytes)
            {
                return;
            }

            if (File.Exists(PreviousLogFilePath))
                File.Delete(PreviousLogFilePath);

            File.Move(LogFilePath, PreviousLogFilePath);
            _sessionHeaderWritten = false;
        }
        catch
        {
            // Rotation is best-effort; diagnostics must never affect playback.
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
