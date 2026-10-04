namespace AsterPlay.WinUI;

internal static class StartupDiagnostics
{
    private static readonly object Sync = new();
    public static readonly string LogPath =
        Path.Combine(AppContext.BaseDirectory, "winui-startup.log");

    public static void Write(string message)
    {
        try
        {
            lock (Sync)
            {
                File.AppendAllText(
                    LogPath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
        }
        catch
        {
        }
    }

    public static void WriteException(string area, Exception exception) =>
        Write($"{area}: {exception.GetType().FullName}: {exception.Message}{Environment.NewLine}{exception.StackTrace}");
}
