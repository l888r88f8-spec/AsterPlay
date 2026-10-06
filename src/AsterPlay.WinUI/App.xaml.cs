using Microsoft.UI.Xaml;
using Windows.UI.ViewManagement;

namespace AsterPlay.WinUI;

public partial class App : Application
{
    private MainWindow? _window;
    private StartupSplashWindow? _splashWindow;

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
            _splashWindow = new StartupSplashWindow();
            _splashWindow.VisualReady +=
                SplashWindow_VisualReady;
            _splashWindow.Activate();

            StartupDiagnostics.Write(
                "OnLaunched: splash activated; waiting for decoded icon pixels");
        }
        catch (Exception ex)
        {
            StartupDiagnostics.WriteException("OnLaunched", ex);
            _splashWindow?.Close();
            _splashWindow = null;
            throw;
        }
    }

    private void SplashWindow_VisualReady(
        object? sender,
        EventArgs e)
    {
        if (_splashWindow is null)
            return;

        _splashWindow.VisualReady -=
            SplashWindow_VisualReady;

        StartupDiagnostics.Write(
            "Static startup page rendered; creating MainWindow");

        Microsoft.UI.Dispatching.DispatcherQueue
            .GetForCurrentThread()
            .TryEnqueue(
                Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                InitializeMainWindow);
    }

    private void InitializeMainWindow()
    {
        try
        {
            using (StartupDiagnostics.Measure("MainWindow constructor"))
                _window = new MainWindow();

            _window.StartupVisualReady +=
                MainWindow_StartupVisualReady;

            // MainWindow is positioned off-screen before activation, so it can
            // run Loaded/Rendering and resolve startup state without flashing
            // a white client area on the user's desktop.
            using (StartupDiagnostics.Measure("MainWindow.Activate"))
                _window.Activate();

            _splashWindow?.Activate();

            StartupDiagnostics.Write(
                "OnLaunched: off-screen MainWindow activated behind splash");
        }
        catch (Exception ex)
        {
            StartupDiagnostics.WriteException("InitializeMainWindow", ex);

            _splashWindow?.Close();
            _splashWindow = null;

            if (_window is not null)
            {
                _window.CompleteStartupWindowReveal();
                _window.Activate();
            }

            throw;
        }
    }

    private async void MainWindow_StartupVisualReady(
        object? sender,
        EventArgs e)
    {
        if (_window is null)
            return;

        _window.StartupVisualReady -=
            MainWindow_StartupVisualReady;

        if (_splashWindow is null)
        {
            _window.CompleteStartupWindowReveal();
            return;
        }

        var splash = _splashWindow;

        try
        {
            await splash.CompleteAsync(_window);
        }
        catch (Exception ex)
        {
            StartupDiagnostics.WriteException(
                "MainWindow_StartupVisualReady handoff",
                ex);

            _window.CompleteStartupWindowReveal();
            splash.Close();
        }
        finally
        {
            if (ReferenceEquals(_splashWindow, splash))
                _splashWindow = null;
        }
    }

    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        StartupDiagnostics.WriteException("Application.UnhandledException", e.Exception);
    }
}
