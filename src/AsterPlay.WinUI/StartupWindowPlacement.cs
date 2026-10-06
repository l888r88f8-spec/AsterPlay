using System.Runtime.InteropServices;
using Windows.Graphics;

namespace AsterPlay.WinUI;

internal static class StartupWindowPlacement
{
    private const uint MonitorDefaultToNearest = 2;

    internal static RectInt32 Resolve()
    {
        var point = new NativePoint();

        if (!GetCursorPos(out point))
            point = new NativePoint { X = 0, Y = 0 };

        var monitor = MonitorFromPoint(
            point,
            MonitorDefaultToNearest);

        var info = new MonitorInfo
        {
            Size = Marshal.SizeOf<MonitorInfo>()
        };

        if (monitor == IntPtr.Zero ||
            !GetMonitorInfo(monitor, ref info))
        {
            // Safe fallback for an ordinary 1920x1080 work area.
            return new RectInt32(
                320,
                146,
                1280,
                748);
        }

        var workWidth =
            info.Work.Right - info.Work.Left;
        var workHeight =
            info.Work.Bottom - info.Work.Top;

        // Preserve AsterPlay's current startup size when the monitor has room.
        // On smaller work areas keep the same 40 px breathing room per side
        // that the previous MainWindow placement code used.
        var width = Math.Min(
            1280,
            Math.Max(
                1,
                workWidth >= 720
                    ? workWidth - 80
                    : workWidth));

        var height = Math.Min(
            748,
            Math.Max(
                1,
                workHeight >= 500
                    ? workHeight - 80
                    : workHeight));

        var x =
            info.Work.Left +
            ((workWidth - width) / 2);
        var y =
            info.Work.Top +
            ((workHeight - height) / 2);

        return new RectInt32(
            x,
            y,
            width,
            height);
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
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(
        out NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(
        NativePoint point,
        uint flags);

    [DllImport(
        "user32.dll",
        CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(
        IntPtr monitor,
        ref MonitorInfo info);
}
