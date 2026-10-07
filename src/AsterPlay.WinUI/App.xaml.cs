using Microsoft.UI.Xaml;
using Windows.Graphics;
using Windows.UI.ViewManagement;

namespace AsterPlay.WinUI;

public partial class App : Application
{
    private MainWindow? _window;

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
            "OnLaunched: resolving startup bounds for single MainWindow");

        try
        {
            var startupBounds =
                StartupWindowPlacement.Resolve();

            StartupDiagnostics.Write(
                $"OnLaunched: MainWindow startup bounds=" +
                $"{startupBounds.X},{startupBounds.Y}," +
                $"{startupBounds.Width}x{startupBounds.Height}");

            // The static startup page is part of MainWindow itself. Creating
            // only one top-level HWND avoids activation, z-order and compositor
            // gaps caused by handing off from a separate splash window.
            using (StartupDiagnostics.Measure("MainWindow constructor"))
                _window = new MainWindow(
                    startupBounds);

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
                    $"OnLaunched: correcting MainWindow bounds; " +
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
                "OnLaunched: single MainWindow activated with startup cover");
        }
        catch (Exception ex)
        {
            StartupDiagnostics.WriteException("OnLaunched", ex);
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

        StartupDiagnostics.Write(
            "StartupVisualReady event received; startupSurface=MainWindow.StartupCover");
        _window.WriteStartupVisualState(
            "App.StartupVisualReady.received");

        try
        {
            await _window.RevealStartupCoverAsync();
            _window.WriteStartupVisualState(
                "App.afterInWindowCoverFade");
            StartupDiagnostics.Write(
                "Single-window startup cover faded into presented Home");

            _window.NotifyStartupRevealCompleted();
            StartupDiagnostics.Write(
                "Single-window startup reveal completed");
        }
        catch (Exception ex)
        {
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
