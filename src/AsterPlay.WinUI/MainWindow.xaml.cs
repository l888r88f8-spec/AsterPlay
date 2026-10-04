using AsterPlay.Services;
using Microsoft.UI.Xaml;

namespace AsterPlay.WinUI;

public sealed partial class MainWindow : Window
{
    private readonly EmbyClient _client = new();

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        LoadSharedCoreState();
    }

    private void LoadSharedCoreState()
    {
        var session = AppStateStore.Load();
        var servers = ServerProfileStore.Load();

        if (session is not null)
        {
            _client.Restore(session);
            CoreStatusText.Text = $"已复用现有 AsterPlay.Core：检测到 {session.UserName} 的已保存会话。";
        }
        else
        {
            CoreStatusText.Text = "已复用现有 AsterPlay.Core：当前没有已保存的 Emby 会话。";
        }

        ServerStatusText.Text = servers.Count == 0
            ? "尚未保存服务器。"
            : $"已读取 {servers.Count} 个已保存 Emby 服务器。";
    }

    private void Home_Click(object sender, RoutedEventArgs e) =>
        CoreStatusText.Text = "首页壳层已就绪；下一步接入 HomeViewModel。";

    private void Library_Click(object sender, RoutedEventArgs e) =>
        CoreStatusText.Text = "媒体库壳层已就绪；下一步接入 LibraryViewModel。";

    private void Servers_Click(object sender, RoutedEventArgs e) =>
        CoreStatusText.Text = $"共享服务器存储正常：{ServerProfileStore.Load().Count} 个服务器。";

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var settings = AppSettingsStore.Load();
        CoreStatusText.Text = $"共享设置存储正常：播放器控制栏自动隐藏 {settings.PlayerControlsAutoHideSeconds} 秒。";
    }
}
