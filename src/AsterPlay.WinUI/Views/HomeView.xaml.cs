using System.Collections.ObjectModel;
using AsterPlay.Models;
using AsterPlay.Services;
using AsterPlay.WinUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AsterPlay.WinUI.Views;

public sealed partial class HomeView : UserControl
{
    private const int MaxLibrarySections = 6;
    private const int SectionItemLimit = 6;

    private readonly EmbyClient _client;
    private readonly ObservableCollection<HomeLibraryTile> _libraries = [];
    private readonly ObservableCollection<ResumeMediaTile> _resume = [];
    private readonly ObservableCollection<HomeLibrarySection> _sections = [];

    private EmbyItem? _heroItem;
    private readonly bool _noServerMode;
    private bool _hasCachedSnapshot;

    public event EventHandler? LibraryRequested;
    public event EventHandler<EmbyItem>? MediaRequested;
    public event EventHandler<EmbyItem>? PlayRequested;
    public event EventHandler<EmbyItem>? RestartRequested;
    public event EventHandler? AuthenticationFailed;
    public event EventHandler? ServerRequested;
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
        HomeScrollViewer.Visibility = Visibility.Collapsed;
        LoadingState.Visibility = Visibility.Collapsed;
        LoadingRing.IsActive = false;
        NoServerState.Visibility = Visibility.Visible;
        StartupDiagnostics.Write("HomeView state: NoServer");
    }

    private void ShowLoadingState()
    {
        HomeScrollViewer.Visibility = Visibility.Collapsed;
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
        HomeScrollViewer.Visibility = Visibility.Visible;

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
    }

    private void ShowLoadingError(string message)
    {
        HomeScrollViewer.Visibility = Visibility.Collapsed;
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

    private void ServerPill_Click(object sender, RoutedEventArgs e) =>
        ServerRequested?.Invoke(this, EventArgs.Empty);

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
                section.TotalCount > 0 ? section.TotalCount.ToString() : "",
                items));
        }

        DispatcherQueue.TryEnqueue(UpdateResumeButtons);
    }

    private void PopulateHero(IReadOnlyList<EmbyItem> latest)
    {
        _heroItem = latest.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.Id));
        HeroPlayButton.IsEnabled = _heroItem is not null;
        HeroFavoriteButton.IsEnabled = _heroItem is not null;

        if (_heroItem is null)
        {
            HeroTitleBlock.Text = "媒体库已连接";
            HeroMetaBlock.Text = "";
            HeroOverviewBlock.Text = "从下方浏览你的媒体库。";
            HeroImage.SourceUrl = "";
            PageBackdropImage.SourceUrl = "";
            PageBackdropLayer.Visibility = Visibility.Collapsed;
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

        HeroImage.SourceUrl = backdropUrl;
        PageBackdropImage.SourceUrl = backdropUrl;
        PageBackdropLayer.Visibility = Visibility.Visible;
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
                result.TotalRecordCount > 0 ? result.TotalRecordCount.ToString() : "",
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

    private void ResumeDetails_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: ResumeMediaTile tile })
            MediaRequested?.Invoke(this, tile.Item);
    }

    private void ResumeRestart_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: ResumeMediaTile tile })
            RestartRequested?.Invoke(this, tile.Item);
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
