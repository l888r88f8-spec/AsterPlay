using System.Windows.Input;
using System.Windows.Threading;
using AsterPlay.Services;
using AsterPlay.Models;
using AsterPlay.ViewModels;

namespace AsterPlay.Views;

public partial class HomeView : UserControl
{
    private readonly HomeViewModel _viewModel;
    private readonly EmbyClient _client;
    private bool _playbackRefreshSubscribed;

    public event EventHandler? LogoutRequested;

    public HomeView(EmbyClient client)
    {
        InitializeComponent();
        _client = client;
        _viewModel = new HomeViewModel(client);
        DataContext = _viewModel;
        Loaded += HomeView_Loaded;
        SizeChanged += HomeView_SizeChanged;
        Unloaded += HomeView_Unloaded;
    }

    private async void HomeView_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_playbackRefreshSubscribed)
        {
            _client.PlaybackStateChanged += EmbyClient_PlaybackStateChanged;
            _playbackRefreshSubscribed = true;
        }

        ApplyResponsiveHeroLayout(ActualWidth);
        LoadingOverlay.Visibility = Visibility.Visible;
        var loadTimer = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            await _viewModel.InitializeAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                UserError.GetMessage(ex, "加载首页"),
                "AsterPlay",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
            loadTimer.Stop();
            PlaybackLog.Write(
                "Performance",
                $"Home initial load: {loadTimer.Elapsed.TotalMilliseconds:0} ms, " +
                $"libraries={_viewModel.Libraries.Count}, resume={_viewModel.ResumeItems.Count}, latest={_viewModel.LatestItems.Count}, " +
                $"workingSet={Environment.WorkingSet / 1024d / 1024d:0.0} MB, managed={GC.GetTotalMemory(false) / 1024d / 1024d:0.0} MB");
        }
    }

    private void HomeView_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_playbackRefreshSubscribed)
        {
            _client.PlaybackStateChanged -= EmbyClient_PlaybackStateChanged;
            _playbackRefreshSubscribed = false;
        }

        _viewModel.Dispose();
    }

    private void EmbyClient_PlaybackStateChanged(
        object? sender,
        PlaybackStateChangedEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() => _ = RefreshResumeAfterPlaybackAsync(e)));
            return;
        }

        _ = RefreshResumeAfterPlaybackAsync(e);
    }

    private async Task RefreshResumeAfterPlaybackAsync(PlaybackStateChangedEventArgs e)
    {
        try
        {
            await _viewModel.RefreshResumeAsync(e.ItemId, e.PositionTicks);
            PlaybackLog.Write(
                "Home",
                $"Continue Watching refreshed after playback update; itemId={e.ItemId}, " +
                $"positionTicks={e.PositionTicks}, event={e.EventName}, items={_viewModel.ResumeItems.Count}");
        }
        catch (Exception ex)
        {
            PlaybackLog.Error("HomeResumeRefresh", ex);
        }
    }

    private void HomeView_SizeChanged(object sender, SizeChangedEventArgs e) =>
        ApplyResponsiveHeroLayout(e.NewSize.Width);

    private void ApplyResponsiveHeroLayout(double width)
    {
        if (width < 1050)
        {
            HeroRoot.Height = 560;
            HeroLayout.Margin = new Thickness(28, 22, 28, 34);
            HeroInfoPanel.MaxWidth = 760;
            HeroTitle.FontSize = 38;
            HeroTitle.LineHeight = 44;
            HeroOverview.MaxHeight = 104;
            HeroThumbScroller.Visibility = Visibility.Collapsed;
            HeroThumbColumn.Width = new GridLength(0);
        }
        else if (width < 1280)
        {
            HeroRoot.Height = 590;
            HeroLayout.Margin = new Thickness(36, 26, 36, 38);
            HeroInfoPanel.MaxWidth = 620;
            HeroTitle.FontSize = 42;
            HeroTitle.LineHeight = 49;
            HeroOverview.MaxHeight = 92;
            HeroThumbScroller.MaxWidth = 360;
            HeroThumbScroller.Visibility = Visibility.Visible;
            HeroThumbColumn.Width = GridLength.Auto;
        }
        else
        {
            HeroRoot.Height = 620;
            HeroLayout.Margin = new Thickness(48, 28, 48, 42);
            HeroInfoPanel.MaxWidth = 620;
            HeroTitle.FontSize = 48;
            HeroTitle.LineHeight = 56;
            HeroOverview.MaxHeight = 82;
            HeroThumbScroller.MaxWidth = 500;
            HeroThumbScroller.Visibility = Visibility.Visible;
            HeroThumbColumn.Width = GridLength.Auto;
        }
    }

    private void MainScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        MainScrollViewer.ScrollToVerticalOffset(
            MainScrollViewer.VerticalOffset - e.Delta * 1.25);
        e.Handled = true;
    }

    private void LibraryTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: LibrarySectionViewModel section })
            _viewModel.SelectLibrary(section);
    }

    private void HeroThumb_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: MediaCardViewModel card })
            _viewModel.SelectHero(card);
    }

    private async void Play_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.CurrentHero is not null)
            await PlayItemAsync(_viewModel.CurrentHero);
    }

    private async void ResumeCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: MediaCardViewModel item })
            await PlayItemAsync(item);
    }

    private void LatestCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: MediaCardViewModel item })
            ShowDetails(item);
    }

    private async Task PlayItemAsync(MediaCardViewModel item)
    {
        try
        {
            var launch = await _viewModel.PlayItemAsync(item);
            new PlayerWindow(_client, launch).Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(UserError.GetMessage(ex, "播放"), "Playback", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Favorite_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.ToggleFavoriteAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(UserError.GetMessage(ex, "更新收藏"), "Favorite", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Details_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.CurrentHero is not null)
            ShowDetails(_viewModel.CurrentHero);
    }

    private void ShowDetails(MediaCardViewModel item) =>
        new DetailsWindow(_client, item.Item).ShowDialog();

    private void Logout_Click(object sender, RoutedEventArgs e) =>
        LogoutRequested?.Invoke(this, EventArgs.Empty);
}
