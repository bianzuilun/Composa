using Composa.Rendering;
using SkiaSharp;

namespace Composa.Filters;

public static class SpatialBlur
{
    /// <summary>Restore the original coverage after blurring straight color, without growing the layer.</summary>
    public static unsafe SKBitmap RestoreAlpha(SKBitmap source, SKBitmap blurred, int offsetX, int offsetY)
    {
        var output = Pixels.NewColor(source.Width, source.Height);
        byte* original = (byte*)source.GetPixels(), soft = (byte*)blurred.GetPixels(), dst = (byte*)output.GetPixels();
        Parallel.For(0, source.Height, y =>
        {
            for (var x = 0; x < source.Width; x++)
            {
                var a = original[(long)y * source.RowBytes + x * 4 + 3];
                var p = soft + (long)(y + offsetY) * blurred.RowBytes + (x + offsetX) * 4;
                var d = dst + (long)y * output.RowBytes + x * 4;
                d[3] = a;
                for (var c = 0; c < 3; c++) d[c] = p[3] == 0 ? (byte)0 : (byte)Math.Min(a, (p[c] * a + p[3] / 2) / p[3]);
            }
        });
        return output;
    }

    /// <summary>Interpolate a bounded Gaussian scale space along the line between the two canvas points.</summary>
    public static unsafe SKBitmap Progressive(SKBitmap source, FilterSettings settings)
    {
        var from = Math.Clamp(double.IsFinite(settings.ProgressiveFrom) ? settings.ProgressiveFrom : 0, 0, 250);
        var to = Math.Clamp(double.IsFinite(settings.ProgressiveTo) ? settings.ProgressiveTo : 20, 0, 250);
        var start = settings.ProgressiveStart ?? new SKPoint(source.Width / 2f, source.Height * 0.35f);
        var end = settings.ProgressiveEnd ?? new SKPoint(source.Width / 2f, source.Height * 0.65f);
        if (!float.IsFinite(start.X) || !float.IsFinite(start.Y) || !float.IsFinite(end.X) || !float.IsFinite(end.Y)) throw new ArgumentException("Blur endpoints must be finite.");
        var minimum = Math.Min(from, to); var maximum = Math.Max(from, to);
        // Eight blur levels plus the source. Keep only two levels in memory, rather than eight full-size copies.
        const int levels = 8;
        var output = Pixels.NewColor(source.Width, source.Height);
        SKBitmap? low = null;
        try
        {
            SKBitmap Blur(double radius)
            {
                if (radius < 0.01) return Pixels.Clone(source);
                var bitmap = Pixels.NewColor(source.Width, source.Height);
                using var filter = SKImageFilter.CreateBlur((float)radius, (float)radius, settings.ClampEdges ? SKShaderTileMode.Clamp : SKShaderTileMode.Decal);
                using var canvas = new SKCanvas(bitmap);
                using var paint = new SKPaint { ImageFilter = filter, BlendMode = SKBlendMode.Src };
                canvas.DrawBitmap(source, 0, 0, paint);
                return bitmap;
            }
            low = Blur(minimum);
            var dx = end.X - start.X; var dy = end.Y - start.Y; var length = Math.Max(1e-8, dx * dx + dy * dy);
            for (var level = 0; level < levels; level++)
            {
                using var high = Blur(minimum + (maximum - minimum) * (level + 1) / levels);
                byte* a = (byte*)low.GetPixels(), b = (byte*)high.GetPixels(), dst = (byte*)output.GetPixels();
                Parallel.For(0, source.Height, y =>
                {
                    for (var x = 0; x < source.Width; x++)
                    {
                        var position = Math.Clamp(((x - start.X) * dx + (y - start.Y) * dy) / length, 0, 1);
                        var radius = from + (to - from) * position;
                        var band = maximum > minimum ? (radius - minimum) / (maximum - minimum) * levels : 0;
                        var index = Math.Min(levels - 1, (int)band);
                        if (index != level) continue;
                        var blend = maximum > minimum ? Math.Clamp(band - level, 0, 1) : 0;
                        var p = a + (long)y * low.RowBytes + x * 4;
                        var q = b + (long)y * high.RowBytes + x * 4;
                        var d = dst + (long)y * output.RowBytes + x * 4;
                        for (var c = 0; c < 4; c++) d[c] = (byte)Math.Round(p[c] * (1 - blend) + q[c] * blend);
                    }
                });
                low.Dispose(); low = Pixels.Clone(high);
            }
            return output;
        }
        catch { output.Dispose(); throw; }
        finally { low?.Dispose(); }
    }
}
