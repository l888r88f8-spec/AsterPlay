using System.Diagnostics;

namespace AsterPlay.WinUI;

internal static class StartupDiagnostics
{
    private static readonly object Sync = new();
    private static readonly Stopwatch Clock = Stopwatch.StartNew();

    private static readonly string DirectoryPath =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AsterPlay",
            "logs");

    public static readonly string LogPath =
        Path.Combine(DirectoryPath, "startup.log");

    private static bool _sessionHeaderWritten;

    public static void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);

            lock (Sync)
            {
                if (!_sessionHeaderWritten)
                {
                    File.AppendAllText(
                        LogPath,
                        $"{Environment.NewLine}========== AsterPlay startup {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} =========={Environment.NewLine}");
                    _sessionHeaderWritten = true;
                }

                File.AppendAllText(
                    LogPath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} +{Clock.Elapsed.TotalMilliseconds,8:0.0} ms  {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Startup diagnostics must never prevent the application from opening.
        }
    }

    public static IDisposable Measure(string area) =>
        new TimingScope(area);

    public static void WriteException(string area, Exception exception) =>
        Write($"{area}: {exception.GetType().FullName}: {exception.Message}{Environment.NewLine}{exception.StackTrace}");

    private sealed class TimingScope : IDisposable
    {
        private readonly string _area;
        private readonly Stopwatch _watch = Stopwatch.StartNew();
        private bool _disposed;

        public TimingScope(string area)
        {
            _area = area;
            Write($"{_area}: begin");
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _watch.Stop();
            Write($"{_area}: end ({_watch.Elapsed.TotalMilliseconds:0.0} ms)");
        }
    }
}
