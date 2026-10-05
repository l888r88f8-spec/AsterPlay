using AsterPlay.Models;
using AsterPlay.Services;
using AsterPlay.WinUI.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace AsterPlay.WinUI;

public sealed partial class MainWindow : Window
{
    private readonly EmbyClient _client = new();
    private bool _initialized;
    private bool _authenticated;
    private string _currentSection = "login";

    public MainWindow()
    {
        StartupDiagnostics.Write("MainWindow constructor: entered");
        InitializeComponent();
        StartupDiagnostics.Write("MainWindow constructor: after InitializeComponent");

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        StartupDiagnostics.Write("MainWindow constructor: title bar ready");
    }

    private async void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        if (_initialized)
            return;

        _initialized = true;
        await InitializeSessionAsync();
    }

    private async Task InitializeSessionAsync()
    {
        var settings = AppSettingsStore.Load();
        if (!settings.RestoreSessionOnStartup)
        {
            ShowLogin();
            return;
        }

        var session = AppStateStore.Load();
        if (session is null)
        {
            ShowLogin();
            return;
        }

        try
        {
            _client.Restore(session);
            await _client.GetViewsAsync();
            ServerProfileStore.AddOrUpdate(session.ServerUrl);
            _authenticated = true;
            ShowHome();
        }
        catch (Exception ex) when (UserError.IsAuthenticationFailure(ex))
        {
            PlaybackLog.Error("WinUISessionRestoreAuth", ex);
            AppStateStore.Clear();
            _client.Reset();
            ShowLogin("登录状态已失效，请重新登录。");
        }
        catch (Exception ex)
        {
            PlaybackLog.Error("WinUISessionRestore", ex);
            _client.Reset();
            ShowLogin(UserError.GetMessage(ex, "连接服务器"));
        }
    }

    private void ShowLogin(string? message = null)
    {
        _authenticated = false;
        _currentSection = "login";
        NavigationDock.Visibility = Visibility.Collapsed;
        PageTitleBlock.Text = "登录";

        var view = new LoginView(_client, message);
        view.LoginSucceeded += (_, _) =>
        {
            _authenticated = true;
            ShowHome();
        };
        view.ManageServersRequested += (_, _) => ShowServers(returnToLogin: true);

        PageHost.Content = view;
    }

    private void ShowHome()
    {
        if (!_client.IsAuthenticated)
        {
            ShowLogin();
            return;
        }

        _authenticated = true;
        _currentSection = "home";
        NavigationDock.Visibility = Visibility.Visible;
        PageTitleBlock.Text = "首页";
        SetActiveNavigation(HomeButton);

        var view = new HomeView(_client);
        view.LibraryRequested += (_, _) => ShowLibrary();
        view.MediaRequested += async (_, item) => await StartPlaybackAsync(item, "home");
        PageHost.Content = view;
    }

    private void ShowLibrary()
    {
        if (!_client.IsAuthenticated)
        {
            ShowLogin();
            return;
        }

        _currentSection = "library";
        NavigationDock.Visibility = Visibility.Visible;
        PageTitleBlock.Text = "媒体库";
        SetActiveNavigation(LibraryButton);

        var view = new LibraryView(_client);
        view.MediaRequested += async (_, item) => await StartPlaybackAsync(item, "library");
        PageHost.Content = view;
    }

    private void ShowServers(bool returnToLogin)
    {
        _currentSection = "servers";
        NavigationDock.Visibility = _authenticated && !returnToLogin
            ? Visibility.Visible
            : Visibility.Collapsed;
        PageTitleBlock.Text = "服务器";

        if (_authenticated && !returnToLogin)
            SetActiveNavigation(ServersButton);

        var view = new ServerManagementView();
        view.DoneRequested += (_, _) =>
        {
            if (returnToLogin || !_authenticated)
                ShowLogin();
            else
                ShowHome();
        };

        PageHost.Content = view;
    }

    private void ShowSettings()
    {
        if (!_authenticated)
        {
            ShowLogin();
            return;
        }

        _currentSection = "settings";
        NavigationDock.Visibility = Visibility.Visible;
        PageTitleBlock.Text = "设置";
        SetActiveNavigation(SettingsButton);
        PageHost.Content = new SettingsView();
    }

    private async Task StartPlaybackAsync(EmbyItem item, string returnSection)
    {
        if (!_client.IsAuthenticated)
        {
            ShowLogin();
            return;
        }

        try
        {
            PageTitleBlock.Text = "正在准备播放…";
            NavigationDock.Visibility = Visibility.Collapsed;

            var launch = await _client.GetPlayableStreamAsync(item);
            var player = new PlayerPocView(_client, launch);
            player.BackRequested += (_, _) =>
            {
                if (string.Equals(returnSection, "library", StringComparison.Ordinal))
                    ShowLibrary();
                else
                    ShowHome();
            };

            _currentSection = "player";
            PageTitleBlock.Text = "播放器 POC";
            PageHost.Content = player;
        }
        catch (Exception ex)
        {
            PlaybackLog.Error("WinUIPlaybackLaunch", ex);

            var dialog = new ContentDialog
            {
                XamlRoot = RootGrid.XamlRoot,
                Title = "无法播放",
                Content = UserError.GetMessage(ex, "播放"),
                CloseButtonText = "关闭",
                DefaultButton = ContentDialogButton.Close
            };

            await dialog.ShowAsync();

            if (string.Equals(returnSection, "library", StringComparison.Ordinal))
                ShowLibrary();
            else
                ShowHome();
        }
    }

    private void Home_Click(object sender, RoutedEventArgs e) => ShowHome();
    private void Library_Click(object sender, RoutedEventArgs e) => ShowLibrary();
    private void Servers_Click(object sender, RoutedEventArgs e) => ShowServers(returnToLogin: false);
    private void Settings_Click(object sender, RoutedEventArgs e) => ShowSettings();

    private void SetActiveNavigation(Button active)
    {
        var inactive = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
        var selected = new SolidColorBrush(Windows.UI.Color.FromArgb(58, 80, 145, 214));

        foreach (var button in new[] { HomeButton, LibraryButton, ServersButton, SettingsButton })
        {
            button.Background = ReferenceEquals(button, active) ? selected : inactive;
            button.Foreground = new SolidColorBrush(
                ReferenceEquals(button, active)
                    ? Windows.UI.Color.FromArgb(255, 255, 255, 255)
                    : Windows.UI.Color.FromArgb(255, 199, 206, 216));
        }
    }
}
