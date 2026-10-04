using AsterPlay.Models.Danmaku;

namespace AsterPlay.Views;

public partial class DanmakuFilterWindow : Window
{
    private readonly DanmakuSettings _settings;

    public DanmakuFilterWindow(DanmakuSettings settings)
    {
        InitializeComponent();

        _settings = settings;
        BlockedWordsTextBox.Text =
            string.Join(Environment.NewLine, settings.BlockedWords);
        BlockedUsersTextBox.Text =
            string.Join(Environment.NewLine, settings.BlockedUsers);

        RefreshCount();
        BlockedWordsTextBox.TextChanged += (_, _) => RefreshCount();
        BlockedUsersTextBox.TextChanged += (_, _) => RefreshCount();
    }

    public DanmakuSettings? Result { get; private set; }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Result = _settings with
        {
            BlockedWords = ParseLines(BlockedWordsTextBox.Text),
            BlockedUsers = ParseLines(BlockedUsersTextBox.Text)
        };

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) =>
        DialogResult = false;

    private void RefreshCount()
    {
        var words = ParseLines(BlockedWordsTextBox.Text);
        var users = ParseLines(BlockedUsersTextBox.Text);

        CountTextBlock.Text =
            $"屏蔽词 {words.Count} 条 · 发送者 {users.Count} 条";
    }

    private static List<string> ParseLines(string text) =>
        text
            .Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(200)
            .ToList();
}
