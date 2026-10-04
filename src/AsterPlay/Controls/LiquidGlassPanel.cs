using System.Windows.Media;

namespace AsterPlay.Controls;

/// <summary>
/// WPF-native liquid-glass surface inspired by the material model used by
/// LiquidGlassWinUI. WPF cannot consume WinUI's XamlCompositionBrushBase directly,
/// so this control keeps the same visual hierarchy (tint, bloom, edge light and glare)
/// inside the existing WPF visual tree.
/// </summary>
public sealed class LiquidGlassPanel : ContentControl
{
    public static readonly DependencyProperty CornerRadiusProperty =
        DependencyProperty.Register(
            nameof(CornerRadius),
            typeof(CornerRadius),
            typeof(LiquidGlassPanel),
            new FrameworkPropertyMetadata(new CornerRadius(24)));

    public static readonly DependencyProperty GlareOpacityProperty =
        DependencyProperty.Register(
            nameof(GlareOpacity),
            typeof(double),
            typeof(LiquidGlassPanel),
            new FrameworkPropertyMetadata(0.28d));

    public static readonly DependencyProperty BloomOpacityProperty =
        DependencyProperty.Register(
            nameof(BloomOpacity),
            typeof(double),
            typeof(LiquidGlassPanel),
            new FrameworkPropertyMetadata(0.30d));

    public static readonly DependencyProperty EdgeLightOpacityProperty =
        DependencyProperty.Register(
            nameof(EdgeLightOpacity),
            typeof(double),
            typeof(LiquidGlassPanel),
            new FrameworkPropertyMetadata(0.52d));

    public static readonly DependencyProperty IsInteractiveProperty =
        DependencyProperty.Register(
            nameof(IsInteractive),
            typeof(bool),
            typeof(LiquidGlassPanel),
            new FrameworkPropertyMetadata(false));

    public CornerRadius CornerRadius
    {
        get => (CornerRadius)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public double GlareOpacity
    {
        get => (double)GetValue(GlareOpacityProperty);
        set => SetValue(GlareOpacityProperty, Math.Clamp(value, 0d, 1d));
    }

    public double BloomOpacity
    {
        get => (double)GetValue(BloomOpacityProperty);
        set => SetValue(BloomOpacityProperty, Math.Clamp(value, 0d, 1d));
    }

    public double EdgeLightOpacity
    {
        get => (double)GetValue(EdgeLightOpacityProperty);
        set => SetValue(EdgeLightOpacityProperty, Math.Clamp(value, 0d, 1d));
    }

    public bool IsInteractive
    {
        get => (bool)GetValue(IsInteractiveProperty);
        set => SetValue(IsInteractiveProperty, value);
    }
}
