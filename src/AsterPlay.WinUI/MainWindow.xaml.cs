using System.Runtime.InteropServices;
using AsterPlay.Models;
using AsterPlay.Services;
using AsterPlay.WinUI.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Windowing;
using Windows.UI.ViewManagement;

namespace AsterPlay.WinUI;

public sealed partial class MainWindow : Window
{
    private EmbyClient _client = null!;
    private UISettings? _uiSettings;
    private readonly AppWindow _appWindow;
    private bool _startupResolutionScheduled;
    private bool _startupResolutionCompleted;
    private bool _splashRevealScheduled;
    private bool _splashTransitionStarted;
    private bool _authenticated;
    private string _currentSection = "home-shell";
    private DateTimeOffset _lastWheelDiagnosticAt = DateTimeOffset.MinValue;
    private readonly IntPtr _hwnd;
    private readonly HookProc _lowLevelMouseHookProc;
    private IntPtr _lowLevelMouseHook;
    private DateTimeOffset _lastNativeWheelDiagnosticAt = DateTimeOffset.MinValue;

    private const uint WmMouseWheel = 0x020A;
    private const int WhMouseLl = 14;
    private const int DwmwaExtendedFrameBounds = 9;

    public MainWindow()
    {
        StartupDiagnostics.Write("MainWindow constructor: entered");
        using (StartupDiagnostics.Measure("MainWindow.InitializeComponent"))
            InitializeComponent();
        StartupDiagnostics.Write("MainWindow constructor: after InitializeComponent");

        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _lowLevelMouseHookProc = LowLevelMouseHook;

        _lowLevelMouseHook = SetWindowsHookEx(
            WhMouseLl,
            _lowLevelMouseHookProc,
            GetModuleHandle(null),
            0);

        if (_lowLevelMouseHook == IntPtr.Zero)
        {
            PlaybackLog.Write(
                "WinUINativeWheel",
                $"WH_MOUSE_LL install failed: {Marshal.GetLastWin32Error()}");
        }
        else
        {
            PlaybackLog.Write(
                "WinUINativeWheel",
                "WH_MOUSE_LL home wheel hook installed.");
        }

        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        ConfigureNativeTitleBar(isLight: true);

        Closed += MainWindow_Closed;
        RootGrid.AddHandler(
            UIElement.PointerWheelChangedEvent,
            new PointerEventHandler(RootGrid_PointerWheelChangedDiagnostic),
            handledEventsToo: true);
        PageTitleBlock.Text = "首页";
        SetActiveNavigation(HomeButton);

        StartupDiagnostics.Write("MainWindow constructor: lightweight home shell ready");
    }

    private IntPtr LowLevelMouseHook(
        int code,
        IntPtr wParam,
        IntPtr lParam)
    {
        if (code >= 0 &&
            unchecked((uint)wParam.ToInt64()) == WmMouseWheel &&
            GetForegroundWindow() == _hwnd &&
            PageHost.Content is HomeView homeView)
        {
            var input = Marshal.PtrToStructure<LowLevelMouseInput>(lParam);
            var delta = unchecked((short)((input.MouseData >> 16) & 0xffff));

            var frameResult = DwmGetWindowAttribute(
                _hwnd,
                DwmwaExtendedFrameBounds,
                out var frameBounds,
                Marshal.SizeOf<NativeRect>());

            var insideWindow =
                frameResult == 0 &&
                input.Point.X >= frameBounds.Left &&
                input.Point.X < frameBounds.Right &&
                input.Point.Y >= frameBounds.Top &&
                input.Point.Y < frameBounds.Bottom;

            if (delta != 0 && insideWindow)
            {
                // DWMWA_EXTENDED_FRAME_BOUNDS is expressed in real screen
                // coordinates and is not DPI-virtualized, so it can be
                // compared directly with MSLLHOOKSTRUCT.pt.
                homeView.HandleNativeMouseWheel(delta);

                var now = DateTimeOffset.UtcNow;
                if (now - _lastNativeWheelDiagnosticAt >= TimeSpan.FromMilliseconds(500))
                {
                    _lastNativeWheelDiagnosticAt = now;
                    PlaybackLog.Write(
                        "WinUINativeWheel",
                        $"low-level-wheel: delta={delta}, screen={input.Point.X},{input.Point.Y}, " +
                        $"frame={frameBounds.Left},{frameBounds.Top}," +
                        $"{frameBounds.Right - frameBounds.Left}x{frameBounds.Bottom - frameBounds.Top}");
                }

                return (IntPtr)1;
            }

            var missNow = DateTimeOffset.UtcNow;
            if (delta != 0 &&
                missNow - _lastNativeWheelDiagnosticAt >= TimeSpan.FromMilliseconds(500))
            {
                _lastNativeWheelDiagnosticAt = missNow;
                PlaybackLog.Write(
                    "WinUINativeWheel",
                    $"wheel-outside-frame: delta={delta}, screen={input.Point.X},{input.Point.Y}, " +
                    $"hr=0x{frameResult:X8}, frame={frameBounds.Left},{frameBounds.Top}," +
                    $"{frameBounds.Right - frameBounds.Left}x{frameBounds.Bottom - frameBounds.Top}");
            }
        }

        return CallNextHookEx(
            _lowLevelMouseHook,
            code,
            wParam,
            lParam);
    }

    private void RootGrid_PointerWheelChangedDiagnostic(
        object sender,
        PointerRoutedEventArgs e)
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastWheelDiagnosticAt < TimeSpan.FromMilliseconds(180))
            return;

        _lastWheelDiagnosticAt = now;
        var point = e.GetCurrentPoint(RootGrid);

        PlaybackLog.Write(
            "WinUIInput",
            $"wheel: delta={point.Properties.MouseWheelDelta}, handled={e.Handled}, " +
            $"source={e.OriginalSource?.GetType().Name ?? "-"}, " +
            $"section={_currentSection}, page={PageHost.Content?.GetType().Name ?? "-"}, " +
            $"splashVisibility={SplashLayer.Visibility}, splashHitTest={SplashLayer.IsHitTestVisible}, " +
            $"contentOpacity={ContentLayer.Opacity:0.00}, contentHitTest={ContentLayer.IsHitTestVisible}");
    }

    private void SystemColorValuesChanged(UISettings sender, object args)
    {
        DispatcherQueue.TryEnqueue(ApplySystemTheme);
    }

    private void ApplySystemTheme()
    {
        if (_uiSettings is null)
            return;

        var background = _uiSettings.GetColorValue(UIColorType.Background);
        var luminance =
            (0.2126 * background.R) +
            (0.7152 * background.G) +
            (0.0722 * background.B);

        var isLight = luminance >= 128;
        RootGrid.RequestedTheme = isLight
            ? ElementTheme.Light
            : ElementTheme.Dark;

        ConfigureNativeTitleBar(isLight);

        StartupDiagnostics.Write($"System theme applied: {RootGrid.RequestedTheme}");
    }

    private void ConfigureNativeTitleBar(bool isLight)
    {
        var transparent = Windows.UI.Color.FromArgb(0, 0, 0, 0);
        var foreground = isLight
            ? Windows.UI.Color.FromArgb(255, 24, 28, 36)
            : Windows.UI.Color.FromArgb(255, 245, 247, 250);
        var hover = isLight
            ? Windows.UI.Color.FromArgb(24, 0, 0, 0)
            : Windows.UI.Color.FromArgb(28, 255, 255, 255);
        var pressed = isLight
            ? Windows.UI.Color.FromArgb(42, 0, 0, 0)
            : Windows.UI.Color.FromArgb(46, 255, 255, 255);

        var titleBar = _appWindow.TitleBar;
        titleBar.BackgroundColor = transparent;
        titleBar.InactiveBackgroundColor = transparent;
        titleBar.ButtonBackgroundColor = transparent;
        titleBar.ButtonInactiveBackgroundColor = transparent;
        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonInactiveForegroundColor = foreground;
        titleBar.ButtonHoverBackgroundColor = hover;
        titleBar.ButtonHoverForegroundColor = foreground;
        titleBar.ButtonPressedBackgroundColor = pressed;
        titleBar.ButtonPressedForegroundColor = foreground;
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        if (_lowLevelMouseHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_lowLevelMouseHook);
            _lowLevelMouseHook = IntPtr.Zero;
        }

        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= FirstFrame_Rendering;
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= SplashReveal_Rendering;

        if (_uiSettings is not null)
            _uiSettings.ColorValuesChanged -= SystemColorValuesChanged;
    }

    private void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        StartupDiagnostics.Write(
            $"RootGrid.Loaded; startupScheduled={_startupResolutionScheduled}, section={_currentSection}");

        if (_startupResolutionScheduled)
            return;

        _startupResolutionScheduled = true;

        // Wait for the first composition frame before touching disk, DPAPI,
        // UISettings, EmbyClient, or page XAML. This guarantees the user sees
        // the actual home shell first.
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += FirstFrame_Rendering;
    }

    private void FirstFrame_Rendering(object? sender, object e)
    {
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= FirstFrame_Rendering;
        StartupDiagnostics.Write("First home shell frame rendered");

        DispatcherQueue.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            async () => await ResolveStartupStateAsync());
    }

    private async Task ResolveStartupStateAsync()
    {
        using var timing = StartupDiagnostics.Measure("ResolveStartupState");

        try
        {
            EnsureDeferredServices();

            var state = await Task.Run(() =>
            {
                var servers = ServerProfileStore.Load();
                if (servers.Count == 0)
                    return new StartupState(0, RestoreSession: false, Session: null);

                var settings = AppSettingsStore.Load();
                if (!settings.RestoreSessionOnStartup)
                    return new StartupState(servers.Count, RestoreSession: false, Session: null);

                return new StartupState(
                    servers.Count,
                    RestoreSession: true,
                    Session: AppStateStore.Load());
            });

            StartupDiagnostics.Write(
                $"ResolveStartupState: servers={state.ServerCount}, restore={state.RestoreSession}, session={(state.Session is null ? "miss" : "hit")}");

            if (state.ServerCount == 0)
            {
                ShowNoServerHome();
                return;
            }

            if (!state.RestoreSession || state.Session is null)
            {
                ShowLogin();
                return;
            }

            _client.Restore(state.Session);
            ServerProfileStore.AddOrUpdate(state.Session.ServerUrl);
            _authenticated = true;
            ShowHome();
        }
        catch (Exception ex)
        {
            PlaybackLog.Error("WinUISessionRestore", ex);
            StartupDiagnostics.WriteException("ResolveStartupState", ex);

            EnsureDeferredServices();
            _client.Reset();
            ShowLogin(UserError.GetMessage(ex, "恢复登录"));
        }
        finally
        {
            _startupResolutionCompleted = true;
        }
    }

    private void EnsureDeferredServices()
    {
        if (_uiSettings is null)
        {
            _uiSettings = new UISettings();
            _uiSettings.ColorValuesChanged += SystemColorValuesChanged;
            ApplySystemTheme();
            StartupDiagnostics.Write("Deferred UISettings initialized");
        }

        if (_client is null)
        {
            _client = new EmbyClient();
            StartupDiagnostics.Write("Deferred EmbyClient initialized");
        }
    }

    private void ScheduleSplashReveal()
    {
        if (_splashTransitionStarted || _splashRevealScheduled)
            return;

        _splashRevealScheduled = true;
        StartupDiagnostics.Write("Splash reveal scheduled after target page frame");
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += SplashReveal_Rendering;
    }

    private void SplashReveal_Rendering(object? sender, object e)
    {
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= SplashReveal_Rendering;
        _splashRevealScheduled = false;
        StartSplashTransition();
    }

    private void StartSplashTransition()
    {
        if (_splashTransitionStarted)
            return;

        _splashTransitionStarted = true;

        var width = Math.Max(1, RootGrid.ActualWidth);
        var height = Math.Max(1, RootGrid.ActualHeight);
        var diagonal = Math.Sqrt((width * width) + (height * height));
        var targetScale = Math.Max(14.0, diagonal / 104.0 * 1.08);

        static CubicEase Ease() => new()
        {
            EasingMode = EasingMode.EaseInOut
        };

        var storyboard = new Storyboard();

        var scaleX = new DoubleAnimation
        {
            From = 1,
            To = targetScale,
            Duration = TimeSpan.FromMilliseconds(1780),
            EasingFunction = Ease()
        };
        Storyboard.SetTarget(scaleX, SplashCircleScale);
        Storyboard.SetTargetProperty(scaleX, "ScaleX");
        storyboard.Children.Add(scaleX);

        var scaleY = new DoubleAnimation
        {
            From = 1,
            To = targetScale,
            Duration = TimeSpan.FromMilliseconds(1780),
            EasingFunction = Ease()
        };
        Storyboard.SetTarget(scaleY, SplashCircleScale);
        Storyboard.SetTargetProperty(scaleY, "ScaleY");
        storyboard.Children.Add(scaleY);

        var markFade = new DoubleAnimation
        {
            From = 1,
            To = 0,
            BeginTime = TimeSpan.FromMilliseconds(260),
            Duration = TimeSpan.FromMilliseconds(820),
            EasingFunction = Ease()
        };
        Storyboard.SetTarget(markFade, SplashMark);
        Storyboard.SetTargetProperty(markFade, "Opacity");
        storyboard.Children.Add(markFade);

        var blurVeilFade = new DoubleAnimation
        {
            From = 0,
            To = 0.92,
            BeginTime = TimeSpan.FromMilliseconds(220),
            Duration = TimeSpan.FromMilliseconds(980),
            EasingFunction = Ease()
        };
        Storyboard.SetTarget(blurVeilFade, SplashBlurVeil);
        Storyboard.SetTargetProperty(blurVeilFade, "Opacity");
        storyboard.Children.Add(blurVeilFade);

        var chromeFade = new DoubleAnimation
        {
            From = 0,
            To = 1,
            BeginTime = TimeSpan.FromMilliseconds(360),
            Duration = TimeSpan.FromMilliseconds(1120),
            EasingFunction = Ease()
        };
        Storyboard.SetTarget(chromeFade, AppTitleBar);
        Storyboard.SetTargetProperty(chromeFade, "Opacity");
        storyboard.Children.Add(chromeFade);

        var contentFade = new DoubleAnimation
        {
            From = 0,
            To = 1,
            BeginTime = TimeSpan.FromMilliseconds(340),
            Duration = TimeSpan.FromMilliseconds(1160),
            EasingFunction = Ease()
        };
        Storyboard.SetTarget(contentFade, ContentLayer);
        Storyboard.SetTargetProperty(contentFade, "Opacity");
        storyboard.Children.Add(contentFade);

        var shadeFade = new DoubleAnimation
        {
            From = 1,
            To = 0,
            BeginTime = TimeSpan.FromMilliseconds(430),
            Duration = TimeSpan.FromMilliseconds(1160),
            EasingFunction = Ease()
        };
        Storyboard.SetTarget(shadeFade, SplashBackdropShade);
        Storyboard.SetTargetProperty(shadeFade, "Opacity");
        storyboard.Children.Add(shadeFade);

        var splashFade = new DoubleAnimation
        {
            From = 1,
            To = 0,
            BeginTime = TimeSpan.FromMilliseconds(1320),
            Duration = TimeSpan.FromMilliseconds(460),
            EasingFunction = Ease()
        };
        Storyboard.SetTarget(splashFade, SplashLayer);
        Storyboard.SetTargetProperty(splashFade, "Opacity");
        storyboard.Children.Add(splashFade);

        storyboard.Completed += (_, _) =>
        {
            AppTitleBar.Opacity = 1;
            ContentLayer.Opacity = 1;
            SplashLayer.IsHitTestVisible = false;
            SplashLayer.Visibility = Visibility.Collapsed;
            StartupDiagnostics.Write("Splash transition completed");
        };

        StartupDiagnostics.Write(
            $"Splash transition started; targetScale={targetScale:0.0}");
        storyboard.Begin();
    }

    private sealed record StartupState(
        int ServerCount,
        bool RestoreSession,
        EmbySession? Session);

    private void ShowLogin(
        string? message = null,
        string? preferredServerUrl = null)
    {
        ExitPlayerChrome();
        _authenticated = false;
        _currentSection = "login";
        NavigationDock.Visibility = Visibility.Collapsed;
        PageTitleBlock.Text = "登录";

        var view = new LoginView(_client, message, preferredServerUrl);
        view.LoginSucceeded += (_, _) =>
        {
            _authenticated = true;
            ShowHome();
        };
        view.ManageServersRequested += (_, _) => ShowServers(returnToLogin: true);

        PageHost.Content = view;
        ScheduleSplashReveal();
    }

    private void ShowNoServerHome()
    {
        ExitPlayerChrome();
        EnterHomeChrome();

        _authenticated = false;
        _currentSection = "home-empty";
        NavigationDock.Visibility = Visibility.Visible;
        PageTitleBlock.Text = "首页";
        SetActiveNavigation(HomeButton);

        var view = new HomeView(_client, noServerMode: true);
        view.ServerRequested += (_, _) =>
            ShowServers(returnToLogin: false, returnToNoServerHome: true);

        PageHost.Content = view;
        StartupDiagnostics.Write("ShowNoServerHome: empty home shell assigned");
        ScheduleSplashReveal();
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
        EnterHomeChrome();
        _currentSection = "home";
        NavigationDock.Visibility = Visibility.Visible;
        PageTitleBlock.Text = "首页";
        SetActiveNavigation(HomeButton);

        var view = new HomeView(_client);
        view.LibraryRequested += (_, _) => ShowLibrary();
        view.ServerRequested += (_, _) =>
            ShowServers(returnToLogin: false);
        view.ServerSwitchRequested += (_, profile) =>
            SwitchServer(profile);
        view.SearchRequested += (_, _) =>
            ShowSearch();
        view.MediaRequested += (_, item) => ShowDetails(item, "home");
        view.PlayRequested += async (_, item) =>
            await StartPlaybackAsync(item, "home");
        view.RestartRequested += async (_, item) =>
            await StartPlaybackAsync(item, "home", restart: true);
        view.AuthenticationFailed += (_, _) =>
        {
            PlaybackLog.Write(
                "WinUISessionRestoreAuth",
                "Home refresh session was rejected by the server.");
            AppStateStore.Clear();
            _client.Reset();
            _authenticated = false;
            ShowLogin("登录状态已失效，请重新登录。");
        };
        view.InitialVisualReady += (_, _) =>
        {
            StartupDiagnostics.Write("ShowHome: cache decision completed; revealing app");
            ScheduleSplashReveal();
        };

        // HomeView construction is now intentionally data-free. Once the shell
        // is attached to PageHost there is nothing left to wait for before
        // showing the window. Cache hydration and Emby refresh happen after the
        // HomeView Loaded event.
        PageHost.Content = view;
        StartupDiagnostics.Write("ShowHome: home shell assigned to PageHost");
    }

    private void ShowLibrary(bool focusSearch = false)
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

        if (focusSearch)
            view.FocusSearch();
    }

    private void ShowSearch()
    {
        ExitPlayerChrome();
        if (!_client.IsAuthenticated)
        {
            ShowLogin();
            return;
        }

        _currentSection = "search";
        NavigationDock.Visibility = Visibility.Visible;
        PageTitleBlock.Text = "搜索";
        SetActiveNavigation(LibraryButton);

        var view = new SearchView(_client);
        view.MediaRequested += (_, item) => ShowDetails(item, "search");
        PageHost.Content = view;
    }

    private void SwitchServer(ServerProfile profile)
    {
        var current = (_client.ServerUrl ?? "").Trim().TrimEnd('/');
        var target = (profile.Url ?? "").Trim().TrimEnd('/');

        if (string.Equals(current, target, StringComparison.OrdinalIgnoreCase))
            return;

        AppStateStore.Clear();
        _client.Reset();
        _authenticated = false;
        ShowLogin(preferredServerUrl: profile.Url);
    }

    private void ShowServers(
        bool returnToLogin,
        bool returnToNoServerHome = false)
    {
        ExitPlayerChrome();
        _currentSection = "servers";
        NavigationDock.Visibility =
            (_authenticated && !returnToLogin) || returnToNoServerHome
                ? Visibility.Visible
                : Visibility.Collapsed;
        PageTitleBlock.Text = "服务器";

        if (NavigationDock.Visibility == Visibility.Visible)
            SetActiveNavigation(ServersButton);

        var view = new ServerManagementView();
        view.DoneRequested += (_, _) =>
        {
            var serverCount = ServerProfileStore.Load().Count;
            if (serverCount == 0)
            {
                AppStateStore.Clear();
                _client.Reset();
                _authenticated = false;
                ShowNoServerHome();
                return;
            }

            if (returnToNoServerHome)
            {
                ShowLogin();
                return;
            }

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
        view.BackRequested += (_, _) => NavigateBackFrom(returnSection);
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
            player.BackRequested += (_, _) => NavigateBackFrom(returnSection);

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

            NavigateBackFrom(returnSection);
        }
    }

    private void NavigateBackFrom(string returnSection)
    {
        if (string.Equals(returnSection, "library", StringComparison.Ordinal))
        {
            ShowLibrary();
            return;
        }

        if (string.Equals(returnSection, "search", StringComparison.Ordinal))
        {
            ShowSearch();
            return;
        }

        ShowHome();
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

    private void EnterHomeChrome()
    {
        Grid.SetRow(ContentLayer, 0);
        Grid.SetRowSpan(ContentLayer, 2);

        TitleBarRow.Height = new GridLength(48);
        AppTitleBar.Visibility = Visibility.Visible;
        AppTitleBar.Background = new SolidColorBrush(
            Windows.UI.Color.FromArgb(0, 0, 0, 0));
        TitleBrandPanel.Visibility = Visibility.Collapsed;
        PageTitleBlock.Visibility = Visibility.Collapsed;

        // The native caption buttons stay available above the Hero.
        ConfigureNativeTitleBar(isLight: false);
    }

    private void RestoreStandardChrome()
    {
        Grid.SetRow(ContentLayer, 1);
        Grid.SetRowSpan(ContentLayer, 1);

        TitleBarRow.Height = new GridLength(48);
        AppTitleBar.Visibility = Visibility.Visible;
        TitleBrandPanel.Visibility = Visibility.Visible;
        PageTitleBlock.Visibility = Visibility.Visible;

        var isLight = RootGrid.RequestedTheme != ElementTheme.Dark;
        AppTitleBar.Background = new SolidColorBrush(
            isLight
                ? Windows.UI.Color.FromArgb(255, 244, 246, 249)
                : Windows.UI.Color.FromArgb(255, 13, 17, 24));
        ConfigureNativeTitleBar(isLight);
    }

    private void EnterPlayerChrome()
    {
        NavigationDock.Visibility = Visibility.Collapsed;
        TitleBrandPanel.Visibility = Visibility.Visible;
        PageTitleBlock.Visibility = Visibility.Visible;
        AppTitleBar.Visibility = Visibility.Collapsed;
        TitleBarRow.Height = new GridLength(0);

        Grid.SetRow(ContentLayer, 0);
        Grid.SetRowSpan(ContentLayer, 2);
    }

    private void ExitPlayerChrome()
    {
        if (_appWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen)
            SetPlayerFullscreen(false);

        RestoreStandardChrome();
    }

    private void Home_Click(object sender, RoutedEventArgs e)
    {
        if (!_startupResolutionCompleted)
            return;
        if (!_authenticated && ServerProfileStore.Load().Count == 0)
            ShowNoServerHome();
        else
            ShowHome();
    }

    private void Library_Click(object sender, RoutedEventArgs e)
    {
        if (!_startupResolutionCompleted)
            return;

        ShowLibrary();
    }

    private void Servers_Click(object sender, RoutedEventArgs e)
    {
        if (!_startupResolutionCompleted)
            return;
        var noServers = ServerProfileStore.Load().Count == 0;
        ShowServers(
            returnToLogin: !_authenticated && !noServers,
            returnToNoServerHome: noServers);
    }
    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (!_startupResolutionCompleted)
            return;

        ShowSettings();
    }

    private void SetActiveNavigation(Button active)
    {
        // The dock floats over artwork and other highly variable backgrounds.
        // Keep its labels bright and stable instead of inheriting page-theme
        // text colors, while the dark glass tint supplies the contrast surface.
        var inactiveBackground =
            new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
        var selectedBackground =
            new SolidColorBrush(Windows.UI.Color.FromArgb(92, 58, 121, 216));
        var inactiveForeground =
            new SolidColorBrush(Windows.UI.Color.FromArgb(242, 255, 255, 255));
        var selectedForeground =
            new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255));

        foreach (var button in new[] { HomeButton, LibraryButton, ServersButton, SettingsButton })
        {
            var selected = ReferenceEquals(button, active);
            button.Background = selected
                ? selectedBackground
                : inactiveBackground;
            button.Foreground = selected
                ? selectedForeground
                : inactiveForeground;
        }
    }


    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LowLevelMouseInput
    {
        public NativePoint Point;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    private delegate IntPtr HookProc(
        int code,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(
        int hookId,
        HookProc callback,
        IntPtr module,
        uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(
        IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(
        IntPtr hook,
        int code,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        IntPtr hWnd,
        int attribute,
        out NativeRect value,
        int size);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();


}
