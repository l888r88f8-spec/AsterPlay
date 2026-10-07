using Microsoft.UI.Xaml;
using Windows.Graphics;
using Windows.UI.ViewManagement;

namespace AsterPlay.WinUI;

public partial class App : Application
{
    private MainWindow? _window;
    private NativeStartupSplash? _splash;

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
        StartupDiagnostics.Write(
            "OnLaunched: resolving one shared rect for native splash and MainWindow");

        try
        {
            var startupBounds =
                StartupWindowPlacement.Resolve();

            StartupDiagnostics.Write(
                $"OnLaunched: shared startup bounds=" +
                $"{startupBounds.X},{startupBounds.Y}," +
                $"{startupBounds.Width}x{startupBounds.Height}");

            using (StartupDiagnostics.Measure("NativeStartupSplash constructor"))
                _splash = new NativeStartupSplash(
                    startupBounds,
                    RequestedTheme);

            _splash.Show();

            using (StartupDiagnostics.Measure("MainWindow constructor"))
                _window = new MainWindow(
                    startupBounds);

            _splash.AttachOwner(
                _window.NativeHandle);

            _window.StartupCoverPresented +=
                MainWindow_StartupCoverPresented;
            _window.StartupVisualReady +=
                MainWindow_StartupVisualReady;

            using (StartupDiagnostics.Measure("MainWindow.Activate"))
                _window.Activate();

            var actualBounds =
                _window.CurrentBounds;

            if (!BoundsEqual(
                    startupBounds,
                    actualBounds))
            {
                StartupDiagnostics.Write(
                    $"OnLaunched: correcting MainWindow bounds behind splash; " +
                    $"actual={actualBounds.X},{actualBounds.Y}," +
                    $"{actualBounds.Width}x{actualBounds.Height}");

                _window.EnsureStartupBounds(
                    startupBounds);

                actualBounds =
                    _window.CurrentBounds;
            }

            LogStartupBoundsMatch(
                startupBounds,
                actualBounds);

            StartupDiagnostics.Write(
                "OnLaunched: MainWindow activated behind native splash");
        }
        catch (Exception ex)
        {
            StartupDiagnostics.WriteException("OnLaunched", ex);

            _splash?.Dispose();
            _splash = null;

            throw;
        }
    }

    private void MainWindow_StartupCoverPresented(
        object? sender,
        EventArgs e)
    {
        if (_window is not null)
        {
            _window.StartupCoverPresented -=
                MainWindow_StartupCoverPresented;
        }

        var splash = _splash;
        _splash = null;

        if (splash is null)
            return;

        // Do not keep a second top-level HWND over MainWindow while Home and the
        // LiquidGlass shaders are warming. DWM can defer real presentation of an
        // occluded owner even though XAML Rendering and DwmFlush have completed.
        splash.Dispose();
        StartupDiagnostics.Write(
            "Native splash handed off to in-window startup cover");
    }

    private async void MainWindow_StartupVisualReady(
        object? sender,
        EventArgs e)
    {
        if (_window is null)
            return;

        _window.StartupVisualReady -=
            MainWindow_StartupVisualReady;

        // Safety: normally the native splash was already removed as soon as the
        // in-window cover was presented. If not, remove it before revealing Home.
        if (_splash is not null)
        {
            _splash.Dispose();
            _splash = null;
            StartupDiagnostics.Write(
                "Native splash removed during final startup reveal fallback");
        }

        try
        {
            await _window.RevealStartupCoverAsync();
            StartupDiagnostics.Write(
                "Native splash to MainWindow handoff completed");
        }
        catch (Exception ex)
        {
            StartupDiagnostics.WriteException(
                "MainWindow_StartupVisualReady",
                ex);
        }
    }

    private static bool BoundsEqual(
        RectInt32 left,
        RectInt32 right) =>
        left.X == right.X &&
        left.Y == right.Y &&
        left.Width == right.Width &&
        left.Height == right.Height;

    private static void LogStartupBoundsMatch(
        RectInt32 requested,
        RectInt32 actual)
    {
        var exactMatch =
            BoundsEqual(
                requested,
                actual);

        StartupDiagnostics.Write(
            $"Startup bounds verification: exactMatch={exactMatch}; " +
            $"requested={requested.X},{requested.Y}," +
            $"{requested.Width}x{requested.Height}; " +
            $"main={actual.X},{actual.Y}," +
            $"{actual.Width}x{actual.Height}");
    }

    private void App_UnhandledException(
        object sender,
        Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        StartupDiagnostics.WriteException(
            "Application.UnhandledException",
            e.Exception);
    }
}
