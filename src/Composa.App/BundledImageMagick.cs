using Magick = ImageMagick;

namespace Composa.App;

/// <summary>
/// ImageMagick as a library, through Magick.NET, for the Windows and macOS builds: neither platform
/// has a package manager that installs ImageMagick, and "install it separately" is the worst possible
/// answer there. Linux builds leave this out and use the distribution's own ImageMagick instead.
/// </summary>
public sealed class BundledImageMagick : IO.IImageMagick, IO.IColorConverter
{
    private BundledImageMagick() { }

    /// <summary>
    /// Loads the native library and returns the backend, or null when it cannot be loaded here. It is
    /// asked for its version rather than assumed to work, because a missing or mismatched native
    /// library only shows itself on the first call into it.
    /// </summary>
    public static BundledImageMagick? TryLoad()
    {
        try
        {
            _ = Magick.MagickNET.Version;
            return new BundledImageMagick();
        }
        catch (Exception error) when (error is DllNotFoundException or TypeInitializationException or BadImageFormatException or EntryPointNotFoundException)
        {
            Console.Error.WriteLine($"The bundled ImageMagick could not be loaded: {error.Message}");
            return null;
        }
    }

    public byte[] Convert(string path, IO.ImageMagickOutput output) => Read(path, new Magick.MagickReadSettings { FrameIndex = 0, FrameCount = 1 }, image =>
    {
        image.AutoOrient();
        if (output == IO.ImageMagickOutput.Exr)
        {
            image.Depth = 32;
            image.Settings.Compression = Magick.CompressionMethod.NoCompression;
            return image.ToByteArray(Magick.MagickFormat.Exr);
        }
        if (image.ColorSpace == Magick.ColorSpace.CMYK)
        {
            var profile = image.GetColorProfile() ?? Magick.ColorProfiles.CoatedFOGRA39;
            image.TransformColorSpace(profile, Magick.ColorProfiles.SRGB);
        }
        if (output == IO.ImageMagickOutput.Pam16) image.Depth = 16;
        return image.ToByteArray(output == IO.ImageMagickOutput.Png ? Magick.MagickFormat.Png : Magick.MagickFormat.Pam);
    });

    /// <summary>The resolution decides how large an SVG is drawn, 96 dots per inch being the size it declares.</summary>
    public byte[] Rasterize(string path, double scale) => Read(path,
        new Magick.MagickReadSettings { FrameIndex = 0, FrameCount = 1, Density = new Magick.Density(96 * scale), BackgroundColor = Magick.MagickColors.Transparent },
        image => image.ToByteArray(Magick.MagickFormat.Png));

    public byte[] ConvertColor(byte[] png, byte[] rgbProfile, byte[] cmykProfile, bool proof, IO.ExportFormat format, int quality, double resolution)
    {
        using var image = new Magick.MagickImage(png);
        var rgb = new Magick.ColorProfile(rgbProfile); var cmyk = new Magick.ColorProfile(cmykProfile);
        if (cmyk.ColorSpace != Magick.ColorSpace.CMYK) throw new InvalidDataException("Choose a CMYK ICC profile.");
        image.RenderingIntent = Magick.RenderingIntent.Relative;
        image.BlackPointCompensation = true;
        image.TransformColorSpace(rgb, cmyk);
        if (proof) image.TransformColorSpace(rgb);
        else image.Alpha(Magick.AlphaOption.Off);
        image.Depth = 8; image.Quality = (uint)Math.Clamp(quality, 1, 100); image.Density = new Magick.Density(resolution);
        if (!proof && format == IO.ExportFormat.Pdf) { using var pixels = image.GetPixels(); return pixels.ToByteArray("CMYK") ?? throw new InvalidDataException("CMYK samples could not be read."); }
        return image.ToByteArray(proof ? Magick.MagickFormat.Png : format == IO.ExportFormat.Jpeg ? Magick.MagickFormat.Jpeg : Magick.MagickFormat.Tiff);
    }

    public byte[] DecodeCmyk(byte[] samples, int width, int height, int depth, byte[]? profile)
    {
        using var image = new Magick.MagickImage();
        image.ReadPixels(samples, new Magick.PixelReadSettings((uint)width, (uint)height, depth == 16 ? Magick.StorageType.Short : Magick.StorageType.Char, "CMYK"));
        image.RenderingIntent = Magick.RenderingIntent.Relative; image.BlackPointCompensation = true;
        var cmyk = profile == null ? Magick.ColorProfiles.CoatedFOGRA39 : new Magick.ColorProfile(profile);
        image.TransformColorSpace(cmyk, Magick.ColorProfiles.SRGB);
        image.Depth = (uint)depth;
        return image.ToByteArray(Magick.MagickFormat.Png);
    }

    private static byte[] Read(string path, Magick.MagickReadSettings settings, Func<Magick.MagickImage, byte[]> produce)
    {
        try
        {
            // Only the first frame, exactly as the command line's "file[0]" does: a multi-page TIFF
            // would otherwise decode every page to keep one.
            using var image = new Magick.MagickImage(path, settings);
            return produce(image);
        }
        catch (Magick.MagickException error)
        {
            throw new InvalidDataException($"{Path.GetFileName(path)} could not be decoded. {error.Message}".Trim(), error);
        }
    }
}
