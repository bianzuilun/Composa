using System.Diagnostics;
using System.Text;
using Composa.Rendering;
using SkiaSharp;

namespace Composa.IO;

/// <summary>
/// What to spend a RAW file's latitude on before importing a 16-bit layer. The camera's own rendering is the starting
/// point; every value is a departure from it, so Reset has somewhere to go back to.
/// </summary>
public sealed record RawDevelopSettings
{
    /// <summary>Stops of exposure either side of what the camera recorded, -3…3.</summary>
    public double Exposure { get; init; }
    /// <summary>White balance away from the camera's reading: -100 cooler to 100 warmer.</summary>
    public double Temperature { get; init; }
    /// <summary>Green to magenta balance away from the camera's reading, -100…100.</summary>
    public double Tint { get; init; }
    /// <summary>Edge-aware luminance noise reduction, 0…100.</summary>
    public double LuminanceNoise { get; init; }
    /// <summary>Chroma noise reduction without smoothing luminance detail, 0…100.</summary>
    public double ColorNoise { get; init; }
    /// <summary>Reconstruct clipped channels from the camera's unclipped channels, 0…100.</summary>
    public double HighlightRecovery { get; init; }
    /// <summary>Optional radial lens correction, -100…100.</summary>
    public double LensDistortion { get; init; }
    public bool OpenCameraRaw { get; init; }

    public bool IsAsShot => Exposure == 0 && Temperature == 0 && Tint == 0 && LuminanceNoise == 0 && ColorNoise == 0 && HighlightRecovery == 0 && LensDistortion == 0;
}

/// <summary>
/// A decoded camera frame at 16 bits per channel, sRGB-encoded, kept until the develop settings are chosen. Sixteen bits
/// hold more of the sensor's range than a layer can, so darkening a bright frame recovers tones an 8-bit decode would
/// have already clipped.
/// </summary>
public sealed class RawImage
{
    public int Width { get; }
    public int Height { get; }
    /// <summary>Interleaved RGB, row by row.</summary>
    public ushort[] Samples { get; }

    public RawImage(int width, int height, ushort[] samples)
    {
        if (width <= 0 || height <= 0 || samples.Length != (long)width * height * 3) throw new ArgumentException("The sample count does not match the size.");
        Width = width;
        Height = height;
        Samples = samples;
    }

    /// <summary>
    /// Reads a Netpbm PAM image (<c>P7</c>) with RGB or RGB plus alpha tuples at 8 or 16 bits, which is what ImageMagick
    /// is asked to write: a header of plain words followed by big-endian samples.
    /// </summary>
    public static RawImage ParsePam(byte[] data)
    {
        var header = Encoding.ASCII.GetString(data, 0, Math.Min(data.Length, 512));
        if (!header.StartsWith("P7", StringComparison.Ordinal)) throw new InvalidDataException("The decoded frame was not in the expected format.");
        var end = header.IndexOf("ENDHDR\n", StringComparison.Ordinal);
        if (end < 0) throw new InvalidDataException("The decoded frame was not in the expected format.");
        int width = 0, height = 0, depth = 0, maxValue = 0;
        foreach (var line in header[..end].Split('\n'))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;
            switch (parts[0])
            {
                case "WIDTH": width = int.Parse(parts[1]); break;
                case "HEIGHT": height = int.Parse(parts[1]); break;
                case "DEPTH": depth = int.Parse(parts[1]); break;
                case "MAXVAL": maxValue = int.Parse(parts[1]); break;
            }
        }
        if (width <= 0 || height <= 0 || depth is not (3 or 4) || maxValue is not (255 or 65535)) throw new InvalidDataException("The decoded frame was not in the expected format.");
        if (!Model.DocumentLimits.FitsSurface(width, height))
            throw new InvalidDataException($"The image is larger than the supported {Model.DocumentLimits.MaxSide:N0} pixels a side and {Model.DocumentLimits.MaxSurfaceMegapixels} megapixels.");
        var bytesPerSample = maxValue == 65535 ? 2 : 1;
        var offset = end + "ENDHDR\n".Length;
        if (data.Length - offset < (long)width * height * depth * bytesPerSample) throw new InvalidDataException("The decoded frame was cut short.");
        var samples = new ushort[(long)width * height * 3];
        var read = offset;
        for (long pixel = 0, i = 0; pixel < (long)width * height; pixel++)
        {
            for (var channel = 0; channel < depth; channel++)
            {
                var value = bytesPerSample == 2 ? (ushort)(data[read] << 8 | data[read + 1]) : (ushort)(data[read] * 257);
                read += bytesPerSample;
                if (channel < 3) samples[i++] = value;
            }
        }
        return new RawImage(width, height, samples);
    }

    /// <summary>
    /// The frame as 8-bit premultiplied pixels with the settings applied: exposure scales linear light, temperature and
    /// tint weigh the channels against each other. <paramref name="maxSide"/> above zero reduces the result to fit,
    /// sampling the frame rather than filtering it, for a preview that keeps up with a slider.
    /// </summary>
    public unsafe SKBitmap Develop(RawDevelopSettings settings, int maxSide = 0)
    {
        var step = maxSide > 0 ? Math.Max(1, (int)Math.Ceiling(Math.Max(Width, Height) / (double)maxSide)) : 1;
        int width = (Width + step - 1) / step, height = (Height + step - 1) / step;
        var tables = Tables(settings);
        var result = DeepPixels.New(width, height);
        var samples = Samples;
        Parallel.For(0, height, y =>
        {
            var source = (long)y * step * Width * 3;
            for (var x = 0; x < width; x++, source += step * 3)
            {
                float r = tables[0][samples[source]] / 65535f, g = tables[1][samples[source + 1]] / 65535f, b = tables[2][samples[source + 2]] / 65535f;
                var recovery = (float)Math.Clamp(settings.HighlightRecovery / 100, 0, 1);
                if (recovery > 0)
                {
                    var sum = 0f; var count = 0;
                    if (samples[source] < 65000) { sum += r; count++; }
                    if (samples[source + 1] < 65000) { sum += g; count++; }
                    if (samples[source + 2] < 65000) { sum += b; count++; }
                    if (count > 0)
                    {
                        var average = sum / count;
                        if (samples[source] >= 65000) r += (average - r) * recovery;
                        if (samples[source + 1] >= 65000) g += (average - g) * recovery;
                        if (samples[source + 2] >= 65000) b += (average - b) * recovery;
                    }
                }
                DeepPixels.Write(result, x, y, new SKColorF(r, g, b, 1));
            }
        });
        if (settings.LuminanceNoise > 0 || settings.ColorNoise > 0) ReduceNoise(result, settings);
        var preview = DeepPixels.Preview(result);
        if (settings.LensDistortion == 0) return preview;
        try { return Composa.Filters.ImageFilters.Run(preview, new Composa.Filters.FilterSettings { Kind = Composa.Filters.FilterKind.LensCorrection, Distortion = settings.LensDistortion }).Result; }
        finally { preview.Dispose(); }
    }

    private static void ReduceNoise(SKBitmap bitmap, RawDevelopSettings settings)
    {
        using var original = Pixels.Clone(bitmap);
        var lumaAmount = (float)Math.Clamp(settings.LuminanceNoise / 100, 0, 1);
        var colorAmount = (float)Math.Clamp(settings.ColorNoise / 100, 0, 1);
        static float Luma(SKColorF p) => .2126f * p.Red + .7152f * p.Green + .0722f * p.Blue;
        Parallel.For(0, bitmap.Height, y =>
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var p = DeepPixels.Read(original, x, y); var l = Luma(p);
                float red = 0, green = 0, blue = 0, weight = 0;
                for (var dy = -2; dy <= 2; dy++) for (var dx = -2; dx <= 2; dx++)
                {
                    var q = DeepPixels.Read(original, Math.Clamp(x + dx, 0, bitmap.Width - 1), Math.Clamp(y + dy, 0, bitmap.Height - 1));
                    var delta = Luma(q) - l;
                    var w = MathF.Exp(-(dx * dx + dy * dy) / 4f - delta * delta / .0036f);
                    red += q.Red * w; green += q.Green * w; blue += q.Blue * w; weight += w;
                }
                var average = new SKColorF(red / weight, green / weight, blue / weight, 1); var al = Luma(average);
                var nl = l + (al - l) * lumaAmount;
                DeepPixels.Write(bitmap, x, y, new SKColorF(Math.Clamp(nl + (p.Red - l) * (1 - colorAmount) + (average.Red - al) * colorAmount, 0, 1),
                    Math.Clamp(nl + (p.Green - l) * (1 - colorAmount) + (average.Green - al) * colorAmount, 0, 1),
                    Math.Clamp(nl + (p.Blue - l) * (1 - colorAmount) + (average.Blue - al) * colorAmount, 0, 1), 1));
            }
        });
    }

    /// <summary>One 16-bit to 8-bit table per channel: decode sRGB, scale in linear light, encode again.</summary>
    private static ushort[][] Tables(RawDevelopSettings settings)
    {
        var exposure = Math.Pow(2, Math.Clamp(settings.Exposure, -3, 3));
        var warmth = Math.Clamp(settings.Temperature, -100, 100) / 100;
        var tint = Math.Clamp(settings.Tint, -100, 100) / 100;
        // Warmer means more red and less blue; a magenta tint takes green away, a green one adds it.
        double[] gains = [exposure * (1 + 0.35 * warmth), exposure * (1 - 0.25 * tint), exposure * (1 - 0.35 * warmth)];
        var tables = new ushort[3][];
        for (var channel = 0; channel < 3; channel++)
        {
            var table = tables[channel] = new ushort[65536];
            var gain = gains[channel];
            for (var i = 0; i < 65536; i++)
            {
                var encoded = i / 65535.0;
                var linear = encoded <= 0.04045 ? encoded / 12.92 : Math.Pow((encoded + 0.055) / 1.055, 2.4);
                linear = Math.Min(1, linear * gain);
                var output = linear <= 0.0031308 ? linear * 12.92 : 1.055 * Math.Pow(linear, 1 / 2.4) - 0.055;
                table[i] = (ushort)Math.Clamp(Math.Round(output * 65535), 0, 65535);
            }
        }
        return tables;
    }
}

/// <summary>
/// Camera RAW files, decoded by ImageMagick's LibRaw delegate when ImageMagick is available. The decode keeps 16 bits
/// per channel so that the develop step has range to work with; the macOS app uses Apple's RAW pipeline, which has no
/// equivalent on other platforms, so exposure and white balance are applied here to the decoded frame rather than to the sensor data.
/// </summary>
public static class RawImporter
{
    /// <summary>The extensions LibRaw reads: Canon, Nikon, Sony, Fujifilm, Olympus, Panasonic, Pentax, Leica, Hasselblad, Phase One, Samsung and DNG.</summary>
    public static readonly string[] Extensions =
    [
        ".dng", ".cr2", ".cr3", ".crw", ".nef", ".nrw", ".arw", ".srf", ".sr2", ".raf", ".orf", ".rw2", ".pef", ".dcr", ".kdc",
        ".raw", ".rwl", ".3fr", ".fff", ".erf", ".mef", ".mos", ".mrw", ".x3f", ".iiq", ".srw"
    ];

    public static bool IsRaw(string path) => Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>Decodes the frame at the camera's own white balance and exposure. Seconds of work: call it off the UI thread.</summary>
    public static RawImage Decode(string path)
    {
        var decoded = ImageMagick.Convert(path, ImageMagickOutput.Pam16)
            ?? throw new InvalidDataException($"{Path.GetFileName(path)} is a camera RAW file. Opening one needs ImageMagick (with its LibRaw delegate), which is not installed.");
        return RawImage.ParsePam(decoded);
    }
}
