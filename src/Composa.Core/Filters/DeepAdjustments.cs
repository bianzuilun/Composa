using Composa.Model;
using Composa.Rendering;
using SkiaSharp;

namespace Composa.Filters;

/// <summary>Adjustments on wide raster tiles, keeping intermediate values in floats and HDR above white.</summary>
internal static class DeepAdjustments
{
    internal static bool ApplyPreview(Adjustment adjustment, SKBitmap preview, SKRectI area, double originX, double originY, double step, bool parallel)
    {
        if (DeepPixels.Source(preview) is not { } source) return false;
        var result = DeepPixels.Convert(source, hdr: true);
        try
        {
            Apply(adjustment, result, area, originX, originY, step, parallel);
            if (!DeepPixels.IsHdr(source)) { var integer = DeepPixels.Convert(result); result.Dispose(); result = integer; }
            using var shown = DeepPixels.Preview(result);
            Pixels.CopyPixels(shown, preview);
            DeepPixels.Attach(preview, result);
            return true;
        }
        catch { result.Dispose(); throw; }
    }

    internal static void Apply(Adjustment adjustment, SKBitmap bitmap, SKRectI area, double originX, double originY, double step, bool parallel)
    {
        if (adjustment.IsIdentity) return;
        area = Geometry.Intersect(area, new SKRectI(0, 0, bitmap.Width, bitmap.Height));
        if (adjustment is GaussianBlurAdjustment gaussian)
        { ImageFilters.BlurInPlace(bitmap, (float)(Math.Clamp(gaussian.Radius, .1, 250) / Math.Max(step, .000001))); return; }
        if (adjustment is MotionBlurAdjustment motion)
        {
            using var blurred = Motion(bitmap, motion.Distance / Math.Max(step, .000001), motion.Angle, clamp: true).Result;
            Pixels.CopyPixels(blurred, bitmap); return;
        }
        var op = step > 1.01 ? adjustment.CreateOp(step) ?? adjustment.CreateOp() : adjustment.CreateOp();
        void Row(int y)
        {
            for (var x = area.Left; x < area.Right; x++)
            {
                var p = DeepPixels.Read(bitmap, x, y);
                if (p.Alpha <= 0) continue;
                double r = p.Red / p.Alpha, g = p.Green / p.Alpha, b = p.Blue / p.Alpha;
                if (adjustment is ExposureAdjustment exposure)
                {
                    var gain = Math.Pow(2, Math.Clamp(exposure.Exposure, -20, 20));
                    var gamma = 1 / Math.Clamp(exposure.Gamma, .01, 9.99);
                    double Expose(double value)
                    {
                        var linear = value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
                        linear = Math.Pow(Math.Max(0, linear * gain + exposure.Offset), gamma);
                        return linear <= .0031308 ? linear * 12.92 : 1.055 * Math.Pow(linear, 1 / 2.4) - .055;
                    }
                    r = Expose(r); g = Expose(g); b = Expose(b);
                }
                else
                {
                    // Compositor's HDR normalization: preserve each highlight's ratio above white across color edits.
                    var peak = Math.Max(1, Math.Max(r, Math.Max(g, b)));
                    r /= peak; g /= peak; b /= peak;
                    switch (adjustment)
                    {
                        case LevelsAdjustment levels:
                            r = levels.Ranges[0].Map(levels.Ranges[1].Map(r * 255)) / 255;
                            g = levels.Ranges[0].Map(levels.Ranges[2].Map(g * 255)) / 255;
                            b = levels.Ranges[0].Map(levels.Ranges[3].Map(b * 255)) / 255; break;
                        case CurvesAdjustment curves:
                            r = curves.Value(curves.Value(r * 255, 1), 0) / 255;
                            g = curves.Value(curves.Value(g * 255, 2), 0) / 255;
                            b = curves.Value(curves.Value(b * 255, 3), 0) / 255; break;
                        case InvertAdjustment:
                            r = 1 - r; g = 1 - g; b = 1 - b; break;
                        case BrightnessContrastAdjustment brightness:
                            var contrast = Math.Clamp(brightness.Contrast, -100, 100) / 100;
                            var slope = contrast >= 0 ? 1 / Math.Max(.004, 1 - contrast) : 1 + contrast;
                            var lift = Math.Clamp(brightness.Brightness, -100, 100) / 200;
                            r = (r - .5) * slope + .5 + lift; g = (g - .5) * slope + .5 + lift; b = (b - .5) * slope + .5 + lift; break;
                        case ColorLookupAdjustment { Lattice: { } lattice } lookup:
                            var mapped = lattice.Sample((float)r, (float)g, (float)b);
                            var amount = Math.Clamp(lookup.Amount / 100, 0, 1);
                            r += (mapped.R - r) * amount; g += (mapped.G - g) * amount; b += (mapped.B - b) * amount; break;
                        default:
                            (r, g, b) = Interpolate(op, r, g, b, (int)Math.Floor(originX + x * step), (int)Math.Floor(originY + y * step)); break;
                    }
                    r = Math.Clamp(r, 0, 1) * peak; g = Math.Clamp(g, 0, 1) * peak; b = Math.Clamp(b, 0, 1) * peak;
                }
                DeepPixels.Write(bitmap, x, y, new SKColorF((float)(r * p.Alpha), (float)(g * p.Alpha), (float)(b * p.Alpha), p.Alpha));
            }
        }
        if (parallel && (long)area.Width * area.Height > 200_000) Parallel.For(area.Top, area.Bottom, Row);
        else for (var y = area.Top; y < area.Bottom; y++) Row(y);
    }

    // Local affine reconstruction of a byte operation, with every input channel stepped separately.
    // Matches deep_change/deep_merge without allocating ten full-frame buffers for a per-pixel adjustment.
    private static (double R, double G, double B) Interpolate(PixelOp op, double r, double g, double b, int x, int y)
    {
        Span<double> input = [r * 255, g * 255, b * 255];
        Span<int> low = stackalloc int[3]; Span<double> past = stackalloc double[3];
        for (var c = 0; c < 3; c++) { low[c] = Math.Clamp((int)Math.Floor(input[c] + DeepPixels.Noise(x, y, c)) - 1, 0, 255); past[c] = Math.Clamp(input[c] - low[c], 0, 2); }
        int lr = low[0], lg = low[1], lb = low[2]; op(ref lr, ref lg, ref lb, x, y);
        Span<int> baseColor = [lr, lg, lb]; Span<double> result = [lr, lg, lb]; Span<double> offsets = stackalloc double[3]; Span<int> fits = stackalloc int[3];
        offsets.Clear(); fits.Clear();
        Span<int> near = stackalloc int[9]; Span<int> mid = stackalloc int[9]; Span<int> far = stackalloc int[9];
        for (var channel = 0; channel < 3; channel++)
        {
            for (var pass = 0; pass < 3; pass++)
            {
                var step = pass == 0 ? 1 : pass == 1 ? 2 : 4;
                int cr = Math.Min(255, low[0] + (channel == 0 ? step : 0)), cg = Math.Min(255, low[1] + (channel == 1 ? step : 0)), cb = Math.Min(255, low[2] + (channel == 2 ? step : 0));
                op(ref cr, ref cg, ref cb, x, y);
                var target = pass == 0 ? near : pass == 1 ? mid : far;
                target[channel * 3] = cr; target[channel * 3 + 1] = cg; target[channel * 3 + 2] = cb;
            }
            for (var output = 0; output < 3; output++)
            {
                var at = channel * 3 + output; var room = 255 - low[channel];
                if (room <= 0) continue;
                double l = baseColor[output], one = near[at], two = mid[at], four = far[at];
                var change = room == 1 || past[channel] < 1 ? (one - l) * Math.Min(past[channel], 1) : one - l + (two - one) * (past[channel] - 1);
                if (room >= 4)
                {
                    var mean = (l + one + two + four) / 4;
                    var slope = (-1.75 * l - .75 * one + .25 * two + 2.25 * four) / 8.75;
                    var start = mean - slope * 1.75;
                    var error = Math.Max(Math.Max(Math.Abs(l - start), Math.Abs(one - start - slope)), Math.Max(Math.Abs(two - start - 2 * slope), Math.Abs(four - start - 4 * slope)));
                    if (error <= 1) { change = slope * past[channel]; offsets[output] += start - l; fits[output]++; }
                }
                result[output] += change;
            }
        }
        for (var c = 0; c < 3; c++) if (fits[c] != 0) result[c] += offsets[c] / fits[c];
        return (Math.Clamp(result[0] / 255, 0, 1), Math.Clamp(result[1] / 255, 0, 1), Math.Clamp(result[2] / 255, 0, 1));
    }

    internal static (SKBitmap Result, int GrowX, int GrowY) Motion(SKBitmap source, double distance, double angle, bool clamp)
    {
        var length = Math.Max(1, (int)Math.Round(distance));
        var radians = angle * Math.PI / 180; var dx = Math.Cos(radians); var dy = -Math.Sin(radians);
        int padX = clamp ? 0 : (int)Math.Ceiling(Math.Abs(dx) * length / 2) + 1, padY = clamp ? 0 : (int)Math.Ceiling(Math.Abs(dy) * length / 2) + 1;
        if (!DocumentLimits.FitsSurface(source.Width + padX * 2, source.Height + padY * 2)) throw new InvalidOperationException("The filter exceeds the supported surface size.");
        var result = DeepPixels.New(source.Width + padX * 2, source.Height + padY * 2, hdr: true);
        var samples = Math.Min(length, 96);
        Parallel.For(0, result.Height, y =>
        {
            for (var x = 0; x < result.Width; x++)
            {
                float r = 0, g = 0, b = 0, a = 0;
                for (var i = 0; i < samples; i++)
                {
                    var t = samples == 1 ? 0 : (i / (double)(samples - 1) - .5) * length;
                    int sx = (int)Math.Round(x - padX + dx * t), sy = (int)Math.Round(y - padY + dy * t);
                    if (clamp) { sx = Math.Clamp(sx, 0, source.Width - 1); sy = Math.Clamp(sy, 0, source.Height - 1); }
                    else if (sx < 0 || sy < 0 || sx >= source.Width || sy >= source.Height) continue;
                    var p = DeepPixels.Read(source, sx, sy); r += p.Red; g += p.Green; b += p.Blue; a += p.Alpha;
                }
                DeepPixels.Write(result, x, y, new SKColorF(r / samples, g / samples, b / samples, a / samples));
            }
        });
        return (result, padX, padY);
    }
}
