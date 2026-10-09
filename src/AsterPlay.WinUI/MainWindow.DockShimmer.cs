using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace AsterPlay.WinUI;

public sealed partial class MainWindow
{
    private bool _dockPointerInside;
    private bool _dockShimmerActivatedLogged;
    private bool _dockShimmerUnavailableLogged;
    private long _lastDockShimmerUpdate;
    private long _dockVisibilityCallbackToken;

    // handledEventsToo receives pointer moves over the Button children. The Dock
    // therefore drives the library's shader directly; no hit-test or light overlay
    // is added to the XAML tree.
    private void InitializeDockShimmer()
    {
        NavigationDock.AddHandler(
            UIElement.PointerEnteredEvent,
            new PointerEventHandler(DockShimmer_PointerEntered),
            true);
        NavigationDock.AddHandler(
            UIElement.PointerMovedEvent,
            new PointerEventHandler(DockShimmer_PointerMoved),
            true);
        NavigationDock.AddHandler(
            UIElement.PointerExitedEvent,
            new PointerEventHandler(DockShimmer_PointerExited),
            true);
        NavigationDock.AddHandler(
            UIElement.PointerReleasedEvent,
            new PointerEventHandler(DockShimmer_PointerReleased),
            true);
        NavigationDock.AddHandler(
            UIElement.PointerCanceledEvent,
            new PointerEventHandler(DockShimmer_PointerCanceled),
            true);

        _dockVisibilityCallbackToken = NavigationDock.RegisterPropertyChangedCallback(
            UIElement.VisibilityProperty,
            (_, _) =>
            {
                if (NavigationDock.Visibility != Visibility.Visible)
                    FadeDockShimmer();
            });
    }

    private void DetachDockShimmer()
    {
        NavigationDock.RemoveHandler(
            UIElement.PointerEnteredEvent,
            new PointerEventHandler(DockShimmer_PointerEntered));
        NavigationDock.RemoveHandler(
            UIElement.PointerMovedEvent,
            new PointerEventHandler(DockShimmer_PointerMoved));
        NavigationDock.RemoveHandler(
            UIElement.PointerExitedEvent,
            new PointerEventHandler(DockShimmer_PointerExited));
        NavigationDock.RemoveHandler(
            UIElement.PointerReleasedEvent,
            new PointerEventHandler(DockShimmer_PointerReleased));
        NavigationDock.RemoveHandler(
            UIElement.PointerCanceledEvent,
            new PointerEventHandler(DockShimmer_PointerCanceled));
        NavigationDock.UnregisterPropertyChangedCallback(
            UIElement.VisibilityProperty,
            _dockVisibilityCallbackToken);

        _dockPointerInside = false;
        _dockGlassBrush = null;
    }

    private void DockShimmer_PointerEntered(
        object sender,
        PointerRoutedEventArgs e) => UpdateDockShimmer(e);

    private void DockShimmer_PointerMoved(
        object sender,
        PointerRoutedEventArgs e) => UpdateDockShimmer(e);

    private void UpdateDockShimmer(PointerRoutedEventArgs e)
    {
        if (NavigationDock.Visibility != Visibility.Visible ||
            NavigationDock.ActualWidth <= 0 ||
            NavigationDock.ActualHeight <= 0)
        {
            return;
        }

        var brush = _dockGlassBrush;
        if (brush is null ||
            brush.PipelineState != LiquidGlassWinUI.LiquidGlassPipelineState.Connected ||
            !brush.IsShimmerActive)
        {
            if (!_dockShimmerUnavailableLogged)
            {
                _dockShimmerUnavailableLogged = true;
                StartupDiagnostics.Write(
                    $"DockShimmer: unavailable; brushPresent={brush is not null}; " +
                    $"pipeline={brush?.PipelineState.ToString() ?? "none"}; " +
                    $"active={brush?.IsShimmerActive.ToString() ?? "false"}; " +
                    $"error={brush?.ShimmerError ?? "none"}");
            }
            return;
        }

        var point = e.GetCurrentPoint(NavigationDock).Position;
        if (point.X < 0 || point.Y < 0 ||
            point.X > NavigationDock.ActualWidth ||
            point.Y > NavigationDock.ActualHeight)
        {
            FadeDockShimmer();
            return;
        }

        var x = (float)Math.Clamp(point.X / NavigationDock.ActualWidth, 0, 1);
        var y = (float)Math.Clamp(point.Y / NavigationDock.ActualHeight, 0, 1);

        if (!_dockPointerInside)
        {
            _dockPointerInside = true;
            _lastDockShimmerUpdate = Stopwatch.GetTimestamp();
            brush.ShimmerX = x;
            brush.ShimmerY = y;
            brush.AnimateScalar(
                "ShimmerStrength",
                RootGrid.ActualTheme == ElementTheme.Light ? 0.58f : 0.82f,
                110);

            if (!_dockShimmerActivatedLogged)
            {
                _dockShimmerActivatedLogged = true;
                StartupDiagnostics.Write(
                    $"DockShimmer: active; size={NavigationDock.ActualWidth:0}x" +
                    $"{NavigationDock.ActualHeight:0}; center={x:0.00},{y:0.00}; " +
                    $"radius={brush.ShimmerRadius:0}");
            }
            return;
        }

        // Limit property animation submissions to about 60 Hz. The compositor
        // interpolates a short ease-out between UI-thread pointer samples so the
        // highlight stays fluid without visibly trailing the pointer.
        if (Stopwatch.GetElapsedTime(_lastDockShimmerUpdate) <
            TimeSpan.FromMilliseconds(16))
        {
            return;
        }

        _lastDockShimmerUpdate = Stopwatch.GetTimestamp();
        brush.AnimateScalar("ShimmerX", x, 55);
        brush.AnimateScalar("ShimmerY", y, 55);
    }

    private void DockShimmer_PointerExited(
        object sender,
        PointerRoutedEventArgs e)
    {
        // PointerExited also bubbles while crossing child buttons. Fade only when
        // the pointer actually leaves the Dock bounds.
        var point = e.GetCurrentPoint(NavigationDock).Position;
        if (point.X >= 0 && point.Y >= 0 &&
            point.X <= NavigationDock.ActualWidth &&
            point.Y <= NavigationDock.ActualHeight)
        {
            return;
        }

        FadeDockShimmer();
    }

    private void DockShimmer_PointerReleased(
        object sender,
        PointerRoutedEventArgs e)
    {
        if (e.Pointer.PointerDeviceType.Equals(
                Windows.Devices.Input.PointerDeviceType.Touch))
        {
            FadeDockShimmer();
        }
    }

    private void DockShimmer_PointerCanceled(
        object sender,
        PointerRoutedEventArgs e) => FadeDockShimmer();

    private void RefreshDockShimmerTheme()
    {
        if (_dockPointerInside && _dockGlassBrush is { IsShimmerActive: true } brush)
        {
            brush.AnimateScalar(
                "ShimmerStrength",
                RootGrid.ActualTheme == ElementTheme.Light ? 0.58f : 0.82f,
                120);
        }
    }

    private void FadeDockShimmer()
    {
        if (!_dockPointerInside)
            return;

        _dockPointerInside = false;
        _lastDockShimmerUpdate = 0;
        _dockGlassBrush?.AnimateScalar("ShimmerStrength", 0, 240);
    }
}
