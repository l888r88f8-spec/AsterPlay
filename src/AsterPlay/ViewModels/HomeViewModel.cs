using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using AsterPlay.Models;
using AsterPlay.Services;

namespace AsterPlay.ViewModels;

public sealed class MediaCardViewModel : INotifyPropertyChanged
{
    private bool _isActive;
    private bool _isFavorite;

    public MediaCardViewModel(EmbyClient client, EmbyItem item)
    {
        Item = item;
        Title = item.Name;
        Overview = item.Overview ?? "";
        BackdropUrl = client.BuildBackdropUrl(item);
        PosterUrl = client.BuildPrimaryUrl(item);
        _isFavorite = item.UserData?.IsFavorite == true;
        PlayedPercentage = Math.Clamp(item.UserData?.PlayedPercentage ?? 0, 0, 100);

        var meta = new List<string>();
        if (item.CommunityRating is > 0)
            meta.Add($"★ {item.CommunityRating:0.0}");
        if (item.ProductionYear is > 0)
            meta.Add(item.ProductionYear.Value.ToString());
        if (item.Genres.Count > 0)
            meta.Add(string.Join(" / ", item.Genres.Take(3)));
        if (item.RunTimeTicks is > 0)
        {
            var minutes = item.RunTimeTicks.Value / 600_000_000L;
            meta.Add(minutes >= 60 ? $"{minutes / 60}h {minutes % 60}m" : $"{minutes}m");
        }

        MetaLine = string.Join("   ·   ", meta);
    }

    public EmbyItem Item { get; }
    public string Title { get; }
    public string ResumeTitle =>
        Item.IndexNumber is > 0
            ? Item.ParentIndexNumber is > 0
                ? $"S{Item.ParentIndexNumber:00}E{Item.IndexNumber:00} · {Title}"
                : $"E{Item.IndexNumber:00} · {Title}"
            : Title;
    public string Overview { get; }
    public string BackdropUrl { get; }
    public string PosterUrl { get; }
    public string MetaLine { get; }
    public double PlayedPercentage { get; }

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value) return;
            _isActive = value;
            OnPropertyChanged();
        }
    }

    public bool IsFavorite
    {
        get => _isFavorite;
        set
        {
            if (_isFavorite == value) return;
            _isFavorite = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FavoriteLabel));
        }
    }

    public string FavoriteLabel => IsFavorite ? "♥  Favorited" : "♥  Favorite";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class LibrarySectionViewModel : INotifyPropertyChanged
{
    private bool _isSelected;

    public required string Id { get; init; }
    public required string Name { get; init; }
    public ObservableCollection<MediaCardViewModel> Items { get; } = [];

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class HomeViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly EmbyClient _client;
    private readonly DispatcherTimer _heroTimer;
    private LibrarySectionViewModel? _currentLibrary;
    private MediaCardViewModel? _currentHero;
    private int _heroIndex;
    private bool _resumeRefreshRunning;
    private bool _resumeRefreshPending;
    private string? _resumePreferredItemId;
    private long _resumePreferredPositionTicks;

    public HomeViewModel(EmbyClient client)
    {
        _client = client;
        UserName = client.UserName;

        _heroTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(6500) };
        _heroTimer.Tick += (_, _) => AdvanceHero();
    }

    public string UserName { get; }
    public ObservableCollection<LibrarySectionViewModel> Libraries { get; } = [];
    public ObservableCollection<MediaCardViewModel> ResumeItems { get; } = [];
    public ObservableCollection<MediaCardViewModel> LatestItems { get; } = [];

    public LibrarySectionViewModel? CurrentLibrary
    {
        get => _currentLibrary;
        private set
        {
            _currentLibrary = value;
            OnPropertyChanged();
        }
    }

    public MediaCardViewModel? CurrentHero
    {
        get => _currentHero;
        private set
        {
            _currentHero = value;
            OnPropertyChanged();
        }
    }

    public async Task InitializeAsync()
    {
        Libraries.Clear();
        ResumeItems.Clear();
        LatestItems.Clear();

        var views = await _client.GetViewsAsync();
        var visibleViews = views
            .Where(v => !new[] { "boxsets", "playlists", "folders", "livetv", "homevideos" }
                .Contains((v.CollectionType ?? "").ToLowerInvariant()))
            .ToList();

        foreach (var view in visibleViews)
        {
            var section = new LibrarySectionViewModel { Id = view.Id, Name = view.Name };
            var items = await _client.GetLibraryLatestAsync(view.Id, 8);
            foreach (var item in items.Where(x => !string.IsNullOrWhiteSpace(x.Id)))
                section.Items.Add(new MediaCardViewModel(_client, item));

            if (section.Items.Count > 0)
                Libraries.Add(section);
        }

        foreach (var item in await BuildResumeItemsAsync(null, 0))
            ResumeItems.Add(new MediaCardViewModel(_client, item));

        foreach (var item in await _client.GetLatestAsync(18))
            LatestItems.Add(new MediaCardViewModel(_client, item));

        if (Libraries.Count == 0 && LatestItems.Count > 0)
        {
            var fallback = new LibrarySectionViewModel { Id = "latest", Name = "Latest" };
            foreach (var item in LatestItems.Take(8))
                fallback.Items.Add(item);
            Libraries.Add(fallback);
        }

        if (Libraries.Count > 0)
            SelectLibrary(Libraries[0]);

        _heroTimer.Start();
    }

    public async Task RefreshResumeAsync(string? preferredItemId = null, long preferredPositionTicks = 0)
    {
        if (!string.IsNullOrWhiteSpace(preferredItemId) && preferredPositionTicks > 0)
        {
            _resumePreferredItemId = preferredItemId;
            _resumePreferredPositionTicks = preferredPositionTicks;
        }

        if (_resumeRefreshRunning)
        {
            _resumeRefreshPending = true;
            return;
        }

        _resumeRefreshRunning = true;

        try
        {
            do
            {
                _resumeRefreshPending = false;

                var currentPreferredItemId = _resumePreferredItemId;
                var currentPreferredPositionTicks = _resumePreferredPositionTicks;

                var items = await BuildResumeItemsAsync(
                    currentPreferredItemId,
                    currentPreferredPositionTicks);

                ResumeItems.Clear();
                foreach (var item in items)
                    ResumeItems.Add(new MediaCardViewModel(_client, item));
            }
            while (_resumeRefreshPending);
        }
        finally
        {
            _resumeRefreshRunning = false;
        }
    }

    private async Task<IReadOnlyList<EmbyItem>> BuildResumeItemsAsync(
        string? preferredItemId,
        long preferredPositionTicks)
    {
        // Fetch extra rows because multiple resumable episodes from one series
        // are collapsed to the most recently watched episode below.
        var candidates = (await _client.GetResumeAsync(28))
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .ToList();

        if (!string.IsNullOrWhiteSpace(preferredItemId) && preferredPositionTicks > 0)
        {
            try
            {
                var preferred = await _client.GetItemAsync(preferredItemId);

                if (preferred.UserData is not null && preferred.UserData.Played != true)
                {
                    preferred.UserData.PlaybackPositionTicks =
                        Math.Max(preferred.UserData.PlaybackPositionTicks, preferredPositionTicks);
                    preferred.UserData.LastPlayedDate = DateTimeOffset.UtcNow;

                    candidates.RemoveAll(item =>
                        string.Equals(item.Id, preferred.Id, StringComparison.OrdinalIgnoreCase));
                    candidates.Insert(0, preferred);
                }
            }
            catch (Exception ex)
            {
                PlaybackLog.Error("HomeResumePreferredItem", ex);
            }
        }

        static string GroupKey(EmbyItem item) =>
            string.Equals(item.Type, "Episode", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(item.SeriesId)
                ? "series:" + item.SeriesId
                : "item:" + item.Id;

        return candidates
            .GroupBy(GroupKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(item => item.UserData?.LastPlayedDate ?? DateTimeOffset.MinValue)
                .ThenByDescending(item => item.UserData?.PlaybackPositionTicks ?? 0)
                .First())
            .OrderByDescending(item => item.UserData?.LastPlayedDate ?? DateTimeOffset.MinValue)
            .Take(14)
            .ToArray();
    }

    public void SelectLibrary(LibrarySectionViewModel section)
    {
        foreach (var library in Libraries)
            library.IsSelected = ReferenceEquals(library, section);

        CurrentLibrary = section;
        _heroIndex = 0;
        ActivateHero(_heroIndex);
        RestartTimer();
    }

    public void SelectHero(MediaCardViewModel card)
    {
        if (CurrentLibrary is null)
            return;

        var index = CurrentLibrary.Items.IndexOf(card);
        if (index < 0)
            return;

        _heroIndex = index;
        ActivateHero(_heroIndex);
        RestartTimer();
    }

    public async Task ToggleFavoriteAsync()
    {
        if (CurrentHero is null)
            return;

        var target = !CurrentHero.IsFavorite;
        await _client.SetFavoriteAsync(CurrentHero.Item.Id, target);
        CurrentHero.IsFavorite = target;
    }

    public Task<PlaybackLaunch> PlayItemAsync(MediaCardViewModel item) =>
        _client.GetPlayableStreamAsync(item.Item);

    public Task<PlaybackLaunch> PlayCurrentAsync()
    {
        if (CurrentHero is null)
            throw new InvalidOperationException("No media item is selected.");

        return PlayItemAsync(CurrentHero);
    }

    private void AdvanceHero()
    {
        if (CurrentLibrary is null || CurrentLibrary.Items.Count <= 1)
            return;

        _heroIndex = (_heroIndex + 1) % CurrentLibrary.Items.Count;
        ActivateHero(_heroIndex);
    }

    private void ActivateHero(int index)
    {
        if (CurrentLibrary is null || CurrentLibrary.Items.Count == 0)
        {
            CurrentHero = null;
            return;
        }

        index = Math.Clamp(index, 0, CurrentLibrary.Items.Count - 1);
        for (var i = 0; i < CurrentLibrary.Items.Count; i++)
            CurrentLibrary.Items[i].IsActive = i == index;

        CurrentHero = CurrentLibrary.Items[index];
    }

    private void RestartTimer()
    {
        _heroTimer.Stop();
        _heroTimer.Start();
    }

    public void Dispose() => _heroTimer.Stop();

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
