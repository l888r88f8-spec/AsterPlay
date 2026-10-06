using AsterPlay.WinUI.Services;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Numerics;
using Windows.Foundation;
using Windows.Graphics.DirectX;
using Windows.UI;

namespace AsterPlay.WinUI.Controls;

/// <summary>
/// Renders a live liquid-glass surface from the current XAML output of another
/// UIElement. The source is captured without this control, so the refraction
/// layer never feeds back into its own next frame.
/// </summary>
public sealed class LiveLiquidGlassSurface : UserControl
{
    private const int CaptureIntervalMilliseconds = 100;
    private const double MaxCaptureWidth = 1440;
    private const double MaxCaptureHeight = 900;
    private const double SampleOverscan = 12;

    private readonly CanvasControl _canvas;
    private readonly RenderTargetBitmap _renderTarget = new();

    private DispatcherQueueTimer? _captureTimer;
    private UIElement? _sourceElement;
    private CanvasBitmap? _captureBitmap;
    private bool _captureBusy;
    private bool _loggedFirstFrame;
    private DateTime _lastErrorLogUtc = DateTime.MinValue;

    private double _captureScaleX = 1;
    private double _captureScaleY = 1;
    private Point _originInSource;
    private int _capturePixelWidth;
    private int _capturePixelHeight;

    public LiveLiquidGlassSurface()
    {
        IsHitTestVisible = false;

        _canvas = new CanvasControl
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            ClearColor = Colors.Transparent
        };

        Content = _canvas;

        _canvas.Draw += Canvas_Draw;
        Loaded += LiveLiquidGlassSurface_Loaded;
        Unloaded += LiveLiquidGlassSurface_Unloaded;
    }

    /// <summary>
    /// XAML subtree to sample. For the navigation dock this is MainWindow.PageHost,
    /// which intentionally excludes the dock itself.
    /// </summary>
    public UIElement? SourceElement
    {
        get => _sourceElement;
        set
        {
            if (ReferenceEquals(_sourceElement, value))
                return;

            _sourceElement = value;
            _captureBitmap?.Dispose();
            _captureBitmap = null;
            _canvas.Invalidate();
        }
    }

    public float Radius { get; set; } = 34f;

    /// <summary>
    /// Pixel displacement strength. Kept moderate so text and artwork remain
    /// recognizable through the glass.
    /// </summary>
    public float DistortionAmount { get; set; } = 18f;

    public float BlurAmount { get; set; } = 1.4f;

    private void LiveLiquidGlassSurface_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        EnsureCaptureTimer();
        _captureTimer?.Start();
        _ = CaptureAsync();
    }

    private void LiveLiquidGlassSurface_Unloaded(
        object sender,
        RoutedEventArgs e)
    {
        _captureTimer?.Stop();

        _captureBitmap?.Dispose();
        _captureBitmap = null;
    }

    private void EnsureCaptureTimer()
    {
        if (_captureTimer is not null)
            return;

        _captureTimer = DispatcherQueue.CreateTimer();
        _captureTimer.Interval =
            TimeSpan.FromMilliseconds(CaptureIntervalMilliseconds);
        _captureTimer.IsRepeating = true;
        _captureTimer.Tick += (_, _) => _ = CaptureAsync();
    }

    private async Task CaptureAsync()
    {
        if (_captureBusy ||
            !IsLoaded ||
            Visibility != Visibility.Visible ||
            _sourceElement is not FrameworkElement source ||
            source.ActualWidth < 2 ||
            source.ActualHeight < 2 ||
            ActualWidth < 2 ||
            ActualHeight < 2)
        {
            return;
        }

        _captureBusy = true;

        try
        {
            var scale = Math.Min(
                1.0,
                Math.Min(
                    MaxCaptureWidth / source.ActualWidth,
                    MaxCaptureHeight / source.ActualHeight));

            var scaledWidth =
                Math.Max(1, (int)Math.Round(source.ActualWidth * scale));
            var scaledHeight =
                Math.Max(1, (int)Math.Round(source.ActualHeight * scale));

            await _renderTarget.RenderAsync(
                source,
                scaledWidth,
                scaledHeight);

            if (_renderTarget.PixelWidth <= 0 ||
                _renderTarget.PixelHeight <= 0)
            {
                return;
            }

            var pixels = await _renderTarget.GetPixelsAsync();
            if (pixels.Length == 0)
                return;

            var origin = TransformToVisual(source).TransformPoint(
                new Point(0, 0));

            var nextBitmap = CanvasBitmap.CreateFromBytes(
                _canvas,
                pixels,
                _renderTarget.PixelWidth,
                _renderTarget.PixelHeight,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                96f,
                CanvasAlphaMode.Premultiplied);

            var previous = _captureBitmap;
            _captureBitmap = nextBitmap;
            previous?.Dispose();

            _capturePixelWidth = _renderTarget.PixelWidth;
            _capturePixelHeight = _renderTarget.PixelHeight;
            _captureScaleX =
                _capturePixelWidth / source.ActualWidth;
            _captureScaleY =
                _capturePixelHeight / source.ActualHeight;
            _originInSource = origin;

            _canvas.Invalidate();

            if (!_loggedFirstFrame)
            {
                _loggedFirstFrame = true;
                StartupDiagnostics.Write(
                    $"LiveLiquidGlassSurface first live frame; " +
                    $"capture={_capturePixelWidth}x{_capturePixelHeight}, " +
                    $"surface={ActualWidth:0}x{ActualHeight:0}");
            }
        }
        catch (Exception ex)
        {
            // The glass has no Acrylic fallback by design. If capture fails,
            // leave it transparent and keep retrying on the next timer tick.
            var now = DateTime.UtcNow;
            if (now - _lastErrorLogUtc > TimeSpan.FromSeconds(5))
            {
                _lastErrorLogUtc = now;
                StartupDiagnostics.WriteException(
                    "LiveLiquidGlassSurface.Capture",
                    ex);
            }
        }
        finally
        {
            _captureBusy = false;
        }
    }

    private void Canvas_Draw(
        CanvasControl sender,
        CanvasDrawEventArgs args)
    {
        var bitmap = _captureBitmap;
        if (bitmap is null ||
            _capturePixelWidth <= 0 ||
            _capturePixelHeight <= 0)
        {
            return;
        }

        var width = sender.Size.Width;
        var height = sender.Size.Height;

        if (width < 2 || height < 2)
            return;

        try
        {
            var sourceX =
                (_originInSource.X - SampleOverscan) * _captureScaleX;
            var sourceY =
                (_originInSource.Y - SampleOverscan) * _captureScaleY;
            var sourceWidth =
                (width + SampleOverscan * 2) * _captureScaleX;
            var sourceHeight =
                (height + SampleOverscan * 2) * _captureScaleY;

            sourceX = Math.Clamp(
                sourceX,
                0,
                Math.Max(0, _capturePixelWidth - 1));
            sourceY = Math.Clamp(
                sourceY,
                0,
                Math.Max(0, _capturePixelHeight - 1));
            sourceWidth = Math.Clamp(
                sourceWidth,
                1,
                _capturePixelWidth - sourceX);
            sourceHeight = Math.Clamp(
                sourceHeight,
                1,
                _capturePixelHeight - sourceY);

            using var backgroundSample =
                new CanvasCommandList(sender);

            using (var sampleSession =
                   backgroundSample.CreateDrawingSession())
            {
                sampleSession.DrawImage(
                    bitmap,
                    new Rect(
                        -SampleOverscan,
                        -SampleOverscan,
                        width + SampleOverscan * 2,
                        height + SampleOverscan * 2),
                    new Rect(
                        sourceX,
                        sourceY,
                        sourceWidth,
                        sourceHeight));
            }

            using var backgroundBlur = new GaussianBlurEffect
            {
                Source = backgroundSample,
                BlurAmount = BlurAmount,
                BorderMode = EffectBorderMode.Hard
            };

            using var turbulence = new TurbulenceEffect
            {
                Size = new Vector2(
                    (float)width,
                    (float)height),
                Frequency = new Vector2(
                    0.018f,
                    0.032f),
                Octaves = 2,
                Seed = 73
            };

            using var softenedTurbulence = new GaussianBlurEffect
            {
                Source = turbulence,
                BlurAmount = 1.6f,
                BorderMode = EffectBorderMode.Hard
            };

            using var displacement = new DisplacementMapEffect
            {
                Source = backgroundBlur,
                Displacement = softenedTurbulence,
                Amount = DistortionAmount,
                XChannelSelect = EffectChannelSelect.Red,
                YChannelSelect = EffectChannelSelect.Green
            };

            using var saturation = new SaturationEffect
            {
                Source = displacement,
                Saturation = 1.10f
            };

            using var contrast = new ContrastEffect
            {
                Source = saturation,
                Contrast = 0.045f
            };

            using var clip = CanvasGeometry.CreateRoundedRectangle(
                sender,
                new Rect(0, 0, width, height),
                Radius,
                Radius);

            using (args.DrawingSession.CreateLayer(1f, clip))
            {
                args.DrawingSession.DrawImage(contrast);

                // This is only a low-opacity glass tint. It is not Acrylic and
                // it is not used as a fallback when live sampling is unavailable.
                args.DrawingSession.FillGeometry(
                    clip,
                    Color.FromArgb(
                        22,
                        238,
                        242,
                        248));
            }
        }
        catch (Exception ex)
        {
            var now = DateTime.UtcNow;
            if (now - _lastErrorLogUtc > TimeSpan.FromSeconds(5))
            {
                _lastErrorLogUtc = now;
                StartupDiagnostics.WriteException(
                    "LiveLiquidGlassSurface.Draw",
                    ex);
            }
        }
    }
}
