using System.Collections.ObjectModel;
using AsterPlay.Models;
using AsterPlay.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

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

    public event EventHandler? LibraryRequested;
    public event EventHandler<EmbyItem>? MediaRequested;
    public event EventHandler<EmbyItem>? PlayRequested;

    public HomeView(EmbyClient client)
    {
        _client = client;
        InitializeComponent();

        LibrariesGrid.ItemsSource = _libraries;
        ResumeGrid.ItemsSource = _resume;
        LibrarySectionsList.ItemsSource = _sections;

        Loaded += HomeView_Loaded;
    }

    private async void HomeView_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= HomeView_Loaded;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        LoadingRing.IsActive = true;
        LoadingRing.Visibility = Visibility.Visible;

        try
        {
            WelcomeBlock.Text = string.IsNullOrWhiteSpace(_client.UserName)
                ? "欢迎回来"
                : $"欢迎回来，{_client.UserName}";

            var viewsTask = _client.GetViewsAsync();
            var resumeTask = _client.GetResumeAsync(24);
            var latestTask = _client.GetLatestAsync(12);

            await Task.WhenAll(viewsTask, resumeTask, latestTask);

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
        }
        catch (Exception ex)
        {
            HeroTitleBlock.Text = "首页加载失败";
            HeroMetaBlock.Text = UserError.GetMessage(ex, "加载首页");
            HeroOverviewBlock.Text = "";
            HeroPlayButton.IsEnabled = false;
            PlaybackLog.Error("WinUIHome", ex);
        }
        finally
        {
            LoadingRing.IsActive = false;
            LoadingRing.Visibility = Visibility.Collapsed;
            DispatcherQueue.TryEnqueue(UpdateResumeButtons);
        }
    }

    private void PopulateHero(IReadOnlyList<EmbyItem> latest)
    {
        _heroItem = latest.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.Id));
        HeroPlayButton.IsEnabled = _heroItem is not null;

        if (_heroItem is null)
        {
            HeroTitleBlock.Text = "媒体库已连接";
            HeroMetaBlock.Text = "";
            HeroOverviewBlock.Text = "从下方浏览你的媒体库。";
            HeroImage.Source = null;
            return;
        }

        HeroTitleBlock.Text = _heroItem.Name;
        HeroMetaBlock.Text = BuildHeroMeta(_heroItem);
        HeroOverviewBlock.Text = _heroItem.Overview ?? "";

        var backdrop = _client.BuildBackdropUrl(_heroItem, 1800);
        if (Uri.TryCreate(backdrop, UriKind.Absolute, out var uri))
            HeroImage.Source = new BitmapImage(uri);
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
                BuildShortProgressText(positionTicks, durationTicks, played)));
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

    private void OpenLibrary_Click(object sender, RoutedEventArgs e) =>
        LibraryRequested?.Invoke(this, EventArgs.Empty);

    private void LibraryTile_Click(object sender, RoutedEventArgs e) =>
        LibraryRequested?.Invoke(this, EventArgs.Empty);

    private void LibrarySectionMore_Click(object sender, RoutedEventArgs e) =>
        LibraryRequested?.Invoke(this, EventArgs.Empty);

    private void ResumeTile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ResumeMediaTile tile })
            MediaRequested?.Invoke(this, tile.Item);
    }

    private void SectionMedia_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SectionMediaTile tile })
            MediaRequested?.Invoke(this, tile.Item);
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
        string ShortProgressText);

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
