using Microsoft.UI.Xaml;
using Windows.Graphics;
using Windows.UI.ViewManagement;

namespace AsterPlay.WinUI;

public partial class App : Application
{
    private MainWindow? _window;
    private NativeStartupSplash? _splash;

    internal MainWindow? HostWindow => _window;

    public App()
    {
        StartupDiagnostics.StartSession();
        StartupDiagnostics.Write(
            $"App constructor: entered; startupLog={StartupDiagnostics.LogPath}");

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
            StartupDiagnostics.Write(
                $"OnLaunched: native splash shown; available={_splash.IsAvailable}");

            using (StartupDiagnostics.Measure("MainWindow constructor"))
                _window = new MainWindow(
                    startupBounds);

            _splash.AttachOwner(
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

        _window.StartupVisualReady -=
            MainWindow_StartupVisualReady;

        var splash = _splash;
        _splash = null;

        StartupDiagnostics.Write(
            $"StartupVisualReady event received; nativeSplashExists={splash is not null}, " +
            $"nativeSplashAvailable={splash?.IsAvailable == true}");
        _window.WriteStartupVisualState(
            "App.StartupVisualReady.received");

        try
        {
            if (splash is not null &&
                splash.IsAvailable)
            {
                if (_window.IsNoServerStartup)
                {
                    // The static empty Home may not paint immediately after
                    // its native owner becomes unoccluded. Never expose it by
                    // fading the native HWND into a collapsed XAML cover.
                    // The XAML cover has the same theme color and icon as the
                    // native splash, so the handoff is visually continuous.
                    StartupDiagnostics.Write(
                        "App: empty home; retaining WinUI startup cover during native handoff");
                    await _window.PrepareEmptyHomeCoverHandoffAsync();
                    splash.Dispose();
                    _window.WriteStartupVisualState(
                        "App.emptyHome.afterNativeHandoff");

                    // Now the WinUI window is really visible and composing.
                    // Fade its own cover into the arranged empty Home page.
                    await _window.RevealStartupCoverAsync();
                    StartupDiagnostics.Write(
                        "Empty-home splash handoff completed through XAML cover");
                }
                else
                {
                    // Authenticated Home and Login retain the already-working
                    // direct native fade after their presentation barrier.
                    StartupDiagnostics.Write(
                        "App: preparing Home behind native splash");
                    await _window.PrepareHomeBehindNativeSplashAsync();
                    _window.WriteStartupVisualState(
                        "App.beforeNativeFade");
                    StartupDiagnostics.Write(
                        "App: starting native splash fade");
                    await splash.FadeOutAsync(
                        durationMilliseconds: 700);
                    _window.WriteStartupVisualState(
                        "App.afterNativeFade");
                    StartupDiagnostics.Write(
                        "Native splash faded directly into presented Home");
                }
            }
            else
            {
                splash?.Dispose();
                await _window.RevealStartupCoverAsync();
                StartupDiagnostics.Write(
                    "In-window startup cover used because native splash was unavailable");
            }

            _window.NotifyStartupRevealCompleted();
            StartupDiagnostics.Write(
                "Native splash to MainWindow handoff completed");
        }
        catch (Exception ex)
        {
            splash?.Dispose();
            _window.NotifyStartupRevealCompleted();
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
