using System.Windows.Controls;
using AsterPlay.Models;
using AsterPlay.Services;
using AsterPlay.ViewModels;

namespace AsterPlay.Views;

public partial class DetailsWindow : Window
{
    private readonly EmbyClient _client;
    private EmbyItem _item;
    private bool _seasonSelectionReady;
    private bool _episodesLoading;

    public DetailsWindow(EmbyClient client, EmbyItem item)
    {
        _client = client;
        _item = item;

        InitializeComponent();

        Owner = Application.Current.MainWindow;
        Loaded += DetailsWindow_Loaded;
    }

    private async void DetailsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadDetailsAsync();
    }

    private async Task LoadDetailsAsync()
    {
        LoadingOverlay.Visibility = Visibility.Visible;
        SetActionsEnabled(false);
        var loadTimer = System.Diagnostics.Stopwatch.StartNew();

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

            PlaybackLog.Write(
                "Details",
                $"Loaded item={_item.Id}, type={_item.Type}, " +
                $"mediaSources={_item.MediaSources.Count}, mediaStreams={_item.MediaStreams.Count}");

            ApplyViewModel();

            if (string.Equals(_item.Type, "Series", StringComparison.OrdinalIgnoreCase))
                await LoadSeriesAsync();

            LoadingOverlay.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                UserError.GetMessage(ex, "加载详情"),
                "Details",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Close();
        }
        finally
        {
            loadTimer.Stop();
            PlaybackLog.Write(
                "Performance",
                $"Details load: {loadTimer.Elapsed.TotalMilliseconds:0} ms, itemId={_item.Id}, type={_item.Type}, " +
                $"workingSet={Environment.WorkingSet / 1024d / 1024d:0.0} MB, managed={GC.GetTotalMemory(false) / 1024d / 1024d:0.0} MB");
            SetActionsEnabled(true);
        }
    }

    private void ApplyViewModel()
    {
        var viewModel = new DetailsViewModel(_client, _item);
        DataContext = viewModel;

        Title = $"AsterPlay · {viewModel.Title}";

        var isSeries = viewModel.IsSeries;

        PlayButton.Visibility = isSeries
            ? Visibility.Collapsed
            : Visibility.Visible;

        RestartButton.Visibility = !isSeries && viewModel.HasResumePosition
            ? Visibility.Visible
            : Visibility.Collapsed;

        SeriesSection.Visibility = isSeries
            ? Visibility.Visible
            : Visibility.Collapsed;

        CreatorPanel.Visibility =
            string.IsNullOrWhiteSpace(viewModel.DirectorsLine) &&
            string.IsNullOrWhiteSpace(viewModel.StudiosLine)
                ? Visibility.Collapsed
                : Visibility.Visible;

        CastSection.Visibility = viewModel.Cast.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        MediaSection.Visibility = !isSeries && viewModel.MediaSummary.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        SourcesSection.Visibility = !isSeries && viewModel.MediaSources.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        TagsSection.Visibility =
            viewModel.Genres.Count > 0 ||
            viewModel.Tags.Count > 0 ||
            viewModel.ProviderBadges.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private async Task LoadSeriesAsync()
    {
        SeriesStatusBlock.Text = "正在加载季度…";
        SeasonComboBox.IsEnabled = false;
        EpisodeItemsControl.ItemsSource = null;

        var seasons = (await _client.GetSeasonsAsync(_item.Id))
            .Select(item => new SeasonViewModel(_client, item))
            .ToArray();

        PlaybackLog.Write(
            "SeriesDetails",
            $"Series={_item.Id}, seasons={seasons.Length}");

        _seasonSelectionReady = false;
        SeasonComboBox.ItemsSource = seasons;

        if (seasons.Length == 0)
        {
            SeriesStatusBlock.Text = "没有可用季度";
            SeasonComboBox.SelectedItem = null;
            SeasonComboBox.IsEnabled = false;
            return;
        }

        var initialSeason = seasons.FirstOrDefault(season => season.Number is > 0)
                            ?? seasons[0];

        SeasonComboBox.SelectedItem = initialSeason;
        SeasonComboBox.IsEnabled = true;
        _seasonSelectionReady = true;

        await LoadEpisodesAsync(initialSeason);
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

            PlaybackLog.Write(
                "SeriesDetails",
                $"Series={_item.Id}, season={season.Item.Id}, episodes={episodes.Length}");
        }
        catch (Exception ex)
        {
            SeriesStatusBlock.Text = $"{season.Title} · 加载失败";
            MessageBox.Show(
                UserError.GetMessage(ex, "加载剧集"),
                "Episodes",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _episodesLoading = false;
            SeasonComboBox.IsEnabled = true;
        }
    }

    private async void EpisodePlay_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: EpisodeViewModel episode })
            return;

        SetActionsEnabled(false);

        try
        {
            PlaybackLog.Write(
                "SeriesDetails",
                $"Episode play: itemId={episode.Item.Id}, label={episode.EpisodeLabel}, resumeTicks={episode.ResumePositionTicks}");

            var launch = await _client.GetPlayableStreamAsync(
                episode.Item,
                restart: false);

            PlaybackNavigation.Open(_client, launch);
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                UserError.GetMessage(ex, "播放"),
                "Playback",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetActionsEnabled(true);
        }
    }

    private async void Play_Click(object sender, RoutedEventArgs e) =>
        await PlayAsync(restart: false);

    private async void Restart_Click(object sender, RoutedEventArgs e) =>
        await PlayAsync(restart: true);

    private async Task PlayAsync(bool restart)
    {
        SetActionsEnabled(false);

        try
        {
            var launch = await _client.GetPlayableStreamAsync(_item, restart);
            PlaybackNavigation.Open(_client, launch);
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                UserError.GetMessage(ex, "播放"),
                "Playback",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetActionsEnabled(true);
        }
    }

    private async void Favorite_Click(object sender, RoutedEventArgs e)
    {
        SetActionsEnabled(false);

        try
        {
            var target = !(_item.UserData?.IsFavorite == true);
            await _client.SetFavoriteAsync(_item.Id, target);

            _item = await _client.GetItemAsync(_item.Id);
            ApplyViewModel();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                UserError.GetMessage(ex, "更新收藏"),
                "Favorite",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetActionsEnabled(true);
        }
    }

    private void SetActionsEnabled(bool enabled)
    {
        PlayButton.IsEnabled = enabled;
        RestartButton.IsEnabled = enabled;
        FavoriteButton.IsEnabled = enabled;

        if (!_episodesLoading)
            SeasonComboBox.IsEnabled = enabled && SeriesSection.Visibility == Visibility.Visible;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
