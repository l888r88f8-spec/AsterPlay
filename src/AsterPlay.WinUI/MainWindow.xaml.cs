using System.Numerics;
using System.Runtime.InteropServices;
using AsterPlay.Models;
using AsterPlay.Services;
using AsterPlay.WinUI.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Windowing;
using Windows.UI.ViewManagement;
using Windows.Graphics;

namespace AsterPlay.WinUI;

public sealed partial class MainWindow : Window
{
    private EmbyClient _client = null!;
    private UISettings? _uiSettings;
    private readonly AppWindow _appWindow;
    private bool _startupResolutionScheduled;
    private bool _startupResolutionCompleted;
    private bool _startupRevealScheduled;
    private bool _startupVisualReadyRaised;
    private bool _startupCoverLogoReady;
    private bool _startupFirstFrameObserved;
    private bool _startupNativeReleaseScheduled;
    private bool _nativeStartupPaintActive = true;
    private readonly SubclassProc _startupSubclassProc;
    private IntPtr _nativeStartupBrush;
    private IntPtr _nativeStartupPng;
    private UIntPtr _gdiplusToken;
    private bool _startupSubclassInstalled;
    private RectInt32 _startupTargetBounds;
    private bool _authenticated;
    private string _currentSection = "home-shell";
    private readonly IntPtr _hwnd;
    private readonly HookProc _lowLevelMouseHookProc;
    private IntPtr _lowLevelMouseHook;

    private const uint WmMouseWheel = 0x020A;
    private const int WhMouseLl = 14;
    private const int DwmwaExtendedFrameBounds = 9;
    private const uint WmPaint = 0x000F;
    private const uint WmEraseBkgnd = 0x0014;
    private const uint WmSetCursor = 0x0020;
    private static readonly IntPtr IdcArrow = new(32512);


    public MainWindow()
    {
        StartupDiagnostics.Write("MainWindow constructor: entered");
        using (StartupDiagnostics.Measure("MainWindow.InitializeComponent"))
            InitializeComponent();
        StartupDiagnostics.Write("MainWindow constructor: after InitializeComponent");

        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

        // Window.Activate() can expose the native HWND before WinUI has produced
        // its first XAML/DirectComposition frame. Paint that short gap natively
        // on the SAME top-level window so Windows never gets a chance to show
        // its default white client surface.
        _startupSubclassProc = StartupWindowSubclassProc;
        InitializeNativeStartupSurface();

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

        try
        {
            var iconPath = Path.Combine(
                AppContext.BaseDirectory,
                "Assets",
                "AsterPlay.ico");
            if (File.Exists(iconPath))
                _appWindow.SetIcon(iconPath);
        }
        catch (Exception ex)
        {
            StartupDiagnostics.WriteException("SetWindowIcon", ex);
        }

        PrepareStartupWindow();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        ConfigureNativeTitleBar(isLight: true);

        Closed += MainWindow_Closed;
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
                return (IntPtr)1;
            }

        }

        return CallNextHookEx(
            _lowLevelMouseHook,
            code,
            wParam,
            lParam);
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
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= StartupNativeRelease_Rendering;
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= StartupReveal_Rendering;

        ReleaseNativeStartupSurface();

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
        // UISettings, EmbyClient, or page XAML. The native painter covers this
        // short gap on the same HWND until the XAML startup cover is confirmed.
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += FirstFrame_Rendering;
    }

    private void FirstFrame_Rendering(object? sender, object e)
    {
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= FirstFrame_Rendering;
        _startupFirstFrameObserved = true;

        StartupDiagnostics.Write(
            $"First MainWindow XAML frame composed; logoReady={_startupCoverLogoReady}, nativePainter={_nativeStartupPaintActive}");

        TryScheduleNativeStartupRelease();

        DispatcherQueue.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            async () => await ResolveStartupStateAsync());
    }

    private void StartupCoverLogoImage_ImageOpened(
        object sender,
        RoutedEventArgs e)
    {
        _startupCoverLogoReady = true;
        StartupDiagnostics.Write(
            $"Startup cover PNG decoded; firstFrameObserved={_startupFirstFrameObserved}");

        TryScheduleNativeStartupRelease();
    }

    private void StartupCoverLogoImage_ImageFailed(
        object sender,
        ExceptionRoutedEventArgs e)
    {
        // A broken logo must never leave the application permanently invisible.
        _startupCoverLogoReady = true;
        StartupDiagnostics.Write(
            $"Startup cover PNG decode failed; continuing with background only: {e.ErrorMessage}");

        TryScheduleNativeStartupRelease();
    }

    private void TryScheduleNativeStartupRelease()
    {
        if (!_nativeStartupPaintActive ||
            !_startupFirstFrameObserved ||
            !_startupCoverLogoReady ||
            _startupNativeReleaseScheduled)
        {
            return;
        }

        _startupNativeReleaseScheduled = true;
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering +=
            StartupNativeRelease_Rendering;

        StartupDiagnostics.Write(
            "XAML startup cover ready; waiting one frame before releasing native startup painter");
    }

    private void StartupNativeRelease_Rendering(object? sender, object e)
    {
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -=
            StartupNativeRelease_Rendering;
        _startupNativeReleaseScheduled = false;

        var flushBefore = DwmFlush();
        StartupDiagnostics.Write(
            $"XAML startup cover composed; pre-native-release DwmFlush={flushBefore}");

        ReleaseNativeStartupSurface();

        // Do not force a synchronous WM_PAINT/RedrawWindow here. The XAML
        // startup cover has already been composed; forcing a native repaint at
        // this exact handoff creates the visible hitch users perceive as a
        // dropped frame.
        var flushAfter = DwmFlush();
        StartupDiagnostics.Write(
            $"Native startup painter released; XAML cover owns MainWindow; DwmFlush={flushAfter}");
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
            ServerProfileStore.EnsureExists(state.Session.ServerUrl);
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

    internal event EventHandler? StartupVisualReady;

    private void PrepareStartupWindow()
    {
        try
        {
            var windowId =
                Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_hwnd);
            var workArea = DisplayArea.GetFromWindowId(
                    windowId,
                    DisplayAreaFallback.Primary)
                .WorkArea;

            var currentSize = _appWindow.Size;
            var width = currentSize.Width >= 640
                ? currentSize.Width
                : 1280;
            var height = currentSize.Height >= 420
                ? currentSize.Height
                : 800;

            width = Math.Min(
                width,
                Math.Max(640, workArea.Width - 80));
            height = Math.Min(
                height,
                Math.Max(420, workArea.Height - 80));

            _startupTargetBounds = new RectInt32(
                workArea.X + ((workArea.Width - width) / 2),
                workArea.Y + ((workArea.Height - height) / 2),
                width,
                height);

            // Keep MainWindow at its final real screen coordinates from the
            // very beginning. Its own startup cover is already part of the
            // XAML tree before Activate(), so HomeView can measure, arrange and
            // compose at the exact DPI/monitor/position used after reveal.
            _appWindow.MoveAndResize(_startupTargetBounds);

            StartupDiagnostics.Write(
                $"MainWindow prepared at final bounds with startup cover; target=" +
                $"{_startupTargetBounds.X},{_startupTargetBounds.Y}," +
                $"{_startupTargetBounds.Width}x{_startupTargetBounds.Height}");
        }
        catch (Exception ex)
        {
            StartupDiagnostics.WriteException(
                "PrepareStartupWindow",
                ex);
        }
    }

    internal async Task DismissStartupCoverAsync()
    {
        if (StartupCoverLayer.Visibility != Visibility.Visible)
            return;

        // HomeView and its first viewport are already ready at this point.
        // Give it two real composition frames underneath the cover, then fade
        // only the cover on the compositor thread. This avoids a layout pass
        // during the transition and keeps the reveal smooth.
        await WaitForRenderingFramesAsync(2);

        var readyDwmResult = DwmFlush();
        StartupDiagnostics.Write(
            $"DismissStartupCoverAsync: home ready under cover; DwmFlush={readyDwmResult}");

        StartupCoverLayer.IsHitTestVisible = false;

        var visual =
            Microsoft.UI.Xaml.Hosting.ElementCompositionPreview
                .GetElementVisual(StartupCoverLayer);
        var compositor = visual.Compositor;

        var easing = compositor.CreateCubicBezierEasingFunction(
            new Vector2(0.22f, 1.0f),
            new Vector2(0.36f, 1.0f));

        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0.0f, 1.0f);
        fade.InsertKeyFrame(1.0f, 0.0f, easing);
        fade.Duration = TimeSpan.FromMilliseconds(320);

        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var batch = compositor.CreateScopedBatch(
            Microsoft.UI.Composition.CompositionBatchTypes.Animation);
        batch.Completed += (_, _) => completion.TrySetResult(true);

        visual.StartAnimation("Opacity", fade);
        batch.End();

        StartupDiagnostics.Write(
            "DismissStartupCoverAsync: 320 ms compositor fade started");

        await completion.Task;

        visual.StopAnimation("Opacity");
        StartupCoverLayer.Opacity = 0;
        StartupCoverLayer.Visibility = Visibility.Collapsed;

        var homeDwmResult = DwmFlush();
        StartupDiagnostics.Write(
            $"DismissStartupCoverAsync: fade completed; home owns window; DwmFlush={homeDwmResult}");

        DispatcherQueue.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () => StartupDiagnostics.Write(
                "DismissStartupCoverAsync: post-reveal dispatcher responsive"));
    }

    private void InitializeNativeStartupSurface()
    {
        var isLight =
            Application.Current.RequestedTheme == ApplicationTheme.Light;
        var background = isLight
            ? Windows.UI.Color.FromArgb(255, 244, 246, 249)
            : Windows.UI.Color.FromArgb(255, 8, 10, 15);

        _nativeStartupBrush = CreateSolidBrush(
            ToColorRef(background.R, background.G, background.B));

        try
        {
            var gdiplusInput = new GdiplusStartupInput
            {
                GdiplusVersion = 1
            };

            var startupStatus = GdiplusStartup(
                out _gdiplusToken,
                ref gdiplusInput,
                IntPtr.Zero);

            if (startupStatus == 0)
            {
                var pngPath = Path.Combine(
                    AppContext.BaseDirectory,
                    "Assets",
                    "AsterPlay.AppIcon.png");

                if (File.Exists(pngPath))
                {
                    var loadStatus = GdipLoadImageFromFile(
                        pngPath,
                        out _nativeStartupPng);

                    if (loadStatus != 0)
                        _nativeStartupPng = IntPtr.Zero;
                }
            }
        }
        catch (Exception ex)
        {
            StartupDiagnostics.WriteException(
                "InitializeNativeStartupSurface PNG",
                ex);
        }

        _startupSubclassInstalled = SetWindowSubclass(
            _hwnd,
            _startupSubclassProc,
            UIntPtr.Zero,
            UIntPtr.Zero);

        StartupDiagnostics.Write(
            $"Native startup painter installed={_startupSubclassInstalled}; " +
            $"brush={_nativeStartupBrush != IntPtr.Zero}, png={_nativeStartupPng != IntPtr.Zero}");
    }

    private IntPtr StartupWindowSubclassProc(
        IntPtr hWnd,
        uint message,
        IntPtr wParam,
        IntPtr lParam,
        UIntPtr subclassId,
        UIntPtr referenceData)
    {
        if (!_nativeStartupPaintActive)
        {
            return DefSubclassProc(
                hWnd,
                message,
                wParam,
                lParam);
        }

        switch (message)
        {
            case WmEraseBkgnd:
                PaintNativeStartupSurface(wParam);
                return (IntPtr)1;

            case WmPaint:
            {
                // Let WinUI/the default window procedure process WM_PAINT first
                // so DirectComposition/XAML initialization is never blocked.
                var result = DefSubclassProc(
                    hWnd,
                    message,
                    wParam,
                    lParam);

                var hdc = GetDC(hWnd);
                if (hdc != IntPtr.Zero)
                {
                    try
                    {
                        PaintNativeStartupSurface(hdc);
                    }
                    finally
                    {
                        ReleaseDC(
                            hWnd,
                            hdc);
                    }
                }

                return result;
            }

            case WmSetCursor:
            {
                var arrow = LoadCursor(
                    IntPtr.Zero,
                    IdcArrow);
                if (arrow != IntPtr.Zero)
                    SetCursor(arrow);

                return (IntPtr)1;
            }
        }

        return DefSubclassProc(
            hWnd,
            message,
            wParam,
            lParam);
    }

    private void PaintNativeStartupSurface(IntPtr hdc)
    {
        if (hdc == IntPtr.Zero)
            return;

        if (GetClientRect(
                _hwnd,
                out var clientRect) &&
            _nativeStartupBrush != IntPtr.Zero)
        {
            FillRect(
                hdc,
                ref clientRect,
                _nativeStartupBrush);
        }

        if (_nativeStartupPng == IntPtr.Zero)
            return;

        var width = clientRect.Right - clientRect.Left;
        var height = clientRect.Bottom - clientRect.Top;
        const int iconSize = 144;
        var x = Math.Max(0, (width - iconSize) / 2);
        var y = Math.Max(0, (height - iconSize) / 2);

        if (GdipCreateFromHDC(
                hdc,
                out var graphics) != 0 ||
            graphics == IntPtr.Zero)
        {
            return;
        }

        try
        {
            // High-quality bicubic interpolation matches the XAML PNG much
            // more closely than scaling an ICO with DrawIconEx.
            GdipSetInterpolationMode(
                graphics,
                InterpolationModeHighQualityBicubic);

            GdipDrawImageRectI(
                graphics,
                _nativeStartupPng,
                x,
                y,
                iconSize,
                iconSize);
        }
        finally
        {
            GdipDeleteGraphics(graphics);
        }
    }

    private void ReleaseNativeStartupSurface()
    {
        if (!_nativeStartupPaintActive &&
            !_startupSubclassInstalled &&
            _nativeStartupBrush == IntPtr.Zero &&
            _nativeStartupPng == IntPtr.Zero &&
            _gdiplusToken == UIntPtr.Zero)
        {
            return;
        }

        _nativeStartupPaintActive = false;

        if (_startupSubclassInstalled)
        {
            RemoveWindowSubclass(
                _hwnd,
                _startupSubclassProc,
                UIntPtr.Zero);
            _startupSubclassInstalled = false;
        }

        if (_nativeStartupBrush != IntPtr.Zero)
        {
            DeleteObject(_nativeStartupBrush);
            _nativeStartupBrush = IntPtr.Zero;
        }

        if (_nativeStartupPng != IntPtr.Zero)
        {
            GdipDisposeImage(_nativeStartupPng);
            _nativeStartupPng = IntPtr.Zero;
        }

        if (_gdiplusToken != UIntPtr.Zero)
        {
            GdiplusShutdown(_gdiplusToken);
            _gdiplusToken = UIntPtr.Zero;
        }

        StartupDiagnostics.Write(
            "Native startup painter released");
    }

    private static uint ToColorRef(
        byte red,
        byte green,
        byte blue) =>
        (uint)(red | (green << 8) | (blue << 16));

    internal void ForceDismissStartupCover()
    {
        StartupCoverLayer.IsHitTestVisible = false;
        StartupCoverLayer.Opacity = 0;
        StartupCoverLayer.Visibility = Visibility.Collapsed;

        StartupDiagnostics.Write(
            "ForceDismissStartupCover: startup cover collapsed");
    }

    private static Task WaitForRenderingFramesAsync(int frameCount)
    {
        if (frameCount <= 0)
            return Task.CompletedTask;

        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var remaining = frameCount;
        EventHandler<object>? handler = null;
        handler = (_, _) =>
        {
            remaining--;
            if (remaining > 0)
                return;

            Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= handler;
            completion.TrySetResult(true);
        };

        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += handler;
        return completion.Task;
    }

    private void ScheduleStartupReveal()
    {
        if (_startupVisualReadyRaised || _startupRevealScheduled)
            return;

        _startupRevealScheduled = true;
        StartupDiagnostics.Write(
            "Startup reveal scheduled after target page frame");
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering +=
            StartupReveal_Rendering;
    }

    private void StartupReveal_Rendering(object? sender, object e)
    {
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -=
            StartupReveal_Rendering;
        _startupRevealScheduled = false;

        if (_startupVisualReadyRaised)
            return;

        _startupVisualReadyRaised = true;
        StartupDiagnostics.Write(
            "Target page frame ready; revealing Home beneath startup cover");
        StartupVisualReady?.Invoke(this, EventArgs.Empty);
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
        ScheduleStartupReveal();
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
        ScheduleStartupReveal();
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
            ScheduleStartupReveal();
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
        // Its pale glass substrate supplies a stable contrast surface, so the
        // labels can stay dark and the selected destination can use blue.
        var inactiveBackground =
            new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
        var selectedBackground =
            new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
        var inactiveForeground =
            new SolidColorBrush(Windows.UI.Color.FromArgb(255, 16, 18, 23));
        var selectedForeground =
            new SolidColorBrush(Windows.UI.Color.FromArgb(255, 35, 120, 245));

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


    private const int InterpolationModeHighQualityBicubic = 7;

    [StructLayout(LayoutKind.Sequential)]
    private struct GdiplusStartupInput
    {
        public uint GdiplusVersion;
        public IntPtr DebugEventCallback;
        [MarshalAs(UnmanagedType.Bool)]
        public bool SuppressBackgroundThread;
        [MarshalAs(UnmanagedType.Bool)]
        public bool SuppressExternalCodecs;
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

    private delegate IntPtr SubclassProc(
        IntPtr hWnd,
        uint message,
        IntPtr wParam,
        IntPtr lParam,
        UIntPtr subclassId,
        UIntPtr referenceData);

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

    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(
        IntPtr hWnd,
        SubclassProc callback,
        UIntPtr subclassId,
        UIntPtr referenceData);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(
        IntPtr hWnd,
        SubclassProc callback,
        UIntPtr subclassId);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(
        IntPtr hWnd,
        uint message,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateSolidBrush(
        uint colorRef);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(
        IntPtr obj);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadCursor(
        IntPtr instance,
        IntPtr cursorName);

    [DllImport("user32.dll")]
    private static extern IntPtr SetCursor(
        IntPtr cursor);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(
        IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(
        IntPtr hWnd,
        IntPtr hdc);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(
        IntPtr hWnd,
        out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern int FillRect(
        IntPtr hdc,
        ref NativeRect rect,
        IntPtr brush);

    [DllImport("gdiplus.dll", ExactSpelling = true)]
    private static extern int GdiplusStartup(
        out UIntPtr token,
        ref GdiplusStartupInput input,
        IntPtr output);

    [DllImport("gdiplus.dll", ExactSpelling = true)]
    private static extern void GdiplusShutdown(
        UIntPtr token);

    [DllImport("gdiplus.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GdipLoadImageFromFile(
        string filename,
        out IntPtr image);

    [DllImport("gdiplus.dll", ExactSpelling = true)]
    private static extern int GdipDisposeImage(
        IntPtr image);

    [DllImport("gdiplus.dll", ExactSpelling = true)]
    private static extern int GdipCreateFromHDC(
        IntPtr hdc,
        out IntPtr graphics);

    [DllImport("gdiplus.dll", ExactSpelling = true)]
    private static extern int GdipDeleteGraphics(
        IntPtr graphics);

    [DllImport("gdiplus.dll", ExactSpelling = true)]
    private static extern int GdipSetInterpolationMode(
        IntPtr graphics,
        int interpolationMode);

    [DllImport("gdiplus.dll", ExactSpelling = true)]
    private static extern int GdipDrawImageRectI(
        IntPtr graphics,
        IntPtr image,
        int x,
        int y,
        int width,
        int height);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();


}
