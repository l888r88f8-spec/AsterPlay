using AsterPlay.Models.Danmaku;
using AsterPlay.Services.Danmaku;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AsterPlay.WinUI.Views;

public sealed partial class DanmakuSourceSettingsDialog : ContentDialog
{
    private readonly DanmakuService _service = new();
    private CancellationTokenSource? _testCts;

    public DanmakuSourceSettingsDialog(
        DanmakuSourceSettings settings)
    {
        InitializeComponent();

        LogVarUrlTextBox.Text = settings.LogVarBaseUrl;
        LogVarAccessTokenBox.Password =
            settings.LogVarAccessToken;
    }

    public DanmakuSourceSettings? Result { get; private set; }

    private async void TestConnection_Click(
        object sender,
        RoutedEventArgs e)
    {
        ValidationTextBlock.Text = "";

        if (!TryBuildSettings(out var settings))
            return;

        _testCts?.Cancel();
        _testCts?.Dispose();
        _testCts = new CancellationTokenSource(
            TimeSpan.FromSeconds(15));

        TestConnectionButton.IsEnabled = false;
        ValidationTextBlock.Text = "正在测试 LogVar 连接…";

        try
        {
            await _service.TestConnectionAsync(
                settings,
                _testCts.Token);

            ValidationTextBlock.Text =
                string.IsNullOrWhiteSpace(
                    settings.LogVarAccessToken)
                    ? "连接成功（未配置 Access Token）"
                    : "连接成功（Access Token 可用）";
        }
        catch (OperationCanceledException)
        {
            ValidationTextBlock.Text = "连接测试超时。";
        }
        catch (Exception ex)
        {
            ValidationTextBlock.Text = ex.Message;
        }
        finally
        {
            TestConnectionButton.IsEnabled = true;
        }
    }

    private void Save_Click(
        ContentDialog sender,
        ContentDialogButtonClickEventArgs args)
    {
        ValidationTextBlock.Text = "";

        if (!TryBuildSettings(out var settings))
        {
            args.Cancel = true;
            return;
        }

        Result = settings;
    }

    private bool TryBuildSettings(
        out DanmakuSourceSettings settings)
    {
        var url = LogVarUrlTextBox.Text.Trim();
        var token =
            LogVarAccessTokenBox.Password.Trim();

        if (!Uri.TryCreate(
                url,
                UriKind.Absolute,
                out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp &&
             uri.Scheme != Uri.UriSchemeHttps))
        {
            ValidationTextBlock.Text =
                "服务器地址必须是 http:// 或 https:// 开头的有效地址。";

            settings = new DanmakuSourceSettings();
            return false;
        }

        settings = new DanmakuSourceSettings
        {
            LogVarBaseUrl = url.TrimEnd('/'),
            LogVarAccessToken = token
        };

        return true;
    }

    private void Dialog_Closed(
        ContentDialog sender,
        ContentDialogClosedEventArgs args)
    {
        _testCts?.Cancel();
        _testCts?.Dispose();
        _testCts = null;
    }
}
