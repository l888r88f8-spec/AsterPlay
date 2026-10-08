using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace AsterPlay.WinUI;

public sealed partial class MainWindow
{
    private bool _dockPointerInside;
    private long _lastDockSpotlightUpdate;
    private long _dockVisibilityCallbackToken;

    // AddHandler(handledEventsToo) receives moves over actual Button children:
    // no transparent hit-test overlay or PointerCapture is needed.
    private void InitializeDockSpotlight()
    {
        NavigationDock.AddHandler(
            UIElement.PointerEnteredEvent,
            new PointerEventHandler(DockSpotlight_PointerEntered),
            true);
        NavigationDock.AddHandler(
            UIElement.PointerMovedEvent,
            new PointerEventHandler(DockSpotlight_PointerMoved),
            true);
        NavigationDock.AddHandler(
            UIElement.PointerExitedEvent,
            new PointerEventHandler(DockSpotlight_PointerExited),
            true);
        NavigationDock.AddHandler(
            UIElement.PointerReleasedEvent,
            new PointerEventHandler(DockSpotlight_PointerReleased),
            true);
        NavigationDock.AddHandler(
            UIElement.PointerCanceledEvent,
            new PointerEventHandler(DockSpotlight_PointerCanceled),
            true);

        _dockVisibilityCallbackToken = NavigationDock.RegisterPropertyChangedCallback(
            UIElement.VisibilityProperty,
            (_, _) =>
            {
                if (NavigationDock.Visibility != Visibility.Visible)
                    FadeDockSpotlight();
            });
    }

    private void DetachDockSpotlight()
    {
        NavigationDock.RemoveHandler(
            UIElement.PointerEnteredEvent,
            new PointerEventHandler(DockSpotlight_PointerEntered));
        NavigationDock.RemoveHandler(
            UIElement.PointerMovedEvent,
            new PointerEventHandler(DockSpotlight_PointerMoved));
        NavigationDock.RemoveHandler(
            UIElement.PointerExitedEvent,
            new PointerEventHandler(DockSpotlight_PointerExited));
        NavigationDock.RemoveHandler(
            UIElement.PointerReleasedEvent,
            new PointerEventHandler(DockSpotlight_PointerReleased));
        NavigationDock.RemoveHandler(
            UIElement.PointerCanceledEvent,
            new PointerEventHandler(DockSpotlight_PointerCanceled));
        NavigationDock.UnregisterPropertyChangedCallback(
            UIElement.VisibilityProperty,
            _dockVisibilityCallbackToken);

        _dockPointerInside = false;
        _dockGlassBrush = null;
    }

    private void DockSpotlight_PointerEntered(
        object sender, PointerRoutedEventArgs e) =>
        UpdateDockSpotlight(e);

    private void DockSpotlight_PointerMoved(
        object sender, PointerRoutedEventArgs e) =>
        UpdateDockSpotlight(e);

    private void UpdateDockSpotlight(PointerRoutedEventArgs e)
    {
        var brush = _dockGlassBrush;
        if (NavigationDock.Visibility != Visibility.Visible ||
            brush is null ||
            brush.PipelineState != LiquidGlassWinUI.LiquidGlassPipelineState.Connected ||
            NavigationDock.ActualWidth <= 0 ||
            NavigationDock.ActualHeight <= 0)
        {
            return;
        }

        var point = e.GetCurrentPoint(NavigationDock).Position;
        if (point.X < 0 || point.Y < 0 ||
            point.X > NavigationDock.ActualWidth ||
            point.Y > NavigationDock.ActualHeight)
        {
            FadeDockSpotlight();
            return;
        }

        var x = (float)Math.Clamp(point.X / NavigationDock.ActualWidth, 0, 1);
        var y = (float)Math.Clamp(point.Y / NavigationDock.ActualHeight, 0, 1);

        if (!_dockPointerInside)
        {
            // Start at the actual pointer position; avoid a visible sweep in
            // from the default center when entering the Dock for the first time.
            _dockPointerInside = true;
            _lastDockSpotlightUpdate = Stopwatch.GetTimestamp();
            brush.SpotlightX = x;
            brush.SpotlightY = y;
            brush.AnimateScalar(
                "SpotlightStrength",
                RootGrid.ActualTheme == ElementTheme.Light ? 0.43f : 0.75f,
                155);
            return;
        }

        // Limit property animations to ~30 Hz; compositor interpolates the
        // intervening frames without placing a timer on the UI thread.
        if (Stopwatch.GetElapsedTime(_lastDockSpotlightUpdate) <
            TimeSpan.FromMilliseconds(32))
        {
            return;
        }

        _lastDockSpotlightUpdate = Stopwatch.GetTimestamp();
        brush.AnimateScalar("SpotlightX", x, 110);
        brush.AnimateScalar("SpotlightY", y, 110);
    }

    private void DockSpotlight_PointerExited(
        object sender, PointerRoutedEventArgs e)
    {
        // PointerExited bubbles from child buttons when crossing items.
        // Ignore those transitions while the cursor remains inside the Dock.
        var point = e.GetCurrentPoint(NavigationDock).Position;
        if (point.X >= 0 && point.Y >= 0 &&
            point.X <= NavigationDock.ActualWidth &&
            point.Y <= NavigationDock.ActualHeight)
        {
            return;
        }

        FadeDockSpotlight();
    }

    private void DockSpotlight_PointerReleased(
        object sender, PointerRoutedEventArgs e)
    {
        // Unlike mouse hover, a touch contact ends as soon as the finger lifts.
        if (e.Pointer.PointerDeviceType.Equals(Windows.Devices.Input.PointerDeviceType.Touch))
            FadeDockSpotlight();
    }

    private void DockSpotlight_PointerCanceled(
        object sender, PointerRoutedEventArgs e) =>
        FadeDockSpotlight();

    private void RefreshDockSpotlightTheme()
    {
        if (_dockPointerInside && _dockGlassBrush is { } brush)
        {
            brush.AnimateScalar(
                "SpotlightStrength",
                RootGrid.ActualTheme == ElementTheme.Light ? 0.43f : 0.75f,
                150);
        }
    }

    private void FadeDockSpotlight()
    {
        if (!_dockPointerInside)
            return;

        _dockPointerInside = false;
        _lastDockSpotlightUpdate = 0;
        _dockGlassBrush?.AnimateScalar("SpotlightStrength", 0, 260);
    }
}
