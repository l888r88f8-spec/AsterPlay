using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace AsterPlay.WinUI.Views;

public sealed partial class HomeView
{
    private void SectionRail_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is ScrollViewer scroller)
            UpdateSectionRailButtons(scroller);
    }

    private void SectionRail_ViewChanged(
        object sender,
        ScrollViewerViewChangedEventArgs e)
    {
        if (sender is ScrollViewer scroller)
            UpdateSectionRailButtons(scroller);
    }

    private void SectionRail_SizeChanged(
        object sender,
        SizeChangedEventArgs e)
    {
        if (sender is ScrollViewer scroller)
            UpdateSectionRailButtons(scroller);
    }

    private void SectionRailArrow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } button ||
            !int.TryParse(tag, out var direction) ||
            VisualTreeHelper.GetParent(button) is not Grid rail)
        {
            return;
        }

        ScrollViewer? scroller = null;
        foreach (var child in rail.Children)
        {
            if (child is ScrollViewer candidate)
            {
                scroller = candidate;
                break;
            }
        }

        if (scroller is null || scroller.ScrollableWidth <= 0)
            return;

        var distance = Math.Max(300, scroller.ViewportWidth * 0.82);
        var target = Math.Clamp(
            scroller.HorizontalOffset + Math.Sign(direction) * distance,
            0,
            scroller.ScrollableWidth);

        scroller.ChangeView(
            horizontalOffset: target,
            verticalOffset: null,
            zoomFactor: null,
            disableAnimation: false);
    }

    private static void UpdateSectionRailButtons(ScrollViewer scroller)
    {
        if (VisualTreeHelper.GetParent(scroller) is not Grid rail)
            return;

        const double epsilon = 1.0;
        var canGoBack = scroller.HorizontalOffset > epsilon;
        var canGoForward =
            scroller.HorizontalOffset < scroller.ScrollableWidth - epsilon;

        foreach (var child in rail.Children)
        {
            if (child is not Button { Tag: string tag } button ||
                !int.TryParse(tag, out var direction))
            {
                continue;
            }

            button.Visibility = direction < 0
                ? (canGoBack ? Visibility.Visible : Visibility.Collapsed)
                : (canGoForward ? Visibility.Visible : Visibility.Collapsed);
        }
    }
}
