using System.Diagnostics;
using System.Text;
using AsterPlay.Services;

namespace AsterPlay.WinUI;

internal static class StartupDiagnostics
{
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly object Sync = new();
    private static readonly string SessionId =
        $"{DateTime.UtcNow:yyyyMMdd-HHmmss.fff}-{Environment.ProcessId}";
    private static readonly string StartupLogFilePath =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AsterPlay",
            "startup-diagnostics.log");

    private static bool _sessionStarted;

    public static string LogPath => StartupLogFilePath;

    public static void StartSession()
    {
        lock (Sync)
        {
            if (_sessionStarted)
                return;

            _sessionStarted = true;

            try
            {
                var directory = Path.GetDirectoryName(StartupLogFilePath);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                var header = new StringBuilder()
                    .AppendLine("========== AsterPlay startup diagnostics ==========")
                    .AppendLine($"session={SessionId}")
                    .AppendLine($"startedLocal={DateTime.Now:O}")
                    .AppendLine($"startedUtc={DateTime.UtcNow:O}")
                    .AppendLine($"processId={Environment.ProcessId}")
                    .AppendLine($"process64Bit={Environment.Is64BitProcess}")
                    .AppendLine($"os={Environment.OSVersion}")
                    .AppendLine($"framework={Environment.Version}")
                    .AppendLine($"machineProcessors={Environment.ProcessorCount}")
                    .AppendLine("====================================================")
                    .ToString();

                File.WriteAllText(
                    StartupLogFilePath,
                    header,
                    Encoding.UTF8);
            }
            catch
            {
                // Startup diagnostics must never affect application startup.
            }
        }
    }

    public static void Write(string message)
    {
        StartSession();

        var elapsedMilliseconds = Clock.Elapsed.TotalMilliseconds;
        var threadId = Environment.CurrentManagedThreadId;
        var formatted =
            $"+{elapsedMilliseconds:0.0} ms [T{threadId}] {message}";

        PlaybackLog.Write(
            "Startup",
            formatted);

        try
        {
            var line =
                $"{DateTime.Now:HH:mm:ss.fff} {formatted}" +
                Environment.NewLine;

            lock (Sync)
            {
                File.AppendAllText(
                    StartupLogFilePath,
                    line,
                    Encoding.UTF8);
            }
        }
        catch
        {
            // Startup diagnostics must never affect application startup.
        }
    }

    public static void WriteState(
        string stage,
        string state) =>
        Write($"STATE {stage}: {state}");

    public static IDisposable Measure(string area) =>
        new TimingScope(area);

    public static void WriteException(string area, Exception exception) =>
        Write(
            $"{area}: {exception.GetType().FullName}: {exception.Message}" +
            $"{Environment.NewLine}{exception.StackTrace}");

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
            Write(
                $"{_area}: end ({_watch.Elapsed.TotalMilliseconds:0.0} ms)");
        }
    }
}
