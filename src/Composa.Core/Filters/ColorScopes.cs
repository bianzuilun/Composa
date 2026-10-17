using Composa.Rendering;
using SkiaSharp;

namespace Composa.Filters;

/// <summary>Bounded Rec.709 chroma density for a vectorscope. Independent of the document's pixel count.</summary>
public sealed class Vectorscope
{
    public const int Size = 128;
    private readonly int[] counts = new int[Size * Size];
    public ReadOnlySpan<int> Counts => counts;
    public int Samples { get; private set; }
    public int Maximum { get; private set; }

    public static unsafe Vectorscope Of(SKBitmap bitmap)
    {
        var result = new Vectorscope();
        var step = Math.Max(1, (int)Math.Ceiling(Math.Sqrt((long)bitmap.Width * bitmap.Height / 131072.0)));
        var wide = DeepPixels.IsWide(bitmap);
        for (var y = 0; y < bitmap.Height; y += step)
        {
            var row = (byte*)bitmap.GetPixels() + (long)y * bitmap.RowBytes;
            for (var x = 0; x < bitmap.Width; x += step)
            {
                float r, g, b;
                if (wide)
                {
                    var p = DeepPixels.Read(bitmap, x, y); if (p.Alpha < 8 / 255f) continue;
                    r = Math.Clamp(p.Red / p.Alpha, 0, 1); g = Math.Clamp(p.Green / p.Alpha, 0, 1); b = Math.Clamp(p.Blue / p.Alpha, 0, 1);
                }
                else
                {
                    var p = row + x * 4; var a = p[3]; if (a < 8) continue;
                    r = p[0] / (float)a; g = p[1] / (float)a; b = p[2] / (float)a;
                }
                var u = -.114572f * r - .385428f * g + .5f * b;
                var v = .5f * r - .454153f * g - .045847f * b;
                int ix = Math.Clamp((int)MathF.Round((.5f + u / 1.2f) * (Size - 1)), 0, Size - 1), iy = Math.Clamp((int)MathF.Round((.5f - v / 1.2f) * (Size - 1)), 0, Size - 1);
                result.Maximum = Math.Max(result.Maximum, ++result.counts[iy * Size + ix]); result.Samples++;
            }
        }
        return result;
    }

    public SKBitmap Plot()
    {
        var bitmap = Pixels.NewColor(Size, Size); bitmap.Erase(new SKColor(18, 18, 18));
        using (var canvas = new SKCanvas(bitmap))
        using (var grid = new SKPaint { Color = new SKColor(65, 65, 65), Style = SKPaintStyle.Stroke, StrokeWidth = .5f, IsAntialias = true })
        {
            canvas.DrawCircle(63.5f, 63.5f, 62, grid); canvas.DrawCircle(63.5f, 63.5f, 31, grid);
            canvas.DrawLine(1, 63.5f, 126, 63.5f, grid); canvas.DrawLine(63.5f, 1, 63.5f, 126, grid);
        }
        if (Maximum == 0) return bitmap;
        var denominator = Math.Log(1 + Maximum);
        for (var y = 0; y < Size; y++) for (var x = 0; x < Size; x++)
        {
            var count = counts[y * Size + x]; if (count == 0) continue;
            var brightness = .25 + .75 * Math.Log(1 + count) / denominator;
            var u = (x / 127.0 - .5) * 1.2; var v = (.5 - y / 127.0) * 1.2;
            double r = .5 + 1.5748 * v, g = .5 - .1873 * u - .4681 * v, b = .5 + 1.8556 * u;
            var maximum = Math.Max(.5, Math.Max(r, Math.Max(g, b)));
            if (Math.Abs(u) + Math.Abs(v) < .02) r = g = b = maximum;
            byte Channel(double c) => (byte)Math.Clamp(Math.Round(c / maximum * brightness * 255), 0, 255);
            bitmap.SetPixel(x, y, new SKColor(Channel(r), Channel(g), Channel(b)));
        }
        return bitmap;
    }
}
