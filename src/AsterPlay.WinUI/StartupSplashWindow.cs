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
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080L;

    private readonly Image _logoImage;
    private bool _visualReadyRaised;

    internal StartupSplashWindow()
    {
        Title = "AsterPlay";

        var backgroundColor =
            Application.Current.RequestedTheme == ApplicationTheme.Light
                ? Windows.UI.Color.FromArgb(255, 244, 246, 249)
                : Windows.UI.Color.FromArgb(255, 8, 10, 15);

        _logoImage = new Image
        {
            Source = new BitmapImage(
                new Uri(
                    "ms-appx:///Assets/AsterPlay.AppIcon.png")),
            Width = 144,
            Height = 144,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        _logoImage.ImageOpened += LogoImage_ImageOpened;
        _logoImage.ImageFailed += LogoImage_ImageFailed;

        var root = new Grid
        {
            Background = new SolidColorBrush(backgroundColor)
        };
        root.Children.Add(_logoImage);
        Content = root;

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId =
            Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(
                hasBorder: false,
                hasTitleBar: false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
        }

        // Keep the splash out of Alt+Tab. It is only the visual front page of
        // the real AsterPlay window that is loading off-screen.
        var style = GetWindowLongPtr(
            hwnd,
            GwlExStyle).ToInt64();
        SetWindowLongPtr(
            hwnd,
            GwlExStyle,
            new IntPtr(style | WsExToolWindow));

        var workArea = DisplayArea.GetFromWindowId(
                windowId,
                DisplayAreaFallback.Primary)
            .WorkArea;

        var width = Math.Min(
            1280,
            Math.Max(640, workArea.Width - 80));
        var height = Math.Min(
            800,
            Math.Max(420, workArea.Height - 80));

        appWindow.MoveAndResize(
            new RectInt32(
                workArea.X + ((workArea.Width - width) / 2),
                workArea.Y + ((workArea.Height - height) / 2),
                width,
                height));

        Closed += (_, _) =>
        {
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
        // Give the compositor one frame after PNG decoding. MainWindow
        // construction starts only after the static splash is actually visible.
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

        // Never leave startup blocked forever because artwork failed.
        RaiseVisualReady();
    }

    private void IconReady_Rendering(
        object? sender,
        object e)
    {
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -=
            IconReady_Rendering;
        RaiseVisualReady();
    }

    private void RaiseVisualReady()
    {
        if (_visualReadyRaised)
            return;

        _visualReadyRaised = true;
        VisualReady?.Invoke(this, EventArgs.Empty);
    }

    internal void Complete(MainWindow mainWindow)
    {
        // Server/home content is already ready. The splash stays top-most until
        // the loaded MainWindow is positioned and activated underneath it.
        mainWindow.CompleteStartupWindowReveal();
        Close();
    }

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
}
