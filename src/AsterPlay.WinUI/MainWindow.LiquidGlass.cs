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

        try
        {
            var liquidGlassBrush =
                new LiquidGlassWinUI.LiquidGlassBrush
                {
                    BlurAmount = 2.2,
                    BloomAmount = 0.08,
                    RefThickness = 38,
                    RefFactor = 2.24,
                    RefDispersion = 3.8,
                    DispersionRange = 0.30,
                    RefFresnelFactor = 22,
                    GlareFactor = 60,
                    TintA = 0.24,
                    TintR = 239,
                    TintG = 239,
                    TintB = 244,
                    ShapeRadius = 0.99,
                    ShapeRoundness = 4.0
                };

            // Remove the solid fallback before connecting the backdrop brush;
            // otherwise the glass would sample the fallback instead of the
            // Home content underneath it. The native splash still hides this.
            NavigationDock.Background = null;
            NavigationDockGlassLayer.Background = liquidGlassBrush;
            StartupDiagnostics.Write(
                "LiquidGlass: real navigation brush attached behind native splash");

            // The package exposes LastError but no ready event. Require both a
            // minimum warmup interval and multiple real composition passes, then
            // fence the submitted frames through DWM.
            var minimumWarmup = Task.Delay(500);
            var frames = await WaitForCompositionFramesAsync(8, 1500);
            await minimumWarmup;

            var flush = Task.Run(DwmFlush);
            var completed = await Task.WhenAny(
                flush,
                Task.Delay(1500));
            var dwmFlushed = ReferenceEquals(completed, flush);
            var error = LiquidGlassWinUI.LiquidGlassBrush.LastError;

            if (!string.IsNullOrWhiteSpace(error))
            {
                NavigationDockGlassLayer.Background = null;
                NavigationDock.Background = fallbackBrush;
                StartupDiagnostics.Write(
                    $"LiquidGlass: startup warmup failed; fallback restored; error={error}");
                return false;
            }

            StartupDiagnostics.Write(
                $"LiquidGlass: startup warmup completed; frames={frames}, " +
                $"dwmFlushed={dwmFlushed}");
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
    }
}
