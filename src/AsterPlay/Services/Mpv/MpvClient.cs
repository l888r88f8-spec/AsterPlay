using System.Globalization;
using System.Runtime.InteropServices;

namespace AsterPlay.Services.Mpv;

public sealed class MpvClient : IDisposable
{
    private const string DllName = "libmpv-2.dll";
    private IntPtr _handle;

    public MpvClient(IntPtr windowHandle)
    {
        _handle = Native.mpv_create();
        if (_handle == IntPtr.Zero)
            throw new InvalidOperationException("mpv_create failed.");

        SetOption("wid", windowHandle.ToInt64().ToString(CultureInfo.InvariantCulture));
        SetOption("vo", "gpu-next");
        SetOption("gpu-api", "d3d11");
        SetOption("hwdec", "auto-safe");
        SetOption("cache", "yes");
        SetOption("keep-open", "yes");

        var result = Native.mpv_initialize(_handle);
        if (result < 0)
        {
            Dispose();
            throw new InvalidOperationException($"mpv_initialize failed: {result}");
        }
    }

    public void Load(string url, double startSeconds = 0)
    {
        if (startSeconds > 0.25)
        {
            Command(
                "loadfile",
                url,
                "replace",
                "-1",
                $"start={startSeconds.ToString("0.###", CultureInfo.InvariantCulture)}");
        }
        else
        {
            Command("loadfile", url, "replace");
        }
    }

    public void TogglePause() => Command("cycle", "pause");

    public void Seek(double seconds) =>
        Command("seek", seconds.ToString("0.###", CultureInfo.InvariantCulture), "relative");

    public void SeekAbsolute(double seconds) =>
        Command("seek", Math.Max(0, seconds).ToString("0.###", CultureInfo.InvariantCulture), "absolute+exact");

    public void SetVolume(double volume) =>
        SetProperty("volume", Math.Clamp(volume, 0, 100).ToString("0.###", CultureInfo.InvariantCulture));

    public void CycleAudio() => Command("cycle", "audio");
    public void CycleSubtitle() => Command("cycle", "sub");

    public double PositionSeconds => GetDoubleProperty("time-pos");
    public double DurationSeconds => GetDoubleProperty("duration");
    public double Volume => GetDoubleProperty("volume", 100);
    public bool IsPaused => GetBoolProperty("pause");
    public bool IsBuffering => GetBoolProperty("paused-for-cache");

    private void SetOption(string name, string value)
    {
        var result = Native.mpv_set_option_string(_handle, name, value);
        if (result < 0)
            throw new InvalidOperationException($"mpv option {name} failed: {result}");
    }

    private void SetProperty(string name, string value)
    {
        if (_handle == IntPtr.Zero)
            return;

        var result = Native.mpv_set_property_string(_handle, name, value);
        if (result < 0)
            throw new InvalidOperationException($"mpv property {name} failed: {result}");
    }

    private string? GetStringProperty(string name)
    {
        if (_handle == IntPtr.Zero)
            return null;

        var ptr = Native.mpv_get_property_string(_handle, name);
        if (ptr == IntPtr.Zero)
            return null;

        try
        {
            return Marshal.PtrToStringUTF8(ptr);
        }
        finally
        {
            Native.mpv_free(ptr);
        }
    }

    private double GetDoubleProperty(string name, double fallback = 0)
    {
        var value = GetStringProperty(name);
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? result
            : fallback;
    }

    private bool GetBoolProperty(string name)
    {
        var value = GetStringProperty(name);
        return string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    public void Command(params string[] args)
    {
        if (_handle == IntPtr.Zero)
            return;

        var allocated = new List<IntPtr>();
        var argv = Marshal.AllocHGlobal(IntPtr.Size * (args.Length + 1));

        try
        {
            for (var i = 0; i < args.Length; i++)
            {
                var p = Marshal.StringToCoTaskMemUTF8(args[i]);
                allocated.Add(p);
                Marshal.WriteIntPtr(argv, i * IntPtr.Size, p);
            }

            Marshal.WriteIntPtr(argv, args.Length * IntPtr.Size, IntPtr.Zero);
            var result = Native.mpv_command(_handle, argv);
            if (result < 0)
                throw new InvalidOperationException($"mpv command failed: {result}");
        }
        finally
        {
            foreach (var p in allocated)
                Marshal.FreeCoTaskMem(p);
            Marshal.FreeHGlobal(argv);
        }
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero)
            return;

        Native.mpv_terminate_destroy(_handle);
        _handle = IntPtr.Zero;
        GC.SuppressFinalize(this);
    }

    private static class Native
    {
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr mpv_create();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int mpv_initialize(IntPtr ctx);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void mpv_terminate_destroy(IntPtr ctx);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int mpv_set_option_string(
            IntPtr ctx,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int mpv_set_property_string(
            IntPtr ctx,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr mpv_get_property_string(
            IntPtr ctx,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void mpv_free(IntPtr data);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int mpv_command(IntPtr ctx, IntPtr args);
    }
}
