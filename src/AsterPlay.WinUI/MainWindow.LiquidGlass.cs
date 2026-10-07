namespace AsterPlay.WinUI;

public sealed partial class MainWindow
{
    private bool _liquidGlassActivationScheduled;

    private async Task EnableLiquidGlassAfterStartupAsync()
    {
        if (_liquidGlassActivationScheduled)
            return;

        _liquidGlassActivationScheduled = true;

        try
        {
            // Home is already visible at this point. Let its post-reveal work
            // settle before connecting the backdrop-flattening custom effect.
            await Task.Delay(500);

            if (RootGrid.XamlRoot is null)
            {
                StartupDiagnostics.Write(
                    "LiquidGlass: activation skipped because MainWindow is no longer connected");
                return;
            }

            var fallbackBrush = NavigationDock.Background;
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

            StartupDiagnostics.Write(
                "LiquidGlass: attaching navigation dock effect after startup reveal");
            NavigationDock.Background = liquidGlassBrush;

            var frames = await WaitForCompositionFramesAsync(3, 1000);
            var error = LiquidGlassWinUI.LiquidGlassBrush.LastError;

            if (!string.IsNullOrWhiteSpace(error))
            {
                NavigationDock.Background = fallbackBrush;
                StartupDiagnostics.Write(
                    $"LiquidGlass: activation failed; fallback restored; error={error}");
                return;
            }

            StartupDiagnostics.Write(
                $"LiquidGlass: post-startup activation completed; frames={frames}");
        }
        catch (Exception ex)
        {
            StartupDiagnostics.WriteException(
                "EnableLiquidGlassAfterStartup",
                ex);
        }
    }
}
