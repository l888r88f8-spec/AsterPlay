using System.Collections.ObjectModel;
using AsterPlay.Models;
using AsterPlay.Services;
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
    private bool _heroImagesReady;
    private bool _heroShowingPrimary = true;
    private bool _heroVisualInitialized;
    private string _currentHeroBackdropUrl = "";
    private Storyboard? _heroTransitionStoryboard;
    private readonly bool _noServerMode;
    private bool _hasCachedSnapshot;
    private DateTimeOffset _lastWheelDiagnosticAt = DateTimeOffset.MinValue;
    private DateTimeOffset _lastViewChangedDiagnosticAt = DateTimeOffset.MinValue;

    public event EventHandler? LibraryRequested;
    public event EventHandler<EmbyItem>? MediaRequested;
    public event EventHandler<EmbyItem>? PlayRequested;
    public event EventHandler<EmbyItem>? RestartRequested;
    public event EventHandler? AuthenticationFailed;
    public event EventHandler? ServerRequested;
    public event EventHandler<ServerProfile>? ServerSwitchRequested;
    public event EventHandler? SearchRequested;
    public event EventHandler? InitialVisualReady;

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

        AddHandler(
            UIElement.PointerWheelChangedEvent,
            new PointerEventHandler(HomeView_PointerWheelChangedDiagnostic),
            handledEventsToo: true);
        HomeScrollViewer.ViewChanged += HomeScrollViewer_ViewChangedDiagnostic;

        LibrariesGrid.ItemsSource = _libraries;
        ResumeGrid.ItemsSource = _resume;
        LibrarySectionsList.ItemsSource = _sections;

        WelcomeBlock.Text = string.IsNullOrWhiteSpace(_client.UserName)
            ? "欢迎回来"
            : $"欢迎回来，{_client.UserName}";

        ServerNameBlock.Text = ResolveServerDisplayName();

        if (_noServerMode)
        {
            ShowNoServerState();
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

        LogHomeScrollState("activated");
        DispatcherQueue.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () => LogHomeScrollState("activated-deferred"));
    }

    private void HomeView_Unloaded(object sender, RoutedEventArgs e)
    {
        _heroTimer.Stop();
        _heroTransitionStoryboard?.Stop();
        _heroTransitionStoryboard = null;
    }

    private void HeroTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        if (_heroCandidates.Count <= 1)
            return;

        _heroIndex = (_heroIndex + 1) % _heroCandidates.Count;
        ApplyHero(_heroCandidates[_heroIndex]);
        StartupDiagnostics.Write(
            $"HomeView hero advanced: index={_heroIndex}, item={_heroItem?.Id}");
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

        PlaybackLog.Write(
            "WinUINativeWheel",
            $"delta={delta}, target={target:0.0}, {BuildHomeScrollState()}");

        return true;
    }

    private void HomeView_PointerWheelChangedDiagnostic(
        object sender,
        PointerRoutedEventArgs e)
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastWheelDiagnosticAt < TimeSpan.FromMilliseconds(180))
            return;

        _lastWheelDiagnosticAt = now;

        var point = e.GetCurrentPoint(this);
        PlaybackLog.Write(
            "WinUIHomeScroll",
            $"wheel: delta={point.Properties.MouseWheelDelta}, handled={e.Handled}, " +
            $"source={e.OriginalSource?.GetType().Name ?? "-"}, " +
            $"position={point.Position.X:0},{point.Position.Y:0}, " +
            BuildHomeScrollState());

        DispatcherQueue.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () => LogHomeScrollState("wheel-after"));
    }

    private void HomeScrollViewer_ViewChangedDiagnostic(
        object? sender,
        ScrollViewerViewChangedEventArgs e)
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastViewChangedDiagnosticAt < TimeSpan.FromMilliseconds(220))
            return;

        _lastViewChangedDiagnosticAt = now;
        LogHomeScrollState($"view-changed intermediate={e.IsIntermediate}");
    }

    private void LogHomeScrollState(string reason) =>
        PlaybackLog.Write(
            "WinUIHomeScroll",
            $"{reason}: {BuildHomeScrollState()}");

    private string BuildHomeScrollState() =>
        $"visible={HomeScrollViewer.Visibility}, " +
        $"offset={HomeScrollViewer.VerticalOffset:0.0}, " +
        $"scrollable={HomeScrollViewer.ScrollableHeight:0.0}, " +
        $"extent={HomeScrollViewer.ExtentHeight:0.0}, " +
        $"viewport={HomeScrollViewer.ViewportHeight:0.0}, " +
        $"viewerActual={HomeScrollViewer.ActualWidth:0.0}x{HomeScrollViewer.ActualHeight:0.0}, " +
        $"contentActual={HomeContentStack.ActualWidth:0.0}x{HomeContentStack.ActualHeight:0.0}, " +
        $"contentDesired={HomeContentStack.DesiredSize.Width:0.0}x{HomeContentStack.DesiredSize.Height:0.0}";

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
            new Thickness(horizontalPadding, 0, horizontalPadding, 122);
        HeroContainer.Margin =
            new Thickness(-horizontalPadding, 0, -horizontalPadding, -172);

        LogHomeScrollState(
            $"size-changed {e.NewSize.Width:0.0}x{e.NewSize.Height:0.0}");
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
                await RefreshServerDisplayNameAsync();
                await LoadCachedSnapshotAsync();
                InitialVisualReady?.Invoke(this, EventArgs.Empty);
                StartupDiagnostics.Write(
                    $"HomeView: initial visual ready; cachedSnapshot={_hasCachedSnapshot}");
                await LoadAsync();
            });
    }

    private Task LoadCachedSnapshotAsync()
    {
        StartupDiagnostics.Write("HomeView: deferred snapshot load begin");

        var snapshot = HomeSnapshotStore.Load(
            _client.ServerUrl,
            _client.UserId);

        StartupDiagnostics.Write(
            $"HomeView: deferred snapshot={(snapshot is null ? "miss" : "hit")}");

        if (snapshot is not null)
        {
            ApplySnapshot(snapshot);
            _hasCachedSnapshot = true;
            ShowContentState();

            PlaybackLog.Write(
                "WinUIHomeSnapshot",
                $"Loaded cached home snapshot; age={(DateTimeOffset.UtcNow - snapshot.SavedAtUtc).TotalMinutes:0.0} min, " +
                $"libraries={snapshot.Views.Count}, resume={snapshot.Resume.Count}, sections={snapshot.Sections.Count}");
        }

        return Task.CompletedTask;
    }

    private async Task LoadAsync()
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

            PopulateHero(latestTask.Result);
            PopulateResume(resumeTask.Result);
            PopulateLibraries(views);

            // Load each real Emby library section concurrently. Only six items
            // are requested for the home page, while TotalRecordCount is kept
            // for the section header count.
            var sectionTasks = views.Select(LoadLibrarySectionAsync).ToArray();
            var sectionResults = await Task.WhenAll(sectionTasks);

            _sections.Clear();
            foreach (var section in sectionResults.Where(section => section is not null))
                _sections.Add(section!);

            var snapshot = new HomeSnapshot
            {
                Latest = latestTask.Result.ToList(),
                Resume = resumeTask.Result.ToList(),
                Views = views.ToList(),
                Sections = sectionResults
                    .Where(section => section is not null)
                    .Select(section => new HomeSectionSnapshot
                    {
                        Library = section!.Library,
                        TotalCount = section.TotalCount,
                        Items = section.Items
                            .Select(item => item.Item)
                            .ToList()
                    })
                    .ToList()
            };

            HomeSnapshotStore.Save(
                _client.ServerUrl,
                _client.UserId,
                snapshot);

            _hasCachedSnapshot = true;
            ShowContentState();
            StartupDiagnostics.Write("HomeView.LoadAsync: refreshed snapshot saved");
        }
        catch (Exception ex)
        {
            PlaybackLog.Error("WinUIHome", ex);

            if (UserError.IsAuthenticationFailure(ex))
            {
                AuthenticationFailed?.Invoke(this, EventArgs.Empty);
                return;
            }

            if (!_hasCachedSnapshot)
            {
                ShowLoadingError(UserError.GetMessage(ex, "加载首页"));
            }
        }
        finally
        {
            DispatcherQueue.TryEnqueue(UpdateResumeButtons);

            StartupDiagnostics.Write("HomeView.LoadAsync: refresh completed");
            loadTimer.Stop();
            PlaybackLog.Write(
                "Performance",
                $"WinUI home load: {loadTimer.Elapsed.TotalMilliseconds:0} ms, " +
                $"libraries={_libraries.Count}, resume={_resume.Count}, sections={_sections.Count}, " +
                $"workingSet={Environment.WorkingSet / 1024d / 1024d:0.0} MB, " +
                $"managed={GC.GetTotalMemory(false) / 1024d / 1024d:0.0} MB");
        }
    }

    private void ShowNoServerState()
    {
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

        LogHomeScrollState("content-visible");
        DispatcherQueue.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () => LogHomeScrollState("content-visible-deferred"));
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

        var currentUrl = NormalizeServerUrl(_client.ServerUrl);
        var flyout = new MenuFlyout();

        foreach (var profile in servers)
        {
            var isCurrent = string.Equals(
                NormalizeServerUrl(profile.Url),
                currentUrl,
                StringComparison.OrdinalIgnoreCase);

            var item = new MenuFlyoutItem
            {
                Text = isCurrent
                    ? $"●  {profile.DisplayName}"
                    : $"    {profile.DisplayName}",
                Tag = profile,
                IsEnabled = !isCurrent
            };
            item.Click += ServerChoice_Click;
            flyout.Items.Add(item);
        }

        flyout.ShowAt(ServerPillButton);
    }

    private void ServerChoice_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: ServerProfile profile })
            ServerSwitchRequested?.Invoke(this, profile);
    }

    private static string NormalizeServerUrl(string? value) =>
        (value ?? "").Trim().TrimEnd('/');

    private void Search_Click(object sender, RoutedEventArgs e) =>
        SearchRequested?.Invoke(this, EventArgs.Empty);

    private async Task RefreshServerDisplayNameAsync()
    {
        if (_noServerMode || !_client.IsAuthenticated)
            return;

        try
        {
            var serverName = await _client.GetServerNameAsync();
            if (string.IsNullOrWhiteSpace(serverName))
                return;

            ServerProfileStore.AddOrUpdate(_client.ServerUrl, serverName);
            ServerNameBlock.Text = serverName;
            StartupDiagnostics.Write($"HomeView: server name refreshed to '{serverName}'");
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
            var items = section.Items
                .Where(item => !string.IsNullOrWhiteSpace(item.Id))
                .Take(SectionItemLimit)
                .Select(item => new SectionMediaTile(
                    item,
                    item.Name,
                    BuildSectionMeta(item),
                    _client.BuildPrimaryUrl(item, 420)))
                .ToArray();

            if (items.Length == 0)
                continue;

            _sections.Add(new HomeLibrarySection(
                section.Library,
                section.Library.Name,
                section.TotalCount,
                section.TotalCount > 0 ? $"{section.TotalCount} 项" : "",
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
        _ = PreloadHeroCandidatesAsync(preloadGeneration);
    }

    private async Task PreloadHeroCandidatesAsync(int generation)
    {
        var urls = _heroCandidates
            .Select(item => _client.BuildBackdropUrl(item, 1800))
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        await ImageCacheService.Shared.PreloadAsync(urls);

        if (generation != _heroPreloadGeneration)
            return;

        _heroImagesReady = true;

        StartupDiagnostics.Write(
            $"HomeView hero preload complete: generation={generation}, images={urls.Length}");

        if (IsLoaded && _heroCandidates.Count > 1)
            _heroTimer.Start();
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
            HeroFavoriteButton.Content = "♡  收藏";
            HeroImage.SourceUrl = "";
            HeroImageAlt.SourceUrl = "";
            PageBackdropImage.SourceUrl = "";
            PageBackdropImageAlt.SourceUrl = "";
            PageBackdropLayer.Visibility = Visibility.Collapsed;
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

        var backdropUrl =
            _client.BuildBackdropUrl(_heroItem, 1800);

        TransitionHeroVisual(backdropUrl);
        UpdateHeroIndicators(animate: true);
    }

    private void TransitionHeroVisual(string backdropUrl)
    {
        PageBackdropLayer.Visibility = string.IsNullOrWhiteSpace(backdropUrl)
            ? Visibility.Collapsed
            : Visibility.Visible;

        if (string.IsNullOrWhiteSpace(backdropUrl))
            return;

        if (!_heroVisualInitialized)
        {
            HeroImage.SourceUrl = backdropUrl;
            HeroImage.Opacity = 1;
            HeroImageAlt.Opacity = 0;
            PageBackdropImage.SourceUrl = backdropUrl;
            PageBackdropImage.Opacity = 0.84;
            PageBackdropImageAlt.Opacity = 0;
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
        var incomingBackdrop = _heroShowingPrimary ? PageBackdropImageAlt : PageBackdropImage;
        var outgoingBackdrop = _heroShowingPrimary ? PageBackdropImage : PageBackdropImageAlt;

        incomingHero.SourceUrl = backdropUrl;
        incomingBackdrop.SourceUrl = backdropUrl;
        incomingHero.Opacity = 0;
        incomingBackdrop.Opacity = 0;
        outgoingHero.Opacity = 1;
        outgoingBackdrop.Opacity = 0.84;

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
        AddOpacityAnimation(
            storyboard,
            incomingBackdrop,
            0,
            0.84,
            920,
            easing);
        AddOpacityAnimation(
            storyboard,
            outgoingBackdrop,
            0.84,
            0,
            920,
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
        _resume.Clear();

        foreach (var item in BuildResumeItems(source))
        {
            var played = Math.Clamp(item.UserData?.PlayedPercentage ?? 0, 0, 100);
            var positionTicks = Math.Max(0, item.UserData?.PlaybackPositionTicks ?? 0);
            var durationTicks = Math.Max(0, item.RunTimeTicks ?? 0);

            _resume.Add(new ResumeMediaTile(
                item,
                BuildResumeTitle(item),
                BuildEpisodeText(item),
                _client.BuildBackdropUrl(item, 900),
                played,
                BuildShortProgressText(positionTicks, durationTicks, played),
                BuildLastPlayedText(item.UserData?.LastPlayedDate)));
        }

        ContinueSection.Visibility = _resume.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void PopulateLibraries(IEnumerable<EmbyItem> views)
    {
        _libraries.Clear();

        foreach (var view in views)
        {
            _libraries.Add(new HomeLibraryTile(
                view,
                view.Name,
                BuildLibrarySubtitle(view),
                _client.BuildBackdropUrl(view, 900)));
        }
    }

    private async Task<HomeLibrarySection?> LoadLibrarySectionAsync(EmbyItem library)
    {
        try
        {
            var result = await _client.GetLibraryItemsAsync(
                parentId: library.Id,
                searchTerm: null,
                includeItemTypes: "Movie,Series",
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
                    _client.BuildPrimaryUrl(item, 420)))
                .ToArray();

            if (items.Length == 0)
                return null;

            return new HomeLibrarySection(
                library,
                library.Name,
                result.TotalRecordCount,
                result.TotalRecordCount > 0 ? $"{result.TotalRecordCount} 项" : "",
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

    private void MediaCard_PointerEntered(
        object sender,
        Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is Button button)
            AnimateCardScale(button, 1.018, 120);
    }

    private void MediaCard_PointerExited(
        object sender,
        Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is Button button)
            AnimateCardScale(button, 1.0, 150);
    }

    private static void AnimateCardScale(
        Button button,
        double targetScale,
        int durationMs)
    {
        if (button.RenderTransform is not ScaleTransform scale)
            return;

        var easing = new CubicEase
        {
            EasingMode = EasingMode.EaseOut
        };

        var storyboard = new Storyboard();

        var x = new DoubleAnimation
        {
            To = targetScale,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = easing,
            EnableDependentAnimation = true
        };
        Storyboard.SetTarget(x, scale);
        Storyboard.SetTargetProperty(x, "ScaleX");
        storyboard.Children.Add(x);

        var y = new DoubleAnimation
        {
            To = targetScale,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = easing,
            EnableDependentAnimation = true
        };
        Storyboard.SetTarget(y, scale);
        Storyboard.SetTargetProperty(y, "ScaleY");
        storyboard.Children.Add(y);

        storyboard.Begin();
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
        ResumePreviousButton.IsEnabled = ResumeScroller.HorizontalOffset > epsilon;
        ResumeNextButton.IsEnabled =
            ResumeScroller.HorizontalOffset < ResumeScroller.ScrollableWidth - epsilon;
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

        if (item.CommunityRating is > 0)
            values.Add($"★ {item.CommunityRating:0.0}");

        if (item.RunTimeTicks is > 0)
        {
            var minutes = item.RunTimeTicks.Value / 600_000_000L;
            values.Add(minutes >= 60
                ? $"{minutes / 60}小时 {minutes % 60}分"
                : $"{minutes}分");
        }

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

        if (item.CommunityRating is > 0)
            values.Add($"★ {item.CommunityRating:0.0}");

        return string.Join(" · ", values);
    }

    private static bool IsVisibleLibrary(EmbyItem view) =>
        !new[] { "boxsets", "playlists", "folders", "livetv", "homevideos" }
            .Contains((view.CollectionType ?? "").ToLowerInvariant());

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
        string PosterUrl);

    private sealed record HomeLibrarySection(
        EmbyItem Library,
        string Name,
        int TotalCount,
        string CountLabel,
        IReadOnlyList<SectionMediaTile> Items);
}
