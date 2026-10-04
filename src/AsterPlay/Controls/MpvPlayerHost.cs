using System.Runtime.InteropServices;
using System.Windows.Interop;
using AsterPlay.Services.Mpv;

namespace AsterPlay.Controls;

public sealed class MpvPlayerHost : HwndHost
{
    private const int WsChild = 0x40000000;
    private const int WsVisible = 0x10000000;
    private const int WsClipChildren = 0x02000000;
    private const int WsClipSiblings = 0x04000000;

    private const int WmSetCursor = 0x0020;
    private const int WmMouseMove = 0x0200;
    private const int WmLButtonDown = 0x0201;
    private const int SmCxDoubleClk = 36;
    private const int SmCyDoubleClk = 37;
    private const int IdcArrow = 32512;

    private IntPtr _hwnd;
    private MpvClient? _mpv;
    private string? _pendingUrl;
    private double _pendingStartSeconds;
    private bool _cursorHidden;
    private long _lastClickTick;
    private int _lastClickX;
    private int _lastClickY;
    private SubclassProc? _subclassProc;

    public event Action? NativeMouseActivity;
    public event Action? NativeDoubleClick;

    public void Load(string url, double startSeconds = 0)
    {
        _pendingUrl = url;
        _pendingStartSeconds = Math.Max(0, startSeconds);
        _mpv?.Load(url, _pendingStartSeconds);
    }

    public void ShutdownPlayback()
    {
        _pendingUrl = null;
        _pendingStartSeconds = 0;

        if (_mpv is null)
            return;

        try
        {
            _mpv.Stop();
        }
        finally
        {
            _mpv.Dispose();
            _mpv = null;
        }
    }

    public void TogglePause() => _mpv?.TogglePause();
    public void Seek(double seconds) => _mpv?.Seek(seconds);
    public void SeekAbsolute(double seconds) => _mpv?.SeekAbsolute(seconds);
    public void SetVolume(double volume) => _mpv?.SetVolume(volume);
    public void SetSpeed(double speed) => _mpv?.SetSpeed(speed);
    public void CycleAudio() => _mpv?.CycleAudio();
    public void CycleSubtitle() => _mpv?.CycleSubtitle();

    public double PositionSeconds => _mpv?.PositionSeconds ?? 0;
    public double DurationSeconds => _mpv?.DurationSeconds ?? 0;
    public double Volume => _mpv?.Volume ?? 100;
    public double Speed => _mpv?.Speed ?? 1;
    public bool IsPaused => _mpv?.IsPaused ?? false;
    public bool IsBuffering => _mpv?.IsBuffering ?? false;
    public string DiagnosticState => _mpv?.DiagnosticState ?? "mpv=null";

    public void SetCursorHidden(bool hidden)
    {
        _cursorHidden = hidden;
        if (_hwnd == IntPtr.Zero)
            return;

        SetCursor(hidden ? IntPtr.Zero : LoadCursor(IntPtr.Zero, new IntPtr(IdcArrow)));
    }

    private IntPtr ChildWindowProc(
        IntPtr hwnd,
        uint msg,
        UIntPtr wParam,
        IntPtr lParam,
        UIntPtr subclassId,
        UIntPtr refData)
    {
        switch ((int)msg)
        {
            case WmMouseMove:
                NativeMouseActivity?.Invoke();
                break;

            case WmLButtonDown:
                NativeMouseActivity?.Invoke();

                var packed = GetMessagePos();
                var screenX = unchecked((short)(packed & 0xFFFF));
                var screenY = unchecked((short)((packed >> 16) & 0xFFFF));
                var now = Environment.TickCount64;
                var maxDx = Math.Max(1, GetSystemMetrics(SmCxDoubleClk));
                var maxDy = Math.Max(1, GetSystemMetrics(SmCyDoubleClk));

                if (_lastClickTick > 0 &&
                    now - _lastClickTick <= GetDoubleClickTime() &&
                    Math.Abs(screenX - _lastClickX) <= maxDx &&
                    Math.Abs(screenY - _lastClickY) <= maxDy)
                {
                    _lastClickTick = 0;
                    PlaybackLog.Write("PlayerHost", "Native double-click detected");
                    NativeDoubleClick?.Invoke();
                }
                else
                {
                    _lastClickTick = now;
                    _lastClickX = screenX;
                    _lastClickY = screenY;
                }
                break;

            case WmSetCursor:
                SetCursor(_cursorHidden
                    ? IntPtr.Zero
                    : LoadCursor(IntPtr.Zero, new IntPtr(IdcArrow)));
                return IntPtr.Zero;
        }

        return DefSubclassProc(hwnd, msg, wParam, lParam);
    }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        _hwnd = CreateWindowEx(
            0, "static", "",
            WsChild | WsVisible | WsClipChildren | WsClipSiblings,
            0, 0, 1, 1,
            hwndParent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
            throw new InvalidOperationException("Failed to create the mpv child window.");

        _subclassProc = ChildWindowProc;
        if (!SetWindowSubclass(_hwnd, _subclassProc, UIntPtr.Zero, UIntPtr.Zero))
            throw new InvalidOperationException("Failed to subclass the mpv child window.");

        PlaybackLog.Write("PlayerHost", $"Installed native mouse hook for hwnd=0x{_hwnd.ToInt64():X}");

        _mpv = new MpvClient(_hwnd);
        if (!string.IsNullOrWhiteSpace(_pendingUrl))
            _mpv.Load(_pendingUrl, _pendingStartSeconds);

        return new HandleRef(this, _hwnd);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        ShutdownPlayback();

        if (hwnd.Handle != IntPtr.Zero)
        {
            if (_subclassProc is not null)
                RemoveWindowSubclass(hwnd.Handle, _subclassProc, UIntPtr.Zero);

            DestroyWindow(hwnd.Handle);
        }

        _subclassProc = null;
        _hwnd = IntPtr.Zero;
    }

    protected override void OnWindowPositionChanged(Rect rcBoundingBox)
    {
        base.OnWindowPositionChanged(rcBoundingBox);
        if (_hwnd == IntPtr.Zero)
            return;

        SetWindowPos(
            _hwnd, IntPtr.Zero,
            0, 0,
            Math.Max(1, (int)rcBoundingBox.Width),
            Math.Max(1, (int)rcBoundingBox.Height),
            0x0014);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        int exStyle, string className, string windowName, int style,
        int x, int y, int width, int height,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hwnd);

    private delegate IntPtr SubclassProc(
        IntPtr hwnd,
        uint msg,
        UIntPtr wParam,
        IntPtr lParam,
        UIntPtr subclassId,
        UIntPtr refData);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(
        IntPtr hwnd,
        SubclassProc callback,
        UIntPtr subclassId,
        UIntPtr refData);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(
        IntPtr hwnd,
        SubclassProc callback,
        UIntPtr subclassId);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(
        IntPtr hwnd,
        uint msg,
        UIntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetMessagePos();

    [DllImport("user32.dll")]
    private static extern IntPtr SetCursor(IntPtr cursor);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadCursor(IntPtr instance, IntPtr cursorName);

    [DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
}
