using System;

namespace LiquidGlassWinUI
{
    /// <summary>Lifecycle state of a LiquidGlass compositor pipeline.</summary>
    public enum LiquidGlassPipelineState
    {
        Disconnected,
        Connecting,
        Connected,
        Failed
    }

    /// <summary>Describes a LiquidGlass compositor pipeline state change.</summary>
    public sealed class LiquidGlassPipelineStateChangedEventArgs : EventArgs
    {
        public LiquidGlassPipelineStateChangedEventArgs(
            LiquidGlassPipelineState state,
            string error)
        {
            State = state;
            Error = error;
        }

        public LiquidGlassPipelineState State { get; }

        public string Error { get; }
    }
}
