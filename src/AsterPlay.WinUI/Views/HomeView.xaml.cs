using System.Collections.ObjectModel;
using AsterPlay.Models;
using AsterPlay.Services;
using AsterPlay.WinUI.Controls;
using AsterPlay.WinUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Dispatching;

namespace AsterPlay.WinUI.Views;

public sealed partial class HomeView : UserControl
{
    private const int MaxLibrarySections = 6;
    private const int SectionItemLimit = 6;

    private readonly EmbyClient _client;
    private readonly ObservableCollection<HomeLibraryTile> _libraries = [];
    private readonly ObservableCollection<ResumeMediaTile> _resume = [];
    private readonly ObservableCollection<HomeLibrarySection> _sections = [];
    private readonly List<EmbyItem> _heroCandidates = [];
    private readonly DispatcherQueueTimer _heroTimer;

    private EmbyItem? _heroItem;
    private int _heroIndex;
    private int _heroPreloadGeneration;
    private int _heroCaptionContrastGeneration;
    private bool? _currentHeroCaptionUseDarkGlyphs;
    private bool _heroImagesReady;
    private bool _heroShowingPrimary = true;
    private bool _heroVisualInitialized;
    private string _currentHeroBackdropUrl = "";
    private Storyboard? _heroTransitionStoryboard;
    private Storyboard? _serverChooserStoryboard;
    private bool _serverChooserClosing;
    private readonly bool _noServerMode;
    private bool _hasCachedSnapshot;
    private HomeSnapshot? _cachedSnapshot;
    private bool _initialVisualReadyRaised;
    private bool _deferredHomeRefreshStarted;
    private bool _navigationRefreshInProgress;
    private IReadOnlyList<EmbyItem>? _pendingSectionViews;
    private IReadOnlyList<EmbyItem>? _pendingLatest;
    private IReadOnlyList<EmbyItem>? _pendingResume;
    private string _pendingServerUrl = "";
    private string _pendingUserId = "";
    private Task _heroPreloadTask = Task.CompletedTask;

    public event EventHandler? LibraryRequested;
    public event EventHandler<EmbyItem>? MediaRequested;
    public event EventHandler<EmbyItem>? PlayRequested;
    public event EventHandler<EmbyItem>? RestartRequested;
    public event EventHandler? AuthenticationFailed;
    public event EventHandler? ServerRequested;
    public event EventHandler<ServerProfile>? ServerSwitchRequested;
    public event EventHandler? SearchRequested;
    public event EventHandler? InitialVisualReady;
    // null = use the page/theme background; bool = explicit Hero contrast.
    public event Action<bool?>? HeroCaptionContrastChanged;

    public HomeView(EmbyClient client, bool noServerMode = false)
    {
        StartupDiagnostics.Write($"HomeView constructor: entered; noServerMode={noServerMode}");
        _client = client;
        _noServerMode = noServerMode;

        using (StartupDiagnostics.Measure("HomeView.InitializeComponent"))
            InitializeComponent();

        StartupDiagnostics.Write("HomeView constructor: after InitializeComponent");

        _heroTimer = DispatcherQueue.CreateTimer();
        _heroTimer.Interval = TimeSpan.FromSeconds(8);
        _heroTimer.IsRepeating = true;
        _heroTimer.Tick += HeroTimer_Tick;

        Loaded += HomeView_Activated;
        Unloaded += HomeView_Unloaded;

        LibrariesGrid.ItemsSource = _libraries;
        ResumeGrid.ItemsSource = _resume;
        LibrarySectionsList.ItemsSource = _sections;

        WelcomeBlock.Text = string.IsNullOrWhiteSpace(_client.UserName)
            ? "欢迎回来"
            : $"欢迎回来，{_client.UserName}";

        ServerNameBlock.Text = ResolveServerDisplayName();
        CurrentUserAvatarImage.SourceUrl =
            _client.IsAuthenticated
                ? _client.BuildCurrentUserPrimaryUrl(160)
                : "";

        if (_noServerMode)
        {
            ShowNoServerState();
            Loaded += HomeView_NoServerLoaded;
        }
        else
        {
            ShowLoadingState();
            Loaded += HomeView_Loaded;
        }

        StartupDiagnostics.Write("HomeView constructor: completed");
    }

    private void HomeView_Activated(object sender, RoutedEventArgs e)
    {
        if (_heroImagesReady && _heroCandidates.Count > 1)
            _heroTimer.Start();

    }

    // Empty home has no network images to wait for, but it still must finish
    // arranging its *visible* content before the native splash can be removed.
    private void HomeView_NoServerLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= HomeView_NoServerLoaded;
        NoServerContent.SizeChanged += NoServerContent_SizeChanged;
        TryRaiseNoServerInitialVisualReady();
    }

    private void NoServerContent_SizeChanged(object sender, SizeChangedEventArgs e) =>
        TryRaiseNoServerInitialVisualReady();

    private void TryRaiseNoServerInitialVisualReady()
    {
        if (!IsLoaded ||
            NoServerState.Visibility != Visibility.Visible ||
            NoServerContent.ActualWidth < 100 ||
            NoServerContent.ActualHeight < 100)
        {
            return;
        }

        NoServerContent.SizeChanged -= NoServerContent_SizeChanged;
        StartupDiagnostics.Write(
            $"HomeView: no-server content arranged; " +
            $"size={NoServerContent.ActualWidth:0}x{NoServerContent.ActualHeight:0}");
        RaiseInitialVisualReady();
    }

    private void HomeView_Unloaded(object sender, RoutedEventArgs e)
    {
        _heroTimer.Stop();
        _heroTransitionStoryboard?.Stop();
        _heroTransitionStoryboard = null;
        _serverChooserStoryboard?.Stop();
        _serverChooserStoryboard = null;
    }

    private void HeroTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        if (_heroCandidates.Count <= 1)
            return;

        _heroIndex = (_heroIndex + 1) % _heroCandidates.Count;
        ApplyHero(_heroCandidates[_heroIndex]);
    }

    internal bool HandleNativeMouseWheel(int delta)
    {
        if (!IsLoaded ||
            HomeScrollViewer.Visibility != Visibility.Visible ||
            !HomeScrollViewer.IsHitTestVisible ||
            HomeScrollViewer.ScrollableHeight <= 0)
        {
            return false;
        }

        // WM_MOUSEWHEEL uses 120 units per traditional wheel notch.
        // Keep the delta proportional so precision touchpads remain smooth.
        var scrollDelta = delta * 0.8;
        var target = Math.Clamp(
            HomeScrollViewer.VerticalOffset - scrollDelta,
            0,
            HomeScrollViewer.ScrollableHeight);

        if (Math.Abs(target - HomeScrollViewer.VerticalOffset) < 0.1)
            return true;

        HomeScrollViewer.ChangeView(
            horizontalOffset: null,
            verticalOffset: target,
            zoomFactor: null,
            disableAnimation: true);

        return true;
    }

    private void HomeScrollViewer_ViewChanged(
        object sender,
        ScrollViewerViewChangedEventArgs e)
    {
        UpdateCaptionContrastForScrollPosition();
    }

    private void UpdateCaptionContrastForScrollPosition()
    {
        // The native caption buttons occupy the top title-bar strip. Once that
        // strip is no longer over the Hero, stop using Hero-derived contrast.
        var heroStillBehindCaption =
            HomeScrollViewer.VerticalOffset <
            Math.Max(0, HeroContainer.Height - 72);

        HeroCaptionContrastChanged?.Invoke(
            heroStillBehindCaption
                ? _currentHeroCaptionUseDarkGlyphs
                : null);
    }

    private void HomeScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var width = e.NewSize.Width;
        var horizontalPadding = width switch
        {
            >= 2200 => 48d,
            >= 1700 => 36d,
            >= 1200 => 28d,
            >= 900 => 22d,
            _ => 16d
        };

        HomeContentStack.Padding =
            new Thickness(horizontalPadding, 0, horizontalPadding, 130);

        // Keep the Hero dominant on large windows while retaining the mobile
        // reference's proportions on shorter desktop windows.
        HeroContainer.Height = Math.Clamp(e.NewSize.Height * 0.78, 590, 760);
        HeroContainer.Margin =
            new Thickness(-horizontalPadding, 0, -horizontalPadding, -112);

        UpdateCaptionContrastForScrollPosition();
    }

    private void HomeView_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= HomeView_Loaded;

        HomeScrollViewer.ChangeView(
            horizontalOffset: null,
            verticalOffset: 0,
            zoomFactor: null,
            disableAnimation: true);

        StartupDiagnostics.Write(
            $"HomeView.Loaded: first home frame is ready; verticalOffset={HomeScrollViewer.VerticalOffset:0.0}");

        DispatcherQueue.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            async () =>
            {
                // Server-name refresh is not a prerequisite for first paint.
                // Let it run independently so a slow server cannot hold the
                // startup splash open.
                _ = RefreshServerDisplayNameAsync();

                await LoadCachedSnapshotAsync();

                // Startup should finish only after this launch has actually
                // contacted the server and populated the home data. Cached
                // content may be applied early behind the splash, but it does
                // not end the splash by itself.
                var serverRefreshSucceeded = await LoadAsync();

                StartupDiagnostics.Write(
                    $"HomeView: server refresh finished; success={serverRefreshSucceeded}, " +
                    $"cachedSnapshot={_hasCachedSnapshot}, isLoaded={IsLoaded}, " +
                    $"libraries={_libraries.Count}, resume={_resume.Count}, sections={_sections.Count}");

                if (!IsLoaded)
                {
                    StartupDiagnostics.Write(
                        "HomeView: startup visual wait aborted because view is no longer loaded");
                    return;
                }

                // Do not dismiss the splash merely because data objects exist.
                // Wait until every image that is actually visible in the initial
                // viewport has finished its byte fetch + BitmapImage decode +
                // Image.Source assignment. A failed image counts as settled so
                // one bad artwork URL cannot trap the app behind the splash.
                if (serverRefreshSucceeded || _hasCachedSnapshot)
                    await WaitForInitialViewportImagesAsync();
                else
                    await WaitForNextRenderingFrameAsync();

                if (IsLoaded)
                {
                    StartupDiagnostics.Write(
                        "HomeView: all startup visual prerequisites finished; raising InitialVisualReady");
                    RaiseInitialVisualReady();
                }
                else
                {
                    StartupDiagnostics.Write(
                        "HomeView: prerequisites finished but view unloaded before InitialVisualReady");
                }
            });
    }

    internal string GetStartupDiagnosticState() =>
        $"loaded={IsLoaded}; noServer={_noServerMode}; cache={_hasCachedSnapshot}; " +
        $"initialReady={_initialVisualReadyRaised}; deferredStarted={_deferredHomeRefreshStarted}; " +
        $"loadingVisibility={LoadingState.Visibility}; loadingActive={LoadingRing.IsActive}; " +
        $"scrollVisibility={HomeScrollViewer.Visibility}; scrollOpacity={HomeScrollViewer.Opacity:0.000}; " +
        $"scroll={HomeScrollViewer.ActualWidth:0}x{HomeScrollViewer.ActualHeight:0}; " +
        $"hero={HeroContainer.ActualWidth:0}x{HeroContainer.ActualHeight:0}; " +
        $"heroCandidates={_heroCandidates.Count}; heroImagesReady={_heroImagesReady}; " +
        $"libraries={_libraries.Count}; resume={_resume.Count}; sections={_sections.Count}; " +
        $"pendingSections={_pendingSectionViews?.Count ?? 0}";

    internal bool IsInitialVisualReady => _initialVisualReadyRaised;

    // A Loaded UserControl alone is not sufficient for the splash handoff:
    // verify that the currently selected visual state has nonzero geometry.
    internal bool HasReadyStartupVisual =>
        _initialVisualReadyRaised &&
        IsLoaded &&
        (_noServerMode
            ? NoServerState.Visibility == Visibility.Visible &&
              NoServerContent.ActualWidth > 100 &&
              NoServerContent.ActualHeight > 100
            : (HomeScrollViewer.Visibility == Visibility.Visible &&
               HomeScrollViewer.Opacity > 0.9 &&
               HomeScrollViewer.ActualWidth > 100 &&
               HomeScrollViewer.ActualHeight > 100) ||
              (LoadingState.Visibility == Visibility.Visible &&
               LoadingState.ActualWidth > 100 &&
               LoadingState.ActualHeight > 100));

    // Loaded/ActualWidth and DwmFlush do not establish that the first-run
    // content has rasterized. Probe the actual nonempty no-server XAML
    // subtree before allowing the native splash to become transparent.
    internal async Task<bool> VerifyNoServerRasterAsync()
    {
        if (!_noServerMode)
            return true;

        if (!HasReadyStartupVisual)
            return false;

        try
        {
            var bitmap =
                new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
            await bitmap.RenderAsync(NoServerContent);
            var buffer = await bitmap.GetPixelsAsync();

            if (bitmap.PixelWidth <= 0 ||
                bitmap.PixelHeight <= 0 ||
                buffer.Length < 4)
            {
                StartupDiagnostics.Write(
                    "No-server raster probe: empty bitmap");
                return false;
            }

            // RenderTargetBitmap pixels are BGRA8. Read only in memory;
            // never persist screenshots or application content to logs.
            var pixels = new byte[checked((int)buffer.Length)];
            using (var reader = Windows.Storage.Streams.DataReader.FromBuffer(buffer))
                reader.ReadBytes(pixels);

            var visibleSamples = 0;
            var minBrightness = 255;
            var maxBrightness = 0;

            // Sample every fourth pixel: first-run artwork consists of
            // readable text and a bordered glyph on a transparent panel.
            for (var i = 0; i + 3 < pixels.Length; i += 16)
            {
                if (pixels[i + 3] < 32)
                    continue;

                visibleSamples++;
                var brightness =
                    (pixels[i] + pixels[i + 1] + pixels[i + 2]) / 3;
                minBrightness = Math.Min(minBrightness, brightness);
                maxBrightness = Math.Max(maxBrightness, brightness);
            }

            var hasVisibleRaster =
                visibleSamples >= 32 &&
                maxBrightness - minBrightness >= 18;

            StartupDiagnostics.Write(
                $"No-server raster probe: rendered={hasVisibleRaster}; " +
                $"size={bitmap.PixelWidth}x{bitmap.PixelHeight}; " +
                $"visibleSamples={visibleSamples}; " +
                $"brightnessRange={maxBrightness - minBrightness}");
            return hasVisibleRaster;
        }
        catch (Exception ex)
        {
            // A failed readback must be distinguishable from a genuinely
            // empty render. The presentation fence owns retry/fallback.
            StartupDiagnostics.WriteException(
                "No-server raster probe failed", ex);
            return false;
        }
    }

    private void RaiseInitialVisualReady()
    {
        if (_initialVisualReadyRaised)
            return;

        _initialVisualReadyRaised = true;

        StartupDiagnostics.WriteState(
            "Home.InitialVisualReady.beforeEvent",
            GetStartupDiagnosticState());
        InitialVisualReady?.Invoke(this, EventArgs.Empty);
        StartupDiagnostics.WriteState(
            "Home.InitialVisualReady.afterEvent",
            GetStartupDiagnosticState());
        StartupDiagnostics.Write(
            $"HomeView: initial visual ready; cachedSnapshot={_hasCachedSnapshot}");
    }

    private async Task WaitForInitialViewportImagesAsync()
    {
        // A Rendering callback alone does not mean the first viewport has
        // been measured. On cold startup (especially with multiple new
        // LiquidGlass brushes) the ScrollViewer can still be zero-sized.
        // Wait for the real hero/content geometry before collecting images.
        for (var frame = 0; IsLoaded && frame < 24; frame++)
        {
            HomeScrollViewer.UpdateLayout();

            if (HomeScrollViewer.ActualWidth > 100 &&
                HomeScrollViewer.ActualHeight > 100 &&
                HeroContainer.ActualWidth > 100 &&
                HeroContainer.ActualHeight > 100 &&
                HomeScrollViewer.Opacity > 0.9)
            {
                break;
            }

            await WaitForNextRenderingFrameAsync();
        }

        if (!IsLoaded)
            return;

        HomeScrollViewer.UpdateLayout();
        await WaitForNextRenderingFrameAsync();

        if (!IsLoaded)
            return;

        StartupDiagnostics.Write(
            $"HomeView: viewport layout ready; hero={HeroContainer.ActualWidth:0.0}x{HeroContainer.ActualHeight:0.0}, " +
            $"visible={HomeScrollViewer.Opacity > 0.9}, actual={HomeScrollViewer.ActualWidth:0.0}x{HomeScrollViewer.ActualHeight:0.0}, " +
            $"viewport={HomeScrollViewer.ViewportWidth:0.0}x{HomeScrollViewer.ViewportHeight:0.0}, " +
            $"extent={HomeScrollViewer.ExtentWidth:0.0}x{HomeScrollViewer.ExtentHeight:0.0}, " +
            $"offset={HomeScrollViewer.VerticalOffset:0.0}, scrollable={HomeScrollViewer.ScrollableHeight:0.0}");

        var realizedImageCount = CountCachedImages(HomeScrollViewer);
        var images = new HashSet<CachedImage>();

        CollectVisibleCachedImages(
            HomeScrollViewer,
            HomeScrollViewer,
            images);

        if (!string.IsNullOrWhiteSpace(CurrentUserAvatarImage.SourceUrl))
            images.Add(CurrentUserAvatarImage);

        var targets = images
            .Where(image => !string.IsNullOrWhiteSpace(image.SourceUrl))
            .ToArray();

        StartupDiagnostics.Write(
            $"HomeView: waiting for initial viewport images; realized={realizedImageCount}, targets={targets.Length}");

        for (var index = 0; index < targets.Length; index++)
        {
            var image = targets[index];
            StartupDiagnostics.Write(
                $"HomeView: startup image[{index}] begin; name={ResolveImageName(image)}, " +
                $"size={image.ActualWidth:0.0}x{image.ActualHeight:0.0}, lazy={image.LazyLoadingEnabled}, " +
                $"source={image.SourceUrl}");
        }

        var imageStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var results = await Task.WhenAll(
            targets.Select(image => image.EnsureLoadedAsync()));
        imageStopwatch.Stop();

        for (var index = 0; index < targets.Length; index++)
        {
            var image = targets[index];
            StartupDiagnostics.Write(
                $"HomeView: startup image[{index}] settled; decoded={results[index]}, " +
                $"name={ResolveImageName(image)}, source={image.SourceUrl}");
        }

        StartupDiagnostics.Write(
            $"HomeView: initial viewport images settled; decoded={results.Count(result => result)}/{results.Length}, " +
            $"elapsed={imageStopwatch.Elapsed.TotalMilliseconds:0.0} ms");

        // Ensure the decoded BitmapImage sources have reached the compositor.
        await WaitForNextRenderingFrameAsync();
        StartupDiagnostics.Write(
            "HomeView: post-image composition frame rendered");
    }

    private static int CountCachedImages(DependencyObject root)
    {
        var count = root is CachedImage ? 1 : 0;
        var childCount = VisualTreeHelper.GetChildrenCount(root);

        for (var index = 0; index < childCount; index++)
            count += CountCachedImages(VisualTreeHelper.GetChild(root, index));

        return count;
    }

    private static string ResolveImageName(CachedImage image)
    {
        if (!string.IsNullOrWhiteSpace(image.Name))
            return image.Name;

        var parent = VisualTreeHelper.GetParent(image);
        return parent is FrameworkElement frameworkElement &&
               !string.IsNullOrWhiteSpace(frameworkElement.Name)
            ? $"{frameworkElement.Name}/CachedImage"
            : "(template CachedImage)";
    }

    private static void CollectVisibleCachedImages(
        DependencyObject root,
        FrameworkElement viewport,
        ISet<CachedImage> images)
    {
        var childCount = VisualTreeHelper.GetChildrenCount(root);

        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);

            if (child is CachedImage image &&
                image.Visibility == Visibility.Visible &&
                image.ActualWidth > 0 &&
                image.ActualHeight > 0 &&
                IsInsideViewport(image, viewport))
            {
                images.Add(image);
            }

            CollectVisibleCachedImages(child, viewport, images);
        }
    }

    private static bool IsInsideViewport(
        FrameworkElement element,
        FrameworkElement viewport)
    {
        if (viewport.ActualWidth <= 0 || viewport.ActualHeight <= 0)
            return true;

        try
        {
            var transform = element.TransformToVisual(viewport);
            var bounds = transform.TransformBounds(
                new Windows.Foundation.Rect(
                    0,
                    0,
                    element.ActualWidth,
                    element.ActualHeight));

            return bounds.Right > 0 &&
                   bounds.Bottom > 0 &&
                   bounds.Left < viewport.ActualWidth &&
                   bounds.Top < viewport.ActualHeight;
        }
        catch
        {
            return false;
        }
    }

    private static Task WaitForNextRenderingFrameAsync()
    {
        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        EventHandler<object>? handler = null;
        handler = (_, _) =>
        {
            Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= handler;
            completion.TrySetResult(true);
        };

        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += handler;
        return completion.Task;
    }

    private async Task LoadCachedSnapshotAsync()
    {
        StartupDiagnostics.Write("HomeView: deferred snapshot load begin");

        var serverUrl = _client.ServerUrl;
        var userId = _client.UserId;
        var snapshot = await Task.Run(
            () => HomeSnapshotStore.Load(
                serverUrl,
                userId));

        StartupDiagnostics.Write(
            $"HomeView: deferred snapshot={(snapshot is null ? "miss" : "hit")}");

        if (snapshot is not null && IsLoaded)
        {
            _cachedSnapshot = snapshot;
            ApplySnapshot(snapshot);
            _hasCachedSnapshot = true;
            ShowContentState();

            PlaybackLog.Write(
                "WinUIHomeSnapshot",
                $"Loaded cached home snapshot; age={(DateTimeOffset.UtcNow - snapshot.SavedAtUtc).TotalMinutes:0.0} min, " +
                $"libraries={snapshot.Views.Count}, resume={snapshot.Resume.Count}, sections={snapshot.Sections.Count}");
        }
    }

    private async Task<bool> LoadAsync()
    {
        StartupDiagnostics.Write($"HomeView.LoadAsync: begin; cachedSnapshot={_hasCachedSnapshot}");
        if (!_hasCachedSnapshot)
            ShowLoadingState();
        var loadTimer = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var viewsTask = _client.GetViewsAsync();
            var resumeTask = _client.GetResumeAsync(24);
            var latestTask = _client.GetLatestAsync(12);

            await Task.WhenAll(viewsTask, resumeTask, latestTask);
            StartupDiagnostics.Write(
                $"HomeView network head ready; views={viewsTask.Result.Count}, resume={resumeTask.Result.Count}, latest={latestTask.Result.Count}");

            var views = viewsTask.Result
                .Where(IsVisibleLibrary)
                .Take(MaxLibrarySections)
                .ToArray();
            var resume = resumeTask.Result.ToArray();
            var latest = latestTask.Result.ToArray();

            // Compare server data with the local snapshot before touching the
            // visual tree. Unchanged groups keep their existing item containers,
            // image sources and scroll state instead of being rebuilt.
            var cached = _cachedSnapshot;
            var latestChanged =
                cached is null ||
                !HomeSnapshotComparer.ItemsEqual(cached.Latest, latest);
            var resumeChanged =
                cached is null ||
                !HomeSnapshotComparer.ItemsEqual(cached.Resume, resume);
            var viewsChanged =
                cached is null ||
                !HomeSnapshotComparer.ItemsEqual(cached.Views, views);

            if (latestChanged)
                PopulateHero(latest);
            if (resumeChanged)
                PopulateResume(resume);
            if (viewsChanged)
                PopulateLibraries(views);

            PlaybackLog.Write(
                "WinUIHomeDiff",
                $"First viewport diff: hero={latestChanged}, resume={resumeChanged}, libraries={viewsChanged}");

            _hasCachedSnapshot = true;
            ShowContentState();

            _pendingSectionViews = views;
            _pendingLatest = latest;
            _pendingResume = resume;
            _pendingServerUrl = _client.ServerUrl;
            _pendingUserId = _client.UserId;

            StartupDiagnostics.Write(
                "HomeView.LoadAsync: live first viewport populated; lower sections deferred until startup reveal");
            StartupDiagnostics.WriteState(
                "Home.LiveViewportPopulated",
                GetStartupDiagnosticState());
            return true;
        }
        catch (Exception ex)
        {
            PlaybackLog.Error("WinUIHome", ex);

            if (UserError.IsAuthenticationFailure(ex))
            {
                AuthenticationFailed?.Invoke(this, EventArgs.Empty);
                return false;
            }

            if (!_hasCachedSnapshot)
            {
                ShowLoadingError(UserError.GetMessage(ex, "加载首页"));
            }

            return false;
        }
        finally
        {
            DispatcherQueue.TryEnqueue(UpdateResumeButtons);

            StartupDiagnostics.Write("HomeView.LoadAsync: first viewport refresh completed");
            loadTimer.Stop();
            PlaybackLog.Write(
                "Performance",
                $"WinUI home first viewport load: {loadTimer.Elapsed.TotalMilliseconds:0} ms, " +
                $"libraries={_libraries.Count}, resume={_resume.Count}, sections={_sections.Count}, " +
                $"workingSet={Environment.WorkingSet / 1024d / 1024d:0.0} MB, " +
                $"managed={GC.GetTotalMemory(false) / 1024d / 1024d:0.0} MB");
        }
    }

    internal void RefreshAfterNavigation()
    {
        if (_noServerMode ||
            !_client.IsAuthenticated ||
            !_hasCachedSnapshot ||
            _navigationRefreshInProgress)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            async () => await RefreshRetainedHomeAsync());
    }

    private async Task RefreshRetainedHomeAsync()
    {
        if (_navigationRefreshInProgress ||
            !_client.IsAuthenticated)
        {
            return;
        }

        _navigationRefreshInProgress = true;

        try
        {
            var viewsTask = _client.GetViewsAsync();
            var resumeTask = _client.GetResumeAsync(24);
            var latestTask = _client.GetLatestAsync(12);

            await Task.WhenAll(
                viewsTask,
                resumeTask,
                latestTask);

            var views = viewsTask.Result
                .Where(IsVisibleLibrary)
                .Take(MaxLibrarySections)
                .ToArray();
            var resume = resumeTask.Result.ToArray();
            var latest = latestTask.Result.ToArray();

            var cached = _cachedSnapshot;
            var latestChanged =
                cached is null ||
                !HomeSnapshotComparer.ItemsEqual(
                    cached.Latest,
                    latest);
            var resumeChanged =
                cached is null ||
                !HomeSnapshotComparer.ItemsEqual(
                    cached.Resume,
                    resume);
            var viewsChanged =
                cached is null ||
                !HomeSnapshotComparer.ItemsEqual(
                    cached.Views,
                    views);

            if (latestChanged)
                PopulateHero(latest);
            if (resumeChanged)
                PopulateResume(resume);
            if (viewsChanged)
                PopulateLibraries(views);

            PlaybackLog.Write(
                "WinUIHomeDiff",
                $"Navigation refresh: hero={latestChanged}, resume={resumeChanged}, libraries={viewsChanged}");

            await RefreshLibrarySectionsAndPersistSnapshotAsync(
                views,
                latest,
                resume,
                _client.ServerUrl,
                _client.UserId);
        }
        catch (Exception ex)
        {
            PlaybackLog.Error(
                "WinUIHomeNavigationRefresh",
                ex);

            if (UserError.IsAuthenticationFailure(ex))
                AuthenticationFailed?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _navigationRefreshInProgress = false;
        }
    }

    internal void NotifyStartupRevealCompleted()
    {
        if (_deferredHomeRefreshStarted)
            return;

        _deferredHomeRefreshStarted = true;

        if (_heroImagesReady && _heroCandidates.Count > 1)
            _heroTimer.Start();

        var views = _pendingSectionViews;
        var latest = _pendingLatest;
        var resume = _pendingResume;
        var serverUrl = _pendingServerUrl;
        var userId = _pendingUserId;

        _pendingSectionViews = null;
        _pendingLatest = null;
        _pendingResume = null;
        _pendingServerUrl = "";
        _pendingUserId = "";

        if (views is null ||
            latest is null ||
            resume is null ||
            string.IsNullOrWhiteSpace(serverUrl) ||
            string.IsNullOrWhiteSpace(userId))
        {
            StartupDiagnostics.Write(
                "HomeView: startup reveal completed with no deferred section refresh");
            return;
        }

        StartupDiagnostics.WriteState(
            "Home.StartupRevealCompleted",
            GetStartupDiagnosticState());
        StartupDiagnostics.Write(
            "HomeView: startup reveal completed; starting lower sections");
        _ = RefreshLibrarySectionsAndPersistSnapshotAsync(
            views,
            latest,
            resume,
            serverUrl,
            userId);
    }

    private async Task RefreshLibrarySectionsAndPersistSnapshotAsync(
        IReadOnlyList<EmbyItem> views,
        IReadOnlyList<EmbyItem> latest,
        IReadOnlyList<EmbyItem> resume,
        string serverUrl,
        string userId)
    {
        try
        {
            var sectionTasks = views
                .Select(LoadLibrarySectionAsync)
                .ToArray();
            var sectionResults = await Task.WhenAll(sectionTasks);
            var sections = sectionResults
                .Where(section => section is not null)
                .Select(section => section!)
                .ToArray();

            var sectionSnapshots = sections
                .Select(CreateSectionSnapshot)
                .ToArray();

            // HomeView is retained across navigation. Keep its detached
            // collections current as well, so returning to the page never
            // exposes an older section tree.
            MergeLibrarySections(
                sections,
                sectionSnapshots,
                _cachedSnapshot?.Sections);

            var snapshot = new HomeSnapshot
            {
                Latest = latest.ToList(),
                Resume = resume.ToList(),
                Views = views.ToList(),
                Sections = sectionSnapshots.ToList()
            };

            var snapshotChanged =
                !HomeSnapshotComparer.SnapshotDataEquals(
                    _cachedSnapshot,
                    snapshot);

            // Persist the server result even when it is identical so the cache
            // freshness timestamp reflects successful contact with the server.
            await Task.Run(
                () => HomeSnapshotStore.Save(
                    serverUrl,
                    userId,
                    snapshot));

            _cachedSnapshot = snapshot;

            StartupDiagnostics.Write(
                $"HomeView: background sections compared; changed={snapshotChanged}, sections={sections.Length}");
        }
        catch (Exception ex)
        {
            PlaybackLog.Error(
                "WinUIHomeBackgroundSections",
                ex);
        }
    }

    private static HomeSectionSnapshot CreateSectionSnapshot(
        HomeLibrarySection section) =>
        new()
        {
            Library = section.Library,
            TotalCount = section.TotalCount,
            Items = section.Items
                .Select(item => item.Item)
                .ToList()
        };

    private void MergeLibrarySections(
        IReadOnlyList<HomeLibrarySection> liveSections,
        IReadOnlyList<HomeSectionSnapshot> liveSnapshots,
        IReadOnlyList<HomeSectionSnapshot>? cachedSnapshots)
    {
        var cachedById = (cachedSnapshots ?? Array.Empty<HomeSectionSnapshot>())
            .Where(section => !string.IsNullOrWhiteSpace(section.Library.Id))
            .GroupBy(
                section => section.Library.Id,
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase);

        var changedCount = 0;

        for (var targetIndex = 0;
             targetIndex < liveSections.Count;
             targetIndex++)
        {
            var liveSection = liveSections[targetIndex];
            var liveSnapshot = liveSnapshots[targetIndex];
            var id = liveSection.Library.Id;

            var currentIndex = FindSectionIndex(id);
            if (currentIndex < 0)
            {
                _sections.Insert(
                    Math.Min(targetIndex, _sections.Count),
                    liveSection);
                changedCount++;
                continue;
            }

            if (currentIndex != targetIndex)
            {
                _sections.Move(
                    currentIndex,
                    targetIndex);
            }

            cachedById.TryGetValue(
                id,
                out var cachedSection);

            if (!HomeSnapshotComparer.SectionEquals(
                    cachedSection,
                    liveSnapshot))
            {
                _sections[targetIndex] = liveSection;
                changedCount++;
            }
        }

        var liveIds = liveSections
            .Select(section => section.Library.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        for (var index = _sections.Count - 1;
             index >= 0;
             index--)
        {
            if (!liveIds.Contains(_sections[index].Library.Id))
            {
                _sections.RemoveAt(index);
                changedCount++;
            }
        }

        PlaybackLog.Write(
            "WinUIHomeDiff",
            $"Library section diff: changed={changedCount}, total={liveSections.Count}");
    }

    private int FindSectionIndex(string libraryId)
    {
        for (var index = 0; index < _sections.Count; index++)
        {
            if (string.Equals(
                    _sections[index].Library.Id,
                    libraryId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private void ShowNoServerState()
    {
        // Do not construct/arrange the invisible media viewport on first-run.
        HomeScrollViewer.Visibility = Visibility.Collapsed;
        HomeScrollViewer.Opacity = 0;
        HomeScrollViewer.IsHitTestVisible = false;
        LoadingState.Visibility = Visibility.Collapsed;
        LoadingRing.IsActive = false;
        NoServerState.Visibility = Visibility.Visible;
        FloatingTopControls.Visibility = Visibility.Visible;
        SearchButton.Visibility = Visibility.Collapsed;
        StartupDiagnostics.Write("HomeView state: NoServer");
    }

    private void ShowLoadingState()
    {
        HomeScrollViewer.Opacity = 0;
        HomeScrollViewer.IsHitTestVisible = false;
        NoServerState.Visibility = Visibility.Collapsed;
        LoadingTextBlock.Text = "正在加载";
        LoadingRing.IsActive = true;
        LoadingState.Visibility = Visibility.Visible;
        StartupDiagnostics.Write("HomeView state: Loading");
        StartupDiagnostics.WriteState(
            "Home.ShowLoadingState",
            GetStartupDiagnosticState());
    }

    private void ShowContentState()
    {
        NoServerState.Visibility = Visibility.Collapsed;
        LoadingState.Visibility = Visibility.Collapsed;
        LoadingRing.IsActive = false;
        FloatingTopControls.Visibility = Visibility.Visible;
        SearchButton.Visibility = Visibility.Visible;
        HomeScrollViewer.Visibility = Visibility.Visible;
        HomeScrollViewer.Opacity = 1;
        HomeScrollViewer.IsHitTestVisible = true;

        if (HomeScrollViewer.VerticalOffset > 1)
        {
            HomeScrollViewer.ChangeView(
                horizontalOffset: null,
                verticalOffset: 0,
                zoomFactor: null,
                disableAnimation: true);
        }

        StartupDiagnostics.Write(
            $"HomeView state: Content; verticalOffset={HomeScrollViewer.VerticalOffset:0.0}");
        StartupDiagnostics.WriteState(
            "Home.ShowContentState",
            GetStartupDiagnosticState());

    }

    private void ShowLoadingError(string message)
    {
        HomeScrollViewer.Opacity = 0;
        HomeScrollViewer.IsHitTestVisible = false;
        NoServerState.Visibility = Visibility.Collapsed;
        LoadingRing.IsActive = false;
        LoadingTextBlock.Text = string.IsNullOrWhiteSpace(message)
            ? "加载失败，请检查服务器连接"
            : message;
        LoadingState.Visibility = Visibility.Visible;
        StartupDiagnostics.Write($"HomeView state: LoadError; {message}");
    }

    private void GoToServers_Click(object sender, RoutedEventArgs e) =>
        ServerRequested?.Invoke(this, EventArgs.Empty);

    private void ServerPill_Click(object sender, RoutedEventArgs e)
    {
        var servers = ServerProfileStore.Load();
        if (servers.Count == 0)
        {
            ServerRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (ServerChooserPanel.Visibility == Visibility.Visible)
        {
            if (!_serverChooserClosing)
                CloseServerChooser();
            return;
        }

        var currentUrl = NormalizeServerUrl(_client.ServerUrl);
        ServerChooserList.ItemsSource = servers
            .Select(profile =>
            {
                var isCurrent = string.Equals(
                    NormalizeServerUrl(profile.Url),
                    currentUrl,
                    StringComparison.OrdinalIgnoreCase);

                return new ServerChooserItem(
                    profile,
                    profile.DisplayName,
                    isCurrent ? "✓" : "");
            })
            .ToArray();

        OpenServerChooser();
    }

    private void ServerChoice_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ServerProfile profile })
            return;

        var currentUrl = NormalizeServerUrl(_client.ServerUrl);
        if (string.Equals(
                NormalizeServerUrl(profile.Url),
                currentUrl,
                StringComparison.OrdinalIgnoreCase))
        {
            CloseServerChooser();
            return;
        }

        CloseServerChooser(() =>
            ServerSwitchRequested?.Invoke(this, profile));
    }

    private void ServerChooserDismiss_Tapped(
        object sender,
        Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        CloseServerChooser();
        e.Handled = true;
    }

    private void OpenServerChooser()
    {
        _serverChooserStoryboard?.Stop();
        _serverChooserStoryboard = null;
        _serverChooserClosing = false;

        ServerChooserDismissLayer.Visibility = Visibility.Visible;
        ServerChooserPanel.Visibility = Visibility.Visible;
        ServerChooserPanel.Opacity = 0;
        ServerChooserTransform.ScaleX = 0.96;
        ServerChooserTransform.ScaleY = 0.96;
        ServerChooserTransform.TranslateY = -8;

        var easing = new CubicEase
        {
            EasingMode = EasingMode.EaseOut
        };

        var storyboard = new Storyboard();
        AddServerChooserAnimation(
            storyboard,
            ServerChooserPanel,
            "Opacity",
            0,
            1,
            180,
            easing);
        AddServerChooserAnimation(
            storyboard,
            ServerChooserTransform,
            "ScaleX",
            0.96,
            1,
            180,
            easing);
        AddServerChooserAnimation(
            storyboard,
            ServerChooserTransform,
            "ScaleY",
            0.96,
            1,
            180,
            easing);
        AddServerChooserAnimation(
            storyboard,
            ServerChooserTransform,
            "TranslateY",
            -8,
            0,
            180,
            easing);

        _serverChooserStoryboard = storyboard;
        storyboard.Completed += (_, _) =>
        {
            if (!ReferenceEquals(_serverChooserStoryboard, storyboard))
                return;

            storyboard.Stop();
            ServerChooserPanel.Opacity = 1;
            ServerChooserTransform.ScaleX = 1;
            ServerChooserTransform.ScaleY = 1;
            ServerChooserTransform.TranslateY = 0;
            _serverChooserStoryboard = null;
        };
        storyboard.Begin();
    }

    private void CloseServerChooser(Action? completed = null)
    {
        if (ServerChooserPanel.Visibility != Visibility.Visible)
        {
            completed?.Invoke();
            return;
        }

        // Capture the currently animated value before stopping, so clicking
        // again during the short opening animation still fades out smoothly.
        var opacity = ServerChooserPanel.Opacity;
        var scaleX = ServerChooserTransform.ScaleX;
        var scaleY = ServerChooserTransform.ScaleY;
        var translateY = ServerChooserTransform.TranslateY;

        _serverChooserStoryboard?.Stop();
        _serverChooserStoryboard = null;

        ServerChooserPanel.Opacity = opacity;
        ServerChooserTransform.ScaleX = scaleX;
        ServerChooserTransform.ScaleY = scaleY;
        ServerChooserTransform.TranslateY = translateY;
        _serverChooserClosing = true;

        var easing = new CubicEase
        {
            EasingMode = EasingMode.EaseIn
        };

        var storyboard = new Storyboard();
        AddServerChooserAnimation(
            storyboard,
            ServerChooserPanel,
            "Opacity",
            opacity,
            0,
            140,
            easing);
        AddServerChooserAnimation(
            storyboard,
            ServerChooserTransform,
            "ScaleX",
            scaleX,
            0.97,
            140,
            easing);
        AddServerChooserAnimation(
            storyboard,
            ServerChooserTransform,
            "ScaleY",
            scaleY,
            0.97,
            140,
            easing);
        AddServerChooserAnimation(
            storyboard,
            ServerChooserTransform,
            "TranslateY",
            translateY,
            -6,
            140,
            easing);

        _serverChooserStoryboard = storyboard;
        storyboard.Completed += (_, _) =>
        {
            if (!ReferenceEquals(_serverChooserStoryboard, storyboard))
                return;

            storyboard.Stop();
            ServerChooserPanel.Visibility = Visibility.Collapsed;
            ServerChooserDismissLayer.Visibility = Visibility.Collapsed;
            ServerChooserPanel.Opacity = 0;
            ServerChooserTransform.ScaleX = 0.96;
            ServerChooserTransform.ScaleY = 0.96;
            ServerChooserTransform.TranslateY = -8;
            _serverChooserClosing = false;
            _serverChooserStoryboard = null;
            completed?.Invoke();
        };
        storyboard.Begin();
    }

    private static void AddServerChooserAnimation(
        Storyboard storyboard,
        DependencyObject target,
        string property,
        double from,
        double to,
        int durationMs,
        EasingFunctionBase easing)
    {
        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = easing
        };

        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        storyboard.Children.Add(animation);
    }

    private static string NormalizeServerUrl(string? value) =>
        (value ?? "").Trim().TrimEnd('/');

    private void Search_Click(object sender, RoutedEventArgs e)
    {
        if (ServerChooserPanel.Visibility == Visibility.Visible)
        {
            CloseServerChooser(() =>
                SearchRequested?.Invoke(this, EventArgs.Empty));
            return;
        }

        SearchRequested?.Invoke(this, EventArgs.Empty);
    }

    private async Task RefreshServerDisplayNameAsync()
    {
        if (_noServerMode || !_client.IsAuthenticated)
            return;

        try
        {
            var serverName = await _client.GetServerNameAsync();
            if (string.IsNullOrWhiteSpace(serverName))
                return;

            ServerProfileStore.UpdateServerName(_client.ServerUrl, serverName);
            ServerNameBlock.Text = ResolveServerDisplayName();
            StartupDiagnostics.Write(
                $"HomeView: discovered server name '{serverName}', display='{ServerNameBlock.Text}'");
        }
        catch (Exception ex)
        {
            PlaybackLog.Error("ServerNameLookup", ex);
        }
    }

    private string ResolveServerDisplayName()
    {
        if (_noServerMode)
            return "添加服务器";

        var serverUrl = (_client.ServerUrl ?? "").Trim().TrimEnd('/');
        var profile = ServerProfileStore.Load()
            .FirstOrDefault(item =>
                string.Equals(
                    item.Url.Trim().TrimEnd('/'),
                    serverUrl,
                    StringComparison.OrdinalIgnoreCase));

        if (profile is not null &&
            !string.IsNullOrWhiteSpace(profile.DisplayName))
        {
            return profile.DisplayName;
        }

        if (Uri.TryCreate(serverUrl, UriKind.Absolute, out var uri))
            return uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";

        return string.IsNullOrWhiteSpace(serverUrl)
            ? "服务器"
            : serverUrl;
    }

    private void ApplySnapshot(HomeSnapshot snapshot)
    {
        PopulateHero(snapshot.Latest);
        PopulateResume(snapshot.Resume);
        PopulateLibraries(snapshot.Views);

        _sections.Clear();
        foreach (var section in snapshot.Sections)
        {
            var isSeriesLibrary = IsSeriesLibrary(section.Library);
            var items = section.Items
                .Where(item => !string.IsNullOrWhiteSpace(item.Id))
                .Take(SectionItemLimit)
                .Select(item => new SectionMediaTile(
                    item,
                    item.Name,
                    BuildSectionMeta(item),
                    _client.BuildPrimaryUrl(item, 420),
                    _client.BuildBackdropUrl(item, 900)))
                .ToArray();

            if (items.Length == 0)
                continue;

            _sections.Add(new HomeLibrarySection(
                section.Library,
                section.Library.Name,
                section.TotalCount,
                section.TotalCount > 0 ? $"{section.TotalCount} 项" : "",
                isSeriesLibrary ? Visibility.Collapsed : Visibility.Visible,
                isSeriesLibrary ? Visibility.Visible : Visibility.Collapsed,
                items));
        }

        DispatcherQueue.TryEnqueue(UpdateResumeButtons);
    }

    private void PopulateHero(IReadOnlyList<EmbyItem> latest)
    {
        var previousId = _heroItem?.Id;
        var preloadGeneration = ++_heroPreloadGeneration;
        _heroImagesReady = false;

        _heroCandidates.Clear();
        _heroCandidates.AddRange(
            latest
                .Where(item => !string.IsNullOrWhiteSpace(item.Id))
                .Take(8));

        if (_heroCandidates.Count == 0)
        {
            _heroTimer.Stop();
            _heroPreloadTask = Task.CompletedTask;
            ApplyHero(null);
            return;
        }

        var previousIndex = string.IsNullOrWhiteSpace(previousId)
            ? -1
            : _heroCandidates.FindIndex(item =>
                string.Equals(
                    item.Id,
                    previousId,
                    StringComparison.OrdinalIgnoreCase));

        _heroIndex = previousIndex >= 0 ? previousIndex : 0;
        RebuildHeroIndicators();
        ApplyHero(_heroCandidates[_heroIndex]);

        _heroTimer.Stop();
        _heroPreloadTask = PreloadHeroCandidatesAsync(preloadGeneration);
    }

    private async Task PreloadHeroCandidatesAsync(int generation)
    {
        try
        {
            var urls = _heroCandidates
                .Select(item => _client.BuildBackdropUrl(item, 1800))
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            // Prioritize the artwork the user will see first. Remaining Hero
            // images are warmed only after the first one has reached cache.
            if (urls.Length > 0)
                await ImageCacheService.Shared.PreloadAsync(urls.Take(1));

            if (generation != _heroPreloadGeneration)
                return;

            if (urls.Length > 1)
                await ImageCacheService.Shared.PreloadAsync(urls.Skip(1));

            if (generation != _heroPreloadGeneration)
                return;

            _heroImagesReady = true;

            StartupDiagnostics.Write(
                $"HomeView hero preload complete: generation={generation}, images={urls.Length}");

            if (IsLoaded &&
                _initialVisualReadyRaised &&
                _heroCandidates.Count > 1)
            {
                _heroTimer.Start();
            }
        }
        catch (Exception ex)
        {
            PlaybackLog.Error("WinUIHeroPreload", ex);
        }
    }

    private void ApplyHero(EmbyItem? item)
    {
        _heroItem = item;
        HeroPlayButton.IsEnabled = _heroItem is not null;
        HeroFavoriteButton.IsEnabled = _heroItem is not null;

        if (_heroItem is null)
        {
            HeroTitleBlock.Text = "媒体库已连接";
            HeroMetaBlock.Text = "";
            HeroOverviewBlock.Text = "从下方浏览你的媒体库。";
            HeroCommunityRatingBadge.Visibility = Visibility.Collapsed;
            HeroCriticRatingBadge.Visibility = Visibility.Collapsed;
            HeroOfficialRatingBadge.Visibility = Visibility.Collapsed;
            HeroFavoriteButton.Content = "♡  收藏";
            HeroImage.SourceUrl = "";
            HeroImageAlt.SourceUrl = "";
            _heroVisualInitialized = false;
            _currentHeroBackdropUrl = "";
            UpdateHeroIndicators(animate: false);
            return;
        }

        HeroTitleBlock.Text = _heroItem.Name;
        HeroMetaBlock.Text = BuildHeroMeta(_heroItem);
        HeroFavoriteButton.Content = _heroItem.UserData?.IsFavorite == true
            ? "♥  已收藏"
            : "♡  收藏";
        HeroOverviewBlock.Text = _heroItem.Overview ?? "";

        if (_heroItem.CommunityRating is > 0)
        {
            HeroCommunityRatingBlock.Text = $"★ {_heroItem.CommunityRating:0.0}";
            HeroCommunityRatingBadge.Visibility = Visibility.Visible;
        }
        else
        {
            HeroCommunityRatingBadge.Visibility = Visibility.Collapsed;
        }

        if (_heroItem.CriticRating is > 0)
        {
            HeroCriticRatingBlock.Text = $"✓ {_heroItem.CriticRating:0}%";
            HeroCriticRatingBadge.Visibility = Visibility.Visible;
        }
        else
        {
            HeroCriticRatingBadge.Visibility = Visibility.Collapsed;
        }

        var officialRating = (_heroItem.OfficialRating ?? "").Trim();
        HeroOfficialRatingBlock.Text = officialRating;
        HeroOfficialRatingBadge.Visibility = string.IsNullOrWhiteSpace(officialRating)
            ? Visibility.Collapsed
            : Visibility.Visible;

        var backdropUrl =
            _client.BuildBackdropUrl(_heroItem, 1800);

        TransitionHeroVisual(backdropUrl);
        UpdateHeroCaptionContrast(backdropUrl);
        UpdateHeroIndicators(animate: true);
    }

    private async void UpdateHeroCaptionContrast(string backdropUrl)
    {
        if (string.IsNullOrWhiteSpace(backdropUrl))
            return;

        var generation = ++_heroCaptionContrastGeneration;

        try
        {
            var luminance =
                await ImageCacheService.Shared.GetTopRightLuminanceAsync(
                    backdropUrl);

            if (generation != _heroCaptionContrastGeneration ||
                luminance is null)
            {
                return;
            }

            // Bright artwork needs dark caption glyphs; dark artwork needs white.
            var useDarkGlyphs = luminance.Value >= 150;
            _currentHeroCaptionUseDarkGlyphs = useDarkGlyphs;
            UpdateCaptionContrastForScrollPosition();

            StartupDiagnostics.Write(
                $"Hero caption contrast: luminance={luminance.Value:0.0}; " +
                $"darkGlyphs={useDarkGlyphs}");
        }
        catch (Exception ex)
        {
            PlaybackLog.Error("WinUIHeroCaptionContrast", ex);
        }
    }

    private void TransitionHeroVisual(string backdropUrl)
    {
        if (string.IsNullOrWhiteSpace(backdropUrl))
            return;

        // While the splash is covering the app, snap to the newest Hero rather
        // than starting a cross-fade that may still be mid-animation when the
        // real window is revealed.
        if (!_initialVisualReadyRaised && _heroVisualInitialized)
        {
            _heroTransitionStoryboard?.Stop();
            _heroTransitionStoryboard = null;

            HeroImage.SourceUrl = backdropUrl;
            HeroImage.Opacity = 1;
            HeroImageAlt.SourceUrl = "";
            HeroImageAlt.Opacity = 0;

            _heroShowingPrimary = true;
            _currentHeroBackdropUrl = backdropUrl;
            return;
        }

        if (!_heroVisualInitialized)
        {
            HeroImage.SourceUrl = backdropUrl;
            HeroImage.Opacity = 1;
            HeroImageAlt.Opacity = 0;

            _heroShowingPrimary = true;
            _heroVisualInitialized = true;
            _currentHeroBackdropUrl = backdropUrl;
            return;
        }

        if (string.Equals(
                _currentHeroBackdropUrl,
                backdropUrl,
                StringComparison.Ordinal))
        {
            return;
        }

        _heroTransitionStoryboard?.Stop();

        var incomingHero = _heroShowingPrimary ? HeroImageAlt : HeroImage;
        var outgoingHero = _heroShowingPrimary ? HeroImage : HeroImageAlt;

        incomingHero.SourceUrl = backdropUrl;
        incomingHero.Opacity = 0;
        outgoingHero.Opacity = 1;

        var easing = new CubicEase
        {
            EasingMode = EasingMode.EaseInOut
        };

        var storyboard = new Storyboard();

        AddOpacityAnimation(
            storyboard,
            incomingHero,
            0,
            1,
            760,
            easing);
        AddOpacityAnimation(
            storyboard,
            outgoingHero,
            1,
            0,
            760,
            easing);

        _heroShowingPrimary = !_heroShowingPrimary;
        _currentHeroBackdropUrl = backdropUrl;
        _heroTransitionStoryboard = storyboard;
        storyboard.Completed += (_, _) =>
        {
            _heroTransitionStoryboard = null;
        };
        storyboard.Begin();
    }

    private static void AddOpacityAnimation(
        Storyboard storyboard,
        DependencyObject target,
        double from,
        double to,
        int durationMs,
        EasingFunctionBase easing)
    {
        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = easing
        };

        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, "Opacity");
        storyboard.Children.Add(animation);
    }

    private void RebuildHeroIndicators()
    {
        HeroIndicatorPanel.Children.Clear();

        for (var i = 0; i < _heroCandidates.Count; i++)
        {
            var selected = i == _heroIndex;
            HeroIndicatorPanel.Children.Add(new Border
            {
                Width = selected ? 22 : 7,
                Height = 7,
                CornerRadius = new CornerRadius(4),
                Opacity = selected ? 0.96 : 0.48,
                Background = new SolidColorBrush(
                    Windows.UI.Color.FromArgb(255, 255, 255, 255))
            });
        }
    }

    private void UpdateHeroIndicators(bool animate)
    {
        if (HeroIndicatorPanel.Children.Count != _heroCandidates.Count)
        {
            RebuildHeroIndicators();
            return;
        }

        var storyboard = animate ? new Storyboard() : null;
        var easing = new CubicEase
        {
            EasingMode = EasingMode.EaseOut
        };

        for (var i = 0; i < HeroIndicatorPanel.Children.Count; i++)
        {
            if (HeroIndicatorPanel.Children[i] is not Border dot)
                continue;

            var selected = i == _heroIndex;
            var targetWidth = selected ? 22d : 7d;
            dot.Opacity = selected ? 0.96 : 0.48;

            if (!animate)
            {
                dot.Width = targetWidth;
                continue;
            }

            var widthAnimation = new DoubleAnimation
            {
                To = targetWidth,
                Duration = TimeSpan.FromMilliseconds(220),
                EasingFunction = easing,
                EnableDependentAnimation = true
            };
            Storyboard.SetTarget(widthAnimation, dot);
            Storyboard.SetTargetProperty(widthAnimation, "Width");
            storyboard!.Children.Add(widthAnimation);
        }

        storyboard?.Begin();
    }

    private void PopulateResume(IEnumerable<EmbyItem> source)
    {
        var desired = BuildResumeItems(source)
            .Select(CreateResumeTile)
            .ToArray();

        SyncCollectionByItemId(
            _resume,
            desired,
            tile => tile.Item,
            (left, right) =>
                HomeSnapshotComparer.ItemEquals(
                    left.Item,
                    right.Item));

        ContinueSection.Visibility = _resume.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private ResumeMediaTile CreateResumeTile(EmbyItem item)
    {
        var played = Math.Clamp(
            item.UserData?.PlayedPercentage ?? 0,
            0,
            100);
        var positionTicks = Math.Max(
            0,
            item.UserData?.PlaybackPositionTicks ?? 0);
        var durationTicks = Math.Max(
            0,
            item.RunTimeTicks ?? 0);

        return new ResumeMediaTile(
            item,
            BuildResumeTitle(item),
            BuildEpisodeText(item),
            _client.BuildBackdropUrl(item, 900),
            played,
            BuildShortProgressText(
                positionTicks,
                durationTicks,
                played),
            BuildLastPlayedText(
                item.UserData?.LastPlayedDate));
    }

    private void PopulateLibraries(IEnumerable<EmbyItem> views)
    {
        var desired = views
            .Select(view => new HomeLibraryTile(
                view,
                view.Name,
                BuildLibrarySubtitle(view),
                _client.BuildBackdropUrl(view, 900)))
            .ToArray();

        SyncCollectionByItemId(
            _libraries,
            desired,
            tile => tile.Item,
            (left, right) =>
                HomeSnapshotComparer.ItemEquals(
                    left.Item,
                    right.Item));

        DispatcherQueue.TryEnqueue(
            UpdateLibraryButtons);
    }

    private static void SyncCollectionByItemId<T>(
        ObservableCollection<T> target,
        IReadOnlyList<T> desired,
        Func<T, EmbyItem> itemSelector,
        Func<T, T, bool> equivalent)
    {
        for (var targetIndex = 0;
             targetIndex < desired.Count;
             targetIndex++)
        {
            var desiredItem = desired[targetIndex];
            var desiredId = itemSelector(desiredItem).Id;

            var currentIndex = -1;
            for (var index = targetIndex;
                 index < target.Count;
                 index++)
            {
                if (string.Equals(
                        itemSelector(target[index]).Id,
                        desiredId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    currentIndex = index;
                    break;
                }
            }

            if (currentIndex < 0)
            {
                target.Insert(
                    Math.Min(targetIndex, target.Count),
                    desiredItem);
                continue;
            }

            if (currentIndex != targetIndex)
            {
                target.Move(
                    currentIndex,
                    targetIndex);
            }

            if (!equivalent(
                    target[targetIndex],
                    desiredItem))
            {
                target[targetIndex] = desiredItem;
            }
        }

        while (target.Count > desired.Count)
        {
            target.RemoveAt(
                target.Count - 1);
        }
    }

    private async Task<HomeLibrarySection?> LoadLibrarySectionAsync(EmbyItem library)
    {
        try
        {
            var isSeriesLibrary = IsSeriesLibrary(library);
            var includeItemTypes = isSeriesLibrary
                ? "Series"
                : string.Equals(library.CollectionType, "movies", StringComparison.OrdinalIgnoreCase)
                    ? "Movie"
                    : "Movie,Series";

            var result = await _client.GetLibraryItemsAsync(
                parentId: library.Id,
                searchTerm: null,
                includeItemTypes: includeItemTypes,
                year: null,
                sortBy: "DateCreated",
                sortOrder: "Descending",
                favoriteOnly: false,
                startIndex: 0,
                limit: SectionItemLimit);

            var items = result.Items
                .Where(item => !string.IsNullOrWhiteSpace(item.Id))
                .Take(SectionItemLimit)
                .Select(item => new SectionMediaTile(
                    item,
                    item.Name,
                    BuildSectionMeta(item),
                    _client.BuildPrimaryUrl(item, 420),
                    _client.BuildBackdropUrl(item, 900)))
                .ToArray();

            if (items.Length == 0)
                return null;

            return new HomeLibrarySection(
                library,
                library.Name,
                result.TotalRecordCount,
                result.TotalRecordCount > 0 ? $"{result.TotalRecordCount} 项" : "",
                isSeriesLibrary ? Visibility.Collapsed : Visibility.Visible,
                isSeriesLibrary ? Visibility.Visible : Visibility.Collapsed,
                items);
        }
        catch (Exception ex)
        {
            PlaybackLog.Error("WinUIHomeSection", ex);
            return null;
        }
    }

    private void HeroPlay_Click(object sender, RoutedEventArgs e)
    {
        if (_heroItem is not null)
            PlayRequested?.Invoke(this, _heroItem);
    }

    private void HeroDetails_Click(object sender, RoutedEventArgs e)
    {
        if (_heroItem is not null)
            MediaRequested?.Invoke(this, _heroItem);
    }

    private async void HeroFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (_heroItem is null)
            return;

        HeroFavoriteButton.IsEnabled = false;

        try
        {
            var target = !(_heroItem.UserData?.IsFavorite == true);
            await _client.SetFavoriteAsync(_heroItem.Id, target);
            _heroItem = await _client.GetItemAsync(_heroItem.Id);

            var candidateIndex = _heroCandidates.FindIndex(item =>
                string.Equals(
                    item.Id,
                    _heroItem.Id,
                    StringComparison.OrdinalIgnoreCase));
            if (candidateIndex >= 0)
                _heroCandidates[candidateIndex] = _heroItem;

            HeroFavoriteButton.Content = _heroItem.UserData?.IsFavorite == true
                ? "♥  已收藏"
                : "♡  收藏";
        }
        catch (Exception ex)
        {
            PlaybackLog.Error("WinUIHomeFavorite", ex);
        }
        finally
        {
            HeroFavoriteButton.IsEnabled = true;
        }
    }

    private void OpenLibrary_Click(object sender, RoutedEventArgs e) =>
        LibraryRequested?.Invoke(this, EventArgs.Empty);

    private void LibraryTile_Click(object sender, RoutedEventArgs e) =>
        LibraryRequested?.Invoke(this, EventArgs.Empty);

    private void LibrarySectionMore_Click(object sender, RoutedEventArgs e) =>
        LibraryRequested?.Invoke(this, EventArgs.Empty);

    private void ResumeTile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ResumeMediaTile tile })
            PlayRequested?.Invoke(this, tile.Item);
    }

    private void SectionMedia_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SectionMediaTile tile })
            MediaRequested?.Invoke(this, tile.Item);
    }

    private void ResumeTile_RightTapped(
        object sender,
        Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
    {
        if (sender is not Button { Tag: ResumeMediaTile tile } button)
            return;

        e.Handled = true;

        var flyout = new MenuFlyout();

        var details = new MenuFlyoutItem
        {
            Text = "详情"
        };
        details.Click += (_, _) =>
            MediaRequested?.Invoke(this, tile.Item);

        var restart = new MenuFlyoutItem
        {
            Text = "从头播放"
        };
        restart.Click += (_, _) =>
            RestartRequested?.Invoke(this, tile.Item);

        flyout.Items.Add(details);
        flyout.Items.Add(restart);
        flyout.ShowAt(button);
    }

    private void ResumeArrow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } ||
            !int.TryParse(tag, out var direction) ||
            ResumeScroller.ScrollableWidth <= 0)
        {
            return;
        }

        var distance = Math.Max(300, ResumeScroller.ViewportWidth * 0.82);
        var target = Math.Clamp(
            ResumeScroller.HorizontalOffset + Math.Sign(direction) * distance,
            0,
            ResumeScroller.ScrollableWidth);

        ResumeScroller.ChangeView(
            horizontalOffset: target,
            verticalOffset: null,
            zoomFactor: null,
            disableAnimation: false);
    }

    private void ResumeScroller_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e) =>
        UpdateResumeButtons();

    private void ResumeScroller_SizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateResumeButtons();

    private void UpdateResumeButtons()
    {
        const double epsilon = 1.0;
        var canGoBack = ResumeScroller.HorizontalOffset > epsilon;
        var canGoForward =
            ResumeScroller.HorizontalOffset <
            ResumeScroller.ScrollableWidth - epsilon;

        var backVisibility =
            canGoBack ? Visibility.Visible : Visibility.Collapsed;
        var forwardVisibility =
            canGoForward ? Visibility.Visible : Visibility.Collapsed;

        ResumePreviousButton.Visibility = backVisibility;
        ResumeNextButton.Visibility = forwardVisibility;
    }

    private void LibrariesArrow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } ||
            !int.TryParse(tag, out var direction) ||
            LibrariesScroller.ScrollableWidth <= 0)
        {
            return;
        }

        var distance = Math.Max(302, LibrariesScroller.ViewportWidth * 0.82);
        var target = Math.Clamp(
            LibrariesScroller.HorizontalOffset + Math.Sign(direction) * distance,
            0,
            LibrariesScroller.ScrollableWidth);

        LibrariesScroller.ChangeView(
            horizontalOffset: target,
            verticalOffset: null,
            zoomFactor: null,
            disableAnimation: false);
    }

    private void LibrariesScroller_ViewChanged(
        object sender,
        ScrollViewerViewChangedEventArgs e) =>
        UpdateLibraryButtons();

    private void LibrariesScroller_SizeChanged(
        object sender,
        SizeChangedEventArgs e) =>
        UpdateLibraryButtons();

    private void UpdateLibraryButtons()
    {
        const double epsilon = 1.0;
        var canGoBack =
            LibrariesScroller.HorizontalOffset > epsilon;
        var canGoForward =
            LibrariesScroller.HorizontalOffset <
            LibrariesScroller.ScrollableWidth - epsilon;

        var backVisibility =
            canGoBack ? Visibility.Visible : Visibility.Collapsed;
        var forwardVisibility =
            canGoForward ? Visibility.Visible : Visibility.Collapsed;

        LibrariesPreviousButton.Visibility = backVisibility;
        LibrariesNextButton.Visibility = forwardVisibility;
    }

    private static IReadOnlyList<EmbyItem> BuildResumeItems(IEnumerable<EmbyItem> source)
    {
        static string GroupKey(EmbyItem item) =>
            string.Equals(item.Type, "Episode", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(item.SeriesId)
                ? "series:" + item.SeriesId
                : "item:" + item.Id;

        return source
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .GroupBy(GroupKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(item => item.UserData?.LastPlayedDate ?? DateTimeOffset.MinValue)
                .ThenByDescending(item => item.UserData?.PlaybackPositionTicks ?? 0)
                .First())
            .OrderByDescending(item => item.UserData?.LastPlayedDate ?? DateTimeOffset.MinValue)
            .Take(12)
            .ToArray();
    }

    private static string BuildResumeTitle(EmbyItem item)
    {
        if (string.Equals(item.Type, "Episode", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(item.SeriesName))
        {
            return item.SeriesName;
        }

        return item.Name;
    }

    private static string BuildEpisodeText(EmbyItem item)
    {
        if (!string.Equals(item.Type, "Episode", StringComparison.OrdinalIgnoreCase))
            return BuildSectionMeta(item);

        var code = item.ParentIndexNumber is > 0 && item.IndexNumber is > 0
            ? $"S{item.ParentIndexNumber:00}E{item.IndexNumber:00}"
            : item.IndexNumber is > 0
                ? $"E{item.IndexNumber:00}"
                : "";

        if (string.IsNullOrWhiteSpace(code))
            return item.Name;

        return string.IsNullOrWhiteSpace(item.Name)
            ? code
            : $"{code} · {item.Name}";
    }

    private static string BuildShortProgressText(
        long positionTicks,
        long durationTicks,
        double percentage)
    {
        if (positionTicks > 0)
        {
            var position = TimeSpan.FromTicks(positionTicks);
            return position.TotalHours >= 1
                ? $"{(int)position.TotalHours}:{position.Minutes:00}:{position.Seconds:00}"
                : $"{position.Minutes:00}:{position.Seconds:00}";
        }

        return percentage > 0 ? $"{percentage:0}%" : "";
    }

    private static string BuildLastPlayedText(DateTimeOffset? lastPlayed)
    {
        if (lastPlayed is null)
            return "";

        var elapsed = DateTimeOffset.Now - lastPlayed.Value.ToLocalTime();
        if (elapsed < TimeSpan.Zero)
            elapsed = TimeSpan.Zero;

        if (elapsed.TotalMinutes < 1)
            return "刚刚";

        if (elapsed.TotalHours < 1)
            return $"{Math.Max(1, (int)elapsed.TotalMinutes)}分钟前";

        if (elapsed.TotalHours < 24)
            return $"{Math.Max(1, (int)elapsed.TotalHours)}小时前";

        if (elapsed.TotalDays < 2)
            return "昨天";

        if (elapsed.TotalDays < 7)
            return $"{Math.Max(2, (int)elapsed.TotalDays)}天前";

        return lastPlayed.Value.ToLocalTime().ToString("M月d日");
    }

    private static string BuildLibrarySubtitle(EmbyItem view)
    {
        var type = (view.CollectionType ?? "").ToLowerInvariant();
        return type switch
        {
            "movies" => "电影",
            "tvshows" => "电视剧",
            "music" => "音乐",
            "books" => "图书",
            _ => string.IsNullOrWhiteSpace(view.CollectionType)
                ? "媒体库"
                : view.CollectionType
        };
    }

    private static string BuildHeroMeta(EmbyItem item)
    {
        var values = new List<string>();

        if (item.ProductionYear is > 0)
            values.Add(item.ProductionYear.Value.ToString());

        if (!string.IsNullOrWhiteSpace(item.Type))
        {
            values.Add(string.Equals(item.Type, "Series", StringComparison.OrdinalIgnoreCase)
                ? "剧集"
                : string.Equals(item.Type, "Movie", StringComparison.OrdinalIgnoreCase)
                    ? "电影"
                    : item.Type);
        }

        foreach (var genre in item.Genres
                     .Where(genre => !string.IsNullOrWhiteSpace(genre))
                     .Take(2))
        {
            values.Add(genre);
        }

        return string.Join("  ·  ", values);
    }

    private static string BuildSectionMeta(EmbyItem item)
    {
        var values = new List<string>();

        if (item.ProductionYear is > 0)
            values.Add(item.ProductionYear.Value.ToString());

        if (string.Equals(item.Type, "Series", StringComparison.OrdinalIgnoreCase))
        {
            if (item.SeasonCount is > 0)
                values.Add($"{item.SeasonCount} 季");
            else if (item.ChildCount is > 0)
                values.Add($"{item.ChildCount} 季");

            var genre = item.Genres
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            if (!string.IsNullOrWhiteSpace(genre))
                values.Add(genre);
        }

        if (item.CommunityRating is > 0)
            values.Add($"★ {item.CommunityRating:0.0}");

        return string.Join(" · ", values);
    }

    private static bool IsSeriesLibrary(EmbyItem library) =>
        string.Equals(
            library.CollectionType,
            "tvshows",
            StringComparison.OrdinalIgnoreCase);

    private static bool IsVisibleLibrary(EmbyItem view) =>
        !new[] { "boxsets", "playlists", "folders", "livetv", "homevideos" }
            .Contains((view.CollectionType ?? "").ToLowerInvariant());

    private sealed record ServerChooserItem(
        ServerProfile Profile,
        string DisplayName,
        string CurrentMark);

    private sealed record HomeLibraryTile(
        EmbyItem Item,
        string Name,
        string Subtitle,
        string BackdropUrl);

    private sealed record ResumeMediaTile(
        EmbyItem Item,
        string ResumeTitle,
        string EpisodeText,
        string BackdropUrl,
        double PlayedPercentage,
        string ShortProgressText,
        string LastPlayedText);

    private sealed record SectionMediaTile(
        EmbyItem Item,
        string Title,
        string Meta,
        string PosterUrl,
        string BackdropUrl);

    private sealed record HomeLibrarySection(
        EmbyItem Library,
        string Name,
        int TotalCount,
        string CountLabel,
        Visibility PosterVisibility,
        Visibility SeriesVisibility,
        IReadOnlyList<SectionMediaTile> Items);
}
