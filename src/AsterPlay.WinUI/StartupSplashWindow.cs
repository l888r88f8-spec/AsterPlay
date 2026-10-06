using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;

namespace AsterPlay.WinUI;

internal sealed class StartupSplashWindow : Window
{
    private const int BaseSize = 152;
    private const int GwlExStyle = -20;
    private const long WsExLayered = 0x00080000L;
    private const long WsExToolWindow = 0x00000080L;
    private const uint LwaAlpha = 0x00000002;

    private readonly IntPtr _hwnd;
    private readonly AppWindow _appWindow;
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = new();
    private readonly Image _logoImage;
    private bool _visualReadyRaised;
    private bool _pngFallbackTried;

    private RectInt32 _workArea;
    private int _currentSize = BaseSize;
    private int _revealStartSize = BaseSize;
    private bool _revealing;
    private bool _mainWindowShown;
    private MainWindow? _mainWindow;
    private Action? _completed;

    internal StartupSplashWindow()
    {
        Title = "AsterPlay";

        var root = new Grid
        {
            Background = new SolidColorBrush(
                Windows.UI.Color.FromArgb(255, 8, 10, 14))
        };

        _logoImage = new Image
        {
            // Use the exact resource that Windows uses for the executable icon.
            // ICO decoding is handled by WIC. PNG remains a fallback below.
            Source = new BitmapImage(
                new Uri(
                    "ms-appx:///Assets/AsterPlay.ico")),
            Stretch = Stretch.UniformToFill,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        _logoImage.ImageOpened += LogoImage_ImageOpened;
        _logoImage.ImageFailed += LogoImage_ImageFailed;
        root.Children.Add(_logoImage);

        Content = root;

        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId =
            Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(
                hasBorder: false,
                hasTitleBar: false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
        }

        var style = GetWindowLongPtr(
            _hwnd,
            GwlExStyle).ToInt64();

        SetWindowLongPtr(
            _hwnd,
            GwlExStyle,
            new IntPtr(
                style |
                WsExLayered |
                WsExToolWindow));

        SetLayeredWindowAttributes(
            _hwnd,
            0,
            255,
            LwaAlpha);

        _workArea = DisplayArea.GetFromWindowId(
                windowId,
                DisplayAreaFallback.Primary)
            .WorkArea;

        UpdateBounds(
            BaseSize,
            255);

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _timer.Tick += Timer_Tick;

        Closed += (_, _) =>
        {
            _timer.Stop();
            _timer.Tick -= Timer_Tick;
            _logoImage.ImageOpened -= LogoImage_ImageOpened;
            _logoImage.ImageFailed -= LogoImage_ImageFailed;
            Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -=
                IconReady_Rendering;
        };
    }

    internal event EventHandler? VisualReady;

    private void LogoImage_ImageOpened(
        object sender,
        RoutedEventArgs e)
    {
        // ImageOpened means the decoder has real pixels. Wait one compositor
        // frame so the user actually sees them before MainWindow construction
        // starts blocking the UI thread.
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -=
            IconReady_Rendering;
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering +=
            IconReady_Rendering;
    }

    private void LogoImage_ImageFailed(
        object sender,
        ExceptionRoutedEventArgs e)
    {
        if (_pngFallbackTried)
        {
            StartupDiagnostics.Write(
                $"Startup icon failed to decode: {e.ErrorMessage}");
            return;
        }

        _pngFallbackTried = true;
        StartupDiagnostics.Write(
            $"Startup ICO decode failed, falling back to PNG: {e.ErrorMessage}");

        _logoImage.Source = new BitmapImage(
            new Uri(
                "ms-appx:///Assets/AsterPlay.AppIcon.png"));
    }

    private void IconReady_Rendering(
        object? sender,
        object e)
    {
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -=
            IconReady_Rendering;

        if (_visualReadyRaised)
            return;

        _visualReadyRaised = true;
        VisualReady?.Invoke(this, EventArgs.Empty);
    }

    internal void StartPulse()
    {
        _revealing = false;
        _clock.Restart();
        _timer.Start();
    }

    internal void BeginReveal(
        MainWindow mainWindow,
        Action? completed = null)
    {
        if (_revealing)
            return;

        _mainWindow = mainWindow;
        _completed = completed;
        _revealStartSize = _currentSize;
        _mainWindowShown = false;
        _revealing = true;
        _clock.Restart();

        if (!_timer.IsEnabled)
            _timer.Start();
    }

    private void Timer_Tick(
        object? sender,
        object e)
    {
        if (_revealing)
        {
            TickReveal();
            return;
        }

        TickPulse();
    }

    private void TickPulse()
    {
        var seconds = _clock.Elapsed.TotalSeconds;
        var phase =
            seconds *
            (Math.PI * 2.0 / 1.84);

        var scale =
            1.0 +
            (Math.Sin(phase) * 0.06);

        var size = Math.Max(
            1,
            (int)Math.Round(
                BaseSize * scale));

        UpdateBounds(
            size,
            255);
    }

    private void TickReveal()
    {
        const double durationMs = 1380.0;

        var raw =
            Math.Clamp(
                _clock.Elapsed.TotalMilliseconds /
                durationMs,
                0.0,
                1.0);

        var eased =
            SmoothStep(raw);

        var targetSize =
            (int)Math.Ceiling(
                Math.Max(
                    _workArea.Width,
                    _workArea.Height) *
                1.38);

        var size =
            (int)Math.Round(
                Lerp(
                    _revealStartSize,
                    targetSize,
                    eased));

        // Keep the real window off-screen until the expanding icon is
        // large enough to cover it. That avoids any white WinUI frame flashing
        // around the splash during initialization.
        if (!_mainWindowShown &&
            raw >= 0.72)
        {
            _mainWindowShown = true;
            _mainWindow?.PositionStartupWindowBehindSplash();
        }

        var splashFade =
            Math.Clamp(
                (raw - 0.72) / 0.28,
                0.0,
                1.0);

        var splashAlpha =
            (byte)Math.Round(
                255.0 *
                (1.0 -
                 SmoothStep(splashFade)));

        UpdateBounds(
            size,
            splashAlpha);

        if (raw < 1.0)
            return;

        _timer.Stop();

        if (!_mainWindowShown)
        {
            _mainWindowShown = true;
            _mainWindow?.PositionStartupWindowBehindSplash();
        }

        _mainWindow?.CompleteStartupWindowReveal();

        var completed = _completed;
        _completed = null;
        _mainWindow = null;

        Close();
        completed?.Invoke();
    }

    private void UpdateBounds(
        int size,
        byte alpha)
    {
        _currentSize = size;

        var centerX =
            _workArea.X +
            (_workArea.Width / 2);
        var centerY =
            _workArea.Y +
            (_workArea.Height / 2);

        var x =
            centerX -
            (size / 2);
        var y =
            centerY -
            (size / 2);

        _appWindow.MoveAndResize(
            new RectInt32(
                x,
                y,
                size,
                size));

        ApplyRoundedRegion(size);

        SetLayeredWindowAttributes(
            _hwnd,
            0,
            alpha,
            LwaAlpha);
    }

    private void ApplyRoundedRegion(
        int size)
    {
        var radius =
            Math.Max(
                12,
                (int)Math.Round(
                    size * 0.19));

        var region = CreateRoundRectRgn(
            0,
            0,
            size + 1,
            size + 1,
            radius * 2,
            radius * 2);

        if (region == IntPtr.Zero)
            return;

        if (SetWindowRgn(
                _hwnd,
                region,
                true) == 0)
        {
            DeleteObject(region);
        }
    }

    private static double SmoothStep(
        double value)
    {
        value =
            Math.Clamp(
                value,
                0.0,
                1.0);

        return value *
               value *
               (3.0 -
                (2.0 * value));
    }

    private static double Lerp(
        double from,
        double to,
        double progress) =>
        from +
        ((to - from) * progress);

    [DllImport(
        "user32.dll",
        EntryPoint = "GetWindowLongPtrW",
        SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(
        IntPtr hWnd,
        int index);

    [DllImport(
        "user32.dll",
        EntryPoint = "SetWindowLongPtrW",
        SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(
        IntPtr hWnd,
        int index,
        IntPtr value);

    [DllImport(
        "user32.dll",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetLayeredWindowAttributes(
        IntPtr hWnd,
        uint colorKey,
        byte alpha,
        uint flags);

    [DllImport(
        "gdi32.dll",
        SetLastError = true)]
    private static extern IntPtr CreateRoundRectRgn(
        int left,
        int top,
        int right,
        int bottom,
        int widthEllipse,
        int heightEllipse);

    [DllImport(
        "user32.dll",
        SetLastError = true)]
    private static extern int SetWindowRgn(
        IntPtr hWnd,
        IntPtr hRgn,
        [MarshalAs(UnmanagedType.Bool)] bool redraw);

    [DllImport(
        "gdi32.dll",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(
        IntPtr hObject);
}
