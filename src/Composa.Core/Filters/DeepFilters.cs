// Precision reconstruction follows Compositor/Rendering/DeepPixels.c at v1.5.3 (MIT).
// Copyright (c) 2026 Wonder Assembly LLC. See packaging/THIRD-PARTY-NOTICES.txt.
using Composa.Rendering;
using SkiaSharp;

namespace Composa.Filters;

/// <summary>Native float spatial filters and Compositor's local reconstruction of legacy byte filters.</summary>
internal static unsafe class DeepFilters
{
    internal static (SKBitmap Result, int GrowX, int GrowY) Run(SKBitmap source, FilterSettings settings)
    {
        SKBitmap result; int gx = 0, gy = 0;
        switch (settings.Kind)
        {
            case FilterKind.GaussianBlur:
                var radius = Math.Max(.1, settings.Radius);
                gx = gy = settings.ClampEdges && OpaqueBorder(source) ? 0 : (int)Math.Ceiling(radius * 3);
                result = Blur(source, radius, gx, gy, gx == 0);
                if (settings.PreserveTransparency)
                {
                    var restored = SpatialBlur.RestoreAlpha(source, result, gx, gy);
                    result.Dispose(); result = restored; gx = gy = 0;
                }
                break;
            case FilterKind.MotionBlur:
                (result, gx, gy) = DeepAdjustments.Motion(source, settings.Radius, settings.Angle, settings.ClampEdges && OpaqueBorder(source)); break;
            case FilterKind.ProgressiveBlur:
                result = SpatialBlur.Progressive(source, settings); break;
            case FilterKind.Sharpen:
                using (var soft = Blur(source, Math.Max(.3, settings.Radius / 2), 0, 0, true))
                {
                    result = DeepPixels.New(source.Width, source.Height, hdr: true);
                    var amount = (float)(settings.Amount / 50);
                    Parallel.For(0, source.Height, y =>
                    {
                        for (var x = 0; x < source.Width; x++)
                        {
                            var p = DeepPixels.Read(source, x, y); var q = DeepPixels.Read(soft, x, y);
                            var limit = DeepPixels.IsHdr(source) ? float.MaxValue : p.Alpha;
                            DeepPixels.Write(result, x, y, new SKColorF(Math.Clamp(p.Red + (p.Red - q.Red) * amount, 0, limit),
                                Math.Clamp(p.Green + (p.Green - q.Green) * amount, 0, limit), Math.Clamp(p.Blue + (p.Blue - q.Blue) * amount, 0, limit), p.Alpha));
                        }
                    });
                }
                break;
            case FilterKind.LensCorrection:
                result = Lens(source, settings.Distortion / 100); break;
            case FilterKind.BloomGlow:
                (result, gx, gy) = Bloom(source, settings); break;
            default:
                result = Reconstruct(source, settings); break;
        }
        try
        {
            if (!DeepPixels.IsHdr(source)) { var integer = DeepPixels.Convert(result); result.Dispose(); result = integer; }
            return (DeepPixels.Preview(result), gx, gy);
        }
        catch { result.Dispose(); throw; }
    }

    private static SKBitmap Blur(SKBitmap source, double radius, int gx, int gy, bool clamp)
    {
        if (!Composa.Model.DocumentLimits.FitsSurface(source.Width + gx * 2, source.Height + gy * 2)) throw new InvalidOperationException("The filter exceeds the supported surface size.");
        var result = DeepPixels.New(source.Width + gx * 2, source.Height + gy * 2, hdr: true);
        using var filter = SKImageFilter.CreateBlur((float)radius, (float)radius, clamp ? SKShaderTileMode.Clamp : SKShaderTileMode.Decal);
        using var canvas = new SKCanvas(result);
        using var paint = new SKPaint { ImageFilter = filter, BlendMode = SKBlendMode.Src };
        canvas.DrawBitmap(source, gx, gy, paint);
        return result;
    }

    private static bool OpaqueBorder(SKBitmap source)
    {
        for (var x = 0; x < source.Width; x++) if (DeepPixels.Read(source, x, 0).Alpha < 1 || DeepPixels.Read(source, x, source.Height - 1).Alpha < 1) return false;
        for (var y = 0; y < source.Height; y++) if (DeepPixels.Read(source, 0, y).Alpha < 1 || DeepPixels.Read(source, source.Width - 1, y).Alpha < 1) return false;
        return true;
    }

    private static SKColorF Lerp(SKColorF a, SKColorF b, float t) => new(a.Red + (b.Red - a.Red) * t, a.Green + (b.Green - a.Green) * t,
        a.Blue + (b.Blue - a.Blue) * t, a.Alpha + (b.Alpha - a.Alpha) * t);

    private static SKBitmap Lens(SKBitmap source, double distortion)
    {
        var result = DeepPixels.New(source.Width, source.Height, hdr: true);
        double cx = source.Width / 2.0, cy = source.Height / 2.0, norm = Math.Sqrt(cx * cx + cy * cy);
        Parallel.For(0, source.Height, y =>
        {
            for (var x = 0; x < source.Width; x++)
            {
                double nx = (x + .5 - cx) / norm, ny = (y + .5 - cy) / norm, factor = 1 + distortion * .5 * (nx * nx + ny * ny);
                double sx = cx + nx * factor * norm - .5, sy = cy + ny * factor * norm - .5;
                int ix = (int)Math.Floor(sx), iy = (int)Math.Floor(sy);
                SKColorF Sample(int px, int py) => px < 0 || py < 0 || px >= source.Width || py >= source.Height ? default : DeepPixels.Read(source, px, py);
                DeepPixels.Write(result, x, y, Lerp(Lerp(Sample(ix, iy), Sample(ix + 1, iy), (float)(sx - ix)),
                    Lerp(Sample(ix, iy + 1), Sample(ix + 1, iy + 1), (float)(sx - ix)), (float)(sy - iy)));
            }
        });
        return result;
    }

    private static (SKBitmap, int, int) Bloom(SKBitmap source, FilterSettings settings)
    {
        var radius = Math.Max(.5, settings.BloomRadius);
        var pad = settings.ClampEdges && OpaqueBorder(source) ? 0 : (int)Math.Ceiling(radius * 3);
        using var lights = DeepPixels.Convert(source, hdr: true);
        Parallel.For(0, source.Height, y =>
        {
            for (var x = 0; x < source.Width; x++)
            {
                var p = DeepPixels.Read(source, x, y);
                var w = p.Alpha <= 0 ? 0 : Math.Clamp(((.2126f * p.Red + .7152f * p.Green + .0722f * p.Blue) / p.Alpha - .4f) / .6f, 0, 1);
                DeepPixels.Write(lights, x, y, new SKColorF(p.Red * w, p.Green * w, p.Blue * w, p.Alpha * w));
            }
        });
        using var glow = Blur(lights, radius, pad, pad, pad == 0);
        var result = DeepPixels.New(glow.Width, glow.Height, hdr: true);
        using (var canvas = new SKCanvas(result)) canvas.DrawBitmap(source, pad, pad);
        var gain = (float)Math.Max(0, settings.BloomAmount / 50);
        Parallel.For(0, result.Height, y =>
        {
            for (var x = 0; x < result.Width; x++)
            {
                var p = DeepPixels.Read(result, x, y); var q = DeepPixels.Read(glow, x, y);
                var a = Math.Clamp(q.Alpha * gain, 0, 1); var alpha = p.Alpha + a - p.Alpha * a;
                var limit = DeepPixels.IsHdr(source) ? float.MaxValue : alpha;
                DeepPixels.Write(result, x, y, new SKColorF(Math.Min(limit, p.Red + q.Red * gain), Math.Min(limit, p.Green + q.Green * gain), Math.Min(limit, p.Blue + q.Blue * gain), alpha));
            }
        });
        return (result, pad, pad);
    }

    private static float Peak(SKColorF p) => p.Alpha > 0 ? Math.Max(1, Math.Max(p.Red, Math.Max(p.Green, p.Blue)) / p.Alpha) : 1;

    private static SKBitmap Split(SKBitmap source, int step, int channel)
    {
        var result = Pixels.NewColor(source.Width, source.Height);
        Parallel.For(0, source.Height, y =>
        {
            var row = (byte*)result.GetPixels() + (long)y * result.RowBytes;
            for (var x = 0; x < source.Width; x++)
            {
                var p = DeepPixels.Read(source, x, y); var peak = Peak(p); var alpha = (int)MathF.Round(p.Alpha * 255);
                for (var c = 0; c < 3; c++)
                {
                    var v = (c == 0 ? p.Red : c == 1 ? p.Green : p.Blue) / peak * 255;
                    var low = Math.Clamp((int)MathF.Floor(v + DeepPixels.Noise(x, y, c)) - 1, 0, alpha);
                    row[x * 4 + c] = (byte)Math.Min(alpha, low + (channel == c ? step : 0));
                }
                row[x * 4 + 3] = (byte)alpha;
            }
        });
        return result;
    }

    private static SKBitmap Reconstruct(SKBitmap source, FilterSettings settings)
    {
        var runs = new List<SKBitmap>();
        try
        {
            // Every call receives plain RGBA8888, so no recursion into this precision path.
            SKBitmap Pass(int step, int channel)
            {
                using var input = Split(source, step, channel);
                var output = ImageFilters.Run(input, settings).Result;
                runs.Add(output);
                if (output.Width != source.Width || output.Height != source.Height) throw new InvalidOperationException("The precision reconstruction requires unchanged dimensions.");
                return output;
            }
            var low = Pass(0, -1);
            for (var k = 0; k < 3; k++) { Pass(1, k); Pass(2, k); Pass(4, k); }
            var result = DeepPixels.New(source.Width, source.Height, DeepPixels.IsHdr(source));
            Parallel.For(0, source.Height, y =>
            {
                Span<float> rgb = stackalloc float[3];
                for (var x = 0; x < source.Width; x++)
                {
                    var p = DeepPixels.Read(source, x, y); var peak = Peak(p); var alpha = (int)MathF.Round(p.Alpha * 255);
                    var first = (byte*)low.GetPixels() + (long)y * low.RowBytes + x * 4;
                    for (var c = 0; c < 3; c++)
                    {
                        float value = first[c], offset = 0; var fits = 0;
                        for (var k = 0; k < 3; k++)
                        {
                            var input = (k == 0 ? p.Red : k == 1 ? p.Green : p.Blue) / peak * 255;
                            var l = Math.Clamp((int)MathF.Floor(input + DeepPixels.Noise(x, y, k)) - 1, 0, alpha);
                            var past = Math.Clamp(input - l, 0, 2); var room = alpha - l;
                            if (room <= 0) continue;
                            float Sample(int pass) { var bitmap = runs[1 + k * 3 + pass]; return ((byte*)bitmap.GetPixels())[(long)y * bitmap.RowBytes + x * 4 + c]; }
                            float one = Sample(0), two = Sample(1), four = Sample(2), baseValue = first[c];
                            var change = room == 1 || past < 1 ? (one - baseValue) * Math.Min(past, 1) : one - baseValue + (two - one) * (past - 1);
                            if (room >= 4)
                            {
                                var slope = (-1.75f * baseValue - .75f * one + .25f * two + 2.25f * four) / 8.75f;
                                var start = (baseValue + one + two + four) / 4 - slope * 1.75f;
                                var error = Math.Max(Math.Max(Math.Abs(baseValue - start), Math.Abs(one - start - slope)), Math.Max(Math.Abs(two - start - 2 * slope), Math.Abs(four - start - 4 * slope)));
                                if (error <= 1) { change = slope * past; offset += start - baseValue; fits++; }
                            }
                            value += change;
                        }
                        if (fits > 0) value += offset / fits;
                        rgb[c] = Math.Clamp(value, 0, first[3]) / 255 * peak;
                    }
                    // Filters in this path preserve coverage, except background removal and painterly marks.
                    var outAlpha = settings.Kind is FilterKind.RemoveBackground or FilterKind.Painterly ? first[3] / 255f : p.Alpha;
                    DeepPixels.Write(result, x, y, new SKColorF(rgb[0], rgb[1], rgb[2], outAlpha));
                }
            });
            return result;
        }
        finally { foreach (var bitmap in runs) bitmap.Dispose(); }
    }
}
