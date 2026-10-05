using AsterPlay.Models;
using AsterPlay.Services;
using AsterPlay.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace AsterPlay.WinUI.Views;

public sealed partial class LibraryView : UserControl
{
    private readonly LibraryViewModel _viewModel;

    public event EventHandler<EmbyItem>? MediaRequested;

    public LibraryView(EmbyClient client)
    {
        _viewModel = new LibraryViewModel(client);
        InitializeComponent();

        ItemsGrid.ItemsSource = _viewModel.Items;
        Loaded += LibraryView_Loaded;
    }

    private async void LibraryView_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= LibraryView_Loaded;
        await InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        SetLoading(true);

        try
        {
            await _viewModel.InitializeAsync();

            LibraryBox.ItemsSource = _viewModel.Libraries;
            TypeBox.ItemsSource = _viewModel.TypeChoices;
            SortBox.ItemsSource = _viewModel.SortChoices;

            LibraryBox.SelectedItem = _viewModel.SelectedLibrary;
            TypeBox.SelectedItem = _viewModel.SelectedType;
            SortBox.SelectedItem = _viewModel.SelectedSort;

            RefreshState();
        }
        catch (Exception ex)
        {
            ResultLabelBlock.Text = UserError.GetMessage(ex, "加载媒体库");
            PlaybackLog.Error("WinUILibrary", ex);
        }
        finally
        {
            SetLoading(false);
            RefreshState();
        }
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        SyncFilters();
        await RunAsync(_viewModel.ApplyFiltersAsync);
    }

    private async void SearchBox_KeyDown(
        object sender,
        KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter)
            return;

        e.Handled = true;
        SyncFilters();
        await RunAsync(_viewModel.ApplyFiltersAsync);
    }

    private async void Previous_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(_viewModel.PreviousPageAsync);

    private async void Next_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(_viewModel.NextPageAsync);

    private void ItemsGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is LibraryItemViewModel item)
            MediaRequested?.Invoke(this, item.Item);
    }

    private void SyncFilters()
    {
        _viewModel.SelectedLibrary = LibraryBox.SelectedItem as LibraryChoice;
        _viewModel.SelectedType = TypeBox.SelectedItem as LibraryTypeChoice;
        _viewModel.SelectedSort = SortBox.SelectedItem as LibrarySortChoice;
        _viewModel.SearchText = SearchBox.Text;
        _viewModel.YearText = YearBox.Text;
        _viewModel.FavoriteOnly = FavoriteOnlyBox.IsChecked == true;
    }

    private async Task RunAsync(Func<Task> action)
    {
        SetLoading(true);

        try
        {
            await action();
        }
        catch (Exception ex)
        {
            ResultLabelBlock.Text = UserError.GetMessage(ex, "加载媒体库");
            PlaybackLog.Error("WinUILibrary", ex);
        }
        finally
        {
            SetLoading(false);
            RefreshState();
        }
    }

    private void SetLoading(bool value)
    {
        LoadingRing.IsActive = value;
        LoadingRing.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshState()
    {
        ResultLabelBlock.Text = _viewModel.ResultLabel;
        PageLabelBlock.Text = _viewModel.PageLabel;
        PreviousButton.IsEnabled = _viewModel.CanGoPrevious;
        NextButton.IsEnabled = _viewModel.CanGoNext;
        EmptyPanel.Visibility = _viewModel.IsEmpty
            ? Visibility.Visible
            : Visibility.Collapsed;
    }
}
