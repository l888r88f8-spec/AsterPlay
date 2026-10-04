using System.Runtime.InteropServices;

namespace AsterPlay.Services.Mpv;

public sealed class MpvRenderContext : IDisposable
{
    private const string MpvDll = "libmpv-2.dll";
    private const int RenderParamInvalid = 0;
    private const int RenderParamApiType = 1;
    private const int RenderParamOpenGlInitParams = 2;
    private const int RenderParamOpenGlFbo = 3;
    private const int RenderParamFlipY = 4;

    private readonly MpvClient _client;
    private readonly Action _requestRender;
    private readonly GetProcAddressCallback _getProcAddressCallback;
    private readonly RenderUpdateCallback _renderUpdateCallback;
    private readonly IntPtr _openGlModule;

    private IntPtr _renderContext;
    private IntPtr _renderParams;
    private IntPtr _fbo;
    private IntPtr _flipY;
    private bool _disposed;

    public MpvRenderContext(MpvClient client, Action requestRender)
    {
        _client = client;
        _requestRender = requestRender;
        _getProcAddressCallback = ResolveOpenGlProcAddress;
        _renderUpdateCallback = OnRenderUpdate;

        _openGlModule = NativeLibrary.Load("opengl32.dll");
    }

    public bool IsInitialized => _renderContext != IntPtr.Zero;

    public void Initialize()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_renderContext != IntPtr.Zero)
            return;

        var apiType = Marshal.StringToCoTaskMemUTF8("opengl");
        var initParams = IntPtr.Zero;
        var createParams = IntPtr.Zero;

        try
        {
            var glInit = new MpvOpenGlInitParams
            {
                GetProcAddress = Marshal.GetFunctionPointerForDelegate(_getProcAddressCallback),
                GetProcAddressContext = IntPtr.Zero
            };

            initParams = Marshal.AllocHGlobal(Marshal.SizeOf<MpvOpenGlInitParams>());
            Marshal.StructureToPtr(glInit, initParams, false);

            var parameters = new[]
            {
                new MpvRenderParam(RenderParamApiType, apiType),
                new MpvRenderParam(RenderParamOpenGlInitParams, initParams),
                new MpvRenderParam(RenderParamInvalid, IntPtr.Zero)
            };

            createParams = AllocateParams(parameters);

            var result = Native.mpv_render_context_create(
                out _renderContext,
                _client.Handle,
                createParams);

            PlaybackLog.Write("mpv-render", $"mpv_render_context_create -> {result}");
            if (result < 0 || _renderContext == IntPtr.Zero)
                throw new InvalidOperationException($"mpv_render_context_create failed: {result}");

            Native.mpv_render_context_set_update_callback(
                _renderContext,
                _renderUpdateCallback,
                IntPtr.Zero);

            PrepareRenderParameters();

            PlaybackLog.Write("mpv-render", "OpenGL render context initialized");
        }
        finally
        {
            if (createParams != IntPtr.Zero)
                Marshal.FreeHGlobal(createParams);

            if (initParams != IntPtr.Zero)
                Marshal.FreeHGlobal(initParams);

            if (apiType != IntPtr.Zero)
                Marshal.FreeCoTaskMem(apiType);
        }
    }

    public void Render(int framebuffer, int width, int height)
    {
        if (_disposed ||
            _renderContext == IntPtr.Zero ||
            _renderParams == IntPtr.Zero ||
            width <= 0 ||
            height <= 0)
        {
            return;
        }

        var target = new MpvOpenGlFbo
        {
            Framebuffer = framebuffer,
            Width = width,
            Height = height,
            InternalFormat = 0
        };

        Marshal.StructureToPtr(target, _fbo, false);
        Marshal.WriteInt32(_flipY, 0);

        Native.mpv_render_context_render(_renderContext, _renderParams);
    }

    private void PrepareRenderParameters()
    {
        _fbo = Marshal.AllocHGlobal(Marshal.SizeOf<MpvOpenGlFbo>());
        _flipY = Marshal.AllocHGlobal(sizeof(int));

        var parameters = new[]
        {
            new MpvRenderParam(RenderParamOpenGlFbo, _fbo),
            new MpvRenderParam(RenderParamFlipY, _flipY),
            new MpvRenderParam(RenderParamInvalid, IntPtr.Zero)
        };

        _renderParams = AllocateParams(parameters);
    }

    private void OnRenderUpdate(IntPtr context)
    {
        try
        {
            _requestRender();
        }
        catch
        {
            // The WPF dispatcher may already be shutting down.
        }
    }

    private IntPtr ResolveOpenGlProcAddress(IntPtr context, IntPtr namePointer)
    {
        var name = Marshal.PtrToStringUTF8(namePointer);
        if (string.IsNullOrWhiteSpace(name))
            return IntPtr.Zero;

        var address = Native.wglGetProcAddress(name);

        // wglGetProcAddress uses several small sentinel values for failure.
        var value = address.ToInt64();
        if (address != IntPtr.Zero &&
            value != 1 &&
            value != 2 &&
            value != 3 &&
            value != -1)
        {
            return address;
        }

        return NativeLibrary.TryGetExport(_openGlModule, name, out address)
            ? address
            : IntPtr.Zero;
    }

    private static IntPtr AllocateParams(IReadOnlyList<MpvRenderParam> parameters)
    {
        var size = Marshal.SizeOf<MpvRenderParam>();
        var pointer = Marshal.AllocHGlobal(size * parameters.Count);

        for (var index = 0; index < parameters.Count; index++)
        {
            Marshal.StructureToPtr(
                parameters[index],
                IntPtr.Add(pointer, size * index),
                false);
        }

        return pointer;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_renderContext != IntPtr.Zero)
        {
            PlaybackLog.Write("mpv-render", "mpv_render_context_free");
            Native.mpv_render_context_free(_renderContext);
            _renderContext = IntPtr.Zero;
        }

        if (_renderParams != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_renderParams);
            _renderParams = IntPtr.Zero;
        }

        if (_fbo != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_fbo);
            _fbo = IntPtr.Zero;
        }

        if (_flipY != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_flipY);
            _flipY = IntPtr.Zero;
        }

        if (_openGlModule != IntPtr.Zero)
            NativeLibrary.Free(_openGlModule);

        GC.SuppressFinalize(this);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr GetProcAddressCallback(IntPtr context, IntPtr name);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void RenderUpdateCallback(IntPtr context);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct MpvRenderParam
    {
        public readonly int Type;
        public readonly IntPtr Data;

        public MpvRenderParam(int type, IntPtr data)
        {
            Type = type;
            Data = data;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MpvOpenGlInitParams
    {
        public IntPtr GetProcAddress;
        public IntPtr GetProcAddressContext;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MpvOpenGlFbo
    {
        public int Framebuffer;
        public int Width;
        public int Height;
        public int InternalFormat;
    }

    private static class Native
    {
        [DllImport(MpvDll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int mpv_render_context_create(
            out IntPtr renderContext,
            IntPtr mpvHandle,
            IntPtr parameters);

        [DllImport(MpvDll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void mpv_render_context_set_update_callback(
            IntPtr renderContext,
            RenderUpdateCallback callback,
            IntPtr callbackContext);

        [DllImport(MpvDll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void mpv_render_context_render(
            IntPtr renderContext,
            IntPtr parameters);

        [DllImport(MpvDll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void mpv_render_context_free(IntPtr renderContext);

        [DllImport("opengl32.dll", CharSet = CharSet.Ansi)]
        internal static extern IntPtr wglGetProcAddress(string procedureName);
    }
}
