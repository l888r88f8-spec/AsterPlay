using System.Diagnostics;
using AsterPlay.Services;

namespace AsterPlay.WinUI;

internal static class StartupDiagnostics
{
    private static readonly Stopwatch Clock = Stopwatch.StartNew();

    public static string LogPath => PlaybackLog.LogPath;

    public static void Write(string message)
    {
        var elapsedMilliseconds = Clock.Elapsed.TotalMilliseconds;

        PlaybackLog.Write(
            "Startup",
            $"+{elapsedMilliseconds:0.0} ms {message}");
    }

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
