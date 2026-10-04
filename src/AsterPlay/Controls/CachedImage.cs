using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AsterPlay.Services;

namespace AsterPlay.Controls;

public sealed class CachedImage : Image
{
    public static readonly DependencyProperty SourceUrlProperty =
        DependencyProperty.Register(
            nameof(SourceUrl),
            typeof(string),
            typeof(CachedImage),
            new PropertyMetadata("", OnSourceUrlChanged));

    private static readonly ImageSource LoadingPlaceholder = CreatePlaceholder(failed: false);
    private static readonly ImageSource FailurePlaceholder = CreatePlaceholder(failed: true);

    private CancellationTokenSource? _loadCts;
    private ScrollViewer? _scrollViewer;
    private bool _loading;
    private string _loadedUrl = "";

    public CachedImage()
    {
        Source = LoadingPlaceholder;
        Loaded += CachedImage_Loaded;
        Unloaded += CachedImage_Unloaded;
    }

    public string SourceUrl
    {
        get => (string)GetValue(SourceUrlProperty);
        set => SetValue(SourceUrlProperty, value);
    }

    private static void OnSourceUrlChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not CachedImage image)
            return;

        image.CancelPendingLoad();
        image._loadedUrl = "";
        image.Source = LoadingPlaceholder;

        if (image.IsLoaded)
            image.ScheduleViewportCheck();
    }

    private void CachedImage_Loaded(object sender, RoutedEventArgs e)
    {
        _scrollViewer = FindVisualParent<ScrollViewer>(this);
        if (_scrollViewer is not null)
            _scrollViewer.ScrollChanged += ScrollViewer_ScrollChanged;

        SizeChanged += CachedImage_SizeChanged;
        ScheduleViewportCheck();
    }

    private void CachedImage_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_scrollViewer is not null)
            _scrollViewer.ScrollChanged -= ScrollViewer_ScrollChanged;

        SizeChanged -= CachedImage_SizeChanged;
        _scrollViewer = null;
        CancelPendingLoad();
    }

    private void ScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e) =>
        TryStartLoad();

    private void CachedImage_SizeChanged(object sender, SizeChangedEventArgs e) =>
        TryStartLoad();

    private void ScheduleViewportCheck() =>
        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(TryStartLoad));

    private void TryStartLoad()
    {
        if (!IsLoaded || _loading)
            return;

        var url = SourceUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            Source = FailurePlaceholder;
            return;
        }

        if (string.Equals(_loadedUrl, url, StringComparison.Ordinal))
            return;

        if (!IsInViewport())
            return;

        _ = LoadAsync(url);
    }

    private async Task LoadAsync(string url)
    {
        _loading = true;
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;

        try
        {
            var image = await ImageCacheService.Shared.GetAsync(url, token);

            if (token.IsCancellationRequested ||
                !string.Equals(SourceUrl, url, StringComparison.Ordinal))
            {
                return;
            }

            Source = image ?? FailurePlaceholder;
            _loadedUrl = url;
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            if (!token.IsCancellationRequested &&
                string.Equals(SourceUrl, url, StringComparison.Ordinal))
            {
                Source = FailurePlaceholder;
                _loadedUrl = url;
            }
        }
        finally
        {
            _loading = false;
            _loadCts?.Dispose();
            _loadCts = null;
        }
    }

    private bool IsInViewport()
    {
        if (_scrollViewer is null)
            return true;

        if (ActualWidth <= 0 || ActualHeight <= 0)
            return false;

        try
        {
            var bounds = TransformToAncestor(_scrollViewer)
                .TransformBounds(new Rect(0, 0, ActualWidth, ActualHeight));

            var viewport = new Rect(
                0,
                0,
                _scrollViewer.ActualWidth,
                _scrollViewer.ActualHeight);

            return viewport.IntersectsWith(bounds);
        }
        catch
        {
            return true;
        }
    }

    private void CancelPendingLoad()
    {
        try { _loadCts?.Cancel(); }
        catch { }

        _loadCts?.Dispose();
        _loadCts = null;
        _loading = false;
    }

    private static T? FindVisualParent<T>(DependencyObject child)
        where T : DependencyObject
    {
        DependencyObject? current = child;

        while (current is not null)
        {
            current = VisualTreeHelper.GetParent(current);
            if (current is T typed)
                return typed;
        }

        return null;
    }

    private static ImageSource CreatePlaceholder(bool failed)
    {
        var group = new DrawingGroup();

        group.Children.Add(
            new GeometryDrawing(
                new SolidColorBrush(Color.FromRgb(0x17, 0x17, 0x1E)),
                null,
                new RectangleGeometry(new Rect(0, 0, 100, 100))));

        if (failed)
        {
            var pen = new Pen(
                new SolidColorBrush(Color.FromRgb(0x65, 0x6B, 0x77)),
                5);

            var geometry = new GeometryGroup();
            geometry.Children.Add(new LineGeometry(new Point(34, 34), new Point(66, 66)));
            geometry.Children.Add(new LineGeometry(new Point(66, 34), new Point(34, 66)));

            group.Children.Add(new GeometryDrawing(null, pen, geometry));
        }

        group.Freeze();

        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }
}
