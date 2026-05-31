using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace MeshWave.Services;

/// <summary>
/// Produces the waveform bar <see cref="UIElement"/>s for a given <see cref="WaveformStyle"/>.
/// All rendering is pure WPF shapes — no DrawingContext needed.
/// </summary>
public static class WaveformRenderer
{
    // ─── shared colour tokens ───────────────────────────────────────────────
    private static readonly Color PrimaryBlue    = Color.FromRgb(64,  128, 192); // Less saturated
    private static readonly Color CoolTeal       = Color.FromRgb(72,  160, 176);
    private static readonly Color NeonCyan       = Color.FromRgb(0,   255, 255);
    private static readonly Color NeonGlow       = Color.FromArgb(80, 0,   255, 255);
    private static readonly Color MirrorDim      = Color.FromArgb(120, 64,  128, 192); // Lower opacity

    // Smooth gradient stops
    private static readonly Color SmoothTop      = Color.FromArgb(0,   100, 200, 255); // transparent sky-blue
    private static readonly Color SmoothMid      = Color.FromRgb(80,   180, 255);      // bright centre
    private static readonly Color SmoothLow      = Color.FromArgb(120, 0,   120, 220); // dimmer lower

    // ─── public entry point ──────────────────────────────────────────────────

    /// <summary>
    /// Builds and returns the waveform elements.
    /// Elements are ordered back-to-front (draw order).
    /// </summary>
    public static IReadOnlyList<UIElement> Render(
        float[]     samples,
        double      canvasWidth,
        double      canvasHeight,
        WaveformStyle style)
    {
        return style switch
        {
            WaveformStyle.Filled  => RenderFilled(samples, canvasWidth, canvasHeight),
            WaveformStyle.Cloudy  => RenderCloudy(samples, canvasWidth, canvasHeight),
            WaveformStyle.Mirror  => RenderMirror(samples, canvasWidth, canvasHeight),
            WaveformStyle.Neon    => RenderNeon  (samples, canvasWidth, canvasHeight),
            WaveformStyle.Smooth  => RenderSmooth(samples, canvasWidth, canvasHeight),
            _                     => RenderFilled(samples, canvasWidth, canvasHeight),
        };
    }

    // ─── Filled ──────────────────────────────────────────────────────────────

    private static List<UIElement> RenderFilled(float[] samples, double w, double h)
    {
        var elements = new List<UIElement>();
        int  count    = Math.Max(samples.Length, 1);
        var  barW     = w / count;
        var  brush    = new SolidColorBrush(PrimaryBlue);

        for (int i = 0; i < count; i++)
        {
            double amp       = Amplitude(samples, i);
            double barHeight = Math.Max(4, amp * h);
            elements.Add(Bar(
                left:   Math.Floor(i * barW),
                top:    (h - barHeight) / 2.0,
                width:  Math.Max(1, Math.Ceiling((i + 1) * barW) - Math.Floor(i * barW)),
                height: barHeight,
                fill:   brush));
        }
        return elements;
    }

    // ─── Cloudy ──────────────────────────────────────────────────────────────

    private static List<UIElement> RenderCloudy(float[] samples, double w, double h)
    {
        var elements = new List<UIElement>();
        const double targetBarW = 3.0; // Fixed pixel width
        const double targetGapW = 1.0;
        const double stride     = targetBarW + targetGapW;

        int barCount = (int)Math.Floor(w / stride);
        if (barCount <= 0) return elements;

        var upperBrush = new SolidColorBrush(PrimaryBlue);
        var lowerBrush = new SolidColorBrush(CoolTeal) { Opacity = 0.4 };

        const double centreMaskHalf = 0.75; // Thinner dividing bar (total 1.5px)
        double centre = h / 2.0;

        for (int i = 0; i < barCount; i++)
        {
            // Skip every 3rd bar in pixel-scale
            if (i % 3 == 2) continue;

            double sampleIdx = (double)i / barCount * (samples.Length - 1);
            double amp       = Amplitude(samples, (int)sampleIdx);
            double halfMax   = (h / 2.0) - centreMaskHalf;

            double upperH   = Math.Max(2, amp * halfMax);
            double upperTop = centre - centreMaskHalf - upperH;
            elements.Add(Bar(i * stride, upperTop, targetBarW, upperH, upperBrush));

            double lowerH   = Math.Max(1, (amp * halfMax) * 0.5);
            elements.Add(Bar(i * stride, centre + centreMaskHalf, targetBarW, lowerH, lowerBrush));
        }

        var mask = new Rectangle
        {
            Width  = w,
            Height = centreMaskHalf * 2,
            Fill   = new SolidColorBrush(Color.FromRgb(15, 15, 15)),
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(mask, 0);
        Canvas.SetTop (mask, centre - centreMaskHalf);
        elements.Add(mask);

        return elements;
    }

    // ─── Mirror ──────────────────────────────────────────────────────────────

    private static List<UIElement> RenderMirror(float[] samples, double w, double h)
    {
        var elements = new List<UIElement>();
        int  count    = Math.Max(samples.Length, 1);
        var  barW     = w / count;

        var upperBrush = new SolidColorBrush(PrimaryBlue);
        var lowerGradient = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint   = new Point(0, 1)
        };
        lowerGradient.GradientStops.Add(new GradientStop(MirrorDim, 0.0));
        lowerGradient.GradientStops.Add(new GradientStop(Color.FromArgb(0, 64, 128, 192), 1.0));

        double centre = h / 2.0;

        for (int i = 0; i < count; i++)
        {
            double amp     = Amplitude(samples, i);
            double barHalf = Math.Max(3, amp * centre);
            double bw      = Math.Max(1, Math.Ceiling((i + 1) * barW) - Math.Floor(i * barW));

            elements.Add(Bar(Math.Floor(i * barW), centre - barHalf, bw, barHalf, upperBrush));
            elements.Add(Bar(Math.Floor(i * barW), centre,           bw, barHalf, lowerGradient));
        }
        return elements;
    }

    // ─── Neon ────────────────────────────────────────────────────────────────

    private static List<UIElement> RenderNeon(float[] samples, double w, double h)
    {
        var elements = new List<UIElement>();
        int  count    = Math.Max(samples.Length, 1);
        var  barW     = w / count;

        var glowBrush = new SolidColorBrush(NeonGlow);
        var coreBrush = new SolidColorBrush(NeonCyan);

        double centre = h / 2.0;

        for (int i = 0; i < count; i++)
        {
            double amp     = Amplitude(samples, i);
            double barHalf = Math.Max(3, amp * centre);
            double left    = Math.Floor(i * barW);

            // Core bar
            double coreW   = Math.Max(1.5, barW * 0.3);
            double coreX   = left + (barW - coreW) / 2.0;
            var coreBar = Bar(coreX, centre - barHalf, coreW, barHalf * 2, coreBrush);

            // Neon glow using BlurEffect
            coreBar.Effect = new System.Windows.Media.Effects.BlurEffect
            {
                Radius = 4,
                KernelType = System.Windows.Media.Effects.KernelType.Gaussian
            };

            elements.Add(Bar(coreX, centre - barHalf, coreW, barHalf * 2, glowBrush)); // Background glow
            elements.Add(coreBar); // Blurred core for bloom
            elements.Add(Bar(coreX, centre - barHalf, Math.Max(0.5, coreW * 0.5), barHalf * 2, coreBrush)); // Sharp center
        }
        return elements;
    }

    // ─── Smooth ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a smooth closed envelope shape:
    ///   1. Convolve samples with a Hann window to produce a smooth amplitude envelope.
    ///   2. Build a PathGeometry: upper outline (left→right), lower outline (right→left).
    ///   3. Fill with a vertical LinearGradientBrush centred on the midline.
    ///   4. Add a thin bright stroke path on top for definition.
    /// </summary>
    private static List<UIElement> RenderSmooth(float[] samples, double w, double h)
    {
        var elements = new List<UIElement>();

        if (samples.Length == 0)
            return elements;

        // ── 1. Hann-window smooth ──────────────────────────────────────────
        // Window width: ~5 % of the sample count, minimum 5, always odd.
        int windowSize = Math.Max(5, (samples.Length / 20) | 1); // force odd
        double[] smoothed = HannSmooth(samples, windowSize);

        int count  = smoothed.Length;
        double centre = h / 2.0;

        // ── 2. Build upper + lower point arrays ───────────────────────────
        // We use a reduced resolution for the path (max 400 path points) for performance.
        int pathPoints = Math.Min(count, 400);
        double step    = (double)(count - 1) / Math.Max(pathPoints - 1, 1);

        var upperPoints = new Point[pathPoints];
        var lowerPoints = new Point[pathPoints];

        for (int pi = 0; pi < pathPoints; pi++)
        {
            double sampleIndex = pi * step;
            int    lo          = (int)sampleIndex;
            int    hi          = Math.Min(lo + 1, count - 1);
            double frac        = sampleIndex - lo;
            double amp         = smoothed[lo] * (1 - frac) + smoothed[hi] * frac;

            double halfH    = Math.Max(2, amp * centre);
            double x        = (pi / (double)(pathPoints - 1)) * w;

            upperPoints[pi] = new Point(x, centre - halfH);
            lowerPoints[pi] = new Point(x, centre + halfH);
        }

        // ── 3. Filled path (gradient) ─────────────────────────────────────
        var fillGeometry = BuildClosedEnvelope(upperPoints, lowerPoints);

        var gradient = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint   = new Point(0, 1),
        };
        gradient.GradientStops.Add(new GradientStop(SmoothTop,  0.0));
        gradient.GradientStops.Add(new GradientStop(SmoothMid,  0.45));
        gradient.GradientStops.Add(new GradientStop(SmoothMid,  0.55));
        gradient.GradientStops.Add(new GradientStop(SmoothTop,  1.0));

        elements.Add(new System.Windows.Shapes.Path
        {
            Data   = fillGeometry,
            Fill   = gradient,
            Stroke = null,
            IsHitTestVisible = false,
        });

        // ── 4. Upper stroke (bright outline) ──────────────────────────────
        var upperStroke = BuildPolyline(upperPoints);
        elements.Add(new System.Windows.Shapes.Path
        {
            Data            = upperStroke,
            Stroke          = new SolidColorBrush(Color.FromArgb(200, 140, 210, 255)),
            StrokeThickness = 1.5,
            Fill            = null,
            IsHitTestVisible = false,
        });

        // Lower stroke (dimmer)
        var lowerStroke = BuildPolyline(lowerPoints);
        elements.Add(new System.Windows.Shapes.Path
        {
            Data            = lowerStroke,
            Stroke          = new SolidColorBrush(Color.FromArgb(100, 80, 160, 220)),
            StrokeThickness = 1.0,
            Fill            = null,
            IsHitTestVisible = false,
        });

        return elements;
    }

    // ─── Smooth helpers ───────────────────────────────────────────────────────

    /// <summary>Convolves <paramref name="samples"/> with a normalised Hann window of <paramref name="windowSize"/>.</summary>
    private static double[] HannSmooth(float[] samples, int windowSize)
    {
        int n = samples.Length;
        var result = new double[n];

        // Pre-compute Hann weights and their sum
        var weights = new double[windowSize];
        double weightSum = 0;
        for (int k = 0; k < windowSize; k++)
        {
            weights[k] = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * k / (windowSize - 1)));
            weightSum  += weights[k];
        }

        int half = windowSize / 2;

        for (int i = 0; i < n; i++)
        {
            double acc = 0, wAcc = 0;
            for (int k = 0; k < windowSize; k++)
            {
                int idx = i - half + k;
                if (idx < 0) idx = 0;
                else if (idx >= n) idx = n - 1;
                acc  += samples[idx] * weights[k];
                wAcc += weights[k];
            }
            result[i] = acc / wAcc;
        }

        return result;
    }

    private static Geometry BuildClosedEnvelope(Point[] upper, Point[] lower)
    {
        var figure = new PathFigure { StartPoint = upper[0], IsClosed = true, IsFilled = true };

        // Upper edge: left → right (PolyLineSegment)
        var upperSeg = new PolyLineSegment();
        for (int i = 1; i < upper.Length; i++)
            upperSeg.Points.Add(upper[i]);
        figure.Segments.Add(upperSeg);

        // Right cap
        figure.Segments.Add(new LineSegment(lower[lower.Length - 1], true));

        // Lower edge: right → left
        var lowerSeg = new PolyLineSegment();
        for (int i = lower.Length - 2; i >= 0; i--)
            lowerSeg.Points.Add(lower[i]);
        figure.Segments.Add(lowerSeg);

        var geo = new PathGeometry();
        geo.Figures.Add(figure);
        return geo;
    }

    private static Geometry BuildPolyline(Point[] points)
    {
        var figure = new PathFigure { StartPoint = points[0], IsFilled = false };
        var seg    = new PolyLineSegment();
        for (int i = 1; i < points.Length; i++)
            seg.Points.Add(points[i]);
        figure.Segments.Add(seg);

        var geo = new PathGeometry();
        geo.Figures.Add(figure);
        return geo;
    }

    // ─── Shared helpers ───────────────────────────────────────────────────────

    private static double Amplitude(float[] samples, int index)
        => samples.Length > index ? Math.Clamp(samples[index], 0f, 1f) : 0.2;

    private static Rectangle Bar(double left, double top, double width, double height, Brush fill)
    {
        var r = new Rectangle
        {
            Width  = width,
            Height = Math.Max(1, height),
            Fill   = fill,
            SnapsToDevicePixels = true,
        };
        RenderOptions.SetEdgeMode(r, EdgeMode.Aliased);
        Canvas.SetLeft(r, left);
        Canvas.SetTop (r, top);
        return r;
    }
}
