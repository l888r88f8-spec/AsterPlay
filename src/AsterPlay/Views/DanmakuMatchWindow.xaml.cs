using AsterPlay.Models.Danmaku;
using AsterPlay.Services;
using AsterPlay.Services.Danmaku;

namespace AsterPlay.Views;

public partial class DanmakuMatchWindow : Window
{
    private readonly DanmakuService _service;
    private readonly DanmakuContext _context;
    private readonly DanmakuSourceSettings _sourceSettings;
    private readonly DanmakuMatchCandidate? _currentManualMatch;
    private CancellationTokenSource? _searchCts;

    public DanmakuMatchWindow(
        DanmakuService service,
        DanmakuContext context,
        DanmakuSourceSettings sourceSettings,
        DanmakuMatchCandidate? currentManualMatch)
    {
        InitializeComponent();

        _service = service;
        _context = context;
        _sourceSettings = sourceSettings;
        _currentManualMatch = currentManualMatch;

        CurrentMediaTextBlock.Text = FormatCurrentMedia(context);
        CurrentMatchTextBlock.Text = currentManualMatch is null
            ? "当前：自动匹配"
            : $"当前手动匹配：{currentManualMatch.DisplayTitle} · episodeId {currentManualMatch.EpisodeId}";

        SearchTextBox.Text =
            DanmakuApiSupport.BuildSearchSubject(context);
    }

    public DanmakuMatchCandidate? SelectedCandidate { get; private set; }

    public bool UseAutomaticMatch { get; private set; }

    private async void Window_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        await SearchAsync();
    }

    private async void Search_Click(
        object sender,
        RoutedEventArgs e)
    {
        await SearchAsync();
    }

    private async Task SearchAsync()
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();

        SearchButton.IsEnabled = false;
        CandidatesListBox.IsEnabled = false;
        StatusTextBlock.Text = "正在搜索 LogVar…";
        EmptyTextBlock.Visibility = Visibility.Collapsed;

        try
        {
            var candidates = await _service.SearchCandidatesAsync(
                _context,
                _sourceSettings,
                SearchTextBox.Text,
                _searchCts.Token);

            CandidatesListBox.ItemsSource = candidates;
            CandidatesListBox.IsEnabled = true;

            if (candidates.Count == 0)
            {
                EmptyTextBlock.Text = "没有找到候选";
                EmptyTextBlock.Visibility = Visibility.Visible;
                StatusTextBlock.Text = "没有找到匹配候选，可以换一个剧名或关键词再搜。";
                return;
            }

            if (_currentManualMatch is not null)
            {
                CandidatesListBox.SelectedItem = candidates.FirstOrDefault(
                    candidate =>
                        candidate.EpisodeId ==
                        _currentManualMatch.EpisodeId);
            }

            CandidatesListBox.SelectedIndex =
                CandidatesListBox.SelectedIndex >= 0
                    ? CandidatesListBox.SelectedIndex
                    : 0;

            StatusTextBlock.Text =
                $"找到 {candidates.Count} 个候选，按评分从高到低排列。";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            EmptyTextBlock.Text = "搜索失败";
            EmptyTextBlock.Visibility = Visibility.Visible;
            StatusTextBlock.Text = $"搜索失败：{ex.Message}";
            PlaybackLog.Error("DanmakuManualSearch", ex);
        }
        finally
        {
            SearchButton.IsEnabled = true;
        }
    }

    private void CandidatesListBox_MouseDoubleClick(
        object sender,
        System.Windows.Input.MouseButtonEventArgs e)
    {
        if (CandidatesListBox.SelectedItem is DanmakuMatchCandidate)
            AcceptSelected();
    }

    private void UseSelected_Click(
        object sender,
        RoutedEventArgs e) =>
        AcceptSelected();

    private void AcceptSelected()
    {
        if (CandidatesListBox.SelectedItem is not DanmakuMatchCandidate candidate)
        {
            StatusTextBlock.Text = "请先选择一个候选。";
            return;
        }

        SelectedCandidate = candidate;
        UseAutomaticMatch = false;
        DialogResult = true;
    }

    private void Automatic_Click(
        object sender,
        RoutedEventArgs e)
    {
        SelectedCandidate = null;
        UseAutomaticMatch = true;
        DialogResult = true;
    }

    private void Cancel_Click(
        object sender,
        RoutedEventArgs e) =>
        DialogResult = false;

    private void Window_Closed(
        object? sender,
        EventArgs e)
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = null;
    }

    private static string FormatCurrentMedia(
        DanmakuContext context)
    {
        if (DanmakuApiSupport.IsEpisode(context))
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
}
