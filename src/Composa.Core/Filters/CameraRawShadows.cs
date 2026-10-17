using SkiaSharp;

namespace Composa.Filters;

public static unsafe partial class CameraRawPixels
{
    private sealed record ShadowTables(int XCount, int KCount, double NormalFrom, double XLow, double XStep,
        double KLow, double KStep, double[] Amounts, float[][][] Coefficients);
    private static readonly Lazy<ShadowTables> ShadowsData = new(LoadShadows);
    private static readonly double[] ShadowDecoded = Enumerable.Range(0, 256).Select(i => SrgbToLinear(i / 255.0)).ToArray();

    private static ShadowTables LoadShadows()
    {
        using var stream = typeof(CameraRawPixels).Assembly.GetManifestResourceStream("CameraRawData/CameraRawShadows.bin")
            ?? throw new InvalidDataException("Camera Raw Shadows data is missing.");
        using var reader = new BinaryReader(stream);
        if (new string(reader.ReadChars(4)) != "CRS2") throw new InvalidDataException("Invalid Camera Raw Shadows header.");
        int sets = reader.ReadUInt16(), amounts = reader.ReadUInt16(), xCount = reader.ReadUInt16(), kCount = reader.ReadUInt16(), terms = reader.ReadUInt16();
        if (sets != 2 || amounts != 5 || terms != 3 || xCount != 35 || kCount != 6) throw new InvalidDataException("Invalid Camera Raw Shadows dimensions.");
        double normal = reader.ReadSingle(), xLow = reader.ReadSingle(), xStep = reader.ReadSingle(), kLow = reader.ReadSingle(), kStep = reader.ReadSingle();
        var values = Enumerable.Range(0, amounts).Select(_ => (double)reader.ReadSingle()).ToArray();
        var coefficients = new float[sets][][];
        for (var set = 0; set < sets; set++)
        {
            coefficients[set] = new float[amounts][];
            for (var amount = 0; amount < amounts; amount++)
                coefficients[set][amount] = Enumerable.Range(0, xCount * kCount * terms).Select(_ => reader.ReadSingle()).ToArray();
        }
        return new ShadowTables(xCount, kCount, normal, xLow, xStep, kLow, kStep, values, coefficients);
    }

    private static float[] ShadowCoefficients(double amount, int set, ShadowTables data)
    {
        var points = data.Amounts.Select((value, index) => (Value: value, Coefficients: (float[]?)data.Coefficients[set][index]))
            .Append((0.0, (float[]?)null)).OrderBy(point => point.Item1).ToArray();
        amount = Math.Clamp(amount, points[0].Item1, points[^1].Item1);
        var at = 0;
        while (at < points.Length - 2 && amount > points[at + 1].Item1) at++;
        var t = (float)((amount - points[at].Item1) / (points[at + 1].Item1 - points[at].Item1));
        var result = new float[data.XCount * data.KCount * 3];
        for (var i = 0; i < result.Length; i++) result[i] = (points[at].Item2?[i] ?? 0) * (1 - t) + (points[at + 1].Item2?[i] ?? 0) * t;
        return result;
    }

    private static void ShadowLinear(byte* pixel, out double r, out double g, out double b)
    {
        double alpha = pixel[3];
        r = alpha == 255 ? ShadowDecoded[pixel[0]] : SrgbToLinear(Math.Min(1, pixel[0] / alpha));
        g = alpha == 255 ? ShadowDecoded[pixel[1]] : SrgbToLinear(Math.Min(1, pixel[1] / alpha));
        b = alpha == 255 ? ShadowDecoded[pixel[2]] : SrgbToLinear(Math.Min(1, pixel[2] / alpha));
    }

    /// <summary>Port of Compositor's region-adaptive Shadows pass, using the original fitted coefficients.</summary>
    private static void AdaptiveShadows(SKBitmap image, double amount, double pictureScale)
    {
        const double floor = 1.0 / 8192;
        var data = ShadowsData.Value;
        int width = image.Width, height = image.Height, stride = image.RowBytes;
        var pixels = (byte*)image.GetPixels();
        var coefficients = ShadowCoefficients(amount, Math.Max(width, height) / pictureScale >= data.NormalFrom ? 1 : 0, data);
        var sums = new (double Total, long Count, double All, long AllCount)[32];
        var rows = (height + 31) / 32;
        Parallel.For(0, 32, band =>
        {
            double total = 0, all = 0; long count = 0, allCount = 0;
            for (var y = band * rows; y < Math.Min(height, (band + 1) * rows); y++)
                for (var x = 0; x < width; x++)
                {
                    var p = pixels + (long)y * stride + x * 4;
                    if (p[3] == 0) continue;
                    ShadowLinear(p, out var r, out var g, out var b);
                    var luma = Rec709(r, g, b); all += luma; allCount++;
                    var v = p[1];
                    var textured = x > 0 && p[-3] != v || x + 1 < width && p[5] != v || y > 0 && (p - stride)[1] != v || y + 1 < height && (p + stride)[1] != v;
                    if (textured) { total += luma; count++; }
                }
            sums[band] = (total, count, all, allCount);
        });
        var texturedCount = sums.Sum(part => part.Count);
        var allPixels = sums.Sum(part => part.AllCount);
        var key = Math.Log2(Math.Max(floor, texturedCount > 0 ? sums.Sum(part => part.Total) / texturedCount : allPixels > 0 ? sums.Sum(part => part.All) / allPixels : .18));
        int scale = Math.Max(1, Math.Min(width, height) / 384), lowWidth = (width + scale - 1) / scale, lowHeight = (height + scale - 1) / scale;
        var low = new float[lowWidth * lowHeight]; var weight = new float[low.Length];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var p = pixels + (long)y * stride + x * 4;
                if (p[3] == 0) continue;
                ShadowLinear(p, out var r, out var g, out var b);
                var index = y / scale * lowWidth + x / scale;
                low[index] += (float)Math.Log2(Math.Max(floor, Rec709(r, g, b))); weight[index]++;
            }
        for (var i = 0; i < low.Length; i++) low[i] = weight[i] > 0 ? low[i] / weight[i] : (float)key;
        var radius = Math.Max(1, (int)Math.Round(.25 * Math.Min(lowWidth, lowHeight), MidpointRounding.AwayFromZero));
        var mean = BoxMean(low, lowWidth, lowHeight, radius);
        for (var i = 0; i < low.Length; i++) weight[i] = low[i] * low[i];
        var square = BoxMean(weight, lowWidth, lowHeight, radius);
        for (var i = 0; i < low.Length; i++)
        {
            var variance = Math.Max(0, square[i] - mean[i] * mean[i]);
            weight[i] = variance / (variance + 1.5f); square[i] = mean[i] - weight[i] * mean[i];
        }
        var ma = BoxMean(weight, lowWidth, lowHeight, radius);
        var mb = BoxMean(square, lowWidth, lowHeight, radius);
        var plane = data.XCount * data.KCount;
        var kp = Math.Clamp((key - data.KLow) / data.KStep, 0, data.KCount - 1);
        var k0 = Math.Min((int)kp, data.KCount - 1); var k1 = Math.Min(k0 + 1, data.KCount - 1); var kt = kp - k0;
        ForRows(height, y =>
        {
            var fy = Math.Clamp((y + .5) / scale - .5, 0, lowHeight - 1);
            var y0 = (int)fy; var y1 = Math.Min(y0 + 1, lowHeight - 1); var ty = fy - y0;
            for (var x = 0; x < width; x++)
            {
                var p = pixels + (long)y * stride + x * 4;
                if (p[3] == 0) continue;
                ShadowLinear(p, out var r, out var g, out var b);
                var luma = Rec709(r, g, b); if (luma <= floor) continue;
                var fx = Math.Clamp((x + .5) / scale - .5, 0, lowWidth - 1);
                var x0 = (int)fx; var x1 = Math.Min(x0 + 1, lowWidth - 1); var tx = fx - x0;
                double Interpolate(float[] field) => (field[y0 * lowWidth + x0] * (1 - tx) + field[y0 * lowWidth + x1] * tx) * (1 - ty)
                    + (field[y1 * lowWidth + x0] * (1 - tx) + field[y1 * lowWidth + x1] * tx) * ty;
                var level = Math.Log2(luma); var region = Interpolate(ma) * level + Interpolate(mb); var detail = level - region;
                var xp = Math.Clamp((region - key - data.XLow) / data.XStep, 0, data.XCount - 1);
                var xc = Math.Min((int)xp, data.XCount - 2); var xt = xp - xc;
                double Term(int term)
                {
                    var offset = term * plane;
                    var row0 = coefficients[offset + k0 * data.XCount + xc] * (1 - xt) + coefficients[offset + k0 * data.XCount + xc + 1] * xt;
                    var row1 = coefficients[offset + k1 * data.XCount + xc] * (1 - xt) + coefficients[offset + k1 * data.XCount + xc + 1] * xt;
                    return row0 * (1 - kt) + row1 * kt;
                }
                var gain = Math.Pow(2, Term(0) + Term(1) * Math.Max(detail, 0) + Term(2) * Math.Min(detail, 0));
                Write(p, LinearToSrgb(r * gain), LinearToSrgb(g * gain), LinearToSrgb(b * gain));
            }
        });
    }

    // Unlike BoxBlur, the window shrinks at the image boundary. This matters for the fitted Shadows coefficients.
    private static float[] BoxMean(float[] source, int width, int height, int radius)
    {
        var temp = new float[source.Length]; var result = new float[source.Length];
        ForRows(height, y =>
        {
            double sum = 0; int low = 0, high = 0;
            for (var x = 0; x < width; x++)
            {
                var wantedLow = Math.Max(0, x - radius); var wantedHigh = Math.Min(width, x + radius + 1);
                while (high < wantedHigh) sum += source[y * width + high++];
                while (low < wantedLow) sum -= source[y * width + low++];
                temp[y * width + x] = (float)(sum / (high - low));
            }
        });
        Parallel.For(0, width, x =>
        {
            double sum = 0; int low = 0, high = 0;
            for (var y = 0; y < height; y++)
            {
                var wantedLow = Math.Max(0, y - radius); var wantedHigh = Math.Min(height, y + radius + 1);
                while (high < wantedHigh) sum += temp[high++ * width + x];
                while (low < wantedLow) sum -= temp[low++ * width + x];
                result[y * width + x] = (float)(sum / (high - low));
            }
        });
        return result;
    }
}
