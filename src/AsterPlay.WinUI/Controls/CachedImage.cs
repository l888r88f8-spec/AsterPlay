using AsterPlay.WinUI.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.Storage.Streams;

namespace AsterPlay.WinUI.Controls;

public sealed class CachedImage : Image
{
    public static readonly DependencyProperty SourceUrlProperty =
        DependencyProperty.Register(
            nameof(SourceUrl),
            typeof(string),
            typeof(CachedImage),
            new PropertyMetadata("", OnSourceUrlChanged));

    private const double PreloadMargin = 220;

    private CancellationTokenSource? _loadCts;
    private readonly List<ScrollViewer> _scrollViewers = [];
    private bool _loading;
    private string _loadedUrl = "";

    public CachedImage()
    {
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
        image.Source = null;

        if (image.IsLoaded)
            image.ScheduleViewportCheck();
    }

    private void CachedImage_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        AttachScrollViewers();
        SizeChanged += CachedImage_SizeChanged;
        ScheduleViewportCheck();
    }

    private void CachedImage_Unloaded(
        object sender,
        RoutedEventArgs e)
    {
        DetachScrollViewers();
        SizeChanged -= CachedImage_SizeChanged;
        CancelPendingLoad();
    }

    private void AttachScrollViewers()
    {
        DetachScrollViewers();

        DependencyObject? current = this;
        while (current is not null)
        {
            current = VisualTreeHelper.GetParent(current);
            if (current is ScrollViewer viewer)
            {
                _scrollViewers.Add(viewer);
                viewer.ViewChanged += ScrollViewer_ViewChanged;
            }
        }
    }

    private void DetachScrollViewers()
    {
        foreach (var viewer in _scrollViewers)
            viewer.ViewChanged -= ScrollViewer_ViewChanged;

        _scrollViewers.Clear();
    }

    private void ScrollViewer_ViewChanged(
        object? sender,
        ScrollViewerViewChangedEventArgs e) =>
        TryStartLoad();

    private void CachedImage_SizeChanged(
        object sender,
        SizeChangedEventArgs e) =>
        TryStartLoad();

    private void ScheduleViewportCheck()
    {
        if (DispatcherQueue is DispatcherQueue dispatcher)
            dispatcher.TryEnqueue(TryStartLoad);
        else
            TryStartLoad();
    }

    private void TryStartLoad()
    {
        if (!IsLoaded || _loading)
            return;

        var url = SourceUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            Source = null;
            return;
        }

        if (string.Equals(
                _loadedUrl,
                url,
                StringComparison.Ordinal))
        {
            return;
        }

        if (!IsNearViewport())
            return;

        _ = LoadAsync(url);
    }

    private async Task LoadAsync(string url)
    {
        _loading = true;
        var loadCts = new CancellationTokenSource();
        _loadCts = loadCts;
        var token = loadCts.Token;

        try
        {
            var bytes =
                await ImageCacheService.Shared.GetBytesAsync(
                    url,
                    token);

            if (token.IsCancellationRequested ||
                !string.Equals(
                    SourceUrl,
                    url,
                    StringComparison.Ordinal))
            {
                return;
            }

            if (bytes is null || bytes.Length == 0)
            {
                Source = null;
                _loadedUrl = url;
                return;
            }

            var bitmap = await DecodeAsync(bytes);

            if (token.IsCancellationRequested ||
                !string.Equals(
                    SourceUrl,
                    url,
                    StringComparison.Ordinal))
            {
                return;
            }

            if (bitmap is null)
            {
                ImageCacheService.Shared.Invalidate(url);
                Source = null;
            }
            else
            {
                Source = bitmap;
            }

            _loadedUrl = url;
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            if (!token.IsCancellationRequested &&
                string.Equals(
                    SourceUrl,
                    url,
                    StringComparison.Ordinal))
            {
                Source = null;
                _loadedUrl = url;
            }
        }
        finally
        {
            loadCts.Dispose();

            if (ReferenceEquals(_loadCts, loadCts))
            {
                _loadCts = null;
                _loading = false;
            }
        }
    }

    private static async Task<BitmapImage?> DecodeAsync(
        byte[] bytes)
    {
        try
        {
            using var stream =
                new InMemoryRandomAccessStream();

            using (var writer =
                   new DataWriter(
                       stream.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(bytes);
                await writer.StoreAsync();
                await writer.FlushAsync();
                writer.DetachStream();
            }

            stream.Seek(0);

            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream);
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private bool IsNearViewport()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0)
            return false;

        if (_scrollViewers.Count == 0)
            return true;

        foreach (var viewer in _scrollViewers)
        {
            if (viewer.ActualWidth <= 0 ||
                viewer.ActualHeight <= 0)
            {
                continue;
            }

            try
            {
                var transform = TransformToVisual(viewer);
                var bounds = transform.TransformBounds(
                    new Rect(
                        0,
                        0,
                        ActualWidth,
                        ActualHeight));

                var viewport = new Rect(
                    -PreloadMargin,
                    -PreloadMargin,
                    viewer.ActualWidth +
                    PreloadMargin * 2,
                    viewer.ActualHeight +
                    PreloadMargin * 2);

                if (!Intersects(viewport, bounds))
                    return false;
            }
            catch
            {
                // If the element is temporarily between visual trees,
                // avoid blocking the load forever.
            }
        }

        return true;
    }

    private static bool Intersects(
        Rect lhs,
        Rect rhs) =>
        lhs.Left < rhs.Right &&
        lhs.Right > rhs.Left &&
        lhs.Top < rhs.Bottom &&
        lhs.Bottom > rhs.Top;

    private void CancelPendingLoad()
    {
        try
        {
            _loadCts?.Cancel();
        }
        catch
        {
        }

        _loadCts?.Dispose();
        _loadCts = null;
        _loading = false;
    }
}
