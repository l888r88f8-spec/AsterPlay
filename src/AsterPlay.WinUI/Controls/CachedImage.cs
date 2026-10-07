using AsterPlay.WinUI.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.Storage.Streams;

namespace AsterPlay.WinUI.Controls;

public sealed class CachedImage : UserControl
{
    public static readonly DependencyProperty SourceUrlProperty =
        DependencyProperty.Register(
            nameof(SourceUrl),
            typeof(string),
            typeof(CachedImage),
            new PropertyMetadata("", OnSourceUrlChanged));

    public static readonly DependencyProperty StretchProperty =
        DependencyProperty.Register(
            nameof(Stretch),
            typeof(Stretch),
            typeof(CachedImage),
            new PropertyMetadata(
                Microsoft.UI.Xaml.Media.Stretch.Uniform,
                OnStretchChanged));

    public static readonly DependencyProperty LazyLoadingEnabledProperty =
        DependencyProperty.Register(
            nameof(LazyLoadingEnabled),
            typeof(bool),
            typeof(CachedImage),
            new PropertyMetadata(true, OnLazyLoadingEnabledChanged));

    public static readonly DependencyProperty CornerRadiusProperty =
        DependencyProperty.Register(
            nameof(CornerRadius),
            typeof(CornerRadius),
            typeof(CachedImage),
            new PropertyMetadata(new CornerRadius(0), OnCornerRadiusChanged));

    private const double PreloadMargin = 220;

    private readonly Border _clipBorder = new()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Stretch
    };

    private readonly Image _image = new()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Stretch
    };

    private CancellationTokenSource? _loadCts;
    private TaskCompletionSource<bool>? _loadCompletion;
    private readonly List<ScrollViewer> _scrollViewers = [];
    private readonly List<ScrollView> _scrollViews = [];
    private bool _loading;
    private string _loadedUrl = "";

    public CachedImage()
    {
        _clipBorder.Child = _image;
        _clipBorder.CornerRadius = CornerRadius;
        Content = _clipBorder;
        _image.Stretch = Stretch;

        Loaded += CachedImage_Loaded;
        Unloaded += CachedImage_Unloaded;
    }

    public string SourceUrl
    {
        get => (string)GetValue(SourceUrlProperty);
        set => SetValue(SourceUrlProperty, value);
    }

    public Stretch Stretch
    {
        get => (Stretch)GetValue(StretchProperty);
        set => SetValue(StretchProperty, value);
    }

    public bool LazyLoadingEnabled
    {
        get => (bool)GetValue(LazyLoadingEnabledProperty);
        set => SetValue(LazyLoadingEnabledProperty, value);
    }

    public CornerRadius CornerRadius
    {
        get => (CornerRadius)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public async Task<bool> EnsureLoadedAsync(
        CancellationToken cancellationToken = default)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var url = SourceUrl;
            if (string.IsNullOrWhiteSpace(url))
                return true;

            if (string.Equals(_loadedUrl, url, StringComparison.Ordinal))
                return _image.Source is not null;

            if (_loading)
            {
                var activeLoad = _loadCompletion?.Task;
                if (activeLoad is null)
                {
                    await Task.Yield();
                    continue;
                }

                await activeLoad.WaitAsync(cancellationToken);
                continue;
            }

            await LoadAsync(url).WaitAsync(cancellationToken);

            if (string.Equals(SourceUrl, url, StringComparison.Ordinal) &&
                string.Equals(_loadedUrl, url, StringComparison.Ordinal))
            {
                return _image.Source is not null;
            }
        }
    }

    private static void OnStretchChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is CachedImage image &&
            e.NewValue is Stretch stretch)
        {
            image._image.Stretch = stretch;
        }
    }

    private static void OnCornerRadiusChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is CachedImage image &&
            e.NewValue is CornerRadius cornerRadius)
        {
            image._clipBorder.CornerRadius = cornerRadius;
        }
    }

    private static void OnLazyLoadingEnabledChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not CachedImage image || !image.IsLoaded)
            return;

        if (image.LazyLoadingEnabled)
            image.AttachScrollViewers();
        else
            image.DetachScrollViewers();

        image.ScheduleViewportCheck();
    }

    private static void OnSourceUrlChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not CachedImage image)
            return;

        image.CancelPendingLoad();
        image._loadedUrl = "";
        image._image.Source = null;

        if (image.IsLoaded)
            image.ScheduleViewportCheck();
    }

    private void CachedImage_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        if (LazyLoadingEnabled)
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
            else if (current is ScrollView scrollView)
            {
                _scrollViews.Add(scrollView);
                scrollView.ViewChanged += ScrollView_ViewChanged;
            }
        }
    }

    private void DetachScrollViewers()
    {
        foreach (var viewer in _scrollViewers)
            viewer.ViewChanged -= ScrollViewer_ViewChanged;

        foreach (var scrollView in _scrollViews)
            scrollView.ViewChanged -= ScrollView_ViewChanged;

        _scrollViewers.Clear();
        _scrollViews.Clear();
    }

    private void ScrollViewer_ViewChanged(
        object? sender,
        ScrollViewerViewChangedEventArgs e) =>
        TryStartLoad();

    private void ScrollView_ViewChanged(
        ScrollView sender,
        object args) =>
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
            _image.Source = null;
            return;
        }

        if (string.Equals(
                _loadedUrl,
                url,
                StringComparison.Ordinal))
        {
            return;
        }

        if (LazyLoadingEnabled && !IsNearViewport())
            return;

        _ = LoadAsync(url);
    }

    private async Task LoadAsync(string url)
    {
        var loadCompletion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        _loadCompletion = loadCompletion;
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
                _image.Source = null;
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
                _image.Source = null;
            }
            else
            {
                _image.Source = bitmap;
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
                _image.Source = null;
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

            if (ReferenceEquals(_loadCompletion, loadCompletion))
                _loadCompletion = null;

            loadCompletion.TrySetResult(true);

            // If the source changed while the previous decode was in flight,
            // make sure the new source gets another chance to start.
            if (IsLoaded &&
                !string.Equals(_loadedUrl, SourceUrl, StringComparison.Ordinal))
            {
                ScheduleViewportCheck();
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

        if (_scrollViewers.Count == 0 && _scrollViews.Count == 0)
            return true;

        foreach (var viewer in _scrollViewers)
        {
            if (!IsNearViewport(viewer))
                return false;
        }

        foreach (var scrollView in _scrollViews)
        {
            if (!IsNearViewport(scrollView))
                return false;
        }

        return true;
    }

    private bool IsNearViewport(FrameworkElement viewer)
    {
        if (viewer.ActualWidth <= 0 ||
            viewer.ActualHeight <= 0)
        {
            return true;
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

            return Intersects(viewport, bounds);
        }
        catch
        {
            // If the element is temporarily between visual trees,
            // avoid blocking the load forever.
            return true;
        }
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
