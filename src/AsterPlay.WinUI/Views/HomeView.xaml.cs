using System.Collections.ObjectModel;
using AsterPlay.Models;
using AsterPlay.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;

namespace AsterPlay.WinUI.Views;

public sealed partial class HomeView : UserControl
{
    private readonly EmbyClient _client;
    private readonly ObservableCollection<HomeLibraryTile> _libraries = [];
    private readonly ObservableCollection<ResumeMediaTile> _resume = [];
    private readonly ObservableCollection<HomeMediaTile> _latest = [];

    public event EventHandler? LibraryRequested;
    public event EventHandler<EmbyItem>? MediaRequested;

    public HomeView(EmbyClient client)
    {
        _client = client;
        InitializeComponent();

        LibrariesGrid.ItemsSource = _libraries;
        ResumeGrid.ItemsSource = _resume;
        LatestGrid.ItemsSource = _latest;

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
            var resumeTask = _client.GetResumeAsync(28);
            var latestTask = _client.GetLatestAsync(18);
            await Task.WhenAll(viewsTask, resumeTask, latestTask);

            var views = viewsTask.Result
                .Where(IsVisibleLibrary)
                .ToList();

            _libraries.Clear();
            foreach (var view in views)
            {
                _libraries.Add(new HomeLibraryTile(
                    view,
                    view.Name,
                    string.IsNullOrWhiteSpace(view.CollectionType)
                        ? "媒体库"
                        : view.CollectionType));
            }

            _resume.Clear();
            foreach (var item in BuildResumeItems(resumeTask.Result))
            {
                var played = Math.Clamp(item.UserData?.PlayedPercentage ?? 0, 0, 100);
                var positionTicks = Math.Max(0, item.UserData?.PlaybackPositionTicks ?? 0);
                var durationTicks = Math.Max(0, item.RunTimeTicks ?? 0);

                _resume.Add(new ResumeMediaTile(
                    item,
                    BuildResumeTitle(item),
                    _client.BuildBackdropUrl(item, 900),
                    played,
                    BuildProgressText(positionTicks, durationTicks, played)));
            }

            ContinueSection.Visibility = _resume.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;

            _latest.Clear();
            foreach (var item in latestTask.Result.Where(x => !string.IsNullOrWhiteSpace(x.Id)))
            {
                _latest.Add(new HomeMediaTile(
                    item,
                    item.Name,
                    BuildMeta(item),
                    _client.BuildPrimaryUrl(item, 420),
                    _client.BuildBackdropUrl(item, 1400)));
            }

            var hero = _latest.FirstOrDefault();
            if (hero is not null)
            {
                HeroTitleBlock.Text = hero.Title;
                HeroMetaBlock.Text = hero.Meta;
                HeroOverviewBlock.Text = hero.Item.Overview ?? "";

                if (Uri.TryCreate(hero.BackdropUrl, UriKind.Absolute, out var backdropUri))
                    HeroImage.Source = new BitmapImage(backdropUri);
            }
            else
            {
                HeroTitleBlock.Text = "媒体库已连接";
                HeroMetaBlock.Text = "";
                HeroOverviewBlock.Text = "从底部导航进入媒体库浏览全部内容。";
            }
        }
        catch (Exception ex)
        {
            HeroTitleBlock.Text = "首页加载失败";
            HeroMetaBlock.Text = UserError.GetMessage(ex, "加载首页");
            HeroOverviewBlock.Text = "";
            PlaybackLog.Error("WinUIHome", ex);
        }
        finally
        {
            LoadingRing.IsActive = false;
            LoadingRing.Visibility = Visibility.Collapsed;
        }
    }

    private void OpenLibrary_Click(object sender, RoutedEventArgs e) =>
        LibraryRequested?.Invoke(this, EventArgs.Empty);

    private void LibraryTile_Click(object sender, RoutedEventArgs e) =>
        LibraryRequested?.Invoke(this, EventArgs.Empty);

    private void ResumeTile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ResumeMediaTile tile })
            MediaRequested?.Invoke(this, tile.Item);
    }

    private void LatestTile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: HomeMediaTile tile })
            MediaRequested?.Invoke(this, tile.Item);
    }

    private void HorizontalRail_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not ScrollViewer scroller || scroller.ScrollableWidth <= 0)
            return;

        var point = e.GetCurrentPoint(scroller);
        var delta = point.Properties.MouseWheelDelta;

        if (delta == 0)
            return;

        // Translate the normal vertical mouse wheel into direct horizontal
        // movement for media rails. Disable the extra scroll animation so
        // each wheel notch tracks the user's input immediately.
        var step = Math.Clamp(Math.Abs(delta) * 1.15, 72, 220);
        var target = delta > 0
            ? scroller.HorizontalOffset - step
            : scroller.HorizontalOffset + step;

        target = Math.Clamp(target, 0, scroller.ScrollableWidth);

        scroller.ChangeView(
            horizontalOffset: target,
            verticalOffset: null,
            zoomFactor: null,
            disableAnimation: true);

        e.Handled = true;
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
            .Take(14)
            .ToArray();
    }

    private static string BuildResumeTitle(EmbyItem item)
    {
        if (string.Equals(item.Type, "Episode", StringComparison.OrdinalIgnoreCase))
        {
            var episodeCode = item.ParentIndexNumber is > 0 && item.IndexNumber is > 0
                ? $"S{item.ParentIndexNumber:00}E{item.IndexNumber:00}"
                : item.IndexNumber is > 0
                    ? $"E{item.IndexNumber:00}"
                    : "";

            if (!string.IsNullOrWhiteSpace(item.SeriesName))
            {
                return string.IsNullOrWhiteSpace(episodeCode)
                    ? $"{item.SeriesName} · {item.Name}"
                    : $"{item.SeriesName} · {episodeCode} · {item.Name}";
            }

            return string.IsNullOrWhiteSpace(episodeCode)
                ? item.Name
                : $"{episodeCode} · {item.Name}";
        }

        return item.Name;
    }

    private static string BuildProgressText(long positionTicks, long durationTicks, double percentage)
    {
        if (positionTicks > 0 && durationTicks > 0)
        {
            var position = TimeSpan.FromTicks(positionTicks);
            var remaining = TimeSpan.FromTicks(Math.Max(0, durationTicks - positionTicks));
            return $"已看 {FormatCompactTime(position)} · 剩余 {FormatCompactTime(remaining)}";
        }

        return percentage > 0
            ? $"已看 {percentage:0}%"
            : "继续播放";
    }

    private static string FormatCompactTime(TimeSpan value)
    {
        if (value.TotalHours >= 1)
            return $"{(int)value.TotalHours}小时 {value.Minutes}分";

        return $"{Math.Max(1, value.Minutes)}分";
    }

    private static bool IsVisibleLibrary(EmbyItem view) =>
        !new[] { "boxsets", "playlists", "folders", "livetv", "homevideos" }
            .Contains((view.CollectionType ?? "").ToLowerInvariant());

    private static string BuildMeta(EmbyItem item)
    {
        var values = new List<string>();

        if (item.ProductionYear is > 0)
            values.Add(item.ProductionYear.Value.ToString());

        if (item.CommunityRating is > 0)
            values.Add($"★ {item.CommunityRating:0.0}");

        if (!string.IsNullOrWhiteSpace(item.Type))
            values.Add(string.Equals(item.Type, "Series", StringComparison.OrdinalIgnoreCase)
                ? "剧集"
                : string.Equals(item.Type, "Movie", StringComparison.OrdinalIgnoreCase)
                    ? "电影"
                    : item.Type);

        return string.Join(" · ", values);
    }

    private sealed record HomeLibraryTile(
        EmbyItem Item,
        string Name,
        string Subtitle);

    private sealed record ResumeMediaTile(
        EmbyItem Item,
        string ResumeTitle,
        string BackdropUrl,
        double PlayedPercentage,
        string ProgressText);

    private sealed record HomeMediaTile(
        EmbyItem Item,
        string Title,
        string Meta,
        string PosterUrl,
        string BackdropUrl);
}
