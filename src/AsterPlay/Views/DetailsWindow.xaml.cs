using AsterPlay.Models;
using AsterPlay.Services;
using AsterPlay.ViewModels;

namespace AsterPlay.Views;

public partial class DetailsWindow : Window
{
    private readonly EmbyClient _client;
    private EmbyItem _item;

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
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Details",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Close();
        }
        finally
        {
            SetActionsEnabled(true);
        }
    }

    private void ApplyViewModel()
    {
        var viewModel = new DetailsViewModel(_client, _item);
        DataContext = viewModel;

        Title = $"AsterPlay · {viewModel.Title}";

        RestartButton.Visibility = viewModel.HasResumePosition
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

        MediaSection.Visibility = viewModel.MediaSummary.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        SourcesSection.Visibility = viewModel.MediaSources.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        TagsSection.Visibility =
            viewModel.Genres.Count > 0 ||
            viewModel.Tags.Count > 0 ||
            viewModel.ProviderBadges.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;

        SeriesNotice.Visibility = viewModel.IsSeries
            ? Visibility.Visible
            : Visibility.Collapsed;
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
            new PlayerWindow(_client, launch).Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
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
                ex.Message,
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
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
