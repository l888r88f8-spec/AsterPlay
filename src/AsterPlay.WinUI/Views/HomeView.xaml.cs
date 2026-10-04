using System.Collections.ObjectModel;
using AsterPlay.Models;
using AsterPlay.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace AsterPlay.WinUI.Views;

public sealed partial class HomeView : UserControl
{
    private readonly EmbyClient _client;
    private readonly ObservableCollection<HomeLibraryTile> _libraries = [];
    private readonly ObservableCollection<HomeMediaTile> _latest = [];

    public event EventHandler? LibraryRequested;
    public event EventHandler<EmbyItem>? MediaRequested;

    public HomeView(EmbyClient client)
    {
        _client = client;
        InitializeComponent();

        LibrariesGrid.ItemsSource = _libraries;
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
            var latestTask = _client.GetLatestAsync(18);
            await Task.WhenAll(viewsTask, latestTask);

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

    private void LibrariesGrid_ItemClick(object sender, ItemClickEventArgs e) =>
        LibraryRequested?.Invoke(this, EventArgs.Empty);

    private void LatestGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is HomeMediaTile tile)
            MediaRequested?.Invoke(this, tile.Item);
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

    private sealed record HomeMediaTile(
        EmbyItem Item,
        string Title,
        string Meta,
        string PosterUrl,
        string BackdropUrl);
}
