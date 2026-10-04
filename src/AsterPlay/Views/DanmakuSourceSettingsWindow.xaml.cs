using AsterPlay.Models.Danmaku;

namespace AsterPlay.Views;

public partial class DanmakuSourceSettingsWindow : Window
{
    private static readonly SourceOption[] Options =
    [
        new(DanmakuSourceKind.BuiltIn, "内置测试源"),
        new(DanmakuSourceKind.DandanPlay, "弹弹play开放弹幕网络"),
        new(DanmakuSourceKind.LogVar, "LogVar 自建源")
    ];

    public DanmakuSourceSettingsWindow(DanmakuSourceSettings settings)
    {
        InitializeComponent();

        SourceComboBox.ItemsSource = Options;
        SourceComboBox.DisplayMemberPath = nameof(SourceOption.Label);
        SourceComboBox.SelectedValuePath = nameof(SourceOption.Kind);
        SourceComboBox.SelectedValue = settings.SourceKind;

        LogVarUrlTextBox.Text = settings.LogVarBaseUrl;
        DandanPlayAppIdTextBox.Text = settings.DandanPlayAppId;
        DandanPlayAppSecretBox.Password = settings.DandanPlayAppSecret;
        DandanPlayRelatedCheckBox.IsChecked =
            settings.DandanPlayWithRelated;

        RefreshPanels();
    }

    public DanmakuSourceSettings? Result { get; private set; }

    private void SourceComboBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e) =>
        RefreshPanels();

    private void RefreshPanels()
    {
        var kind = SelectedKind;

        BuiltInPanel.Visibility = kind == DanmakuSourceKind.BuiltIn
            ? Visibility.Visible
            : Visibility.Collapsed;
        DandanPlayPanel.Visibility = kind == DanmakuSourceKind.DandanPlay
            ? Visibility.Visible
            : Visibility.Collapsed;
        LogVarPanel.Visibility = kind == DanmakuSourceKind.LogVar
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private DanmakuSourceKind SelectedKind =>
        SourceComboBox.SelectedValue is DanmakuSourceKind kind
            ? kind
            : DanmakuSourceKind.BuiltIn;

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ValidationTextBlock.Text = "";

        var logVarUrl = LogVarUrlTextBox.Text.Trim();
        var appId = DandanPlayAppIdTextBox.Text.Trim();
        var appSecret = DandanPlayAppSecretBox.Password;

        if (SelectedKind == DanmakuSourceKind.LogVar)
        {
            if (!Uri.TryCreate(logVarUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp &&
                 uri.Scheme != Uri.UriSchemeHttps))
            {
                ValidationTextBlock.Text =
                    "LogVar 地址必须是 http:// 或 https:// 开头的基础地址。";
                return;
            }
        }

        if (SelectedKind == DanmakuSourceKind.DandanPlay &&
            (string.IsNullOrWhiteSpace(appId) ||
             string.IsNullOrWhiteSpace(appSecret)))
        {
            ValidationTextBlock.Text =
                "弹弹play 数据源需要填写 AppId 和 AppSecret。";
            return;
        }

        Result = new DanmakuSourceSettings
        {
            SourceKind = SelectedKind,
            LogVarBaseUrl = logVarUrl.TrimEnd('/'),
            DandanPlayAppId = appId,
            DandanPlayAppSecret = appSecret,
            DandanPlayWithRelated =
                DandanPlayRelatedCheckBox.IsChecked == true
        };

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) =>
        DialogResult = false;

    private sealed record SourceOption(
        DanmakuSourceKind Kind,
        string Label);
}
