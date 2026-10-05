using AsterPlay.Models.Danmaku;
using Microsoft.UI.Xaml.Controls;

namespace AsterPlay.WinUI.Views;

public sealed partial class DanmakuFilterDialog : ContentDialog
{
    private readonly DanmakuSettings _settings;

    public DanmakuFilterDialog(DanmakuSettings settings)
    {
        InitializeComponent();

        _settings = settings;
        BlockedWordsTextBox.Text =
            string.Join(Environment.NewLine, settings.BlockedWords);
        BlockedUsersTextBox.Text =
            string.Join(Environment.NewLine, settings.BlockedUsers);
        RefreshCount();
    }

    public DanmakuSettings? Result { get; private set; }

    private void FilterTextChanged(
        object sender,
        TextChangedEventArgs e) =>
        RefreshCount();

    private void Save_Click(
        ContentDialog sender,
        ContentDialogButtonClickEventArgs args)
    {
        Result = _settings with
        {
            BlockedWords = ParseLines(BlockedWordsTextBox.Text),
            BlockedUsers = ParseLines(BlockedUsersTextBox.Text)
        };
    }

    private void RefreshCount()
    {
        var words = ParseLines(BlockedWordsTextBox.Text);
        var users = ParseLines(BlockedUsersTextBox.Text);

        CountTextBlock.Text =
            $"屏蔽词 {words.Count} 条 · 发送者 {users.Count} 条";
    }

    private static List<string> ParseLines(string text) =>
        text.Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(200)
            .ToList();
}
