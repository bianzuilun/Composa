// Ported from Compositor Rendering/DitherPixels.c and Document/Scanlines.swift at 554104cf.
// Copyright (c) 2026 Wonder Assembly LLC. MIT; full notice in THIRD-PARTY-NOTICES.txt.
using Composa.Rendering;
using SkiaSharp;

namespace Composa.Filters;

public sealed record ScanlinesSettings
{
    public int Spacing { get; init; } = 4;
    public double Thickness { get; init; } = 70;
    public double Glow { get; init; }
    public double Dots { get; init; }
    public double Wobble { get; init; }
    public double Displace { get; init; }
    public double Smoothness { get; init; } = 50;
    public double Threshold { get; init; }
    public int Split { get; init; }
    public double Density { get; init; }
    public double Contrast { get; init; }
    public double BlackLevel { get; init; }
    public DitherColors Colors { get; init; }
    public uint Dark { get; init; } = 0xFF000000;
    public uint Light { get; init; } = 0xFFFFFFFF;

    public ScanlinesSettings Normalized()
    {
        static double C(double value, double min, double max, double fallback = 0) => double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
        return this with
        {
            Spacing = Math.Clamp(Spacing, 2, 32), Thickness = C(Thickness, 5, 100, 70), Glow = C(Glow, 0, 100),
            Dots = C(Dots, 0, 100), Wobble = C(Wobble, 0, 64), Displace = C(Displace, -100, 100),
            Smoothness = C(Smoothness, 0, 100, 50), Threshold = C(Threshold, 0, 100), Split = Math.Clamp(Split, 0, 16),
            Density = C(Density, -100, 100), Contrast = C(Contrast, -100, 100), BlackLevel = C(BlackLevel, 0, 100)
        };
    }
}

public static class ScanlinesPixels
{
    private static double Clamp(double value) => Math.Clamp(value, 0, 1);

    public static unsafe SKBitmap Apply(SKBitmap source, ScanlinesSettings settings)
    {
        var s = settings.Normalized();
        int width = source.Width, height = source.Height, spacing = s.Spacing;
        var lines = (height + spacing - 1) / spacing;
        var count = checked(width * height); var plane = checked(lines * width);
        var original = s.Colors == DitherColors.Original;
        var planes = original ? 3 : 1;
        var tone = new float[checked(count * planes)]; var alpha = new byte[count]; var scan = new float[checked(plane * planes)];
        var gamma = Math.Pow(2, s.Density / 100 * 1.5);
        var contrast = s.Contrast >= 0 ? 1 / (1 - 0.95 * s.Contrast / 100) : 1 + s.Contrast / 100;
        double Adjust(double value) => Clamp((Math.Pow(Clamp(value), gamma) - 0.5) * contrast + 0.5);
        var output = Pixels.Clone(source);
        byte* src = (byte*)source.GetPixels(), dst = (byte*)output.GetPixels();
        Parallel.For(0, height, y =>
        {
            for (var x = 0; x < width; x++)
            {
                var p = src + (long)y * source.RowBytes + x * 4; var i = y * width + x; alpha[i] = p[3];
                double r = p[3] == 0 ? 0 : p[0] / (double)p[3], g = p[3] == 0 ? 0 : p[1] / (double)p[3], b = p[3] == 0 ? 0 : p[2] / (double)p[3];
                if (original) { tone[i] = (float)Adjust(r); tone[count + i] = (float)Adjust(g); tone[2 * count + i] = (float)Adjust(b); }
                else tone[i] = (float)Adjust(0.2126 * r + 0.7152 * g + 0.0722 * b);
            }
        });
        Parallel.For(0, lines, line =>
        {
            var wave = Math.Sin(line * 0.45) * 0.7 + Math.Sin(line * 1.7 + 1.3) * 0.3;
            var shift = (int)Math.Round(s.Wobble * wave);
            for (var x = 0; x < width; x++)
            {
                var sx = x - shift; if (sx < 0 || sx >= width) continue;
                for (var c = 0; c < planes; c++)
                {
                    double sum = 0; var n = 0;
                    for (var y = line * spacing; y < Math.Min(height, (line + 1) * spacing); y++)
                        if (alpha[y * width + sx] != 0) { sum += tone[c * count + y * width + sx]; n++; }
                    scan[c * plane + line * width + x] = n > 0 ? (float)(sum / n) : 0;
                }
            }
        });
        var luminance = new float[plane];
        for (var i = 0; i < plane; i++) luminance[i] = original ? (float)(scan[i] * 0.2126 + scan[plane + i] * 0.7152 + scan[2 * plane + i] * 0.0722) : scan[i];
        var lift = (float[])luminance.Clone();
        var radius = (int)Math.Round(s.Smoothness / 100 * spacing * 2);
        if (radius > 0 && s.Displace != 0)
        {
            Parallel.For(0, lines, line =>
            {
                var copy = new float[width];
                for (var pass = 0; pass < 3; pass++)
                {
                    Array.Copy(lift, line * width, copy, 0, width);
                    double sum = 0; for (var x = -radius; x <= radius; x++) sum += copy[Math.Clamp(x, 0, width - 1)];
                    for (var x = 0; x < width; x++)
                    {
                        lift[line * width + x] = (float)(sum / (2 * radius + 1));
                        sum += copy[Math.Clamp(x + radius + 1, 0, width - 1)] - copy[Math.Clamp(x - radius, 0, width - 1)];
                    }
                }
            });
            for (var pass = 0; pass < (int)Math.Round(s.Smoothness / 50); pass++)
            {
                var copy = (float[])lift.Clone();
                for (var line = 0; line < lines; line++) for (var x = 0; x < width; x++)
                    lift[line * width + x] = (copy[Math.Max(0, line - 1) * width + x] + 2 * copy[line * width + x] + copy[Math.Min(lines - 1, line + 1) * width + x]) / 4;
            }
        }
        var dark = new SKColor(s.Colors == DitherColors.TwoColors ? s.Dark : 0xFF000000);
        var light = new SKColor(s.Colors == DitherColors.TwoColors ? s.Light : 0xFFFFFFFF);
        var screen = new[] { dark.Red / 255.0, dark.Green / 255.0, dark.Blue / 255.0 };
        var phosphor = new[] { light.Red / 255.0, light.Green / 255.0, light.Blue / 255.0 };
        var middle = spacing / 2.0; var rising = s.Displace >= 0;
        Parallel.For(0, width, x =>
        {
            var cover = new double[height]; var color = new double[height * 3];
            var along = (x + 0.5) % spacing - middle;
            var horizon = rising ? double.PositiveInfinity : double.NegativeInfinity;
            for (var step = 0; step < lines; step++)
            {
                var line = rising ? lines - 1 - step : step;
                var beading = s.Dots > 0 ? Clamp((s.Dots / 100 - luminance[line * width + x]) / 0.2) : 0;
                var across = along * beading; var at = Math.Clamp((int)Math.Round(x - across), 0, width - 1); var i = line * width + at;
                var t = luminance[i]; var lit = s.Threshold > 0 ? Clamp((t - s.Threshold / 100) / 0.04) : 1;
                if (lit <= 0) continue;
                var baseY = line * spacing + middle; var here = baseY - s.Displace * lift[i]; var before = at > 0 ? baseY - s.Displace * lift[i - 1] : here;
                var lo = Math.Min(here, before); var hi = Math.Max(here, before);
                var level = s.BlackLevel / 100 + (1 - s.BlackLevel / 100) * t;
                var beam = middle * s.Thickness / 100 * (0.29 + 0.71 * Math.Sqrt(Clamp(level)));
                var top = Math.Max(0, (int)Math.Floor(rising ? lo - beam - 1 : Math.Max(lo - beam - 1, horizon - 1)));
                var bottom = Math.Min(height, (int)Math.Ceiling(rising ? Math.Min(hi + beam + 1, horizon + 1) : hi + beam + 1));
                for (var y = top; y < bottom; y++)
                {
                    var yy = y + 0.5; var off = yy < lo ? lo - yy : yy > hi ? yy - hi : 0;
                    var hidden = rising ? Clamp(horizon - yy + 0.5) : Clamp(yy - horizon + 0.5);
                    var shown = Clamp(beam - Math.Sqrt(off * off + across * across) + 0.5) * lit * hidden;
                    if (shown <= cover[y]) continue;
                    cover[y] = shown;
                    for (var c = 0; c < 3; c++) color[y * 3 + c] = original ? s.BlackLevel / 100 + (1 - s.BlackLevel / 100) * scan[c * plane + i] : screen[c] + (phosphor[c] - screen[c]) * level;
                }
                if (lit > 0.5) horizon = rising ? Math.Min(horizon, lo - beam) : Math.Max(horizon, hi + beam);
            }
            for (var y = 0; y < height; y++)
            {
                var p = dst + (long)y * output.RowBytes + x * 4; var a = alpha[y * width + x];
                for (var c = 0; c < 3; c++) p[c] = (byte)Math.Round(Clamp(screen[c] + (color[y * 3 + c] * 1.35 - screen[c]) * cover[y]) * a);
            }
        });
        if (s.Split > 0)
            Parallel.For(0, height, y =>
            {
                var row = new Span<byte>(dst + (long)y * output.RowBytes, width * 4); var copy = row.ToArray();
                for (var x = 0; x < width; x++)
                {
                    row[x * 4] = (byte)Math.Min(row[x * 4 + 3], x >= s.Split ? copy[(x - s.Split) * 4] : 0);
                    row[x * 4 + 2] = (byte)Math.Min(row[x * 4 + 3], x + s.Split < width ? copy[(x + s.Split) * 4 + 2] : 0);
                }
            });
        if (s.Glow > 0)
        {
            using var soft = Pixels.NewColor(width, height);
            using (var filter = SKImageFilter.CreateBlur(spacing * 3 + 3, spacing * 3 + 3, SKShaderTileMode.Clamp))
            using (var canvas = new SKCanvas(soft))
            using (var paint = new SKPaint { ImageFilter = filter }) canvas.DrawBitmap(output, 0, 0, paint);
            var blurred = (byte*)soft.GetPixels();
            Parallel.For(0, height, y =>
            {
                for (var x = 0; x < width; x++)
                {
                    var p = dst + (long)y * output.RowBytes + x * 4; var q = blurred + (long)y * soft.RowBytes + x * 4;
                    for (var c = 0; c < 3; c++)
                    {
                        var room = (p[3] - p[c]) * 0.7; var added = q[c] * s.Glow / 100 * 2.5 * p[3] / 255;
                        if (room > 0) p[c] = (byte)Math.Min(p[3], Math.Round(p[c] + room * (1 - Math.Exp(-added / room))));
                    }
                }
            });
        }
        return output;
    }
}
