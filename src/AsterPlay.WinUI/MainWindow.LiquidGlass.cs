namespace AsterPlay.WinUI;

public sealed partial class MainWindow
{
    private Task<bool>? _liquidGlassWarmupTask;

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
                ShapeRoundness = 4.0
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

            // Connected means the native factories and the complete effect graph
            // exist. Now wait for that graph to participate in real frames and
            // fence those frames through DWM before allowing splash dismissal.
            var frames = await WaitForCompositionFramesAsync(3, 1000);
            var flush = Task.Run(DwmFlush);
            var completed = await Task.WhenAny(
                flush,
                Task.Delay(1500));
            var dwmFlushed = ReferenceEquals(completed, flush);

            StartupDiagnostics.Write(
                $"LiquidGlass: pipeline connected; frames={frames}, " +
                $"dwmFlushed={dwmFlushed}; final presentation is verified by MainWindow");
            return true;
        }
        catch (Exception ex)
        {
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
