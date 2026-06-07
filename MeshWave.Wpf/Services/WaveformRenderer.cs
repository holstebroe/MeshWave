using System.Windows.Media;

namespace MeshWave.Wpf.Services;

/// <summary>
/// Produces the waveform bar <see cref="Geometry"/> for a given <see cref="WaveformStyle"/>.
/// All rendering is pure WPF StreamGeometry for high performance.
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
    /// Builds and returns the waveform geometry.
    /// </summary>
    public static Geometry Render(
        float[]     samples,
        double      canvasWidth,
        double      canvasHeight,
        WaveformStyle style)
    {
        return style switch
        {
            WaveformStyle.Filled  => RenderSolidPolygon(samples, canvasWidth, canvasHeight),
            WaveformStyle.Cloudy  => RenderCloudy(samples, canvasWidth, canvasHeight),
            WaveformStyle.Mirror  => RenderSolidPolygon(samples, canvasWidth, canvasHeight),
            WaveformStyle.Neon    => RenderSolidPolygon(samples, canvasWidth, canvasHeight),
            WaveformStyle.Smooth  => RenderSmooth(samples, canvasWidth, canvasHeight),
            _                     => RenderSolidPolygon(samples, canvasWidth, canvasHeight)
        };
    }

    // ─── Solid Polygon (used for Filled, Mirror, Neon) ───────────────────────

    private static Geometry RenderSolidPolygon(float[] samples, double w, double h)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            var  count    = Math.Max(samples.Length, 1);
            var  barW     = w / count;
            var centre = h / 2.0;

            if (count > 0)
            {
                // Start at the left midline
                ctx.BeginFigure(new Point(0, centre), true, true);

                // Top edge (left to right)
                for (var i = 0; i < count; i++)
                {
                    var amp       = Amplitude(samples, i);
                    var barHeight = Math.Max(4, amp * h);
                    var left      = Math.Floor(i * barW);
                    var top       = (h - barHeight) / 2.0;
                    var width     = Math.Max(1, Math.Ceiling((i + 1) * barW) - Math.Floor(i * barW));

                    ctx.LineTo(new Point(left, top), true, false);
                    ctx.LineTo(new Point(left + width, top), true, false);
                }

                // Bottom edge (right to left)
                for (var i = count - 1; i >= 0; i--)
                {
                    var amp       = Amplitude(samples, i);
                    var barHeight = Math.Max(4, amp * h);
                    var left      = Math.Floor(i * barW);
                    var bottom    = (h + barHeight) / 2.0;
                    var width     = Math.Max(1, Math.Ceiling((i + 1) * barW) - Math.Floor(i * barW));

                    ctx.LineTo(new Point(left + width, bottom), true, false);
                    ctx.LineTo(new Point(left, bottom), true, false);
                }
            }
        }
        geometry.Freeze();
        return geometry;
    }

    // ─── Cloudy ──────────────────────────────────────────────────────────────

    private static Geometry RenderCloudy(float[] samples, double w, double h)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            const double targetBarW = 2.0; // Fixed pixel width
            const double targetGapW = 1.0;
            const double stride     = targetBarW + targetGapW;

            var barCount = (int)Math.Floor(w / stride);
            if (barCount <= 0) return geometry;

            const double centreMaskHalf = 0.5; // Thinner dividing bar (total 1.5px)
            var centre = h / 2.0;

            for (var i = 0; i < barCount; i++)
            {
                var sampleIdx = (double)i / barCount * (samples.Length - 1);
                var amp       = Amplitude(samples, (int)sampleIdx);
                var halfMax   = (h / 2.0) - centreMaskHalf;

                var upperH   = Math.Max(2, amp * halfMax);
                var upperTop = centre - centreMaskHalf - upperH;

                var left = i * stride;

                // Upper bar
                ctx.BeginFigure(new Point(left, upperTop), true, true);
                ctx.LineTo(new Point(left + targetBarW, upperTop), true, false);
                ctx.LineTo(new Point(left + targetBarW, upperTop + upperH), true, false);
                ctx.LineTo(new Point(left, upperTop + upperH), true, false);

                // Lower bar
                var lowerH   = Math.Max(1, (amp * halfMax) * 0.5);
                var lowerTop = centre + centreMaskHalf;
                ctx.BeginFigure(new Point(left, lowerTop), true, true);
                ctx.LineTo(new Point(left + targetBarW, lowerTop), true, false);
                ctx.LineTo(new Point(left + targetBarW, lowerTop + lowerH), true, false);
                ctx.LineTo(new Point(left, lowerTop + lowerH), true, false);
            }
        }
        geometry.Freeze();
        return geometry;
    }

    // ─── Smooth ──────────────────────────────────────────────────────────────

    private static Geometry RenderSmooth(float[] samples, double w, double h)
    {
        if (samples.Length == 0)
            return new StreamGeometry();

        var windowSize = Math.Max(5, (samples.Length / 20) | 1); // force odd
        var smoothed = HannSmooth(samples, windowSize);

        var count  = smoothed.Length;
        var centre = h / 2.0;

        var pathPoints = Math.Min(count, 400);
        var step    = (double)(count - 1) / Math.Max(pathPoints - 1, 1);

        var upperPoints = new Point[pathPoints];
        var lowerPoints = new Point[pathPoints];

        for (var pi = 0; pi < pathPoints; pi++)
        {
            var sampleIndex = pi * step;
            var    lo          = (int)sampleIndex;
            var    hi          = Math.Min(lo + 1, count - 1);
            var frac        = sampleIndex - lo;
            var amp         = smoothed[lo] * (1 - frac) + smoothed[hi] * frac;

            var halfH    = Math.Max(2, amp * centre);
            var x        = (pi / (double)(pathPoints - 1)) * w;

            upperPoints[pi] = new Point(x, centre - halfH);
            lowerPoints[pi] = new Point(x, centre + halfH);
        }

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(upperPoints[0], true, true);
            for (var i = 1; i < upperPoints.Length; i++)
                ctx.LineTo(upperPoints[i], true, false);

            ctx.LineTo(lowerPoints[lowerPoints.Length - 1], true, false);

            for (var i = lowerPoints.Length - 2; i >= 0; i--)
                ctx.LineTo(lowerPoints[i], true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    // ─── Smooth helpers ───────────────────────────────────────────────────────

    private static double[] HannSmooth(float[] samples, int windowSize)
    {
        var n = samples.Length;
        var result = new double[n];

        var weights = new double[windowSize];
        double weightSum = 0;
        for (var k = 0; k < windowSize; k++)
        {
            weights[k] = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * k / (windowSize - 1)));
            weightSum  += weights[k];
        }

        var half = windowSize / 2;

        for (var i = 0; i < n; i++)
        {
            double acc = 0, wAcc = 0;
            for (var k = 0; k < windowSize; k++)
            {
                var idx = i - half + k;
                if (idx < 0) idx = 0;
                else if (idx >= n) idx = n - 1;
                acc  += samples[idx] * weights[k];
                wAcc += weights[k];
            }
            result[i] = acc / wAcc;
        }

        return result;
    }

    // ─── Shared helpers ───────────────────────────────────────────────────────

    private static double Amplitude(float[] samples, int index)
    {
        return samples.Length > index ? Math.Clamp(samples[index], 0f, 1f) : 0.2;
    }
}
