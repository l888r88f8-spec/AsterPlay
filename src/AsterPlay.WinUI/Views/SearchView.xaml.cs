using System.Collections.ObjectModel;
using AsterPlay.Models;
using AsterPlay.Services;
using AsterPlay.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace AsterPlay.WinUI.Views;

public sealed partial class SearchView : UserControl
{
    private readonly EmbyClient _client;
    private readonly ObservableCollection<LibraryItemViewModel> _results = [];
    private readonly DispatcherQueueTimer _debounceTimer;
    private int _searchGeneration;

    public event EventHandler<EmbyItem>? MediaRequested;

    public SearchView(EmbyClient client)
    {
        _client = client;
        InitializeComponent();

        ItemsGrid.ItemsSource = _results;

        _debounceTimer = DispatcherQueue.CreateTimer();
        _debounceTimer.Interval = TimeSpan.FromMilliseconds(360);
        _debounceTimer.IsRepeating = false;
        _debounceTimer.Tick += async (_, _) =>
        {
            _debounceTimer.Stop();
            await SearchAsync();
        };

        Loaded += SearchView_Loaded;
        Unloaded += SearchView_Unloaded;
    }

    private void SearchView_Loaded(object sender, RoutedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            SearchBox.Focus(FocusState.Programmatic);
            SearchBox.SelectAll();
        });
    }

    private void SearchView_Unloaded(object sender, RoutedEventArgs e)
    {
        _debounceTimer.Stop();
        _searchGeneration++;
    }

    private void SearchBox_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        _debounceTimer.Stop();

        if (string.IsNullOrWhiteSpace(SearchBox.Text))
        {
            _searchGeneration++;
            _results.Clear();
            SearchStatusBlock.Text = "";
            SearchHintPanel.Visibility = Visibility.Visible;
            EmptyPanel.Visibility = Visibility.Collapsed;
            SetLoading(false);
            return;
        }

        _debounceTimer.Start();
    }

    private async void SearchBox_KeyDown(
        object sender,
        KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter)
            return;

        e.Handled = true;
        _debounceTimer.Stop();
        await SearchAsync();
    }

    private async void Search_Click(object sender, RoutedEventArgs e)
    {
        _debounceTimer.Stop();
        await SearchAsync();
    }

    private async Task SearchAsync()
    {
        var query = SearchBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(query))
            return;

        var generation = ++_searchGeneration;
        SetLoading(true);
        SearchHintPanel.Visibility = Visibility.Collapsed;
        EmptyPanel.Visibility = Visibility.Collapsed;
        SearchStatusBlock.Text = $"正在搜索“{query}”…";

        try
        {
            var result = await _client.GetLibraryItemsAsync(
                parentId: null,
                searchTerm: query,
                includeItemTypes: "Movie,Series,Episode",
                year: null,
                sortBy: "SortName",
                sortOrder: "Ascending",
                favoriteOnly: false,
                startIndex: 0,
                limit: 80);

            if (generation != _searchGeneration)
                return;

            _results.Clear();
            foreach (var item in result.Items.Where(item =>
                         !string.IsNullOrWhiteSpace(item.Id)))
            {
                _results.Add(new LibraryItemViewModel(_client, item));
            }

            SearchStatusBlock.Text =
                $"“{query}” · {result.TotalRecordCount} 项结果";
            EmptyDetailBlock.Text = $"没有找到“{query}”";
            EmptyPanel.Visibility = _results.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            if (generation != _searchGeneration)
                return;

            _results.Clear();
            SearchStatusBlock.Text = UserError.GetMessage(ex, "搜索");
            EmptyDetailBlock.Text = "请检查服务器连接后重试。";
            EmptyPanel.Visibility = Visibility.Visible;
            PlaybackLog.Error("WinUISearch", ex);
        }
        finally
        {
            if (generation == _searchGeneration)
                SetLoading(false);
        }
    }

    private void SetLoading(bool value)
    {
        LoadingRing.IsActive = value;
        LoadingRing.Visibility = value
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void ItemsGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is LibraryItemViewModel item)
            MediaRequested?.Invoke(this, item.Item);
    }
}
