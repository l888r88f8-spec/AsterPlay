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

    private IntPtr _hwnd;
    private MpvClient? _mpv;
    private string? _pendingUrl;

    private double _pendingStartSeconds;

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
    public void CycleAudio() => _mpv?.CycleAudio();
    public void CycleSubtitle() => _mpv?.CycleSubtitle();

    public double PositionSeconds => _mpv?.PositionSeconds ?? 0;
    public double DurationSeconds => _mpv?.DurationSeconds ?? 0;
    public double Volume => _mpv?.Volume ?? 100;
    public bool IsPaused => _mpv?.IsPaused ?? false;
    public bool IsBuffering => _mpv?.IsBuffering ?? false;
    public string DiagnosticState => _mpv?.DiagnosticState ?? "mpv=null";

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        _hwnd = CreateWindowEx(
            0, "static", "",
            WsChild | WsVisible | WsClipChildren | WsClipSiblings,
            0, 0, 1, 1,
            hwndParent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
            throw new InvalidOperationException("Failed to create the mpv child window.");

        _mpv = new MpvClient(_hwnd);
        if (!string.IsNullOrWhiteSpace(_pendingUrl))
            _mpv.Load(_pendingUrl, _pendingStartSeconds);

        return new HandleRef(this, _hwnd);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        ShutdownPlayback();

        if (hwnd.Handle != IntPtr.Zero)
            DestroyWindow(hwnd.Handle);

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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
}
