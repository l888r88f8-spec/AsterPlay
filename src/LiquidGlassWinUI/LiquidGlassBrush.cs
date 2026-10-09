using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using LiquidGlassWinUI.Effects;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace LiquidGlassWinUI
{
    /// <summary>
    /// A XAML composition brush that renders an Apple-style "liquid glass" material
    /// over whatever is behind the element it fills (the backdrop).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The brush owns the pipeline
    /// <c>backdrop -&gt; BlurH -&gt; BlurV -&gt; LiquidGlassEffect</c> (two 1D
    /// separable blur passes + glass), followed by an optional isolated shimmer
    /// pass, and exposes every material parameter of the
    /// glass effect as a <see cref="DependencyProperty"/>, so each one can be bound
    /// — and animated — directly from XAML with no code-behind. Set it as any
    /// element's <c>Fill</c>/<c>Background</c> (or use a <c>Rectangle</c> overlay)
    /// and let it read the backdrop through it.
    /// </para>
    /// <para>
    /// The glass effect has <c>FlattenSource</c> enabled, so DWM materializes the
    /// backdrop into a real texture the glass sampler reads. The DPI the glass
    /// scales its bands by is auto-measured from the system DPI when the brush
    /// connects (see <see cref="Dpr"/>), so no code-behind is required.
    /// </para>
    /// <para>
    /// If the effect fails to compile/link (for example, if the shader is too complex
    /// for the current DWM), the brush degrades to transparent instead of throwing,
    /// so it never crashes the host app.
    /// </para>
    /// <para>
    /// Requires an x64 process: the underlying native runtime is x64-only.
    /// </para>
    /// <example>
    /// <code>
    /// xmlns:lg="using:LiquidGlassWinUI"
    /// ...
    /// &lt;Rectangle Fill="{x:Null}"&gt;
    ///   &lt;Rectangle.Background&gt;
    ///     &lt;lg:LiquidGlassBrush RefThickness="20"
    ///                          GlareFactor="90"
    ///                          BlurAmount="1.5"
    ///                          TintA="0.1"
    ///                          ShapeRadius="0.4"/&gt;
    ///   &lt;/Rectangle.Background&gt;
    /// &lt;/Rectangle&gt;
    /// </code>
    /// </example>
    /// </remarks>
    public sealed class LiquidGlassBrush : XamlCompositionBrushBase
    {
        // Maps each parameter's DependencyProperty to its KEY (also the effect property
        // name). The full animatable path is <EffectName>.<key>.
        private static readonly Dictionary<DependencyProperty, string> s_paramKeys = new();
        private static readonly Dictionary<DependencyProperty, string> s_shimmerParamKeys = new();
        private static readonly HashSet<string> s_shimmerKeys = new()
        {
            "ShimmerX", "ShimmerY", "ShimmerStrength", "ShimmerRadius",
        };

        // Post-processing parameter keys — routed to PostProcessingEffect instead of the glass brush.
        private static readonly HashSet<string> s_postProcessKeys = new()
        {
            "BloomAmount", "Brightness", "Contrast", "Saturation",
            "Temperature", "Exposure", "Vibrance",
        };

        private static DependencyProperty RegisterParam(string key, double defaultValue)
        {
            var dp = DependencyProperty.Register(key, typeof(double), typeof(LiquidGlassBrush),
                new PropertyMetadata(defaultValue, OnParamChanged));
            s_paramKeys[dp] = key;
            return dp;
        }

        // Glass parameter: default comes from LiquidGlassEffect.Params (single source of truth).
        private static DependencyProperty RegisterGlassParam(string key)
        {
            float defaultVal = LiquidGlassEffect.Params.First(p => p.Key == key).Default;
            return RegisterParam(key, defaultVal);
        }

        private static DependencyProperty RegisterShimmerParam(
            string key,
            double defaultValue)
        {
            var dp = DependencyProperty.Register(
                key,
                typeof(double),
                typeof(LiquidGlassBrush),
                new PropertyMetadata(defaultValue, OnShimmerParamChanged));
            s_shimmerParamKeys[dp] = key;
            return dp;
        }

        // ---- dependency properties: one per material parameter ----

        // ---- Refraction ----

        /// <summary>Backing dependency property for <see cref="RefThickness"/>.</summary>
        public static readonly DependencyProperty RefThicknessProperty = RegisterGlassParam("RefThickness");
        /// <summary>Refraction edge thickness, in logical pixels (default 20).</summary>
        public double RefThickness { get => (double)GetValue(RefThicknessProperty); set => SetValue(RefThicknessProperty, value); }

        /// <summary>Backing dependency property for <see cref="RefFactor"/>.</summary>
        public static readonly DependencyProperty RefFactorProperty = RegisterGlassParam("RefFactor");
        /// <summary>Index of refraction driving how strongly the backdrop is bent (default 1.4).</summary>
        public double RefFactor { get => (double)GetValue(RefFactorProperty); set => SetValue(RefFactorProperty, value); }

        /// <summary>Backing dependency property for <see cref="RefDispersion"/>.</summary>
        public static readonly DependencyProperty RefDispersionProperty = RegisterGlassParam("RefDispersion");
        /// <summary>Chromatic dispersion spreading the refraction by wavelength (default 7).</summary>
        public double RefDispersion { get => (double)GetValue(RefDispersionProperty); set => SetValue(RefDispersionProperty, value); }

        /// <summary>Backing dependency property for <see cref="DispersionRange"/>.</summary>
        public static readonly DependencyProperty DispersionRangeProperty = RegisterGlassParam("DispersionRange");
        /// <summary>Scales chromatic dispersion: 0 = no dispersion (single UV sample), 1 = full (default 1.0).</summary>
        public double DispersionRange { get => (double)GetValue(DispersionRangeProperty); set => SetValue(DispersionRangeProperty, value); }

        /// <summary>Backing dependency property for <see cref="RefFresnelRange"/>.</summary>
        public static readonly DependencyProperty RefFresnelRangeProperty = RegisterGlassParam("RefFresnelRange");
        /// <summary>Width of the Fresnel refraction band near grazing angles (default 30).</summary>
        public double RefFresnelRange { get => (double)GetValue(RefFresnelRangeProperty); set => SetValue(RefFresnelRangeProperty, value); }

        /// <summary>Backing dependency property for <see cref="RefFresnelHardness"/>.</summary>
        public static readonly DependencyProperty RefFresnelHardnessProperty = RegisterGlassParam("RefFresnelHardness");
        /// <summary>Hardness (sharpness) of the Fresnel refraction band falloff (default 20).</summary>
        public double RefFresnelHardness { get => (double)GetValue(RefFresnelHardnessProperty); set => SetValue(RefFresnelHardnessProperty, value); }

        /// <summary>Backing dependency property for <see cref="RefFresnelFactor"/>.</summary>
        public static readonly DependencyProperty RefFresnelFactorProperty = RegisterGlassParam("RefFresnelFactor");
        /// <summary>Strength multiplier applied to the Fresnel refraction term (default 20).</summary>
        public double RefFresnelFactor { get => (double)GetValue(RefFresnelFactorProperty); set => SetValue(RefFresnelFactorProperty, value); }

        /// <summary>Backing dependency property for <see cref="Magnification"/>.</summary>
        public static readonly DependencyProperty MagnificationProperty = RegisterGlassParam("Magnification");
        /// <summary>Backdrop zoom factor centered on the glass: 1.0 = none, >1 = zoom in. Cannot go below 1 — sampling outside the backdrop content rect would read void.</summary>
        public double Magnification { get => (double)GetValue(MagnificationProperty); set => SetValue(MagnificationProperty, value); }

        // ---- Glare ----

        /// <summary>Backing dependency property for <see cref="GlareRange"/>.</summary>
        public static readonly DependencyProperty GlareRangeProperty = RegisterGlassParam("GlareRange");
        /// <summary>Angular width of the specular glare streak (default 30).</summary>
        public double GlareRange { get => (double)GetValue(GlareRangeProperty); set => SetValue(GlareRangeProperty, value); }

        /// <summary>Backing dependency property for <see cref="GlareHardness"/>.</summary>
        public static readonly DependencyProperty GlareHardnessProperty = RegisterGlassParam("GlareHardness");
        /// <summary>Hardness (sharpness) of the glare streak falloff (default 20).</summary>
        public double GlareHardness { get => (double)GetValue(GlareHardnessProperty); set => SetValue(GlareHardnessProperty, value); }

        /// <summary>Backing dependency property for <see cref="GlareFactor"/>.</summary>
        public static readonly DependencyProperty GlareFactorProperty = RegisterGlassParam("GlareFactor");
        /// <summary>Intensity of the glare highlight (default 90).</summary>
        public double GlareFactor { get => (double)GetValue(GlareFactorProperty); set => SetValue(GlareFactorProperty, value); }

        /// <summary>Backing dependency property for <see cref="GlareConvergence"/>.</summary>
        public static readonly DependencyProperty GlareConvergenceProperty = RegisterGlassParam("GlareConvergence");
        /// <summary>How tightly the glare converges toward its center (default 50).</summary>
        public double GlareConvergence { get => (double)GetValue(GlareConvergenceProperty); set => SetValue(GlareConvergenceProperty, value); }

        /// <summary>Backing dependency property for <see cref="GlareOppositeFactor"/>.</summary>
        public static readonly DependencyProperty GlareOppositeFactorProperty = RegisterGlassParam("GlareOppositeFactor");
        /// <summary>Intensity of the secondary, opposite-facing glare highlight (default 80).</summary>
        public double GlareOppositeFactor { get => (double)GetValue(GlareOppositeFactorProperty); set => SetValue(GlareOppositeFactorProperty, value); }

        /// <summary>Backing dependency property for <see cref="GlareAngle"/>.</summary>
        public static readonly DependencyProperty GlareAngleProperty = RegisterGlassParam("GlareAngle");
        /// <summary>Direction of the glare streak, in degrees (default -45).</summary>
        public double GlareAngle { get => (double)GetValue(GlareAngleProperty); set => SetValue(GlareAngleProperty, value); }

        // ---- Optional pointer-following shimmer ----

        public static readonly DependencyProperty IsShimmerEnabledProperty =
            DependencyProperty.Register(
                nameof(IsShimmerEnabled),
                typeof(bool),
                typeof(LiquidGlassBrush),
                new PropertyMetadata(false, OnShimmerEnabledChanged));

        /// <summary>
        /// Enables the isolated pointer-following reflection pass. It is disabled by
        /// default, so ordinary glass controls keep the original pipeline and cost.
        /// </summary>
        public bool IsShimmerEnabled
        {
            get => (bool)GetValue(IsShimmerEnabledProperty);
            set => SetValue(IsShimmerEnabledProperty, value);
        }

        public static readonly DependencyProperty ShimmerXProperty =
            RegisterShimmerParam("ShimmerX", 0.5);
        /// <summary>Horizontal reflection center normalized to the brush (0..1).</summary>
        public double ShimmerX { get => (double)GetValue(ShimmerXProperty); set => SetValue(ShimmerXProperty, value); }

        public static readonly DependencyProperty ShimmerYProperty =
            RegisterShimmerParam("ShimmerY", 0.5);
        /// <summary>Vertical reflection center normalized to the brush (0..1).</summary>
        public double ShimmerY { get => (double)GetValue(ShimmerYProperty); set => SetValue(ShimmerYProperty, value); }

        public static readonly DependencyProperty ShimmerStrengthProperty =
            RegisterShimmerParam("ShimmerStrength", 0.0);
        /// <summary>Reflection intensity. Zero keeps the pass visually neutral.</summary>
        public double ShimmerStrength { get => (double)GetValue(ShimmerStrengthProperty); set => SetValue(ShimmerStrengthProperty, value); }

        public static readonly DependencyProperty ShimmerRadiusProperty =
            RegisterShimmerParam("ShimmerRadius", 104.0);
        /// <summary>Horizontal reflection radius in device-independent pixels.</summary>
        public double ShimmerRadius { get => (double)GetValue(ShimmerRadiusProperty); set => SetValue(ShimmerRadiusProperty, value); }

        // ---- Blur ----

        /// <summary>Backing dependency property for <see cref="BlurAmount"/>.</summary>
        public static readonly DependencyProperty BlurAmountProperty = RegisterParam("BlurAmount", 1.0);
        /// <summary>
        /// Backdrop blur radius in pixels (default 1). Drives the separable H/V 1D
        /// blur passes upstream of the glass.
        /// </summary>
        public double BlurAmount { get => (double)GetValue(BlurAmountProperty); set => SetValue(BlurAmountProperty, value); }

        // ---- Tint ----

        /// <summary>Backing dependency property for <see cref="TintR"/>.</summary>
        public static readonly DependencyProperty TintRProperty = RegisterGlassParam("TintR");
        /// <summary>Red channel of the glass tint, 0–255 (default 255).</summary>
        public double TintR { get => (double)GetValue(TintRProperty); set => SetValue(TintRProperty, value); }

        /// <summary>Backing dependency property for <see cref="TintG"/>.</summary>
        public static readonly DependencyProperty TintGProperty = RegisterGlassParam("TintG");
        /// <summary>Green channel of the glass tint, 0–255 (default 255).</summary>
        public double TintG { get => (double)GetValue(TintGProperty); set => SetValue(TintGProperty, value); }

        /// <summary>Backing dependency property for <see cref="TintB"/>.</summary>
        public static readonly DependencyProperty TintBProperty = RegisterGlassParam("TintB");
        /// <summary>Blue channel of the glass tint, 0–255 (default 255).</summary>
        public double TintB { get => (double)GetValue(TintBProperty); set => SetValue(TintBProperty, value); }

        /// <summary>Backing dependency property for <see cref="TintA"/>.</summary>
        public static readonly DependencyProperty TintAProperty = RegisterGlassParam("TintA");
        /// <summary>Alpha (opacity) of the glass tint, 0–1 (default 0 = untinted).</summary>
        public double TintA { get => (double)GetValue(TintAProperty); set => SetValue(TintAProperty, value); }

        // Exposure moved to PostProcessingEffect.

        // ---- Bloom (PostProcessingEffect) ----

        /// <summary>Backing dependency property for <see cref="BloomAmount"/>.</summary>
        public static readonly DependencyProperty BloomAmountProperty = RegisterParam("BloomAmount", 0.0);
        /// <summary>Cross-fade between blurred and raw backdrop: 0 = pure blurred glass (default), 1 = fully sharp.</summary>
        public double BloomAmount { get => (double)GetValue(BloomAmountProperty); set => SetValue(BloomAmountProperty, value); }

        // ---- Colour Adjustments (PostProcessingEffect) ----

        /// <summary>Backing dependency property for <see cref="Brightness"/>.</summary>
        public static readonly DependencyProperty BrightnessProperty = RegisterParam("Brightness", 0.0);
        /// <summary>Additive brightness: -1 = fully dark, 0 = unchanged, +1 = fully bright.</summary>
        public double Brightness { get => (double)GetValue(BrightnessProperty); set => SetValue(BrightnessProperty, value); }

        /// <summary>Backing dependency property for <see cref="Contrast"/>.</summary>
        public static readonly DependencyProperty ContrastProperty = RegisterParam("Contrast", 1.0);
        /// <summary>Contrast multiplier around mid-grey: 0 = flat grey, 1 = unchanged, 2 = doubled.</summary>
        public double Contrast { get => (double)GetValue(ContrastProperty); set => SetValue(ContrastProperty, value); }

        /// <summary>Backing dependency property for <see cref="Saturation"/>.</summary>
        public static readonly DependencyProperty SaturationProperty = RegisterParam("Saturation", 1.0);
        /// <summary>Saturation multiplier: 0 = greyscale, 1 = unchanged, 2 = oversaturated.</summary>
        public double Saturation { get => (double)GetValue(SaturationProperty); set => SetValue(SaturationProperty, value); }

        /// <summary>Backing dependency property for <see cref="Temperature"/>.</summary>
        public static readonly DependencyProperty TemperatureProperty = RegisterParam("Temperature", 0.0);
        /// <summary>Colour temperature shift: -1 = cool (blue), 0 = unchanged, +1 = warm (yellow/red).</summary>
        public double Temperature { get => (double)GetValue(TemperatureProperty); set => SetValue(TemperatureProperty, value); }

        /// <summary>Backing dependency property for <see cref="Exposure"/> (PostProcessingEffect).</summary>
        public static readonly DependencyProperty ExposureProperty = RegisterParam("Exposure", 1.0);
        /// <summary>Exposure gain: 0.5 = half brightness, 1 = unchanged, 2 = double brightness.</summary>
        public double Exposure { get => (double)GetValue(ExposureProperty); set => SetValue(ExposureProperty, value); }

        /// <summary>Backing dependency property for <see cref="Vibrance"/>.</summary>
        public static readonly DependencyProperty VibranceProperty = RegisterParam("Vibrance", 0.0);
        /// <summary>Smart vibrance boost targeting low-saturation regions: 0 = off, 1 = full.</summary>
        public double Vibrance { get => (double)GetValue(VibranceProperty); set => SetValue(VibranceProperty, value); }

        // ---- Shape ----

        /// <summary>Backing dependency property for <see cref="ShapeRadius"/>.</summary>
        public static readonly DependencyProperty ShapeRadiusProperty = RegisterGlassParam("ShapeRadius");
        /// <summary>Corner radius as a 0–1 fraction of the shorter half-side (default 0.4).</summary>
        public double ShapeRadius { get => (double)GetValue(ShapeRadiusProperty); set => SetValue(ShapeRadiusProperty, value); }

        /// <summary>Backing dependency property for <see cref="ShapeRoundness"/>.</summary>
        public static readonly DependencyProperty ShapeRoundnessProperty = RegisterGlassParam("ShapeRoundness");
        /// <summary>Superellipse roundness exponent n (default 5).</summary>
        public double ShapeRoundness { get => (double)GetValue(ShapeRoundnessProperty); set => SetValue(ShapeRoundnessProperty, value); }

        /// <summary>
        /// Optional DPI override (physical px per logical px). Leave at 0 (the
        /// default) to auto-measure from the system DPI when the brush connects, so
        /// the brush is fully usable from XAML with no code-behind. If set to a value
        /// greater than 0, that value is used as-is.
        /// </summary>
        public float Dpr { get; set; }

        // ---- effect pipeline (built lazily when the brush attaches) ----
        //
        // FACTORY POOLING: CompositionEffectFactory registers animatable properties
        // in the compositor's global tracking table. The compositor has a hard cap of
        // 256 animatable properties aggregate across all factories. To avoid hitting
        // this cap when pages are navigated (and brushes are disconnected/reconnected),
        // factories are created ONCE, stored statically, and reused by every brush
        // instance. Only the brushes themselves (created from the pooled factories)
        // are per-instance and disposed in OnDisconnected.
        private static CompositionEffectFactory s_hBlurFactory;
        private static CompositionEffectFactory s_vBlurFactory;
        private static CompositionEffectFactory s_glassFactory;
        private static CompositionEffectFactory s_postProcessFactory;
        private static CompositionEffectFactory s_shimmerFactory;
        private static readonly object s_poolLock = new();

        private Compositor _compositor;
        private CompositionEffectBrush _glassBrush;
        private CompositionEffectBrush _hBlurBrush;   // separable blur (H pass)
        private CompositionEffectBrush _vBlurBrush;   // separable blur (V pass)
        private CompositionEffectBrush _postProcessBrush; // bloom + colour adjustments
        private CompositionEffectBrush _shimmerBrush; // optional isolated reflection pass
        private CompositionBrush _backdropBrush;        // raw backdrop source (tracked for disposal on toggle)
        private bool _blurBypassed;                   // true when BlurAmount <= 0 (blur chain disconnected)
        private float _effectiveDpr = 1.0f;

        /// <summary>Error raised while constructing the optional shimmer pass.</summary>
        public string ShimmerError { get; private set; }

        /// <summary>Whether this instance currently routes through the shimmer pass.</summary>
        public bool IsShimmerActive => IsShimmerEnabled && _shimmerBrush != null;

        // Creating a CompositionEffectBrush is not equivalent to having its
        // custom shaders processed by the compositor. Track EFFECT commit
        // completion for every visible brush before dismissing startup.
        private static readonly object s_effectCommitSync = new();
        private static int s_pendingEffectCommitCount;
        private static readonly HashSet<LiquidGlassBrush> s_pendingEffectBrushes = new();
        private static TaskCompletionSource<bool> s_effectCommitCompletion =
            NewEffectCompletionSource();
        private CompositionCommitBatch _firstEffectCommitBatch;
        private bool _effectCommitPending;

        private static TaskCompletionSource<bool> NewEffectCompletionSource() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static int PendingEffectCommitCount
        {
            get
            {
                lock (s_effectCommitSync)
                    return s_pendingEffectCommitCount;
            }
        }

        public static Task WaitForPendingEffectCommitsAsync()
        {
            lock (s_effectCommitSync)
            {
                return s_pendingEffectCommitCount == 0
                    ? Task.CompletedTask
                    : s_effectCommitCompletion.Task;
            }
        }

        /// <summary>
        /// If an effect batch never completes (unsupported driver/runtime),
        /// fail open with a transparent control background instead of leaving
        /// the application forever behind the native startup window.
        /// This only changes brushes whose first GPU commit is still pending.
        /// </summary>
        public static int FallBackPendingEffectCommits()
        {
            LiquidGlassBrush[] pending;
            lock (s_effectCommitSync)
                pending = s_pendingEffectBrushes.ToArray();

            foreach (var brush in pending)
            {
                if (!brush._effectCommitPending)
                    continue;

                brush.CompleteFirstEffectCommit(false);
                try
                {
                    brush.CompositionBrush =
                        brush._compositor?.CreateColorBrush(Colors.Transparent);
                }
                catch
                {
                    // A pending shader should not abort the window startup.
                }

                brush.SetPipelineState(
                    LiquidGlassPipelineState.Failed,
                    "First GPU effect commit did not complete; using transparent fallback");
            }

            return pending.Length;
        }

        private void TrackFirstEffectCommit()
        {
            // Obtain the batch BEFORE exposing the effect brush. The factory
            // can exist already, but the live effect chain must be committed
            // before we report the pipeline as ready.
            var batch = _compositor.GetCommitBatch(CompositionBatchTypes.Effect);

            lock (s_effectCommitSync)
            {
                if (s_pendingEffectCommitCount++ == 0)
                    s_effectCommitCompletion = NewEffectCompletionSource();
                s_pendingEffectBrushes.Add(this);
            }

            _firstEffectCommitBatch = batch;
            _effectCommitPending = true;
            batch.Completed += FirstEffectCommitCompleted;
        }

        private void FirstEffectCommitCompleted(
            object sender,
            CompositionBatchCompletedEventArgs args)
        {
            var queue = DispatcherQueue;
            if (queue != null && !queue.HasThreadAccess)
            {
                // Brush and XAML dependency properties are UI-thread-owned.
                // If the dispatcher is shutting down, OnDisconnected also
                // cancels the outstanding registration.
                queue.TryEnqueue(() => CompleteFirstEffectCommit(true));
            }
            else
            {
                CompleteFirstEffectCommit(true);
            }
        }

        private void CompleteFirstEffectCommit(bool connected)
        {
            if (!_effectCommitPending)
                return;

            _effectCommitPending = false;
            var batch = _firstEffectCommitBatch;
            _firstEffectCommitBatch = null;
            if (batch != null)
                batch.Completed -= FirstEffectCommitCompleted;

            lock (s_effectCommitSync)
            {
                s_pendingEffectBrushes.Remove(this);
                if (--s_pendingEffectCommitCount == 0)
                    s_effectCommitCompletion.TrySetResult(true);
            }

            if (connected &&
                PipelineState == LiquidGlassPipelineState.Connecting &&
                (ReferenceEquals(CompositionBrush, _glassBrush) ||
                 ReferenceEquals(CompositionBrush, _shimmerBrush)))
            {
                SetPipelineState(LiquidGlassPipelineState.Connected);
            }
        }

        /// <summary>
        /// If the effect pipeline fails to compile or link (e.g. shader too complex for
        /// the current DWM, or the native hook cannot be installed), the exception message
        /// and stack trace are written here. The brush degrades to a solid red fill
        /// instead of crashing the host app. Check this property after the brush connects
        /// to diagnose pipeline failures.
        /// </summary>
        public static string LastError { get; private set; }

        /// <summary>The current connection state of this brush's compositor pipeline.</summary>
        public LiquidGlassPipelineState PipelineState { get; private set; } =
            LiquidGlassPipelineState.Disconnected;

        /// <summary>The connection error for this brush instance, if any.</summary>
        public string PipelineError { get; private set; }

        /// <summary>Raised whenever the compositor pipeline changes state.</summary>
        public event EventHandler<LiquidGlassPipelineStateChangedEventArgs> PipelineStateChanged;

        private void SetPipelineState(
            LiquidGlassPipelineState state,
            string error = null)
        {
            if (PipelineState == state &&
                string.Equals(PipelineError, error, StringComparison.Ordinal))
            {
                return;
            }

            PipelineState = state;
            PipelineError = error;

            try
            {
                PipelineStateChanged?.Invoke(
                    this,
                    new LiquidGlassPipelineStateChangedEventArgs(
                        state,
                        error));
            }
            catch
            {
                // A consumer's diagnostic callback must not break the effect.
            }
        }

        /// <summary>
        /// Builds the glass pipeline and connects it. Called by XAML when the brush
        /// is first used; not called directly from user code.
        /// </summary>
        protected override void OnConnected()
        {
            if (CompositionBrush != null)
            {
                if (!_effectCommitPending &&
                    PipelineState != LiquidGlassPipelineState.Failed)
                    SetPipelineState(LiquidGlassPipelineState.Connected);
                return;
            }

            LastError = null;
            ShimmerError = null;
            SetPipelineState(
                LiquidGlassPipelineState.Connecting);

            try
            {
                _compositor = CompositionTarget.GetCompositorForCurrentThread();
                _effectiveDpr = Dpr > 0 ? Dpr : MeasureDpr();

                CompositionEffectFactory hFactory, vFactory, gFactory, postProcessFactory;
                lock (s_poolLock)
                {
                    if (s_hBlurFactory == null)
                    {
                        s_hBlurFactory = _compositor.CreateEffectFactory(
                            new BlurHEffect().Create(),
                            new List<string> { BlurHEffect.BlurAmountPropertyPath });

                        s_vBlurFactory = _compositor.CreateEffectFactory(
                            new BlurVEffect().Create(),
                            new List<string> { BlurVEffect.BlurAmountPropertyPath });

                        var glassEffect = new LiquidGlassEffect
                        {
                            Dpr = _effectiveDpr
                        }.Create();
                        List<string> glassPaths = LiquidGlassEffect.Params
                            .Select(p => LiquidGlassEffect.EffectNameValue + "." + p.Key)
                            .ToList();
                        s_glassFactory = _compositor.CreateEffectFactory(glassEffect, glassPaths);

                        s_postProcessFactory = _compositor.CreateEffectFactory(
                            new PostProcessingEffect().Create(),
                            new List<string>
                            {
                                PostProcessingEffect.BloomAmountPropertyPath,
                                PostProcessingEffect.BrightnessPropertyPath,
                                PostProcessingEffect.ContrastPropertyPath,
                                PostProcessingEffect.SaturationPropertyPath,
                                PostProcessingEffect.TemperaturePropertyPath,
                                PostProcessingEffect.ExposurePropertyPath,
                                PostProcessingEffect.VibrancePropertyPath,
                            });
                    }
                    hFactory = s_hBlurFactory;
                    vFactory = s_vBlurFactory;
                    gFactory = s_glassFactory;
                    postProcessFactory = s_postProcessFactory;
                }

                _backdropBrush = _compositor.CreateBackdropBrush();
                // Create per-instance brushes from the pooled factories.
                _hBlurBrush = hFactory.CreateBrush();
                _hBlurBrush.SetSourceParameter("Backdrop", _backdropBrush);

                _vBlurBrush = vFactory.CreateBrush();
                _vBlurBrush.SetSourceParameter("Backdrop", _hBlurBrush);

                _postProcessBrush = postProcessFactory.CreateBrush();
                _postProcessBrush.SetSourceParameter("RawBackdrop", _backdropBrush);

                _glassBrush = gFactory.CreateBrush();
                _glassBrush.SetSourceParameter("Backdrop", _postProcessBrush);

                // When BlurAmount is 0 at connect time, both bloom sources see
                // the raw backdrop (lerp(raw, raw, B) == raw — identity). When
                // > 0, the bloom blends the blurred and raw backdrops.
                if (BlurAmount <= 0)
                {
                    _blurBypassed = true;
                    _postProcessBrush.SetSourceParameter("Backdrop", _backdropBrush);
                }
                else
                {
                    _postProcessBrush.SetSourceParameter("Backdrop", _vBlurBrush);
                }

                // Push every parameter's current value (DPs may have been set before
                // the brush connected). After this, OnParamChanged keeps them in sync.
                foreach (var pair in s_paramKeys)
                {
                    ApplyValue(pair.Value, (float)(double)GetValue(pair.Key));
                }

                if (IsShimmerEnabled)
                    TryEnsureShimmerBrush();

                // The shader may be queued for DWM processing long after
                // the effect graph is constructed. Do not emit Connected until
                // its first Effect commit has completed.
                TrackFirstEffectCommit();
                CompositionBrush = _shimmerBrush ?? _glassBrush;
            }
            catch (Exception e)
            {
                CompleteFirstEffectCommit(false);
                LastError = e.Message + "\n" + e.StackTrace;

                _hBlurBrush?.Dispose();
                _vBlurBrush?.Dispose();
                _postProcessBrush?.Dispose();
                _shimmerBrush?.Dispose();
                _backdropBrush?.Dispose();
                _glassBrush?.Dispose();

                CompositionBrush =
                    _compositor?.CreateColorBrush(Colors.Red);
                SetPipelineState(
                    LiquidGlassPipelineState.Failed,
                    LastError);
            }
        }

        /// <summary>
        /// Drops the effect pipeline. Called by XAML when the brush is no longer used;
        /// not called directly from user code.
        /// </summary>
        protected override void OnDisconnected()
        {
            CompleteFirstEffectCommit(false);
            // Dispose per-instance brushes. The factories are pooled statically
            // and shared across all brush instances — they must NOT be disposed
            // here or subsequent brush instances would fail to create brushes.
            _hBlurBrush?.Dispose();
            _vBlurBrush?.Dispose();
            _postProcessBrush?.Dispose();
            _backdropBrush?.Dispose();

            // The active brush is either the optional shimmer output or the base
            // glass output. The GPU-timeout fallback uses a separate transparent
            // color brush; dispose every owned brush without double-disposing.
            var activeBrush = CompositionBrush;
            activeBrush?.Dispose();
            if (!ReferenceEquals(activeBrush, _shimmerBrush))
                _shimmerBrush?.Dispose();
            if (!ReferenceEquals(activeBrush, _glassBrush))
                _glassBrush?.Dispose();

            CompositionBrush = null;
            _glassBrush = null;
            _postProcessBrush = null;
            _shimmerBrush = null;
            _hBlurBrush = null;
            _vBlurBrush = null;
            _backdropBrush = null;
            _compositor = null;

            SetPipelineState(
                LiquidGlassPipelineState.Disconnected);
        }

        private static void OnParamChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var brush = (LiquidGlassBrush)d;
            if (s_paramKeys.TryGetValue(e.Property, out string key))
            {
                brush.ApplyValue(key, (float)(double)e.NewValue);
            }
        }

        private static void OnShimmerParamChanged(
            DependencyObject d,
            DependencyPropertyChangedEventArgs e)
        {
            var brush = (LiquidGlassBrush)d;
            if (s_shimmerParamKeys.TryGetValue(e.Property, out string key))
                brush.ApplyShimmerValue(key, (float)(double)e.NewValue);
        }

        private static void OnShimmerEnabledChanged(
            DependencyObject d,
            DependencyPropertyChangedEventArgs e)
        {
            ((LiquidGlassBrush)d).UpdateShimmerPipeline();
        }

        private static CompositionEffectFactory GetOrCreateShimmerFactory(
            Compositor compositor)
        {
            lock (s_poolLock)
            {
                if (s_shimmerFactory == null)
                {
                    s_shimmerFactory = compositor.CreateEffectFactory(
                        new ShimmerEffect().Create(),
                        new List<string>
                        {
                            ShimmerEffect.XPropertyPath,
                            ShimmerEffect.YPropertyPath,
                            ShimmerEffect.StrengthPropertyPath,
                            ShimmerEffect.RadiusPropertyPath,
                            ShimmerEffect.DprPropertyPath,
                        });
                }

                return s_shimmerFactory;
            }
        }

        private bool TryEnsureShimmerBrush()
        {
            if (_shimmerBrush != null)
                return true;
            if (_compositor == null || _glassBrush == null)
                return false;

            try
            {
                var factory = GetOrCreateShimmerFactory(_compositor);
                var shimmerBrush = factory.CreateBrush();
                _shimmerBrush = shimmerBrush;
                shimmerBrush.SetSourceParameter("Source", _glassBrush);

                foreach (var pair in s_shimmerParamKeys)
                {
                    ApplyShimmerValue(
                        pair.Value,
                        (float)(double)GetValue(pair.Key));
                }
                _shimmerBrush.Properties.InsertScalar(
                    ShimmerEffect.DprPropertyPath,
                    _effectiveDpr);

                ShimmerError = null;
                return true;
            }
            catch (Exception e)
            {
                ShimmerError = e.Message + "\n" + e.StackTrace;
                _shimmerBrush?.Dispose();
                _shimmerBrush = null;
                return false;
            }
        }

        private void UpdateShimmerPipeline()
        {
            if (_compositor == null || _glassBrush == null)
                return;

            if (IsShimmerEnabled && TryEnsureShimmerBrush())
                CompositionBrush = _shimmerBrush;
            else
                CompositionBrush = _glassBrush;
        }

        private void ApplyShimmerValue(string key, float value)
        {
            _shimmerBrush?.Properties.InsertScalar(
                ShimmerEffect.EffectNameValue + "." + key,
                value);
        }

        // Route one parameter to the right effect brush. Post-processing params
        // (bloom + colour adjustments) go to the PostProcessingEffect. BlurAmount
        // drives both 1D separable blur passes; when it drops to ≤ 0 the blur chain
        // is bypassed at the post-process stage. Glass params go to the
        // LiquidGlassEffect. No-op until the pipeline is connected; OnConnected
        // applies all values at once.
        private void ApplyValue(string key, float value)
        {
            // Post-processing params: route to the PostProcessingEffect brush.
            if (s_postProcessKeys.Contains(key))
            {
                _postProcessBrush?.Properties.InsertScalar(
                    PostProcessingEffect.EffectNameValue + "." + key, value);
                return;
            }

            if (key == "BlurAmount")
            {
                bool bypass = value <= 0;
                if (bypass != _blurBypassed && _postProcessBrush != null)
                {
                    _blurBypassed = bypass;
                    // Swap the post-process "Backdrop" source only — "RawBackdrop"
                    // stays connected to _backdropBrush; glass always reads from
                    // _postProcessBrush. No backdrop dispose/recreate needed.
                    _postProcessBrush.SetSourceParameter("Backdrop",
                        bypass ? _backdropBrush : _vBlurBrush);

                    // SetSourceParameter resets animatable properties, so re-sync
                    // post-processing and glass parameters after the swap.
                    foreach (var kv in s_paramKeys)
                    {
                        if (kv.Value == "BlurAmount") continue;
                        float v = (float)(double)GetValue(kv.Key);
                        if (s_postProcessKeys.Contains(kv.Value))
                        {
                            _postProcessBrush.Properties.InsertScalar(
                                PostProcessingEffect.EffectNameValue + "." + kv.Value, v);
                        }
                        else
                        {
                            _glassBrush.Properties.InsertScalar(
                                LiquidGlassEffect.EffectNameValue + "." + kv.Value, v);
                        }
                    }
                }
                if (!bypass)
                {
                    _hBlurBrush?.Properties.InsertScalar(BlurHEffect.BlurAmountPropertyPath, value);
                    _vBlurBrush?.Properties.InsertScalar(BlurVEffect.BlurAmountPropertyPath, value);
                }
                return;
            }
            _glassBrush?.Properties.InsertScalar(LiquidGlassEffect.EffectNameValue + "." + key, value);
        }

        /// <summary>
        /// Animates a named scalar property using a compositor-thread
        /// <see cref="Microsoft.UI.Composition.ScalarKeyFrameAnimation"/> with cubic
        /// ease-out. Runs entirely on the compositor thread — no UI-thread timers.
        /// </summary>
        /// <param name="key">Parameter key (e.g. "TintA", "GlareAngle").</param>
        /// <param name="to">Target value.</param>
        /// <param name="durationMs">Animation duration in milliseconds.</param>
        public void AnimateScalar(string key, float to, double durationMs)
        {
            if (_compositor == null) return;

            if (s_shimmerKeys.Contains(key))
            {
                if (_shimmerBrush == null) return;
                var path = ShimmerEffect.EffectNameValue + "." + key;
                var anim = _compositor.CreateScalarKeyFrameAnimation();
                anim.Duration = TimeSpan.FromMilliseconds(durationMs);
                anim.InsertKeyFrame(1.0f, to,
                    _compositor.CreateCubicBezierEasingFunction(
                        new System.Numerics.Vector2(0.215f, 0.61f),
                        new System.Numerics.Vector2(0.355f, 1.0f)));
                _shimmerBrush.Properties.StartAnimation(path, anim);
                return;
            }

            // Post-processing keys animate on the PostProcessingEffect brush.
            if (s_postProcessKeys.Contains(key))
            {
                if (_postProcessBrush == null) return;
                var path = PostProcessingEffect.EffectNameValue + "." + key;
                var anim = _compositor.CreateScalarKeyFrameAnimation();
                anim.Duration = TimeSpan.FromMilliseconds(durationMs);
                anim.InsertKeyFrame(1.0f, to,
                    _compositor.CreateCubicBezierEasingFunction(
                        new System.Numerics.Vector2(0.215f, 0.61f),
                        new System.Numerics.Vector2(0.355f, 1.0f)));
                _postProcessBrush.Properties.StartAnimation(path, anim);
                return;
            }

            if (_glassBrush == null) return;

            var fullPath = LiquidGlassEffect.EffectNameValue + "." + key;
            var animation = _compositor.CreateScalarKeyFrameAnimation();
            animation.Duration = TimeSpan.FromMilliseconds(durationMs);
            animation.InsertKeyFrame(1.0f, to,
                _compositor.CreateCubicBezierEasingFunction(
                    new System.Numerics.Vector2(0.215f, 0.61f),   // ease-out cubic
                    new System.Numerics.Vector2(0.355f, 1.0f)));

            _glassBrush.Properties.StartAnimation(fullPath, animation);
        }

        /// <summary>
        /// Animates every animatable material parameter toward the values stored in
        /// <paramref name="target"/>. All animations are batched into a single
        /// <see cref="CompositionCommitBatch"/> so they start on the same compositor
        /// frame. <c>BlurAmount</c> is set directly (it lives on the H/V blur brushes,
        /// not the glass cbuffer).
        /// </summary>
        /// <remarks>
        /// The <paramref name="target"/> brush does not need to be connected — only its
        /// <see cref="DependencyProperty"/> values are read.
        /// </remarks>
        public void TransitionTo(LiquidGlassBrush target, double durationMs)
        {
            if (_glassBrush == null || _compositor == null || target == null) return;

            var batch = _compositor.GetCommitBatch(CompositionBatchTypes.Animation);

            foreach (var (dp, key) in s_paramKeys)
            {
                var targetValue = (float)(double)target.GetValue(dp);

                // BlurAmount drives the separable H/V blur passes, not the glass
                // cbuffer. Set it directly so OnParamChanged → ApplyValue updates
                // both blur brushes. Not animated, but it's 1 param out of 22.
                if (key == "BlurAmount")
                {
                    SetValue(dp, (double)targetValue);
                }
                else if (s_postProcessKeys.Contains(key))
                {
                    // Post-processing params animate on the PostProcessingEffect brush.
                    if (_postProcessBrush != null && _compositor != null)
                    {
                        var path = PostProcessingEffect.EffectNameValue + "." + key;
                        var anim = _compositor.CreateScalarKeyFrameAnimation();
                        anim.Duration = TimeSpan.FromMilliseconds(durationMs);
                        anim.InsertKeyFrame(1.0f, targetValue,
                            _compositor.CreateCubicBezierEasingFunction(
                                new System.Numerics.Vector2(0.215f, 0.61f),
                                new System.Numerics.Vector2(0.355f, 1.0f)));
                        _postProcessBrush.Properties.StartAnimation(path, anim);
                    }
                }
                else
                {
                    AnimateScalar(key, targetValue, durationMs);
                }
            }
        }

        // System DPI (physical px per logical px). GetDpiForSystem needs no window
        // handle, so the brush can measure it itself in OnConnected — this is what
        // makes the brush usable from XAML with no code-behind. (Returns the primary
        // monitor's DPI; falls back to 1.0 on any failure.)
        [DllImport("user32.dll")]
        private static extern uint GetDpiForSystem();

        private static float MeasureDpr()
        {
            try
            {
                return GetDpiForSystem() / 96f;
            }
            catch
            {
                return 1.0f;
            }
        }
    }
}
