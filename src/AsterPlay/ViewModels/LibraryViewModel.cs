using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AsterPlay.Models;
using AsterPlay.Services;

namespace AsterPlay.ViewModels;

public sealed record LibraryChoice(string Id, string Name);
public sealed record LibraryTypeChoice(string Value, string Name);
public sealed record LibrarySortChoice(string Value, string Order, string Name);

public sealed class LibraryItemViewModel
{
    public LibraryItemViewModel(EmbyClient client, EmbyItem item)
    {
        Item = item;
        Title = item.Name;
        PosterUrl = client.BuildPrimaryUrl(item, 360);

        var meta = new List<string>();

        if (item.ProductionYear is > 0)
            meta.Add(item.ProductionYear.Value.ToString());

        if (item.CommunityRating is > 0)
            meta.Add($"★ {item.CommunityRating:0.0}");

        if (item.RunTimeTicks is > 0)
        {
            var minutes = item.RunTimeTicks.Value / 600_000_000L;
            meta.Add(minutes >= 60
                ? $"{minutes / 60}h {minutes % 60}m"
                : $"{minutes}m");
        }

        MetaLine = string.Join(" · ", meta);
        TypeLabel = string.Equals(item.Type, "Series", StringComparison.OrdinalIgnoreCase)
            ? "剧集"
            : string.Equals(item.Type, "Movie", StringComparison.OrdinalIgnoreCase)
                ? "电影"
                : item.Type;

        FavoriteLabel = item.UserData?.IsFavorite == true ? "♥" : "";
    }

    public EmbyItem Item { get; }
    public string Title { get; }
    public string PosterUrl { get; }
    public string MetaLine { get; }
    public string TypeLabel { get; }
    public string FavoriteLabel { get; }
}

public sealed class LibraryViewModel : INotifyPropertyChanged
{
    private const int PageSize = 40;

    private readonly EmbyClient _client;
    private LibraryChoice? _selectedLibrary;
    private LibraryTypeChoice? _selectedType;
    private LibrarySortChoice? _selectedSort;
    private string _searchText = "";
    private string _yearText = "";
    private bool _favoriteOnly;
    private bool _isLoading;
    private int _pageIndex;
    private int _totalRecordCount;
    private bool _initialized;

    public LibraryViewModel(EmbyClient client)
    {
        _client = client;

        TypeChoices.Add(new LibraryTypeChoice("Movie,Series", "电影 + 剧集"));
        TypeChoices.Add(new LibraryTypeChoice("Movie", "电影"));
        TypeChoices.Add(new LibraryTypeChoice("Series", "剧集"));
        _selectedType = TypeChoices[0];

        SortChoices.Add(new LibrarySortChoice("SortName", "Ascending", "名称"));
        SortChoices.Add(new LibrarySortChoice("DateCreated", "Descending", "添加时间"));
        SortChoices.Add(new LibrarySortChoice("ProductionYear", "Descending", "上映年份"));
        SortChoices.Add(new LibrarySortChoice("CommunityRating", "Descending", "社区评分"));
        SortChoices.Add(new LibrarySortChoice("DatePlayed", "Descending", "最近播放"));
        _selectedSort = SortChoices[1];
    }

    public ObservableCollection<LibraryChoice> Libraries { get; } = [];
    public ObservableCollection<LibraryTypeChoice> TypeChoices { get; } = [];
    public ObservableCollection<LibrarySortChoice> SortChoices { get; } = [];
    public ObservableCollection<LibraryItemViewModel> Items { get; } = [];

    public LibraryChoice? SelectedLibrary
    {
        get => _selectedLibrary;
        set
        {
            if (Equals(_selectedLibrary, value)) return;
            _selectedLibrary = value;
            OnPropertyChanged();
        }
    }

    public LibraryTypeChoice? SelectedType
    {
        get => _selectedType;
        set
        {
            if (Equals(_selectedType, value)) return;
            _selectedType = value;
            OnPropertyChanged();
        }
    }

    public LibrarySortChoice? SelectedSort
    {
        get => _selectedSort;
        set
        {
            if (Equals(_selectedSort, value)) return;
            _selectedSort = value;
            OnPropertyChanged();
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText == value) return;
            _searchText = value;
            OnPropertyChanged();
        }
    }

    public string YearText
    {
        get => _yearText;
        set
        {
            if (_yearText == value) return;
            _yearText = value;
            OnPropertyChanged();
        }
    }

    public bool FavoriteOnly
    {
        get => _favoriteOnly;
        set
        {
            if (_favoriteOnly == value) return;
            _favoriteOnly = value;
            OnPropertyChanged();
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (_isLoading == value) return;
            _isLoading = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanGoPrevious));
            OnPropertyChanged(nameof(CanGoNext));
        }
    }

    public int TotalRecordCount
    {
        get => _totalRecordCount;
        private set
        {
            if (_totalRecordCount == value) return;
            _totalRecordCount = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PageLabel));
            OnPropertyChanged(nameof(CanGoNext));
        }
    }

    public bool CanGoPrevious => !IsLoading && _pageIndex > 0;
    public bool CanGoNext =>
        !IsLoading && ((_pageIndex + 1) * PageSize) < TotalRecordCount;

    public string PageLabel
    {
        get
        {
            if (TotalRecordCount <= 0)
                return "0 项";

            var first = _pageIndex * PageSize + 1;
            var last = Math.Min((_pageIndex + 1) * PageSize, TotalRecordCount);
            return $"{first}–{last} / {TotalRecordCount}";
        }
    }

    public string ResultLabel =>
        string.IsNullOrWhiteSpace(SearchText)
            ? "完整媒体库"
            : $"搜索：{SearchText.Trim()}";

    public async Task InitializeAsync()
    {
        if (_initialized)
            return;

        _initialized = true;

        Libraries.Clear();
        Libraries.Add(new LibraryChoice("", "全部媒体库"));

        var views = await _client.GetViewsAsync();
        foreach (var view in views
                     .Where(view => !new[]
                     {
                         "boxsets", "playlists", "folders", "livetv", "homevideos"
                     }.Contains((view.CollectionType ?? "").ToLowerInvariant()))
                     .OrderBy(view => view.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            Libraries.Add(new LibraryChoice(view.Id, view.Name));
        }

        SelectedLibrary = Libraries[0];
        await LoadFirstPageAsync();
    }

    public async Task ApplyFiltersAsync()
    {
        _pageIndex = 0;
        await LoadCurrentPageAsync();
    }

    public async Task NextPageAsync()
    {
        if (!CanGoNext)
            return;

        _pageIndex++;
        await LoadCurrentPageAsync();
    }

    public async Task PreviousPageAsync()
    {
        if (!CanGoPrevious)
            return;

        _pageIndex--;
        await LoadCurrentPageAsync();
    }

    private async Task LoadFirstPageAsync()
    {
        _pageIndex = 0;
        await LoadCurrentPageAsync();
    }

    private async Task LoadCurrentPageAsync()
    {
        if (IsLoading)
            return;

        IsLoading = true;

        try
        {
            int? year = null;
            if (!string.IsNullOrWhiteSpace(YearText))
            {
                if (!int.TryParse(YearText.Trim(), out var parsedYear) ||
                    parsedYear < 1800 ||
                    parsedYear > DateTime.Now.Year + 5)
                {
                    throw new ArgumentException("年份请输入有效的四位年份。");
                }

                year = parsedYear;
            }

            var result = await _client.GetLibraryItemsAsync(
                SelectedLibrary?.Id,
                SearchText,
                SelectedType?.Value ?? "Movie,Series",
                year,
                SelectedSort?.Value ?? "DateCreated",
                SelectedSort?.Order ?? "Descending",
                FavoriteOnly,
                _pageIndex * PageSize,
                PageSize);

            Items.Clear();
            foreach (var item in result.Items.Where(item =>
                         !string.IsNullOrWhiteSpace(item.Id)))
            {
                Items.Add(new LibraryItemViewModel(_client, item));
            }

            TotalRecordCount = result.TotalRecordCount;
            OnPropertyChanged(nameof(PageLabel));
            OnPropertyChanged(nameof(ResultLabel));
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(CanGoPrevious));
            OnPropertyChanged(nameof(CanGoNext));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
