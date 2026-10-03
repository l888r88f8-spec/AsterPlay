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
        Unloaded += (_, _) => _viewModel.Dispose();
    }

    private async void HomeView_Loaded(object sender, RoutedEventArgs e)
    {
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
        try
        {
            var launch = await _viewModel.PlayCurrentAsync();
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
            new DetailsWindow(_viewModel.CurrentHero).ShowDialog();
    }

    private void Logout_Click(object sender, RoutedEventArgs e) =>
        LogoutRequested?.Invoke(this, EventArgs.Empty);
}
