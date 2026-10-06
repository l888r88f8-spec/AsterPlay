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
    private const int WindowSize = 176;
    private const int GwlExStyle = -20;
    private const long WsExLayered = 0x00080000L;
    private const long WsExToolWindow = 0x00000080L;
    private const uint LwaAlpha = 0x00000002;

    private readonly IntPtr _hwnd;
    private readonly AppWindow _appWindow;
    private readonly Image _logoImage;
    private readonly ScaleTransform _logoScale;
    private readonly Stopwatch _clock = new();

    private bool _visualReadyRaised;
    private bool _renderSubscribed;
    private bool _revealing;
    private double _currentScale = 1.0;
    private double _revealStartScale = 1.0;
    private MainWindow? _mainWindow;
    private Action? _completed;

    internal StartupSplashWindow()
    {
        Title = "AsterPlay";

        _logoScale = new ScaleTransform
        {
            ScaleX = 1,
            ScaleY = 1
        };

        _logoImage = new Image
        {
            // Startup animation always uses the PNG directly. The executable
            // ICO is only for Windows/taskbar icon metadata.
            Source = new BitmapImage(
                new Uri(
                    "ms-appx:///Assets/AsterPlay.AppIcon.png")),
            Stretch = Stretch.UniformToFill,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5),
            RenderTransform = _logoScale
        };
        _logoImage.ImageOpened += LogoImage_ImageOpened;
        _logoImage.ImageFailed += LogoImage_ImageFailed;

        var root = new Grid
        {
            // The PNG fills the complete rounded window. This color is only a
            // decoder fallback and never forms a separate splash panel.
            Background = new SolidColorBrush(
                Windows.UI.Color.FromArgb(255, 8, 10, 14))
        };
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

        var workArea = DisplayArea.GetFromWindowId(
                windowId,
                DisplayAreaFallback.Primary)
            .WorkArea;

        _appWindow.MoveAndResize(
            new RectInt32(
                workArea.X + ((workArea.Width - WindowSize) / 2),
                workArea.Y + ((workArea.Height - WindowSize) / 2),
                WindowSize,
                WindowSize));

        ApplyRoundedRegion();

        Closed += (_, _) =>
        {
            StopRendering();
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
        // Wait one real compositor frame after PNG decoding so the first thing
        // the user sees is the actual icon, never the fallback surface.
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -=
            IconReady_Rendering;
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering +=
            IconReady_Rendering;
    }

    private void LogoImage_ImageFailed(
        object sender,
        ExceptionRoutedEventArgs e)
    {
        StartupDiagnostics.Write(
            $"Startup PNG failed to decode: {e.ErrorMessage}");
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
        StartRendering();
    }

    internal void BeginReveal(
        MainWindow mainWindow,
        Action? completed = null)
    {
        if (_revealing)
            return;

        _mainWindow = mainWindow;
        _completed = completed;
        _revealStartScale = _currentScale;
        _revealing = true;
        _clock.Restart();

        // Home content is fully ready at this point. Put it on-screen now,
        // behind the always-on-top icon, instead of revealing a blank window
        // midway through a full-screen splash expansion.
        _mainWindow.PositionStartupWindowBehindSplash();

        StartRendering();
    }

    private void StartRendering()
    {
        if (_renderSubscribed)
            return;

        _renderSubscribed = true;
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering +=
            Animation_Rendering;
    }

    private void StopRendering()
    {
        if (!_renderSubscribed)
            return;

        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -=
            Animation_Rendering;
        _renderSubscribed = false;
    }

    private void Animation_Rendering(
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
        // RenderTransform scaling is compositor-friendly and avoids resizing
        // the native window every frame. One cycle is intentionally gentle.
        var seconds = _clock.Elapsed.TotalSeconds;
        var phase =
            (seconds * (Math.PI * 2.0 / 1.9)) -
            (Math.PI / 2.0);

        _currentScale =
            1.025 +
            (Math.Sin(phase) * 0.025);

        ApplyScale(_currentScale);
    }

    private void TickReveal()
    {
        const double durationMs = 460.0;

        var raw = Math.Clamp(
            _clock.Elapsed.TotalMilliseconds / durationMs,
            0.0,
            1.0);
        var eased = SmoothStep(raw);

        _currentScale = Lerp(
            _revealStartScale,
            1.10,
            eased);
        ApplyScale(_currentScale);

        // Fade the small icon window itself. It never grows to fullscreen.
        var alpha = (byte)Math.Round(
            255.0 * (1.0 - eased));

        SetLayeredWindowAttributes(
            _hwnd,
            0,
            alpha,
            LwaAlpha);

        if (raw < 1.0)
            return;

        StopRendering();

        _mainWindow?.CompleteStartupWindowReveal();

        var completed = _completed;
        _completed = null;
        _mainWindow = null;

        Close();
        completed?.Invoke();
    }

    private void ApplyScale(double scale)
    {
        _logoScale.ScaleX = scale;
        _logoScale.ScaleY = scale;
    }

    private void ApplyRoundedRegion()
    {
        var radius = (int)Math.Round(
            WindowSize * 0.19);

        var region = CreateRoundRectRgn(
            0,
            0,
            WindowSize + 1,
            WindowSize + 1,
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

    private static double SmoothStep(double value)
    {
        value = Math.Clamp(
            value,
            0.0,
            1.0);

        return value *
               value *
               (3.0 - (2.0 * value));
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
