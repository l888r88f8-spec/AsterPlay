using System.Text;

namespace AsterPlay;

public partial class App : Application
{
    private static readonly string CrashLogPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     "AsterPlay", "crash.log");

    public App()
    {
        DispatcherUnhandledException += (_, e) =>
        {
            WriteCrash("DispatcherUnhandledException", e.Exception);
            MessageBox.Show(
                $"AsterPlay crashed. Details were written to:\n{CrashLogPath}\n\n{e.Exception.Message}",
                "AsterPlay crash",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            e.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                WriteCrash("AppDomain.UnhandledException", ex);
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            WriteCrash("TaskScheduler.UnobservedTaskException", e.Exception);
            e.SetObserved();
        };
    }

    private static void WriteCrash(string source, Exception exception)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CrashLogPath)!);
            var text = new StringBuilder()
                .AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}")
                .AppendLine(exception.ToString())
                .AppendLine(new string('-', 80))
                .ToString();
            File.AppendAllText(CrashLogPath, text);
        }
        catch
        {
        }
    }
}
