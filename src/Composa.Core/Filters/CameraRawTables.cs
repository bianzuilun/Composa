using System.IO.Compression;
using System.Runtime.CompilerServices;
using Composa.Rendering;
using SkiaSharp;

namespace Composa.Filters;

/// <summary>Measured Camera Raw tables and interpolation from Compositor v1.5.3 (MIT).
/// Tables are red-slowest, unlike ColorLattice. The composing grid retains floats between stages.</summary>
internal static class CameraRawTables
{
    internal const int Grid = 33;
    private const int Size = 17, Cells = Size * Size * Size, Entries = Cells * 3, Count = 763;
    private static readonly Lazy<byte[]> Tables = new(Load);
    private static readonly double[] Brightnesses = [53, 80, 104, 130, 160, 190, 224];
    private static readonly double[] AdaptiveValues = [-100, -50, -25, 0, 25, 50, 100];
    private static readonly double[] Decoded = Enumerable.Range(0, 256).Select(i => CameraRawPixels.SrgbToLinear(i / 255.0)).ToArray();
    private sealed record Statistics(double Brightest, double LogBrightest);
    private static readonly ConditionalWeakTable<SKBitmap, Statistics> StatisticsCache = new();
    static CameraRawTables() => Pixels.Invalidated += bitmap => StatisticsCache.Remove(bitmap);

    internal readonly record struct Part(int Table, double Weight);
    internal sealed record Stage(Part[] Parts)
    {
        internal (double R, double G, double B) Apply(double r, double g, double b)
        {
            double red = 0, green = 0, blue = 0;
            foreach (var part in Parts)
            {
                if (part.Weight == 0) continue;
                var color = part.Table < 0 ? (r, g, b) : Sample(part.Table, r, g, b);
                red += color.Item1 * part.Weight; green += color.Item2 * part.Weight; blue += color.Item3 * part.Weight;
            }
            return (Math.Clamp(red, 0, 1), Math.Clamp(green, 0, 1), Math.Clamp(blue, 0, 1));
        }
    }

    private static byte[] Load()
    {
        using var resource = typeof(CameraRawTables).Assembly.GetManifestResourceStream("CameraRawData/CameraRawTables.gz")
            ?? throw new InvalidDataException("Camera Raw tables are missing.");
        using var compressed = new GZipStream(resource, CompressionMode.Decompress);
        using var reader = new BinaryReader(compressed);
        if (new string(reader.ReadChars(4)) != "CRT3" || reader.ReadUInt16() != Size || reader.ReadUInt16() != Count)
            throw new InvalidDataException("Invalid Camera Raw table header.");
        var step = reader.ReadUInt16();
        var planes = reader.ReadBytes(Count * Entries);
        if (step == 0 || planes.Length != Count * Entries) throw new InvalidDataException("Camera Raw tables are truncated.");
        var result = new byte[Count * Entries];
        for (var table = 0; table < Count; table++)
            for (var channel = 0; channel < 3; channel++)
            {
                var start = (table * 3 + channel) * Cells;
                byte running = 0;
                for (var cell = 0; cell < Cells; cell++)
                {
                    running = unchecked((byte)(running + planes[start + cell]));
                    var axis = channel == 0 ? cell / (Size * Size) : channel == 1 ? cell / Size % Size : cell % Size;
                    var level = (int)Math.Round(axis * 255.0 / (Size - 1), MidpointRounding.AwayFromZero);
                    result[table * Entries + cell * 3 + channel] = (byte)Math.Clamp(level + unchecked((sbyte)running) * step, 0, 255);
                }
            }
        return result;
    }

    private static (int Index, double Fraction) Between(double value, double low, double step, int count)
    {
        var position = Math.Clamp((value - low) / step, 0, count - 1);
        var index = Math.Min(count - 2, (int)position);
        return (index, position - index);
    }

    private static (int Index, double Fraction) Between(double value, double[] points)
    {
        value = Math.Clamp(value, points[0], points[^1]);
        var index = 0;
        while (index < points.Length - 2 && value > points[index + 1]) index++;
        return (index, (value - points[index]) / (points[index + 1] - points[index]));
    }

    private static Stage Range(int start, double value, double low = -100, double step = 50, int count = 5)
    {
        var (index, t) = Between(value, low, step, count);
        return new Stage([new(start + index, 1 - t), new(start + index + 1, t)]);
    }

    private static Stage Adaptive(int start, double value, double brightness)
    {
        var (row, down) = Between(brightness, Brightnesses);
        var (column, across) = Between(value, AdaptiveValues);
        var first = start + row * AdaptiveValues.Length + column;
        return new Stage([new(first, (1 - down) * (1 - across)), new(first + 1, (1 - down) * across),
            new(first + 7, down * (1 - across)), new(first + 8, down * across)]);
    }

    private static Stage WhiteBalance(double temperature, double tint)
    {
        var (row, down) = Between(temperature, -100, 25, 9);
        var (column, across) = Between(tint, -100, 25, 9);
        return new Stage([new(row * 9 + column, (1 - down) * (1 - across)), new(row * 9 + column + 1, (1 - down) * across),
            new((row + 1) * 9 + column, down * (1 - across)), new((row + 1) * 9 + column + 1, down * across)]);
    }

    internal static unsafe List<Stage> LightStages(SKBitmap source, CameraRawSettings settings, out int beforeShadows)
    {
        var s = settings;
        var stats = s.Contrast != 0 || s.Highlights != 0 ? StatisticsCache.GetValue(source, Measure) : new Statistics(128, 128);
        var result = new List<Stage>();
        double[] calibration = [s.Calibration.RedHue, s.Calibration.RedSaturation, s.Calibration.GreenHue, s.Calibration.GreenSaturation, s.Calibration.BlueHue, s.Calibration.BlueSaturation];
        for (var i = 0; i < calibration.Length; i++) if (calibration[i] != 0) result.Add(Range(405 + i * 5, calibration[i]));
        if (s.Exposure != 0) result.Add(Range(81, s.Exposure, -5, .5, 21));
        if (s.Temperature != 0 || s.Tint != 0) result.Add(WhiteBalance(s.Temperature, s.Tint));
        if (s.Contrast != 0) result.Add(Adaptive(138, s.Contrast, stats.Brightest));
        if (s.Highlights != 0) result.Add(Adaptive(187, s.Highlights, stats.LogBrightest));
        beforeShadows = result.Count;
        if (s.Whites != 0) result.Add(Range(102, s.Whites, -100, 25, 9));
        if (s.Blacks != 0) result.Add(Range(111, s.Blacks, -100, 25, 9));
        if (s.Saturation != 0) result.Add(Range(120, s.Saturation, -100, 25, 9));
        if (s.Vibrance != 0) result.Add(Range(129, s.Vibrance, -100, 25, 9));
        if (s.Curve.HasMeasuredDividers)
        {
            double[] amounts = [s.Curve.Shadows, s.Curve.Darks, s.Curve.Lights, s.Curve.Highlights];
            for (var i = 0; i < amounts.Length; i++) if (amounts[i] != 0) result.Add(Range(435 + i * 5, amounts[i]));
        }
        return result;
    }

    private static unsafe Statistics Measure(SKBitmap source)
    {
        var parts = new (double Top, double LogTop, double Weight)[32];
        var rows = (source.Height + 31) / 32;
        var pixels = (byte*)source.GetPixels();
        Parallel.For(0, 32, band =>
        {
            double brightest = 0, logBrightest = 0, weight = 0;
            for (var y = band * rows; y < Math.Min(source.Height, (band + 1) * rows); y++)
                for (var x = 0; x < source.Width; x++)
                {
                    var p = pixels + (long)y * source.RowBytes + x * 4;
                    if (p[3] == 0) continue;
                    var top = Math.Max(p[0], Math.Max(p[1], p[2]));
                    var linear = p[3] == 255 ? Decoded[top] : CameraRawPixels.SrgbToLinear(Math.Min(1, (double)top / p[3]));
                    var w = p[3] / 255.0;
                    brightest += w * linear; logBrightest += w * Math.Log(linear + .0003); weight += w;
                }
            parts[band] = (brightest, logBrightest, weight);
        });
        var total = parts.Sum(p => p.Weight);
        return total == 0 ? new Statistics(128, 128) : new Statistics(
            CameraRawPixels.LinearToSrgb(parts.Sum(p => p.Top) / total) * 255,
            CameraRawPixels.LinearToSrgb(Math.Exp(parts.Sum(p => p.LogTop) / total) - .0003) * 255);
    }

    internal static List<Stage> ColorStages(CameraRawSettings settings)
    {
        var result = new List<Stage>();
        double[][] mixer = [settings.Mixer.Luminance, settings.Mixer.Saturation, settings.Mixer.Hue];
        for (var kind = 0; kind < 3; kind++)
            for (var color = 0; color < 8; color++) if (mixer[kind][color] != 0) result.Add(Range(285 + (kind * 8 + color) * 5, mixer[kind][color]));
        var grading = settings.Grading;
        var wheels = grading.Wheels;
        for (var i = 0; i < wheels.Length; i++) if (wheels[i].Luminance != 0) result.Add(Range(743 + i * 5, wheels[i].Luminance));
        var balance = grading.Balance / 100;
        var highlights = Math.Max(0, balance < 0 ? 1 + 1.4 * balance : 1 + 2 * balance);
        var shadows = Math.Max(0, balance < 0 ? 1 - 1.2 * balance : 1 - 1.4 * balance);
        var (blendLow, blendUp) = Between(grading.Blending, [0, 50, 100]);
        int Section(int blend, int wheel) => 455 + (blend == 0 ? 0 : blend == 1 ? 72 : 216) + wheel * 36;
        // The table order is shadows, highlights, midtones, global; the UI order is shadows, midtones, highlights, global.
        foreach (var (wheel, color, share, blends) in new[] { (3, wheels[3], 1.0, false), (2, wheels[1], 1.0, false), (1, wheels[2], highlights, true), (0, wheels[0], shadows, true) })
        {
            if (color.Saturation * share <= 0) continue;
            var position = color.Hue % 360 / 30;
            var first = (int)position % 12; var second = (first + 1) % 12; var around = position - (int)position;
            var (level, up) = Between(color.Saturation * share, [0, 25, 50, 100]);
            var parts = new List<Part>();
            foreach (var (blend, blendWeight) in blends ? new[] { (blendLow, 1 - blendUp), (blendLow + 1, blendUp) } : [(1, 1.0)])
                foreach (var (hue, hueWeight) in new[] { (first, 1 - around), (second, around) })
                    foreach (var (saturation, saturationWeight) in new[] { (level, 1 - up), (level + 1, up) })
                        parts.Add(new Part(saturation == 0 ? -1 : Section(blend, wheel) + hue * 3 + saturation - 1, blendWeight * hueWeight * saturationWeight));
            result.Add(new Stage(parts.ToArray()));
        }
        return result;
    }

    private static (double R, double G, double B) Sample(int table, double r, double g, double b)
    {
        var data = Tables.Value;
        double xr = Math.Clamp(r, 0, 1) * (Size - 1), xg = Math.Clamp(g, 0, 1) * (Size - 1), xb = Math.Clamp(b, 0, 1) * (Size - 1);
        int ir = Math.Min(Size - 2, (int)xr), ig = Math.Min(Size - 2, (int)xg), ib = Math.Min(Size - 2, (int)xb);
        double fr = xr - ir, fg = xg - ig, fb = xb - ib, red = 0, green = 0, blue = 0;
        for (var corner = 0; corner < 8; corner++)
        {
            int dr = corner >> 2, dg = corner >> 1 & 1, db = corner & 1;
            var weight = (dr == 1 ? fr : 1 - fr) * (dg == 1 ? fg : 1 - fg) * (db == 1 ? fb : 1 - fb);
            if (weight == 0) continue;
            var at = table * Entries + (((ir + dr) * Size + ig + dg) * Size + ib + db) * 3;
            red += weight * data[at]; green += weight * data[at + 1]; blue += weight * data[at + 2];
        }
        return (red / 255, green / 255, blue / 255);
    }

    internal static float[] Compose(IReadOnlyList<Stage> stages, float[]? onto = null)
    {
        var result = onto ?? new float[Grid * Grid * Grid * 3];
        Parallel.For(0, Grid, red =>
        {
            for (var green = 0; green < Grid; green++)
                for (var blue = 0; blue < Grid; blue++)
                {
                    var at = ((red * Grid + green) * Grid + blue) * 3;
                    (double R, double G, double B) color = onto == null ? (red / (double)(Grid - 1), green / (double)(Grid - 1), blue / (double)(Grid - 1)) : (onto[at], onto[at + 1], onto[at + 2]);
                    foreach (var stage in stages) color = stage.Apply(color.R, color.G, color.B);
                    result[at] = (float)color.R; result[at + 1] = (float)color.G; result[at + 2] = (float)color.B;
                }
        });
        return result;
    }

    internal static (double R, double G, double B) Lookup(float[] table, double r, double g, double b)
    {
        double xr = Math.Clamp(r, 0, 1) * (Grid - 1), xg = Math.Clamp(g, 0, 1) * (Grid - 1), xb = Math.Clamp(b, 0, 1) * (Grid - 1);
        int ir = Math.Min(Grid - 2, (int)xr), ig = Math.Min(Grid - 2, (int)xg), ib = Math.Min(Grid - 2, (int)xb);
        double fr = xr - ir, fg = xg - ig, fb = xb - ib, red = 0, green = 0, blue = 0;
        for (var corner = 0; corner < 8; corner++)
        {
            int dr = corner >> 2, dg = corner >> 1 & 1, db = corner & 1;
            var weight = (dr == 1 ? fr : 1 - fr) * (dg == 1 ? fg : 1 - fg) * (db == 1 ? fb : 1 - fb);
            if (weight == 0) continue;
            var at = (((ir + dr) * Grid + ig + dg) * Grid + ib + db) * 3;
            red += weight * table[at]; green += weight * table[at + 1]; blue += weight * table[at + 2];
        }
        return (Math.Clamp(red, 0, 1), Math.Clamp(green, 0, 1), Math.Clamp(blue, 0, 1));
    }

    internal static (double Temperature, double Tint)? Neutralize(double r, double g, double b)
    {
        if (Math.Min(r, Math.Min(g, b)) <= .0001) return null;
        double Cast(double temperature, double tint)
        {
            var (red, green, blue) = WhiteBalance(temperature, tint).Apply(r, g, b);
            return Math.Max(red, Math.Max(green, blue)) - Math.Min(red, Math.Min(green, blue));
        }
        var best = (Temperature: 0.0, Tint: 0.0, Cast: Cast(0, 0));
        foreach (var (step, reach) in new[] { (10.0, 100.0), (2.0, 10.0), (.5, 2.0) })
        {
            var center = best;
            for (var temperature = center.Temperature - reach; temperature <= center.Temperature + reach; temperature += step)
                for (var tint = center.Tint - reach; tint <= center.Tint + reach; tint += step)
                {
                    if (Math.Abs(temperature) > 100 || Math.Abs(tint) > 100) continue;
                    var cast = Cast(temperature, tint);
                    if (cast < best.Cast) best = (temperature, tint, cast);
                }
        }
        return (best.Temperature, best.Tint);
    }
}
