using AsterPlay.Models;
using AsterPlay.Services;
using AsterPlay.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace AsterPlay.WinUI.Views;

public sealed partial class DetailsView : UserControl
{
    private readonly EmbyClient _client;
    private EmbyItem _item;
    private bool _seasonSelectionReady;
    private bool _episodesLoading;

    public event EventHandler? BackRequested;
    public event EventHandler<DetailsPlaybackRequestedEventArgs>? PlaybackRequested;

    public DetailsView(EmbyClient client, EmbyItem item)
    {
        _client = client;
        _item = item;

        InitializeComponent();
        Loaded += DetailsView_Loaded;
    }

    private async void DetailsView_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= DetailsView_Loaded;
        await LoadDetailsAsync();
    }

    private async Task LoadDetailsAsync()
    {
        SetLoading(true);

        try
        {
            _item = await _client.GetItemAsync(_item.Id);

            if (string.Equals(_item.Type, "Movie", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(_item.Type, "Episode", StringComparison.OrdinalIgnoreCase))
            {
                var mediaInfo = await _client.GetMediaInfoAsync(_item.Id);
                if (mediaInfo.MediaSources.Count > 0)
                {
                    _item.MediaSources = mediaInfo.MediaSources;
                    _item.MediaStreams = mediaInfo.MediaSources
                        .SelectMany(source => source.MediaStreams)
                        .ToList();
                }
            }

            ApplyViewModel();

            if (string.Equals(_item.Type, "Series", StringComparison.OrdinalIgnoreCase))
                await LoadSeriesAsync();
        }
        catch (Exception ex)
        {
            PlaybackLog.Error("WinUIDetails", ex);
            TitleBlock.Text = "详情加载失败";
            MetaBlock.Text = UserError.GetMessage(ex, "加载详情");
        }
        finally
        {
            SetLoading(false);
        }
    }

    private void ApplyViewModel()
    {
        var vm = new DetailsViewModel(_client, _item);

        TitleBlock.Text = vm.Title;
        OriginalTitleBlock.Text = vm.OriginalTitle;
        OriginalTitleBlock.Visibility = string.IsNullOrWhiteSpace(vm.OriginalTitle)
            ? Visibility.Collapsed
            : Visibility.Visible;

        TaglineBlock.Text = vm.Tagline;
        TaglineBlock.Visibility = string.IsNullOrWhiteSpace(vm.Tagline)
            ? Visibility.Collapsed
            : Visibility.Visible;

        MetaBlock.Text = vm.MetaLine;
        OverviewBlock.Text = vm.Overview;

        PlayButton.Content = vm.ResumeLabel;
        PlayButton.Visibility = vm.IsSeries ? Visibility.Collapsed : Visibility.Visible;
        RestartButton.Visibility = !vm.IsSeries && vm.HasResumePosition
            ? Visibility.Visible
            : Visibility.Collapsed;

        FavoriteButton.Content = vm.FavoriteLabel;

        if (Uri.TryCreate(vm.PosterUrl, UriKind.Absolute, out var poster))
            PosterImage.Source = new BitmapImage(poster);

        if (Uri.TryCreate(vm.BackdropUrl, UriKind.Absolute, out var backdrop))
            BackdropImage.Source = new BitmapImage(backdrop);

        DirectorsBlock.Text = string.IsNullOrWhiteSpace(vm.DirectorsLine)
            ? ""
            : $"导演  {vm.DirectorsLine}";
        StudiosBlock.Text = string.IsNullOrWhiteSpace(vm.StudiosLine)
            ? ""
            : $"制作  {vm.StudiosLine}";
        CreatorPanel.Visibility =
            string.IsNullOrWhiteSpace(vm.DirectorsLine) &&
            string.IsNullOrWhiteSpace(vm.StudiosLine)
                ? Visibility.Collapsed
                : Visibility.Visible;

        CastItemsControl.ItemsSource = vm.Cast;
        CastSection.Visibility = vm.Cast.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        SeriesSection.Visibility = vm.IsSeries
            ? Visibility.Visible
            : Visibility.Collapsed;

        MediaSummaryItems.ItemsSource = vm.MediaSummary;
        MediaSection.Visibility = !vm.IsSeries && vm.MediaSummary.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        GenresBlock.Text = vm.Genres.Count > 0
            ? $"类型  {string.Join(" / ", vm.Genres)}"
            : "";
        TagsBlock.Text = vm.Tags.Count > 0
            ? $"标签  {string.Join(" / ", vm.Tags.Take(12))}"
            : "";
        ProviderItems.ItemsSource = vm.ProviderBadges;

        TagsSection.Visibility =
            vm.Genres.Count > 0 ||
            vm.Tags.Count > 0 ||
            vm.ProviderBadges.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;

        InfoGrid.Visibility =
            MediaSection.Visibility == Visibility.Visible ||
            TagsSection.Visibility == Visibility.Visible
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private async Task LoadSeriesAsync()
    {
        SeriesStatusBlock.Text = "正在加载季度…";
        SeasonComboBox.IsEnabled = false;

        try
        {
            var seasons = (await _client.GetSeasonsAsync(_item.Id))
                .Select(item => new SeasonViewModel(_client, item))
                .ToArray();

            _seasonSelectionReady = false;
            SeasonComboBox.ItemsSource = seasons;

            if (seasons.Length == 0)
            {
                SeriesStatusBlock.Text = "没有可用季度";
                return;
            }

            var initialSeason = seasons.FirstOrDefault(season => season.Number is > 0)
                                ?? seasons[0];

            SeasonComboBox.SelectedItem = initialSeason;
            SeasonComboBox.IsEnabled = true;
            _seasonSelectionReady = true;

            await LoadEpisodesAsync(initialSeason);
        }
        catch (Exception ex)
        {
            SeriesStatusBlock.Text = UserError.GetMessage(ex, "加载季度");
            PlaybackLog.Error("WinUISeriesDetails", ex);
        }
    }

    private async void SeasonComboBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (!_seasonSelectionReady ||
            _episodesLoading ||
            SeasonComboBox.SelectedItem is not SeasonViewModel season)
        {
            return;
        }

        await LoadEpisodesAsync(season);
    }

    private async Task LoadEpisodesAsync(SeasonViewModel season)
    {
        if (_episodesLoading)
            return;

        _episodesLoading = true;
        SeasonComboBox.IsEnabled = false;
        EpisodeItemsControl.ItemsSource = null;
        SeriesStatusBlock.Text = $"正在加载 {season.Title}…";

        try
        {
            var episodes = (await _client.GetEpisodesAsync(_item.Id, season.Item.Id))
                .Select(item => new EpisodeViewModel(_client, item))
                .ToArray();

            EpisodeItemsControl.ItemsSource = episodes;
            SeriesStatusBlock.Text = episodes.Length == 0
                ? $"{season.Title} · 没有剧集"
                : $"{season.Title} · {episodes.Length} 集";
        }
        catch (Exception ex)
        {
            SeriesStatusBlock.Text = $"{season.Title} · 加载失败";
            PlaybackLog.Error("WinUIEpisodes", ex);
        }
        finally
        {
            _episodesLoading = false;
            SeasonComboBox.IsEnabled = true;
        }
    }

    private void Play_Click(object sender, RoutedEventArgs e) =>
        PlaybackRequested?.Invoke(
            this,
            new DetailsPlaybackRequestedEventArgs(_item, restart: false));

    private void Restart_Click(object sender, RoutedEventArgs e) =>
        PlaybackRequested?.Invoke(
            this,
            new DetailsPlaybackRequestedEventArgs(_item, restart: true));

    private void EpisodePlay_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: EpisodeViewModel episode })
        {
            PlaybackRequested?.Invoke(
                this,
                new DetailsPlaybackRequestedEventArgs(episode.Item, restart: false));
        }
    }

    private async void Favorite_Click(object sender, RoutedEventArgs e)
    {
        FavoriteButton.IsEnabled = false;

        try
        {
            var target = !(_item.UserData?.IsFavorite == true);
            await _client.SetFavoriteAsync(_item.Id, target);
            _item = await _client.GetItemAsync(_item.Id);
            ApplyViewModel();
        }
        catch (Exception ex)
        {
            PlaybackLog.Error("WinUIFavorite", ex);
        }
        finally
        {
            FavoriteButton.IsEnabled = true;
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e) =>
        BackRequested?.Invoke(this, EventArgs.Empty);

    private void SetLoading(bool loading)
    {
        LoadingOverlay.Visibility = loading
            ? Visibility.Visible
            : Visibility.Collapsed;
    }
}

public sealed class DetailsPlaybackRequestedEventArgs(
    EmbyItem item,
    bool restart) : EventArgs
{
    public EmbyItem Item { get; } = item;
    public bool Restart { get; } = restart;
}
