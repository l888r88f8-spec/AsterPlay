using System.Windows.Controls;
using System.Windows.Input;
using AsterPlay.Services;
using AsterPlay.ViewModels;

namespace AsterPlay.Views;

public partial class LibraryView : UserControl
{
    private readonly EmbyClient _client;
    private readonly LibraryViewModel _viewModel;

    public LibraryView(EmbyClient client)
    {
        InitializeComponent();

        _client = client;
        _viewModel = new LibraryViewModel(client);
        DataContext = _viewModel;

        Loaded += LibraryView_Loaded;
    }

    private async void LibraryView_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.InitializeAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                UserError.GetMessage(ex, "加载媒体库"),
                "媒体库",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void Apply_Click(object sender, RoutedEventArgs e) =>
        await ApplyFiltersAsync();

    private async void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        e.Handled = true;
        await ApplyFiltersAsync();
    }

    private async Task ApplyFiltersAsync()
    {
        try
        {
            await _viewModel.ApplyFiltersAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                UserError.GetMessage(ex, "加载媒体库"),
                "媒体库筛选",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private async void Previous_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.PreviousPageAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(UserError.GetMessage(ex, "加载媒体库"), "媒体库", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Next_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.NextPageAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(UserError.GetMessage(ex, "加载媒体库"), "媒体库", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Item_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: LibraryItemViewModel item })
            new DetailsWindow(_client, item.Item).ShowDialog();
    }

    private void ItemContextDetails_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Parent: ContextMenu menu } &&
            menu.PlacementTarget is Button { Tag: LibraryItemViewModel item })
        {
            new DetailsWindow(_client, item.Item).ShowDialog();
        }
    }
}
