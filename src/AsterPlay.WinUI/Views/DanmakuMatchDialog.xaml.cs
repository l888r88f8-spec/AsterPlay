using AsterPlay.Models.Danmaku;
using AsterPlay.Services;
using AsterPlay.Services.Danmaku;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System.Text.RegularExpressions;

namespace AsterPlay.WinUI.Views;

public sealed partial class DanmakuMatchDialog : ContentDialog
{
    private readonly DanmakuService _service;
    private readonly DanmakuContext _context;
    private readonly DanmakuSourceSettings _sourceSettings;
    private readonly DanmakuSeriesMatchBinding? _currentBinding;
    private CancellationTokenSource? _searchCts;

    public DanmakuMatchDialog(
        DanmakuService service,
        DanmakuContext context,
        DanmakuSourceSettings sourceSettings,
        DanmakuSeriesMatchBinding? currentBinding)
    {
        InitializeComponent();

        _service = service;
        _context = context;
        _sourceSettings = sourceSettings;
        _currentBinding = currentBinding;

        CurrentMediaTextBlock.Text = FormatCurrentMedia(context);
        CurrentMatchTextBlock.Text =
            FormatCurrentBinding(currentBinding);
        SearchTextBox.Text =
            BuildSearchSubject(context);
    }

    public DanmakuSeriesMatchCandidate? SelectedSeries { get; private set; }

    public DanmakuMatchCandidate? SelectedEpisode { get; private set; }

    public bool UseAutomaticMatch { get; private set; }

    private async void Dialog_Loaded(
        object sender,
        RoutedEventArgs e) =>
        await SearchAsync();

    private async void Search_Click(
        object sender,
        RoutedEventArgs e) =>
        await SearchAsync();

    private async Task SearchAsync()
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();

        SearchButton.IsEnabled = false;
        SeriesListBox.IsEnabled = false;
        EpisodesListBox.IsEnabled = false;
        StatusTextBlock.Text = "正在搜索 LogVar 剧集…";
        SeriesEmptyTextBlock.Visibility = Visibility.Collapsed;
        EpisodesEmptyTextBlock.Visibility = Visibility.Visible;
        SeriesListBox.ItemsSource = null;
        EpisodesListBox.ItemsSource = null;

        try
        {
            var series = await _service.SearchSeriesCandidatesAsync(
                _context,
                _sourceSettings,
                SearchTextBox.Text,
                _searchCts.Token);

            SeriesListBox.ItemsSource = series;
            SeriesListBox.IsEnabled = true;

            if (series.Count == 0)
            {
                SeriesEmptyTextBlock.Text = "没有找到剧集";
                SeriesEmptyTextBlock.Visibility = Visibility.Visible;
                StatusTextBlock.Text =
                    "没有找到匹配剧集，可以换一个剧名或关键词再搜。";
                return;
            }

            var selectedSeries =
                FindBoundSeries(series) ?? series.First();

            SeriesListBox.SelectedItem = selectedSeries;
            SeriesListBox.ScrollIntoView(selectedSeries);

            StatusTextBlock.Text =
                $"找到 {series.Count} 个剧集候选。先选剧集，再确认当前集。";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            SeriesEmptyTextBlock.Text = "搜索失败";
            SeriesEmptyTextBlock.Visibility = Visibility.Visible;
            StatusTextBlock.Text = $"搜索失败：{ex.Message}";
            PlaybackLog.Error("WinUIDanmakuManualSeriesSearch", ex);
        }
        finally
        {
            SearchButton.IsEnabled = true;
        }
    }

    private DanmakuSeriesMatchCandidate? FindBoundSeries(
        IReadOnlyList<DanmakuSeriesMatchCandidate> series)
    {
        if (_currentBinding is null)
            return null;

        if (_currentBinding.AnimeId > 0)
        {
            var byId = series.FirstOrDefault(
                item => item.AnimeId == _currentBinding.AnimeId);
            if (byId is not null)
                return byId;
        }

        return series.FirstOrDefault(
            item =>
                string.Equals(
                    item.AnimeTitle,
                    _currentBinding.AnimeTitle,
                    StringComparison.OrdinalIgnoreCase) &&
                (_currentBinding.LogVarSeasonNumber <= 0 ||
                 item.SeasonNumber <= 0 ||
                 item.SeasonNumber ==
                 _currentBinding.LogVarSeasonNumber));
    }

    private void SeriesListBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (SeriesListBox.SelectedItem is not DanmakuSeriesMatchCandidate series)
        {
            EpisodesListBox.ItemsSource = null;
            EpisodesEmptyTextBlock.Visibility = Visibility.Visible;
            return;
        }

        EpisodesListBox.ItemsSource = series.Episodes;
        EpisodesListBox.IsEnabled = true;
        EpisodesEmptyTextBlock.Visibility =
            series.Episodes.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;

        if (series.Episodes.Count == 0)
            return;

        var targetEpisodeNumber =
            _context.EpisodeNumber.GetValueOrDefault(0);

        if (_currentBinding is not null &&
            IsBoundSeries(series) &&
            targetEpisodeNumber > 0)
        {
            targetEpisodeNumber +=
                _currentBinding.EpisodeOffset;
        }

        var selectedEpisode =
            targetEpisodeNumber > 0
                ? series.Episodes.FirstOrDefault(
                    item =>
                        item.EpisodeNumber ==
                        targetEpisodeNumber)
                : null;

        selectedEpisode ??= series.Episodes.First();

        EpisodesListBox.SelectedItem = selectedEpisode;
        EpisodesListBox.ScrollIntoView(selectedEpisode);

        StatusTextBlock.Text =
            targetEpisodeNumber > 0
                ? $"当前 Emby 集数 E{_context.EpisodeNumber.GetValueOrDefault():00}；已定位 LogVar E{selectedEpisode.EpisodeNumber:00}。"
                : "请选择当前媒体对应的 LogVar 集数。";
    }

    private bool IsBoundSeries(
        DanmakuSeriesMatchCandidate series)
    {
        if (_currentBinding is null)
            return false;

        if (_currentBinding.AnimeId > 0 &&
            series.AnimeId == _currentBinding.AnimeId)
        {
            return true;
        }

        return string.Equals(
            series.AnimeTitle,
            _currentBinding.AnimeTitle,
            StringComparison.OrdinalIgnoreCase);
    }

    private void EpisodesListBox_DoubleTapped(
        object sender,
        DoubleTappedRoutedEventArgs e)
    {
        if (TryAcceptSelected())
            Hide();
    }

    private void UseSelected_Click(
        ContentDialog sender,
        ContentDialogButtonClickEventArgs args)
    {
        if (!TryAcceptSelected())
            args.Cancel = true;
    }

    private bool TryAcceptSelected()
    {
        if (SeriesListBox.SelectedItem is not DanmakuSeriesMatchCandidate series)
        {
            StatusTextBlock.Text = "请先选择一个剧集。";
            return false;
        }

        if (EpisodesListBox.SelectedItem is not DanmakuMatchCandidate episode)
        {
            StatusTextBlock.Text = "请选择当前视频对应的集数。";
            return false;
        }

        SelectedSeries = series;
        SelectedEpisode = episode;
        UseAutomaticMatch = false;
        return true;
    }

    private void Automatic_Click(
        ContentDialog sender,
        ContentDialogButtonClickEventArgs args)
    {
        SelectedSeries = null;
        SelectedEpisode = null;
        UseAutomaticMatch = true;
    }

    private void Dialog_Closed(
        ContentDialog sender,
        ContentDialogClosedEventArgs args)
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = null;
    }

    private static bool IsEpisode(DanmakuContext context) =>
        string.Equals(
            context.ItemType,
            "Episode",
            StringComparison.OrdinalIgnoreCase) ||
        (!string.IsNullOrWhiteSpace(context.SeriesName) &&
         context.EpisodeNumber is > 0);

    private static string BuildSearchSubject(
        DanmakuContext context)
    {
        if (IsEpisode(context) &&
            !string.IsNullOrWhiteSpace(context.SeriesName))
        {
            var subject = context.SeriesName.Trim();
            if (context.SeasonNumber is > 1 &&
                ExtractSeasonNumber(subject) <= 0)
            {
                subject += $" S{context.SeasonNumber.Value:00}";
            }

            return subject;
        }

        if (!string.IsNullOrWhiteSpace(context.OriginalTitle))
            return context.OriginalTitle.Trim();

        return context.Title.Trim();
    }

    private static int ExtractSeasonNumber(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        var patterns = new[]
        {
            @"(?:^|[^A-Za-z0-9])S\s*0*(\d{1,2})(?:[^A-Za-z0-9]|$)",
            @"第\s*0*(\d{1,2})\s*[季部期]",
            @"(?:Season\s*|)(\d{1,2})(?:st|nd|rd|th)?\s*(?:Season|期)"
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(
                text,
                pattern,
                RegexOptions.IgnoreCase);

            if (match.Success &&
                int.TryParse(match.Groups[1].Value, out var value))
            {
                return value;
            }
        }

        return 0;
    }

    private static string FormatCurrentMedia(
        DanmakuContext context)
    {
        if (IsEpisode(context))
        {
            var series = string.IsNullOrWhiteSpace(context.SeriesName)
                ? context.Title
                : context.SeriesName;
            var season = context.SeasonNumber is > 0
                ? $"S{context.SeasonNumber.Value:00}"
                : "";
            var episode = context.EpisodeNumber is > 0
                ? $"E{context.EpisodeNumber.Value:00}"
                : "";

            return $"当前媒体：{series} {season}{episode}".Trim();
        }

        return $"当前媒体：{context.Title}";
    }

    private static string FormatCurrentBinding(
        DanmakuSeriesMatchBinding? binding)
    {
        if (binding is null)
            return "当前：自动匹配";

        var offset = binding.EpisodeOffset == 0
            ? "集数一一对应"
            : binding.EpisodeOffset > 0
                ? $"LogVar 集数 +{binding.EpisodeOffset}"
                : $"LogVar 集数 {binding.EpisodeOffset}";

        return $"当前剧集绑定：{binding.AnimeTitle} · {offset}";
    }
}
