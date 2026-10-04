using Microsoft.UI.Xaml;

namespace AsterPlay.WinUI;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        StartupDiagnostics.Write("App constructor: entered");
        UnhandledException += App_UnhandledException;
        StartupDiagnostics.Write("App constructor: before InitializeComponent");
        InitializeComponent();
        StartupDiagnostics.Write("App constructor: after InitializeComponent");
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        StartupDiagnostics.Write("OnLaunched: entered");

        try
        {
            StartupDiagnostics.Write("OnLaunched: before MainWindow constructor");
            _window = new MainWindow();
            StartupDiagnostics.Write("OnLaunched: after MainWindow constructor");
            _window.Activate();
            StartupDiagnostics.Write("OnLaunched: after Window.Activate");
        }
        catch (Exception ex)
        {
            StartupDiagnostics.WriteException("OnLaunched", ex);
            throw;
        }
    }

    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        StartupDiagnostics.WriteException("Application.UnhandledException", e.Exception);
    }
}
