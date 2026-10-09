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

    // Observe the whole content root instead of relying on NavigationDock's
    // PointerExited event. Child buttons can capture/handle the pointer and make
    // that event unreliable. The root always sees the next move after the pointer
    // crosses the Dock boundary and can deterministically fade the shader.
    private void InitializeDockShimmer()
    {
        RootGrid.AddHandler(
            UIElement.PointerMovedEvent,
            new PointerEventHandler(DockShimmer_PointerMoved),
            true);
        RootGrid.AddHandler(
            UIElement.PointerExitedEvent,
            new PointerEventHandler(DockShimmer_PointerExited),
            true);
        RootGrid.AddHandler(
            UIElement.PointerReleasedEvent,
            new PointerEventHandler(DockShimmer_PointerReleased),
            true);
        RootGrid.AddHandler(
            UIElement.PointerCanceledEvent,
            new PointerEventHandler(DockShimmer_PointerCanceled),
            true);
        RootGrid.AddHandler(
            UIElement.PointerCaptureLostEvent,
            new PointerEventHandler(DockShimmer_PointerCaptureLost),
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
        RootGrid.RemoveHandler(
            UIElement.PointerMovedEvent,
            new PointerEventHandler(DockShimmer_PointerMoved));
        RootGrid.RemoveHandler(
            UIElement.PointerExitedEvent,
            new PointerEventHandler(DockShimmer_PointerExited));
        RootGrid.RemoveHandler(
            UIElement.PointerReleasedEvent,
            new PointerEventHandler(DockShimmer_PointerReleased));
        RootGrid.RemoveHandler(
            UIElement.PointerCanceledEvent,
            new PointerEventHandler(DockShimmer_PointerCanceled));
        RootGrid.RemoveHandler(
            UIElement.PointerCaptureLostEvent,
            new PointerEventHandler(DockShimmer_PointerCaptureLost));
        NavigationDock.UnregisterPropertyChangedCallback(
            UIElement.VisibilityProperty,
            _dockVisibilityCallbackToken);

        _dockPointerInside = false;
        _dockGlassBrush = null;
    }

    private void DockShimmer_PointerMoved(
        object sender,
        PointerRoutedEventArgs e)
    {
        if (NavigationDock.Visibility == Visibility.Visible && IsInsideDock(e))
            UpdateDockShimmer(e);
        else
            FadeDockShimmer();
    }

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
        // Ignore routed exits from individual Dock children. A real Dock/window
        // exit reports a point outside these bounds and is faded immediately.
        if (!IsInsideDock(e))
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

    private void DockShimmer_PointerCaptureLost(
        object sender,
        PointerRoutedEventArgs e)
    {
        if (!IsInsideDock(e))
            FadeDockShimmer();
    }

    private bool IsInsideDock(PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(NavigationDock).Position;
        const double edgeInset = 0.5;
        return point.X > edgeInset && point.Y > edgeInset &&
            point.X < NavigationDock.ActualWidth - edgeInset &&
            point.Y < NavigationDock.ActualHeight - edgeInset;
    }

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
