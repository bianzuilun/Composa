using Composa.Model;
using Composa.Rendering;
using SkiaSharp;

namespace Composa.Filters;

/// <summary>Float convolution without Skia image-filter intermediate quantization.</summary>
internal static unsafe class FloatBlur
{
    internal static SKBitmap Gaussian(SKBitmap source, double sigma, bool clamp, int padX = 0, int padY = 0)
    {
        int width = checked(source.Width + padX * 2), height = checked(source.Height + padY * 2);
        if (!DocumentLimits.FitsSurface(width, height)) throw new InvalidOperationException("The filter exceeds the supported surface size.");
        var result = DeepPixels.New(width, height, hdr: true);
        using var scratch = DeepPixels.New(width, height, hdr: true);
        try
        {
            using (var drawing = new SKCanvas(result)) drawing.DrawBitmap(source, padX, padY);
            sigma = Math.Max(.1, sigma);
            if (sigma <= 3)
            {
                var radius = (int)Math.Ceiling(sigma * 3); var kernel = new double[radius * 2 + 1]; double sum = 0;
                for (var i = -radius; i <= radius; i++) sum += kernel[i + radius] = Math.Exp(-i * i / (2 * sigma * sigma));
                for (var i = 0; i < kernel.Length; i++) kernel[i] /= sum;
                Convolve(result, scratch, kernel, true, clamp); Convolve(scratch, result, kernel, false, clamp);
            }
            else
            {
                // Three odd box kernels approximate the requested Gaussian variance in O(pixels), regardless of radius.
                var lower = (int)Math.Floor(Math.Sqrt(4 * sigma * sigma + 1)); if ((lower & 1) == 0) lower--;
                var upper = lower + 2;
                var count = (int)Math.Round((12 * sigma * sigma - 3 * lower * lower - 12 * lower - 9) / (-4.0 * lower - 4));
                for (var i = 0; i < 3; i++)
                {
                    var radius = ((i < count ? lower : upper) - 1) / 2;
                    Box(result, scratch, radius, true, clamp); Box(scratch, result, radius, false, clamp);
                }
            }
            return result;
        }
        catch { result.Dispose(); throw; }
    }

    private static void Convolve(SKBitmap source, SKBitmap target, double[] kernel, bool horizontal, bool clamp)
    {
        var length = horizontal ? source.Width : source.Height; var lines = horizontal ? source.Height : source.Width;
        var sourceStep = horizontal ? 4 : source.RowBytes / 4; var targetStep = horizontal ? 4 : target.RowBytes / 4;
        var radius = kernel.Length / 2;
        Parallel.For(0, lines, line =>
        {
            var input = (float*)((byte*)source.GetPixels() + (horizontal ? (long)line * source.RowBytes : line * 16L));
            var output = (float*)((byte*)target.GetPixels() + (horizontal ? (long)line * target.RowBytes : line * 16L));
            Span<double> sum = stackalloc double[4];
            for (var x = 0; x < length; x++)
            {
                sum.Clear();
                for (var k = -radius; k <= radius; k++)
                {
                    var index = x + k;
                    if (!clamp && (index < 0 || index >= length)) continue;
                    index = Math.Clamp(index, 0, length - 1); var at = index * sourceStep; var weight = kernel[k + radius];
                    for (var c = 0; c < 4; c++) sum[c] += input[at + c] * weight;
                }
                for (var c = 0; c < 4; c++) output[x * targetStep + c] = (float)sum[c];
            }
        });
    }

    private static void Box(SKBitmap source, SKBitmap target, int radius, bool horizontal, bool clamp)
    {
        if (radius <= 0) { Pixels.CopyPixels(source, target); return; }
        var length = horizontal ? source.Width : source.Height; var lines = horizontal ? source.Height : source.Width;
        var sourceStep = horizontal ? 4 : source.RowBytes / 4; var targetStep = horizontal ? 4 : target.RowBytes / 4;
        var divisor = radius * 2 + 1.0;
        Parallel.For(0, lines, line =>
        {
            var input = (float*)((byte*)source.GetPixels() + (horizontal ? (long)line * source.RowBytes : line * 16L));
            var output = (float*)((byte*)target.GetPixels() + (horizontal ? (long)line * target.RowBytes : line * 16L));
            Span<double> sum = stackalloc double[4]; sum.Clear();
            for (var i = -radius; i <= radius; i++)
            {
                if (!clamp && (i < 0 || i >= length)) continue;
                var at = Math.Clamp(i, 0, length - 1) * sourceStep;
                for (var c = 0; c < 4; c++) sum[c] += input[at + c];
            }
            for (var x = 0; x < length; x++)
            {
                for (var c = 0; c < 4; c++) output[x * targetStep + c] = (float)(sum[c] / divisor);
                var remove = x - radius; var add = x + radius + 1;
                if (clamp || remove >= 0 && remove < length)
                { var at = Math.Clamp(remove, 0, length - 1) * sourceStep; for (var c = 0; c < 4; c++) sum[c] -= input[at + c]; }
                if (clamp || add >= 0 && add < length)
                { var at = Math.Clamp(add, 0, length - 1) * sourceStep; for (var c = 0; c < 4; c++) sum[c] += input[at + c]; }
            }
        });
    }
}
