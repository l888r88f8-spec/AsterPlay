using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace AsterPlay.WinUI.Views;

public sealed partial class HomeView
{
    private readonly Dictionary<ScrollViewer, RailNavigation> _sectionRailNavigation = [];
    private bool _railOverlayHooksAttached;

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (_railOverlayHooksAttached)
            return;

        _railOverlayHooksAttached = true;
        Loaded += HomeView_RailOverlayLoaded;
        Unloaded += HomeView_RailOverlayUnloaded;
    }

    private void HomeView_RailOverlayLoaded(object sender, RoutedEventArgs e)
    {
        ConfigurePrimaryRailOverlays();

        LayoutUpdated -= HomeView_RailOverlayLayoutUpdated;
        LayoutUpdated += HomeView_RailOverlayLayoutUpdated;

        DispatcherQueue.TryEnqueue(ConfigureSectionRailOverlays);
    }

    private void HomeView_RailOverlayUnloaded(object sender, RoutedEventArgs e)
    {
        LayoutUpdated -= HomeView_RailOverlayLayoutUpdated;
    }

    private void HomeView_RailOverlayLayoutUpdated(object? sender, object e)
    {
        ConfigureSectionRailOverlays();
    }

    private void ConfigurePrimaryRailOverlays()
    {
        ConfigureNamedRailOverlay(
            ResumeScroller,
            ResumePreviousButton,
            ResumeNextButton,
            verticalOffset: 44);

        ConfigureNamedRailOverlay(
            LibrariesScroller,
            LibrariesPreviousButton,
            LibrariesNextButton,
            verticalOffset: 0);

        UpdateResumeButtons();
        UpdateLibraryButtons();
    }

    private static void ConfigureNamedRailOverlay(
        ScrollViewer scroller,
        Button previous,
        Button next,
        double verticalOffset)
    {
        if (VisualTreeHelper.GetParent(scroller) is not Grid rail)
            return;

        // Collapse the old three-column geometry into one full-width layer.
        // The arrows then visually float above the first/last card while their
        // Button roots remain the only top-most hit targets in those regions.
        rail.ColumnDefinitions.Clear();

        Grid.SetColumn(scroller, 0);
        Grid.SetColumnSpan(scroller, 1);

        Grid.SetColumn(previous, 0);
        Grid.SetColumnSpan(previous, 1);
        previous.HorizontalAlignment = HorizontalAlignment.Left;
        previous.VerticalAlignment = VerticalAlignment.Center;
        previous.Margin = new Thickness(8, 0, 0, verticalOffset);
        Canvas.SetZIndex(previous, 10);

        Grid.SetColumn(next, 0);
        Grid.SetColumnSpan(next, 1);
        next.HorizontalAlignment = HorizontalAlignment.Right;
        next.VerticalAlignment = VerticalAlignment.Center;
        next.Margin = new Thickness(0, 0, 8, verticalOffset);
        Canvas.SetZIndex(next, 10);
    }

    private void ConfigureSectionRailOverlays()
    {
        if (!LibrarySectionsList.IsLoaded)
            return;

        var scrollers = new List<ScrollViewer>();
        CollectHorizontalScrollViewers(LibrarySectionsList, scrollers);

        foreach (var scroller in scrollers)
        {
            if (_sectionRailNavigation.ContainsKey(scroller))
                continue;

            if (VisualTreeHelper.GetParent(scroller) is not Panel parent)
                continue;

            var index = parent.Children.IndexOf(scroller);
            if (index < 0)
                continue;

            var rail = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            parent.Children.RemoveAt(index);
            parent.Children.Insert(index, rail);
            rail.Children.Add(scroller);

            var previous = CreateFloatingRailButton("‹", -1, scroller);
            var next = CreateFloatingRailButton("›", 1, scroller);

            previous.HorizontalAlignment = HorizontalAlignment.Left;
            next.HorizontalAlignment = HorizontalAlignment.Right;

            // Both poster and landscape rows include text below the artwork.
            // Lift the arrows slightly so they sit over the artwork instead of
            // over the title/meta area.
            previous.Margin = new Thickness(8, 0, 0, 50);
            next.Margin = new Thickness(0, 0, 8, 50);

            rail.Children.Add(previous);
            rail.Children.Add(next);

            _sectionRailNavigation[scroller] =
                new RailNavigation(previous, next);

            scroller.ViewChanged += SectionRail_ViewChanged;
            scroller.SizeChanged += SectionRail_SizeChanged;

            UpdateSectionRailButtons(scroller);
        }
    }

    private Button CreateFloatingRailButton(
        string content,
        int direction,
        ScrollViewer scroller)
    {
        var button = new Button
        {
            Content = content,
            Tag = new RailArrowContext(scroller, direction),
            Visibility = Visibility.Collapsed,
            VerticalAlignment = VerticalAlignment.Center
        };

        if (Resources["RailEdgeButtonStyle"] is Style style)
            button.Style = style;

        Canvas.SetZIndex(button, 10);
        button.Click += SectionRailArrow_Click;
        return button;
    }

    private static void CollectHorizontalScrollViewers(
        DependencyObject root,
        ICollection<ScrollViewer> result)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);

        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);

            if (child is ScrollViewer scroller &&
                scroller.HorizontalScrollMode == ScrollMode.Enabled &&
                scroller.VerticalScrollMode == ScrollMode.Disabled)
            {
                result.Add(scroller);
                continue;
            }

            CollectHorizontalScrollViewers(child, result);
        }
    }

    private void SectionRail_ViewChanged(
        object? sender,
        ScrollViewerViewChangedEventArgs e)
    {
        if (sender is ScrollViewer scroller)
            UpdateSectionRailButtons(scroller);
    }

    private void SectionRail_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is ScrollViewer scroller)
            UpdateSectionRailButtons(scroller);
    }

    private void SectionRailArrow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: RailArrowContext context })
            return;

        ScrollRail(context.Scroller, context.Direction);
    }

    private static void ScrollRail(ScrollViewer scroller, int direction)
    {
        if (scroller.ScrollableWidth <= 0)
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

    private void UpdateSectionRailButtons(ScrollViewer scroller)
    {
        if (!_sectionRailNavigation.TryGetValue(scroller, out var navigation))
            return;

        const double epsilon = 1.0;

        navigation.Previous.Visibility =
            scroller.HorizontalOffset > epsilon
                ? Visibility.Visible
                : Visibility.Collapsed;

        navigation.Next.Visibility =
            scroller.HorizontalOffset < scroller.ScrollableWidth - epsilon
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private sealed record RailArrowContext(
        ScrollViewer Scroller,
        int Direction);

    private sealed record RailNavigation(
        Button Previous,
        Button Next);
}
