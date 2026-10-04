using AsterPlay.Models.Danmaku;

namespace AsterPlay.Views;

public partial class DanmakuSourceSettingsWindow : Window
{
    public DanmakuSourceSettingsWindow(
        DanmakuSourceSettings settings)
    {
        InitializeComponent();

        LogVarUrlTextBox.Text = settings.LogVarBaseUrl;
        LogVarAccessTokenBox.Password =
            settings.LogVarAccessToken;
    }

    public DanmakuSourceSettings? Result { get; private set; }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ValidationTextBlock.Text = "";

        var url = LogVarUrlTextBox.Text.Trim();
        var token = LogVarAccessTokenBox.Password.Trim();

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp &&
             uri.Scheme != Uri.UriSchemeHttps))
        {
            ValidationTextBlock.Text =
                "服务器地址必须是 http:// 或 https:// 开头的有效地址。";
            return;
        }

        Result = new DanmakuSourceSettings
        {
            LogVarBaseUrl = url.TrimEnd('/'),
            LogVarAccessToken = token
        };

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) =>
        DialogResult = false;
}
