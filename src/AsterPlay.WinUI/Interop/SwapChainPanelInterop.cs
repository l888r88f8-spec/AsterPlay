using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Controls;

namespace AsterPlay.WinUI.Interop;

internal sealed class SwapChainPanelInterop : IDisposable
{
    [ComImport]
    [Guid("63AAD0B8-7C24-40FF-85A8-640D944CC325")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISwapChainPanelNative
    {
        [PreserveSig]
        int SetSwapChain(IntPtr swapChain);
    }

    private ISwapChainPanelNative? _nativePanel;
    private IntPtr _attachedSwapChain;

    public IntPtr AttachedSwapChain => _attachedSwapChain;

    public void Attach(SwapChainPanel panel, IntPtr swapChain)
    {
        ArgumentNullException.ThrowIfNull(panel);

        if (swapChain == IntPtr.Zero)
            throw new ArgumentException("Swap chain pointer must not be zero.", nameof(swapChain));

        if (_nativePanel is null)
            _nativePanel = WinRT.CastExtensions.As<ISwapChainPanelNative>(panel);

        if (_attachedSwapChain == swapChain)
            return;

        var hr = _nativePanel.SetSwapChain(swapChain);
        Marshal.ThrowExceptionForHR(hr);
        _attachedSwapChain = swapChain;
    }

    public void Detach()
    {
        if (_nativePanel is null)
            return;

        try
        {
            var hr = _nativePanel.SetSwapChain(IntPtr.Zero);
            Marshal.ThrowExceptionForHR(hr);
        }
        finally
        {
            _attachedSwapChain = IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        Detach();
        _nativePanel = null;
        GC.SuppressFinalize(this);
    }
}
