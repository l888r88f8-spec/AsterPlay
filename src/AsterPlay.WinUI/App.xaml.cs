using AsterPlay.Services;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using Windows.UI.ViewManagement;

namespace AsterPlay.WinUI;

public partial class App : Application
{
    private MainWindow? _window;
    private NativeStartupSplash? _splash;

    internal MainWindow? HostWindow => _window;

    internal string ThemeMode { get; private set; } = "system";

    public App()
    {
        StartupDiagnostics.StartSession();
        StartupDiagnostics.Write(
            $"App constructor: entered; startupLog={StartupDiagnostics.LogPath}");
        StartupDiagnostics.Write(
            "Startup diagnostic: liquidGlassShaderBypass=" +
            (string.Equals(
                Environment.GetEnvironmentVariable("ASTERPLAY_DIAG_NO_GLASS"),
                "1",
                StringComparison.Ordinal) ? "ON" : "OFF"));

        UnhandledException += App_UnhandledException;
        StartupDiagnostics.Write("App constructor: before InitializeComponent");
        ThemeMode = AppSettingsStore.Load().ThemeMode;
        RequestedTheme = ResolveStartupTheme(ThemeMode);
        StartupDiagnostics.Write($"App constructor: startup theme={RequestedTheme}");
        using (StartupDiagnostics.Measure("App.InitializeComponent"))
            InitializeComponent();
        StartupDiagnostics.Write("App constructor: after InitializeComponent");
    }

    private static ApplicationTheme ResolveStartupTheme(string mode)
    {
        if (string.Equals(mode, "light", StringComparison.OrdinalIgnoreCase))
            return ApplicationTheme.Light;
        if (string.Equals(mode, "dark", StringComparison.OrdinalIgnoreCase))
            return ApplicationTheme.Dark;

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

            NativeStartupSplash splash;
            using (StartupDiagnostics.Measure("NativeStartupSplash constructor"))
                splash = new NativeStartupSplash(
                    startupBounds,
                    RequestedTheme);

            _splash = splash;
            splash.Show();
            StartupDiagnostics.Write(
                $"OnLaunched: native splash shown; available={splash.IsAvailable}");

            using (StartupDiagnostics.Measure("MainWindow constructor"))
                _window = new MainWindow(
                    startupBounds,
                    nativeSplashAvailable: splash.IsAvailable);

            splash.AttachOwner(
                _window.NativeHandle);

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

    private async void MainWindow_StartupVisualReady(
        object? sender,
        EventArgs e)
    {
        if (_window is null)
            return;

        _window.StartupVisualReady -= MainWindow_StartupVisualReady;
        var splash = _splash;

        StartupDiagnostics.Write(
            $"StartupVisualReady received; nativeSplashExists={splash is not null}, " +
            $"nativeSplashAvailable={splash?.IsAvailable == true}");
        _window.WriteStartupVisualState("App.StartupVisualReady.received");

        try
        {
            if (splash is not null && splash.IsAvailable)
            {
                // Both the first-run (no server) Home and the authenticated
                // Home must transition directly from the native splash to the
                // REAL page. A second in-window cover creates a separate
                // composition handoff and can expose an unrendered white frame.
                //
                // Keep the native splash visible while removing the XAML
                // startup cover, arranging the target page and confirming its
                // presentation. Only then fade the native HWND out.
                StartupDiagnostics.Write(
                    "App: verifying real startup page behind native splash");

                if (!await _window.PrepareHomeBehindNativeSplashAsync())
                    return;

                await splash.FadeOutAsync(durationMilliseconds: 700);
                _window.WriteStartupVisualState("App.afterNativeFade");
                StartupDiagnostics.Write(
                    "App: native splash faded directly into verified startup page");
            }
            else
            {
                // Native startup window was unavailable. The WinUI cover
                // remains until the visible window passes the same checks.
                if (!await _window.RevealStartupCoverAsync())
                    return;

                StartupDiagnostics.Write(
                    "App: in-window startup cover used after verified composition");
            }

            _splash = null;
            splash?.Dispose();
            _window.NotifyStartupRevealCompleted();
            StartupDiagnostics.Write(
                "Native splash to MainWindow handoff completed");
        }
        catch (Exception ex)
        {
            // Never mark an unsuccessful handoff as completed or destroy an
            // available splash in the exception path: that exposed blank HWND
            // frames in the previous startup implementation.
            StartupDiagnostics.WriteException(
                "MainWindow_StartupVisualReady", ex);
            StartupDiagnostics.Write(
                "Startup handoff failed; startup surface retained for diagnostics");
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
