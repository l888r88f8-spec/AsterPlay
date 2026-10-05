using Microsoft.UI.Xaml;
using Windows.UI.ViewManagement;

namespace AsterPlay.WinUI;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        StartupDiagnostics.Write("App constructor: entered");
        UnhandledException += App_UnhandledException;
        StartupDiagnostics.Write("App constructor: before InitializeComponent");
        RequestedTheme = ResolveStartupTheme();
        StartupDiagnostics.Write($"App constructor: startup theme={RequestedTheme}");
        using (StartupDiagnostics.Measure("App.InitializeComponent"))
            InitializeComponent();
        StartupDiagnostics.Write("App constructor: after InitializeComponent");
    }

    private static ApplicationTheme ResolveStartupTheme()
    {
        try
        {
            var settings = new UISettings();
            var background = settings.GetColorValue(UIColorType.Background);
            var luminance =
                (0.2126 * background.R) +
                (0.7152 * background.G) +
                (0.0722 * background.B);

            return luminance >= 128
                ? ApplicationTheme.Light
                : ApplicationTheme.Dark;
        }
        catch (Exception ex)
        {
            StartupDiagnostics.WriteException("ResolveStartupTheme", ex);
            return ApplicationTheme.Dark;
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        StartupDiagnostics.Write("OnLaunched: entered");

        try
        {
            StartupDiagnostics.Write("OnLaunched: before MainWindow constructor");
            using (StartupDiagnostics.Measure("MainWindow constructor"))
                _window = new MainWindow();
            StartupDiagnostics.Write("OnLaunched: after MainWindow constructor");

            using (StartupDiagnostics.Measure("Window.Activate"))
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
