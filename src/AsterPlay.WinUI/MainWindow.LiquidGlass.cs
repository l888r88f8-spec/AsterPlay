namespace AsterPlay.WinUI;

public sealed partial class MainWindow
{
    private Task<bool>? _liquidGlassWarmupTask;
    private LiquidGlassWinUI.LiquidGlassBrush? _dockGlassBrush;

    private Task<bool> EnsureLiquidGlassWarmupAsync() =>
        _liquidGlassWarmupTask ??=
            WarmLiquidGlassBehindNativeSplashAsync();

    private async Task<bool> WarmLiquidGlassBehindNativeSplashAsync()
    {
        var fallbackBrush = NavigationDock.Background;
        var liquidGlassBrush =
            new LiquidGlassWinUI.LiquidGlassBrush
            {
                BlurAmount = 1.9,
                BloomAmount = 0.08,
                RefThickness = 38,
                RefFactor = 2.24,
                RefDispersion = 3.8,
                DispersionRange = 0.30,
                RefFresnelFactor = 22,
                GlareFactor = 60,
                TintA = 0.16,
                TintR = 239,
                TintG = 239,
                TintB = 244,
                ShapeRadius = 0.99,
                ShapeRoundness = 4.0,
                SpotlightRadius = 104,
                SpotlightStrength = 0
            };

        var connection =
            new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

        EventHandler<LiquidGlassWinUI.LiquidGlassPipelineStateChangedEventArgs>? handler = null;
        handler = (_, args) =>
        {
            StartupDiagnostics.Write(
                $"LiquidGlass: pipeline state={args.State}");

            if (args.State == LiquidGlassWinUI.LiquidGlassPipelineState.Connected)
                connection.TrySetResult(true);
            else if (args.State == LiquidGlassWinUI.LiquidGlassPipelineState.Failed)
                connection.TrySetResult(false);
        };

        liquidGlassBrush.PipelineStateChanged += handler;

        try
        {
            // Remove the solid fallback before connecting the backdrop brush;
            // otherwise the glass would sample the fallback instead of Home.
            // The opaque native splash still covers MainWindow at this point.
            NavigationDock.Background = null;
            NavigationDockGlassLayer.Background = liquidGlassBrush;
            _dockGlassBrush = liquidGlassBrush;
            StartupDiagnostics.Write(
                "LiquidGlass: real navigation brush attached behind native splash");

            if (liquidGlassBrush.PipelineState ==
                LiquidGlassWinUI.LiquidGlassPipelineState.Connected)
            {
                connection.TrySetResult(true);
            }
            else if (liquidGlassBrush.PipelineState ==
                     LiquidGlassWinUI.LiquidGlassPipelineState.Failed)
            {
                connection.TrySetResult(false);
            }

            var stateWait = await Task.WhenAny(
                connection.Task,
                Task.Delay(5000));

            var connected =
                ReferenceEquals(stateWait, connection.Task) &&
                await connection.Task;

            if (!connected)
            {
                _dockGlassBrush = null;
                NavigationDockGlassLayer.Background = null;
                NavigationDock.Background = fallbackBrush;
                StartupDiagnostics.Write(
                    $"LiquidGlass: pipeline did not connect; fallback restored; " +
                    $"state={liquidGlassBrush.PipelineState}; " +
                    $"error={liquidGlassBrush.PipelineError}; glassFallbackReady=true");
                // The opaque fallback is a valid rendered state; do not block
                // startup forever waiting for a failed optional shader.
                return true;
            }

            // Connected means the effect graph is usable, not that its first
            // pixels were displayed. The shared startup presentation fence
            // now verifies XAML frames and DWM after the target page is ready.
            // Avoid a second untracked DwmFlush thread competing with it.
            StartupDiagnostics.Write(
                "LiquidGlass: pipeline connected; awaiting shared startup presentation fence");
            return true;
        }
        catch (Exception ex)
        {
            _dockGlassBrush = null;
            NavigationDockGlassLayer.Background = null;
            NavigationDock.Background = fallbackBrush;
            StartupDiagnostics.WriteException(
                "WarmLiquidGlassBehindNativeSplash",
                ex);
            return false;
        }
        finally
        {
            liquidGlassBrush.PipelineStateChanged -= handler;
        }
    }
}
