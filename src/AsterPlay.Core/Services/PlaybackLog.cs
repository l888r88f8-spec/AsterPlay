using System.Text.RegularExpressions;

namespace AsterPlay.Services;

public static class PlaybackLog
{
    private static readonly object Sync = new();
    private static readonly string DirectoryPath =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AsterPlay");

    private static readonly string LogFilePath =
        Path.Combine(DirectoryPath, "AsterPlay.log");

    private const long MaxLogBytes = 8L * 1024 * 1024;
    private const long TrimTargetBytes = 4L * 1024 * 1024;

    private static readonly string SessionStartedAt =
        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");

    private static bool _sessionHeaderWritten;

    public static string LogPath => LogFilePath;

    public static void Write(string area, string message)
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            var line =
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{area}] " +
                $"{Redact(message)}{Environment.NewLine}";

            lock (Sync)
            {
                TrimIfNeeded();

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
            // Diagnostics must never affect application behavior.
        }
    }

    private static void TrimIfNeeded()
    {
        try
        {
            if (!File.Exists(LogFilePath) ||
                new FileInfo(LogFilePath).Length < MaxLogBytes)
            {
                return;
            }

            var text = File.ReadAllText(LogFilePath);
            if (text.Length == 0)
                return;

            // Keep roughly the newest half of the log in the same file so
            // AsterPlay always has one active runtime log: AsterPlay.log.
            var approximateKeepChars = (int)Math.Min(
                text.Length,
                TrimTargetBytes);

            var startIndex = Math.Max(0, text.Length - approximateKeepChars);
            if (startIndex > 0)
            {
                var nextLine = text.IndexOf('\n', startIndex);
                if (nextLine >= 0 && nextLine + 1 < text.Length)
                    startIndex = nextLine + 1;
            }

            var retained = text[startIndex..];
            var marker =
                $"========== AsterPlay.log trimmed {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}; older entries removed =========={Environment.NewLine}";

            File.WriteAllText(
                LogFilePath,
                marker + retained);
        }
        catch
        {
            // Trimming is best-effort; diagnostics must never affect the app.
        }
    }

    public static void Error(string area, Exception ex) =>
        Write(
            area,
            $"{ex.GetType().Name}: {ex.Message}{Environment.NewLine}{ex.StackTrace}");

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
