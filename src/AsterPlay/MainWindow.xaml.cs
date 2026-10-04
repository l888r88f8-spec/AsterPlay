using System.Windows.Media;
using AsterPlay.Models;
using AsterPlay.Services;
using AsterPlay.Views;

namespace AsterPlay;

public partial class MainWindow : Window
{
    private readonly EmbyClient _client = new();
    private string _activeSection = "home";
    private string _returnSection = "home";
    private bool _authenticated;

    public MainWindow()
    {
        InitializeComponent();
        FitToWorkArea();
        Loaded += OnLoaded;
        Closed += MainWindow_Closed;
        StateChanged += (_, _) => UpdateMaximizeButton();
        PlaybackNavigation.Requested += PlaybackNavigation_Requested;
    }

    private void MainWindow_Closed(object? sender, EventArgs e) =>
        PlaybackNavigation.Requested -= PlaybackNavigation_Requested;

    private void Minimize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void HomeNav_Click(object sender, RoutedEventArgs e) => ShowHome();
    private void LibraryNav_Click(object sender, RoutedEventArgs e) => ShowLibrary();
    private void ServersNav_Click(object sender, RoutedEventArgs e) => ShowServers(returnToLogin: false);
    private void SettingsNav_Click(object sender, RoutedEventArgs e) => ShowSettings();

    private void UpdateMaximizeButton()
    {
        if (MaximizeButton is not null)
            MaximizeButton.Content = WindowState == WindowState.Maximized ? "❐" : "□";
    }

    private void FitToWorkArea()
    {
        var workArea = SystemParameters.WorkArea;

        Width = Math.Min(Width, workArea.Width * 0.92);
        Height = Math.Min(Height, workArea.Height * 0.92);

        Left = workArea.Left + (workArea.Width - Width) / 2;
        Top = workArea.Top + (workArea.Height - Height) / 2;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        var settings = AppSettingsStore.Load();
        if (!settings.RestoreSessionOnStartup)
        {
            ShowLogin();
            return;
        }

        var session = AppStateStore.Load();
        if (session is not null)
        {
            try
            {
                _client.Restore(session);
                await _client.GetViewsAsync();
                ServerProfileStore.AddOrUpdate(session.ServerUrl);
                _authenticated = true;
                ShowHome();
                return;
            }
            catch (Exception ex) when (UserError.IsAuthenticationFailure(ex))
            {
                PlaybackLog.Error("SessionRestoreAuth", ex);
                AppStateStore.Clear();
                _client.Reset();
                ShowLogin("登录状态已失效，请重新登录。");
                return;
            }
            catch (Exception ex)
            {
                PlaybackLog.Error("SessionRestore", ex);
                ShowLogin(UserError.GetMessage(ex, "连接服务器"));
                return;
            }
        }

        ShowLogin();
    }

    private void ShowLogin(string? message = null)
    {
        _authenticated = false;
        SetNavigationVisible(false);
        PageTitleBlock.Text = "登录";

        var login = new LoginView(_client, message);
        login.LoginSucceeded += (_, _) =>
        {
            _authenticated = true;
            ShowHome();
        };
        login.ManageServersRequested += (_, _) => ShowServers(returnToLogin: true);

        RootContent.Content = login;
    }

    private void ShowHome()
    {
        _authenticated = true;
        _activeSection = "home";
        SetNavigationVisible(true);
        SetActiveNavigation("home");
        PageTitleBlock.Text = "首页";

        var home = new HomeView(_client);
        home.LibraryRequested += (_, _) => ShowLibrary();
        home.LogoutRequested += (_, _) =>
        {
            AppStateStore.Clear();
            _client.Reset();
            ShowLogin();
        };

        RootContent.Content = home;
    }

    private void ShowLibrary()
    {
        _authenticated = true;
        _activeSection = "library";
        SetNavigationVisible(true);
        SetActiveNavigation("library");
        PageTitleBlock.Text = "媒体库";
        RootContent.Content = new LibraryView(_client);
    }

    private void ShowServers(bool returnToLogin)
    {
        _activeSection = "servers";
        SetNavigationVisible(_authenticated && !returnToLogin);
        if (_authenticated && !returnToLogin)
            SetActiveNavigation("servers");

        PageTitleBlock.Text = "服务器";

        var view = new ServerManagementView();
        view.DoneRequested += (_, _) =>
        {
            if (returnToLogin || !_authenticated)
                ShowLogin();
            else
                ShowHome();
        };
        RootContent.Content = view;
    }

    private void ShowSettings()
    {
        _activeSection = "settings";
        SetNavigationVisible(true);
        SetActiveNavigation("settings");
        PageTitleBlock.Text = "设置";
        RootContent.Content = new SettingsView();
    }

    private void PlaybackNavigation_Requested(
        object? sender,
        PlaybackNavigationRequestedEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => PlaybackNavigation_Requested(sender, e));
            return;
        }

        if (_activeSection != "player")
            _returnSection = _activeSection;

        ShowPlayer(e.Client, e.Launch);
    }

    private void ShowPlayer(EmbyClient client, PlaybackLaunch launch)
    {
        _activeSection = "player";
        SetNavigationVisible(false);
        PageTitleBlock.Text = "";

        var player = new PlayerWindow(client, launch);
        player.BackRequested += (_, _) => RestoreSectionAfterPlayer();
        player.PlaybackReplacementRequested += replacement =>
            ShowPlayer(client, replacement);

        RootContent.Content = player;
        player.Focus();
    }

    private void RestoreSectionAfterPlayer()
    {
        switch (_returnSection)
        {
            case "library":
                ShowLibrary();
                break;
            case "servers":
                ShowServers(returnToLogin: false);
                break;
            case "settings":
                ShowSettings();
                break;
            default:
                ShowHome();
                break;
        }
    }

    private void SetNavigationVisible(bool visible)
    {
        BottomNavigation.Visibility = visible
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void SetActiveNavigation(string section)
    {
        var inactiveForeground = new SolidColorBrush(Color.FromRgb(0xB7, 0xBB, 0xC4));
        var activeForeground = Brushes.White;
        var activeBackground = new SolidColorBrush(Color.FromArgb(0x24, 0x0A, 0x84, 0xFF));

        foreach (var button in new[]
                 {
                     HomeNavButton,
                     LibraryNavButton,
                     ServersNavButton,
                     SettingsNavButton
                 })
        {
            button.Background = Brushes.Transparent;
            button.Foreground = inactiveForeground;
        }

        var active = section switch
        {
            "library" => LibraryNavButton,
            "servers" => ServersNavButton,
            "settings" => SettingsNavButton,
            _ => HomeNavButton
        };

        active.Background = activeBackground;
        active.Foreground = activeForeground;
    }
}
