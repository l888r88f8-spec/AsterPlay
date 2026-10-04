using System.Windows.Input;
using AsterPlay.Services;
using AsterPlay.Models;
using AsterPlay.ViewModels;

namespace AsterPlay.Views;

public partial class HomeView : UserControl
{
    private readonly HomeViewModel _viewModel;
    private readonly EmbyClient _client;

    public event EventHandler? LogoutRequested;

    public HomeView(EmbyClient client)
    {
        InitializeComponent();
        _client = client;
        _viewModel = new HomeViewModel(client);
        DataContext = _viewModel;
        Loaded += HomeView_Loaded;
        SizeChanged += HomeView_SizeChanged;
        Unloaded += (_, _) => _viewModel.Dispose();
    }

    private async void HomeView_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyResponsiveHeroLayout(ActualWidth);
        LoadingOverlay.Visibility = Visibility.Visible;

        try
        {
            await _viewModel.InitializeAsync();
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "AsterPlay", MessageBoxButton.OK, MessageBoxImage.Error);
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
            MessageBox.Show(ex.Message, "Playback", MessageBoxButton.OK, MessageBoxImage.Error);
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
            MessageBox.Show(ex.Message, "Favorite", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Details_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.CurrentHero is not null)
            ShowDetails(_viewModel.CurrentHero);
    }

    private static void ShowDetails(MediaCardViewModel item) =>
        new DetailsWindow(item).ShowDialog();

    private void Logout_Click(object sender, RoutedEventArgs e) =>
        LogoutRequested?.Invoke(this, EventArgs.Empty);
}
