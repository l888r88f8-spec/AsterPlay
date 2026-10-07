using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace AsterPlay.WinUI;

internal sealed class NativeStartupSplash : IDisposable
{
    private readonly RectInt32 _bounds;
    private readonly bool _isLight;
    private IntPtr _hwnd;
    private IntPtr _memoryDc;
    private IntPtr _bitmap;
    private IntPtr _oldBitmap;
    private IntPtr _backgroundBrush;
    private IntPtr _image;
    private UIntPtr _gdiplusToken;
    private bool _disposed;

    private const uint WsPopup = 0x80000000;
    private const uint WsExLayered = 0x00080000;
    private const uint WsExToolWindow = 0x00000080;
    private const uint WsExNoActivate = 0x08000000;
    private const uint UlwAlpha = 0x00000002;
    private const byte AcSrcOver = 0x00;
    private const int BiRgb = 0;
    private const int DibRgbColors = 0;
    private const int SwShowNoActivate = 4;
    private const int GwlpHwndParent = -8;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private const int InterpolationModeHighQualityBicubic = 7;
    private static readonly IntPtr HwndTop = IntPtr.Zero;

    internal NativeStartupSplash(
        RectInt32 bounds,
        ApplicationTheme theme)
    {
        _bounds = bounds;
        _isLight =
            theme == ApplicationTheme.Light;

        CreateWindowAndSurface();
    }

    internal RectInt32 Bounds => _bounds;

    internal void Show()
    {
        if (_hwnd == IntPtr.Zero)
            return;

        // Keep the overlay visually opaque but not fully occluding. An
        // alpha of 254 allows DWM to keep composing the WinUI owner underneath
        // while its first LiquidGlass/Home textures become available.
        Present(254);
        ShowWindow(
            _hwnd,
            SwShowNoActivate);

        StartupDiagnostics.Write(
            $"NativeStartupSplash shown; bounds=" +
            $"{_bounds.X},{_bounds.Y}," +
            $"{_bounds.Width}x{_bounds.Height}");
    }

    internal void AttachOwner(
        IntPtr ownerHwnd)
    {
        if (_hwnd == IntPtr.Zero ||
            ownerHwnd == IntPtr.Zero)
        {
            return;
        }

        SetWindowLongPtr(
            _hwnd,
            GwlpHwndParent,
            ownerHwnd);

        SetWindowPos(
            _hwnd,
            HwndTop,
            0,
            0,
            0,
            0,
            SwpNoMove |
            SwpNoSize |
            SwpNoActivate |
            SwpShowWindow);

        StartupDiagnostics.Write(
            "NativeStartupSplash attached to MainWindow owner");
    }

    internal async Task FadeOutAsync(
        int durationMilliseconds = 640)
    {
        if (_hwnd == IntPtr.Zero)
            return;

        var duration = Math.Max(
            1,
            durationMilliseconds);
        var stopwatch =
            Stopwatch.StartNew();

        StartupDiagnostics.Write(
            $"NativeStartupSplash fade started; duration={duration} ms");

        while (true)
        {
            var progress = Math.Clamp(
                stopwatch.Elapsed.TotalMilliseconds /
                duration,
                0.0,
                1.0);

            // Smoothstep: no abrupt velocity change at either end.
            var eased =
                progress *
                progress *
                (3.0 - (2.0 * progress));

            var alpha = (byte)Math.Clamp(
                (int)Math.Round(
                    255.0 *
                    (1.0 - eased)),
                0,
                254);

            Present(alpha);

            if (progress >= 1.0)
                break;

            await Task.Delay(16);
        }

        StartupDiagnostics.Write(
            "NativeStartupSplash fade completed");

        Dispose();
    }

    private void CreateWindowAndSurface()
    {
        var instance =
            GetModuleHandle(null);

        _hwnd = CreateWindowEx(
            WsExLayered |
            WsExToolWindow |
            WsExNoActivate,
            "STATIC",
            null,
            WsPopup,
            _bounds.X,
            _bounds.Y,
            _bounds.Width,
            _bounds.Height,
            IntPtr.Zero,
            IntPtr.Zero,
            instance,
            IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
        {
            StartupDiagnostics.Write(
                $"NativeStartupSplash CreateWindowEx failed: " +
                $"{Marshal.GetLastWin32Error()}");
            return;
        }

        InitializeGdiPlus();

        _memoryDc =
            CreateCompatibleDC(IntPtr.Zero);

        var info = new BitmapInfo
        {
            Header = new BitmapInfoHeader
            {
                Size =
                    (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                Width = _bounds.Width,
                Height = -_bounds.Height,
                Planes = 1,
                BitCount = 32,
                Compression = BiRgb
            }
        };

        _bitmap = CreateDIBSection(
            _memoryDc,
            ref info,
            DibRgbColors,
            out _,
            IntPtr.Zero,
            0);

        if (_bitmap == IntPtr.Zero)
        {
            StartupDiagnostics.Write(
                $"NativeStartupSplash CreateDIBSection failed: " +
                $"{Marshal.GetLastWin32Error()}");
            return;
        }

        _oldBitmap =
            SelectObject(
                _memoryDc,
                _bitmap);

        var background = _isLight
            ? Windows.UI.Color.FromArgb(
                255,
                244,
                246,
                249)
            : Windows.UI.Color.FromArgb(
                255,
                8,
                10,
                15);

        _backgroundBrush =
            CreateSolidBrush(
                ToColorRef(
                    background.R,
                    background.G,
                    background.B));

        var rect = new NativeRect
        {
            Left = 0,
            Top = 0,
            Right = _bounds.Width,
            Bottom = _bounds.Height
        };

        FillRect(
            _memoryDc,
            ref rect,
            _backgroundBrush);

        LoadAndDrawLogo();

        StartupDiagnostics.Write(
            $"NativeStartupSplash surface ready; png={_image != IntPtr.Zero}, " +
            $"bounds={_bounds.X},{_bounds.Y}," +
            $"{_bounds.Width}x{_bounds.Height}");
    }

    private void InitializeGdiPlus()
    {
        var input = new GdiplusStartupInput
        {
            GdiplusVersion = 1
        };

        var status = GdiplusStartup(
            out _gdiplusToken,
            ref input,
            IntPtr.Zero);

        if (status != 0)
        {
            _gdiplusToken =
                UIntPtr.Zero;

            StartupDiagnostics.Write(
                $"NativeStartupSplash GDI+ startup failed: {status}");
        }
    }

    private void LoadAndDrawLogo()
    {
        if (_gdiplusToken ==
            UIntPtr.Zero)
        {
            return;
        }

        var path = Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "AsterPlay.AppIcon.png");

        if (!File.Exists(path))
        {
            StartupDiagnostics.Write(
                "NativeStartupSplash PNG missing");
            return;
        }

        var loadStatus =
            GdipLoadImageFromFile(
                path,
                out _image);

        if (loadStatus != 0 ||
            _image == IntPtr.Zero)
        {
            _image = IntPtr.Zero;
            StartupDiagnostics.Write(
                $"NativeStartupSplash PNG load failed: {loadStatus}");
            return;
        }

        var graphicsStatus =
            GdipCreateFromHDC(
                _memoryDc,
                out var graphics);

        if (graphicsStatus != 0 ||
            graphics == IntPtr.Zero)
        {
            StartupDiagnostics.Write(
                $"NativeStartupSplash graphics create failed: {graphicsStatus}");
            return;
        }

        try
        {
            GdipSetInterpolationMode(
                graphics,
                InterpolationModeHighQualityBicubic);

            var dpi =
                GetDpiForWindow(
                    _hwnd);

            if (dpi == 0)
                dpi = 96;

            var iconSize = Math.Max(
                1,
                (int)Math.Round(
                    144.0 *
                    dpi /
                    96.0));

            var x =
                Math.Max(
                    0,
                    (_bounds.Width -
                     iconSize) /
                    2);
            var y =
                Math.Max(
                    0,
                    (_bounds.Height -
                     iconSize) /
                    2);

            GdipDrawImageRectI(
                graphics,
                _image,
                x,
                y,
                iconSize,
                iconSize);
        }
        finally
        {
            GdipDeleteGraphics(
                graphics);
        }
    }

    private void Present(
        byte alpha)
    {
        if (_hwnd == IntPtr.Zero ||
            _memoryDc == IntPtr.Zero ||
            _bitmap == IntPtr.Zero)
        {
            return;
        }

        var destination =
            new NativePoint
            {
                X = _bounds.X,
                Y = _bounds.Y
            };

        var size =
            new NativeSize
            {
                Width = _bounds.Width,
                Height = _bounds.Height
            };

        var source =
            new NativePoint
            {
                X = 0,
                Y = 0
            };

        var blend =
            new BlendFunction
            {
                BlendOp = AcSrcOver,
                BlendFlags = 0,
                SourceConstantAlpha = alpha,
                AlphaFormat = 0
            };

        if (!UpdateLayeredWindow(
                _hwnd,
                IntPtr.Zero,
                ref destination,
                ref size,
                _memoryDc,
                ref source,
                0,
                ref blend,
                UlwAlpha))
        {
            StartupDiagnostics.Write(
                $"NativeStartupSplash UpdateLayeredWindow failed: " +
                $"{Marshal.GetLastWin32Error()}");
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_hwnd != IntPtr.Zero)
        {
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }

        if (_oldBitmap != IntPtr.Zero &&
            _memoryDc != IntPtr.Zero)
        {
            SelectObject(
                _memoryDc,
                _oldBitmap);
            _oldBitmap = IntPtr.Zero;
        }

        if (_bitmap != IntPtr.Zero)
        {
            DeleteObject(_bitmap);
            _bitmap = IntPtr.Zero;
        }

        if (_backgroundBrush !=
            IntPtr.Zero)
        {
            DeleteObject(
                _backgroundBrush);
            _backgroundBrush =
                IntPtr.Zero;
        }

        if (_memoryDc != IntPtr.Zero)
        {
            DeleteDC(_memoryDc);
            _memoryDc = IntPtr.Zero;
        }

        if (_image != IntPtr.Zero)
        {
            GdipDisposeImage(_image);
            _image = IntPtr.Zero;
        }

        if (_gdiplusToken !=
            UIntPtr.Zero)
        {
            GdiplusShutdown(
                _gdiplusToken);
            _gdiplusToken =
                UIntPtr.Zero;
        }

        StartupDiagnostics.Write(
            "NativeStartupSplash disposed");
    }

    private static uint ToColorRef(
        byte red,
        byte green,
        byte blue) =>
        (uint)(
            red |
            (green << 8) |
            (blue << 16));

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize
    {
        public int Width;
        public int Height;
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
    private struct BlendFunction
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public int Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader Header;
        public uint ColorPlaceholder;
    }

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

    [DllImport(
        "user32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        uint exStyle,
        string className,
        string? windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parent,
        IntPtr menu,
        IntPtr instance,
        IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(
        IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(
        IntPtr hWnd,
        int command);

    [DllImport(
        "user32.dll",
        EntryPoint = "SetWindowLongPtrW",
        CharSet = CharSet.Unicode)]
    private static extern IntPtr SetWindowLongPtr(
        IntPtr hWnd,
        int index,
        IntPtr value);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport(
        "user32.dll",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateLayeredWindow(
        IntPtr hWnd,
        IntPtr destinationDc,
        ref NativePoint destination,
        ref NativeSize size,
        IntPtr sourceDc,
        ref NativePoint source,
        uint colorKey,
        ref BlendFunction blend,
        uint flags);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(
        IntPtr hWnd);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(
        IntPtr hdc);

    [DllImport(
        "gdi32.dll",
        SetLastError = true)]
    private static extern IntPtr CreateDIBSection(
        IntPtr hdc,
        ref BitmapInfo info,
        int usage,
        out IntPtr bits,
        IntPtr section,
        uint offset);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(
        IntPtr hdc,
        IntPtr obj);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(
        IntPtr obj);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(
        IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateSolidBrush(
        uint colorRef);

    [DllImport("user32.dll")]
    private static extern int FillRect(
        IntPtr hdc,
        ref NativeRect rect,
        IntPtr brush);

    [DllImport(
        "gdiplus.dll",
        ExactSpelling = true)]
    private static extern int GdiplusStartup(
        out UIntPtr token,
        ref GdiplusStartupInput input,
        IntPtr output);

    [DllImport(
        "gdiplus.dll",
        ExactSpelling = true)]
    private static extern void GdiplusShutdown(
        UIntPtr token);

    [DllImport(
        "gdiplus.dll",
        CharSet = CharSet.Unicode,
        ExactSpelling = true)]
    private static extern int GdipLoadImageFromFile(
        string filename,
        out IntPtr image);

    [DllImport(
        "gdiplus.dll",
        ExactSpelling = true)]
    private static extern int GdipDisposeImage(
        IntPtr image);

    [DllImport(
        "gdiplus.dll",
        ExactSpelling = true)]
    private static extern int GdipCreateFromHDC(
        IntPtr hdc,
        out IntPtr graphics);

    [DllImport(
        "gdiplus.dll",
        ExactSpelling = true)]
    private static extern int GdipDeleteGraphics(
        IntPtr graphics);

    [DllImport(
        "gdiplus.dll",
        ExactSpelling = true)]
    private static extern int GdipSetInterpolationMode(
        IntPtr graphics,
        int interpolationMode);

    [DllImport(
        "gdiplus.dll",
        ExactSpelling = true)]
    private static extern int GdipDrawImageRectI(
        IntPtr graphics,
        IntPtr image,
        int x,
        int y,
        int width,
        int height);

    [DllImport(
        "kernel32.dll",
        CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(
        string? moduleName);
}
