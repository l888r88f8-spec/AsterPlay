using AsterPlay.Models;
using AsterPlay.Services;
using AsterPlay.WinUI.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Windowing;
using Windows.UI.ViewManagement;

namespace AsterPlay.WinUI;

public sealed partial class MainWindow : Window
{
    private readonly EmbyClient _client = new();
    private readonly UISettings _uiSettings = new();
    private readonly AppWindow _appWindow;
    private bool _initialized;
    private bool _authenticated;
    private string _currentSection = "login";

    public MainWindow()
    {
        StartupDiagnostics.Write("MainWindow constructor: entered");
        using (StartupDiagnostics.Measure("MainWindow.InitializeComponent"))
            InitializeComponent();
        StartupDiagnostics.Write("MainWindow constructor: after InitializeComponent");

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        _uiSettings.ColorValuesChanged += SystemColorValuesChanged;
        Closed += MainWindow_Closed;
        ApplySystemTheme();

        using (StartupDiagnostics.Measure("PrepareInitialContent"))
            PrepareInitialContent();

        StartupDiagnostics.Write("MainWindow constructor: title bar and initial content ready");
    }

    private void SystemColorValuesChanged(UISettings sender, object args)
    {
        DispatcherQueue.TryEnqueue(ApplySystemTheme);
    }

    private void ApplySystemTheme()
    {
        var background = _uiSettings.GetColorValue(UIColorType.Background);
        var luminance =
            (0.2126 * background.R) +
            (0.7152 * background.G) +
            (0.0722 * background.B);

        RootGrid.RequestedTheme = luminance >= 128
            ? ElementTheme.Light
            : ElementTheme.Dark;

        StartupDiagnostics.Write($"System theme applied: {RootGrid.RequestedTheme}");
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        _uiSettings.ColorValuesChanged -= SystemColorValuesChanged;
    }

    private void CompleteStartup()
    {
        StartupDiagnostics.Write(
            $"CompleteStartup requested; startupLayer={StartupLayer.Visibility}, section={_currentSection}");

        if (StartupLayer.Visibility != Visibility.Visible)
            return;

        StartupLayer.IsHitTestVisible = false;
        StartupLayer.Visibility = Visibility.Collapsed;
        StartupDiagnostics.Write("Startup layer dismissed");
    }

    private void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        StartupDiagnostics.Write(
            $"RootGrid.Loaded; initialized={_initialized}, section={_currentSection}");
        _initialized = true;
    }

    private void PrepareInitialContent()
    {
        if (_initialized)
            return;

        StartupDiagnostics.Write("PrepareInitialContent: loading settings");
        var settings = AppSettingsStore.Load();
        if (!settings.RestoreSessionOnStartup)
        {
            StartupDiagnostics.Write("PrepareInitialContent: restore disabled -> login");
            ShowLogin();
            _initialized = true;
            return;
        }

        StartupDiagnostics.Write("PrepareInitialContent: loading session");
        var session = AppStateStore.Load();
        StartupDiagnostics.Write(
            $"PrepareInitialContent: session={(session is null ? "miss" : "hit")}");

        if (session is null)
        {
            ShowLogin();
            _initialized = true;
            return;
        }

        try
        {
            StartupDiagnostics.Write("PrepareInitialContent: restoring client");
            _client.Restore(session);
            ServerProfileStore.AddOrUpdate(session.ServerUrl);
            _authenticated = true;

            using (StartupDiagnostics.Measure("PrepareInitialContent.ShowHome"))
                ShowHome();

            _initialized = true;
        }
        catch (Exception ex)
        {
            PlaybackLog.Error("WinUISessionRestore", ex);
            StartupDiagnostics.WriteException("PrepareInitialContent", ex);
            _client.Reset();
            ShowLogin(UserError.GetMessage(ex, "恢复登录"));
            _initialized = true;
        }
    }

    private void ShowLogin(string? message = null)
    {
        ExitPlayerChrome();
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
        CompleteStartup();
    }

    private void ShowHome()
    {
        ExitPlayerChrome();
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
        view.MediaRequested += (_, item) => ShowDetails(item, "home");
        view.PlayRequested += async (_, item) =>
            await StartPlaybackAsync(item, "home");
        view.AuthenticationFailed += (_, _) =>
        {
            PlaybackLog.Write(
                "WinUISessionRestoreAuth",
                "Cached home session was rejected by the server.");
            AppStateStore.Clear();
            _client.Reset();
            _authenticated = false;
            ShowLogin("登录状态已失效，请重新登录。");
        };
        view.InitialContentReady += (_, _) => CompleteStartup();

        StartupDiagnostics.Write($"ShowHome: cachedSnapshot={view.HasCachedSnapshot}");
        PageHost.Content = view;
        StartupDiagnostics.Write("ShowHome: PageHost.Content assigned");

        if (view.HasCachedSnapshot)
            CompleteStartup();
    }

    private void ShowLibrary()
    {
        ExitPlayerChrome();
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
        view.MediaRequested += (_, item) => ShowDetails(item, "library");
        PageHost.Content = view;
    }

    private void ShowServers(bool returnToLogin)
    {
        ExitPlayerChrome();
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
        ExitPlayerChrome();
        if (!_authenticated)
        {
            ShowLogin();
            return;
        }

        _currentSection = "settings";
        NavigationDock.Visibility = Visibility.Visible;
        PageTitleBlock.Text = "设置";
        SetActiveNavigation(SettingsButton);

        var view = new SettingsView();
        view.LogoutRequested += (_, _) =>
        {
            AppStateStore.Clear();
            _client.Reset();
            _authenticated = false;
            ShowLogin();
        };

        PageHost.Content = view;
    }

    private void ShowDetails(EmbyItem item, string returnSection)
    {
        ExitPlayerChrome();

        if (!_client.IsAuthenticated)
        {
            ShowLogin();
            return;
        }

        _currentSection = "details";
        NavigationDock.Visibility = Visibility.Collapsed;
        PageTitleBlock.Text = "详情";

        var view = new DetailsView(_client, item);
        view.BackRequested += (_, _) =>
        {
            if (string.Equals(returnSection, "library", StringComparison.Ordinal))
                ShowLibrary();
            else
                ShowHome();
        };
        view.PlaybackRequested += async (_, args) =>
            await StartPlaybackAsync(args.Item, returnSection, args.Restart);

        PageHost.Content = view;
    }

    private async Task StartPlaybackAsync(
        EmbyItem item,
        string returnSection,
        bool restart = false)
    {
        if (!_client.IsAuthenticated)
        {
            ShowLogin();
            return;
        }

        try
        {
            PageTitleBlock.Text = "正在准备播放…";
            EnterPlayerChrome();

            var launch = await _client.GetPlayableStreamAsync(item, restart);
            var player = new PlayerView(_client, launch);
            player.BackRequested += (_, _) =>
            {
                if (string.Equals(returnSection, "library", StringComparison.Ordinal))
                    ShowLibrary();
                else
                    ShowHome();
            };

            player.EpisodeRequested += async (_, episode) =>
                await StartPlaybackAsync(episode, returnSection);
            player.FullscreenRequested += (_, fullscreen) =>
                SetPlayerFullscreen(fullscreen);

            _currentSection = "player";
            PageTitleBlock.Text = "播放器";
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

    private void SetPlayerFullscreen(bool fullscreen)
    {
        try
        {
            _appWindow.SetPresenter(
                fullscreen
                    ? AppWindowPresenterKind.FullScreen
                    : AppWindowPresenterKind.Default);
        }
        catch (Exception ex)
        {
            PlaybackLog.Error("WinUIFullscreen", ex);
        }
    }

    private void EnterPlayerChrome()
    {
        NavigationDock.Visibility = Visibility.Collapsed;
        AppTitleBar.Visibility = Visibility.Collapsed;
        TitleBarRow.Height = new GridLength(0);

        Grid.SetRow(ContentLayer, 0);
        Grid.SetRowSpan(ContentLayer, 2);
    }

    private void ExitPlayerChrome()
    {
        if (_appWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen)
            SetPlayerFullscreen(false);

        Grid.SetRow(ContentLayer, 1);
        Grid.SetRowSpan(ContentLayer, 1);

        TitleBarRow.Height = new GridLength(48);
        AppTitleBar.Visibility = Visibility.Visible;
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
            button.Background = ReferenceEquals(button, active) ? selected : inactive;
    }
}
