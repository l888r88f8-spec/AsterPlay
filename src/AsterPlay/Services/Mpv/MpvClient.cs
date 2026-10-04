using System.Globalization;
using System.Runtime.InteropServices;

namespace AsterPlay.Services.Mpv;

public sealed class MpvClient : IDisposable
{
    private const string DllName = "libmpv-2.dll";
    private IntPtr _handle;
    private Thread? _eventThread;
    private volatile bool _eventLoopRunning;

    public MpvClient()
    {
        PlaybackLog.Write("mpv", "Creating mpv for Render API");
        _handle = Native.mpv_create();
        if (_handle == IntPtr.Zero)
            throw new InvalidOperationException("mpv_create failed.");

        SetOption("config", "no");
        SetOption("load-scripts", "no");
        SetOption("osc", "no");
        SetOption("vo", "libmpv");
        SetOption("hwdec", "auto-copy");

        // Keep mpv's normal one-second network readahead, but do not delay the
        // first frame waiting for cache-pause's initial threshold. Once playback
        // has started, normal cache-pause behavior still protects against real
        // network underruns.
        SetOption("cache-pause-initial", "no");

        SetOption("keep-open", "yes");

        var result = Native.mpv_initialize(_handle);
        PlaybackLog.Write("mpv", $"mpv_initialize result={result}");
        if (result < 0)
        {
            Dispose();
            throw new InvalidOperationException($"mpv_initialize failed: {result}");
        }

        var logResult = Native.mpv_request_log_messages(_handle, "debug");
        PlaybackLog.Write("mpv", $"request_log_messages(debug) -> {logResult}");
        StartEventLoop();
    }

    internal IntPtr Handle => _handle;

    public void Load(string url, double startSeconds = 0)
    {
        PlaybackLog.Write("mpv", $"Load: url={url}, startSeconds={startSeconds:0.###}");
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

        SetProperty("pause", "no");
        PlaybackLog.Write("mpv", $"Load dispatched. pause={GetStringProperty("pause")}, time-pos={GetStringProperty("time-pos")}, duration={GetStringProperty("duration")}");
    }

    public void Stop()
    {
        if (_handle == IntPtr.Zero)
            return;

        PlaybackLog.Write("mpv", "stop requested");
        Command("stop");
    }

    public void TogglePause() => Command("cycle", "pause");

    public void SetPaused(bool paused) =>
        SetProperty("pause", paused ? "yes" : "no");

    public void Seek(double seconds) =>
        Command("seek", seconds.ToString("0.###", CultureInfo.InvariantCulture), "relative");

    public void SeekAbsolute(double seconds) =>
        Command("seek", Math.Max(0, seconds).ToString("0.###", CultureInfo.InvariantCulture), "absolute+exact");

    public void SetVolume(double volume) =>
        SetProperty("volume", Math.Clamp(volume, 0, 100).ToString("0.###", CultureInfo.InvariantCulture));

    public void SetSpeed(double speed) =>
        SetProperty("speed", Math.Clamp(speed, 0.25, 4.0).ToString("0.###", CultureInfo.InvariantCulture));

    public IReadOnlyList<PlayerTrack> GetTracks()
    {
        var count = GetIntProperty("track-list/count");
        if (count <= 0)
            return Array.Empty<PlayerTrack>();

        var tracks = new List<PlayerTrack>(count);

        for (var index = 0; index < count; index++)
        {
            var prefix = $"track-list/{index}";
            var id = GetStringProperty($"{prefix}/id");
            var type = GetStringProperty($"{prefix}/type");

            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(type))
                continue;

            var channelCount = GetNullableIntProperty($"{prefix}/demux-channel-count");

            tracks.Add(new PlayerTrack(
                id,
                type,
                GetStringProperty($"{prefix}/lang"),
                GetStringProperty($"{prefix}/title"),
                GetStringProperty($"{prefix}/codec"),
                GetBoolProperty($"{prefix}/selected"),
                GetBoolProperty($"{prefix}/external"),
                GetBoolProperty($"{prefix}/default"),
                GetBoolProperty($"{prefix}/forced"),
                channelCount,
                GetStringProperty($"{prefix}/demux-channels")));
        }

        return tracks;
    }

    public void SetAudioTrack(string id)
    {
        PlaybackLog.Write("mpv-track", $"select audio id={id}");
        SetProperty("aid", id);
    }

    public void SetSubtitleTrack(string? id)
    {
        var value = string.IsNullOrWhiteSpace(id) ? "no" : id;
        PlaybackLog.Write("mpv-track", $"select subtitle id={value}");
        SetProperty("sid", value);
    }

    public double PositionSeconds => GetDoubleProperty("time-pos");
    public double DurationSeconds => GetDoubleProperty("duration");
    public double Volume => GetDoubleProperty("volume", 100);
    public double Speed => GetDoubleProperty("speed", 1);
    public bool IsPaused => GetBoolProperty("pause");
    public bool IsBuffering => GetBoolProperty("paused-for-cache");

    public MpvDiagnosticSnapshot GetDiagnosticSnapshot()
    {
        var width = GetNullableIntProperty("video-params/w")
                    ?? GetNullableIntProperty("dwidth");
        var height = GetNullableIntProperty("video-params/h")
                     ?? GetNullableIntProperty("dheight");

        var fps = GetNullableDoubleProperty("estimated-vf-fps")
                  ?? GetNullableDoubleProperty("container-fps");

        var videoBitrate = GetNullableDoubleProperty("packet-video-bitrate")
                           ?? GetNullableDoubleProperty("video-bitrate");
        var audioBitrate = GetNullableDoubleProperty("packet-audio-bitrate")
                           ?? GetNullableDoubleProperty("audio-bitrate");

        return new MpvDiagnosticSnapshot(
            GetStringProperty("video-codec") ?? GetStringProperty("video-format") ?? "",
            GetStringProperty("audio-codec-name") ?? "",
            width,
            height,
            fps,
            videoBitrate,
            audioBitrate,
            GetStringProperty("hwdec-current") ?? "",
            GetStringProperty("current-vo") ?? GetStringProperty("vo") ?? "",
            GetStringProperty("aid") ?? "",
            GetStringProperty("sid") ?? "",
            GetNullableDoubleProperty("demuxer-cache-duration"),
            GetNullableDoubleProperty("cache-buffering-state"),
            IsBuffering,
            GetNullableDoubleProperty("avsync"));
    }

    public string DiagnosticState =>
        $"eof={GetStringProperty("eof-reached") ?? "-"}, " +
        $"abort={GetStringProperty("playback-abort") ?? "-"}, " +
        $"coreIdle={GetStringProperty("core-idle") ?? "-"}, " +
        $"idleActive={GetStringProperty("idle-active") ?? "-"}, " +
        $"seeking={GetStringProperty("seeking") ?? "-"}, " +
        $"cacheState={GetStringProperty("cache-buffering-state") ?? "-"}, " +
        $"cacheSecs={GetStringProperty("demuxer-cache-duration") ?? "-"}, " +
        $"speed={GetStringProperty("speed") ?? "-"}, " +
        $"vid={GetStringProperty("vid") ?? "-"}, aid={GetStringProperty("aid") ?? "-"}, sid={GetStringProperty("sid") ?? "-"}, " +
        $"vfmt={GetStringProperty("video-format") ?? "-"}, acodec={GetStringProperty("audio-codec-name") ?? "-"}, " +
        $"hwdec={GetStringProperty("hwdec-current") ?? "-"}, vo={GetStringProperty("vo-configured") ?? "-"}, " +
        $"avsync={GetStringProperty("avsync") ?? "-"}, apts={GetStringProperty("audio-pts") ?? "-"}, vpts={GetStringProperty("video-pts") ?? "-"}";

    private void StartEventLoop()
    {
        _eventLoopRunning = true;
        _eventThread = new Thread(EventLoop)
        {
            IsBackground = true,
            Name = "AsterPlay-mpv-events"
        };
        _eventThread.Start();
    }

    private void EventLoop()
    {
        PlaybackLog.Write("mpv-event", "event loop started");

        while (_eventLoopRunning && _handle != IntPtr.Zero)
        {
            try
            {
                var ptr = Native.mpv_wait_event(_handle, 0.25);
                if (ptr == IntPtr.Zero)
                    continue;

                var ev = Marshal.PtrToStructure<MpvEvent>(ptr);
                if (ev.EventId == MpvEventId.None)
                    continue;

                if (ev.Error < 0)
                    PlaybackLog.Write("mpv-event", $"{ev.EventId}: error={ev.Error} ({ErrorString(ev.Error)})");

                switch (ev.EventId)
                {
                    case MpvEventId.LogMessage:
                        if (ev.Data != IntPtr.Zero)
                        {
                            var msg = Marshal.PtrToStructure<MpvLogMessage>(ev.Data);
                            var prefix = Marshal.PtrToStringUTF8(msg.Prefix) ?? "mpv";
                            var level = Marshal.PtrToStringUTF8(msg.Level) ?? "";
                            var text = (Marshal.PtrToStringUTF8(msg.Text) ?? "").TrimEnd();
                            if (!string.IsNullOrWhiteSpace(text))
                                PlaybackLog.Write("mpv-log", $"[{level}] {prefix}: {text}");
                        }
                        break;

                    case MpvEventId.StartFile:
                        PlaybackLog.Write("mpv-event", "START_FILE");
                        break;

                    case MpvEventId.FileLoaded:
                        PlaybackLog.Write("mpv-event", $"FILE_LOADED | {DiagnosticState}");
                        break;

                    case MpvEventId.VideoReconfig:
                        PlaybackLog.Write("mpv-event", $"VIDEO_RECONFIG | {DiagnosticState}");
                        break;

                    case MpvEventId.AudioReconfig:
                        PlaybackLog.Write("mpv-event", $"AUDIO_RECONFIG | {DiagnosticState}");
                        break;

                    case MpvEventId.Seek:
                        PlaybackLog.Write("mpv-event", $"SEEK | {DiagnosticState}");
                        break;

                    case MpvEventId.PlaybackRestart:
                        PlaybackLog.Write("mpv-event", $"PLAYBACK_RESTART | {DiagnosticState}");
                        break;

                    case MpvEventId.EndFile:
                        if (ev.Data != IntPtr.Zero)
                        {
                            var reason = Marshal.ReadInt32(ev.Data, 0);
                            var error = Marshal.ReadInt32(ev.Data, 4);
                            PlaybackLog.Write("mpv-event",
                                $"END_FILE reason={EndReasonName(reason)}({reason}), error={error} ({ErrorString(error)}) | {DiagnosticState}");
                        }
                        else
                        {
                            PlaybackLog.Write("mpv-event", "END_FILE without data");
                        }
                        break;

                    case MpvEventId.Idle:
                        PlaybackLog.Write("mpv-event", $"IDLE | {DiagnosticState}");
                        break;

                    case MpvEventId.Shutdown:
                        PlaybackLog.Write("mpv-event", "SHUTDOWN");
                        break;

                    default:
                        PlaybackLog.Write("mpv-event", ev.EventId.ToString().ToUpperInvariant());
                        break;
                }
            }
            catch (Exception ex)
            {
                if (_eventLoopRunning)
                    PlaybackLog.Error("mpv-event", ex);
            }
        }

        PlaybackLog.Write("mpv-event", "event loop stopped");
    }

    private static string EndReasonName(int reason) => reason switch
    {
        0 => "EOF",
        2 => "STOP",
        3 => "QUIT",
        4 => "ERROR",
        5 => "REDIRECT",
        _ => "UNKNOWN"
    };

    private static string ErrorString(int error)
    {
        if (error >= 0)
            return "success";

        var ptr = Native.mpv_error_string(error);
        return ptr == IntPtr.Zero ? $"mpv error {error}" : Marshal.PtrToStringUTF8(ptr) ?? $"mpv error {error}";
    }

    private void SetOption(string name, string value)
    {
        var result = Native.mpv_set_option_string(_handle, name, value);
        PlaybackLog.Write("mpv", $"option {name}={value} -> {result}");
        if (result < 0)
            throw new InvalidOperationException($"mpv option {name} failed: {result}");
    }

    private void SetProperty(string name, string value)
    {
        if (_handle == IntPtr.Zero)
            return;

        var result = Native.mpv_set_property_string(_handle, name, value);
        PlaybackLog.Write("mpv", $"property {name}={value} -> {result}");
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

    private int GetIntProperty(string name, int fallback = 0)
    {
        var value = GetStringProperty(name);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : fallback;
    }

    private int? GetNullableIntProperty(string name)
    {
        var value = GetStringProperty(name);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
    }

    private double? GetNullableDoubleProperty(string name)
    {
        var value = GetStringProperty(name);
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
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
            PlaybackLog.Write("mpv", $"command {string.Join(" ", args.Select((x, i) => i == 1 && args.Length > 1 && args[0] == "loadfile" ? PlaybackLog.Redact(x) : x))} -> {result}");
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

        _eventLoopRunning = false;
        if (_eventThread is not null && _eventThread.IsAlive && Thread.CurrentThread != _eventThread)
            _eventThread.Join(TimeSpan.FromSeconds(1));

        PlaybackLog.Write("mpv", "terminate_destroy");
        Native.mpv_terminate_destroy(_handle);
        _handle = IntPtr.Zero;
        GC.SuppressFinalize(this);
    }

    private enum MpvEventId
    {
        None = 0,
        Shutdown = 1,
        LogMessage = 2,
        GetPropertyReply = 3,
        SetPropertyReply = 4,
        CommandReply = 5,
        StartFile = 6,
        EndFile = 7,
        FileLoaded = 8,
        Idle = 11,
        Tick = 14,
        ClientMessage = 16,
        VideoReconfig = 17,
        AudioReconfig = 18,
        Seek = 20,
        PlaybackRestart = 21,
        PropertyChange = 22,
        QueueOverflow = 24,
        Hook = 25
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MpvEvent
    {
        public MpvEventId EventId;
        public int Error;
        public ulong ReplyUserData;
        public IntPtr Data;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MpvLogMessage
    {
        public IntPtr Prefix;
        public IntPtr Level;
        public IntPtr Text;
        public int LogLevel;
    }

    private static class Native
    {
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr mpv_create();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int mpv_request_log_messages(
            IntPtr ctx,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string minLevel);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr mpv_wait_event(IntPtr ctx, double timeout);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr mpv_error_string(int error);

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
