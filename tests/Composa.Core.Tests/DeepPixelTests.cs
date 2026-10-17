using Composa.Editing;
using Composa.IO;
using Composa.Rendering;
using SkiaSharp;

namespace Composa.Core.Tests;

public class DeepPixelTests
{
    [Fact]
    public void Png_sixteen_bit_gray_rgb_alpha_transparency_and_adam7_keep_native_samples()
    {
        foreach (var name in new[] { "gray.png", "rgb-adam7.png", "gray-alpha-adam7.png", "rgba.png", "rgb-transparent.png" })
        {
            using var preview = ImageFiles.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Png16", name));
            var wide = Assert.IsType<SKBitmap>(DeepPixels.Source(preview));
            for (var y = 0; y < 11; y++) for (var x = 0; x < 13; x++)
            {
                var gray = name.StartsWith("gray", StringComparison.Ordinal);
                var alpha = name.Contains("alpha", StringComparison.Ordinal) || name == "rgba.png" ? ((x + y) % 3 == 0 ? 65535 : 32768) : 65535;
                if (name == "rgb-transparent.png" && x == 0 && y == 0) alpha = 0;
                var red = 20000 + x * 301 + y * 19;
                var green = gray ? red : 40000 + x * 31 + y * 173;
                var blue = gray ? red : 10000 + x * 113 + y * 7;
                float Premultiply(int value) => (uint)(((long)value * alpha + 32767) / 65535) / 65535f;
                Assert.Equal(new SKColorF(Premultiply(red), Premultiply(green), Premultiply(blue), alpha / 65535f), DeepPixels.Read(wide, x, y));
            }
        }
    }

    [Fact]
    public void Native_wide_surfaces_render_and_crc_damage_is_rejected()
    {
        using var source = Pixels.NewColor(3, 2); source.Erase(SKColors.CornflowerBlue);
        using var wide = DeepPixels.Convert(source);
        Assert.Equal(SKColors.CornflowerBlue.Red / 255f, DeepPixels.Read(wide, 1, 1).Red);
        var png = Png16.Encode(wide);
        png[30] ^= 1; // Critical IHDR chunk's CRC.
        Assert.Throws<InvalidDataException>(() => ImageFiles.Load(new MemoryStream(png)));
    }

    private static SKBitmap FineGradient()
    {
        var source = DeepPixels.New(128, 2);
        for (var y = 0; y < source.Height; y++) for (var x = 0; x < source.Width; x++)
        {
            var value = (30000 + x) / 65535f;
            DeepPixels.Write(source, x, y, new SKColorF(value, value, value, 1));
        }
        return source;
    }

    [Fact]
    public void Copying_and_painting_a_preview_keep_untouched_source_samples_exactly()
    {
        using var source = FineGradient();
        using var preview = DeepPixels.Preview(source);
        using var painted = Pixels.Clone(preview);
        painted.SetPixel(10, 1, SKColors.Red); Pixels.Invalidate(painted);
        var result = DeepPixels.Source(painted)!;
        Assert.Equal(DeepPixels.Read(source, 11, 1), DeepPixels.Read(result, 11, 1));
        Assert.Equal(new SKColorF(1, 0, 0, 1), DeepPixels.Read(result, 10, 1));
        Assert.Equal((30010 / 65535f), DeepPixels.Read(source, 10, 1).Red);
        Assert.Same(source, DeepPixels.Source(preview));
    }

    [Fact]
    public void Sixteen_bit_png_round_trip_keeps_steps_smaller_than_one_byte()
    {
        using var source = FineGradient();
        using var preview = DeepPixels.Preview(source);
        var encoded = ImageFiles.Encode(preview, ExportFormat.Png16, resolution: 300);
        Assert.Equal(16, encoded[24]);
        using var decoded = ImageFiles.Load(new MemoryStream(encoded));
        var wide = Assert.IsType<SKBitmap>(DeepPixels.Source(decoded));
        for (var x = 0; x < source.Width; x++) Assert.Equal(DeepPixels.Read(source, x, 1), DeepPixels.Read(wide, x, 1));
        Assert.NotEqual(DeepPixels.Read(wide, 50, 1).Red, DeepPixels.Read(wide, 51, 1).Red);
    }

    [Fact]
    public void Project_round_trip_keeps_the_source_precision_separate_from_its_preview()
    {
        using var source = FineGradient();
        var session = EditorSession.NewCanvas(128, 2);
        session.AddImageLayer("Fine gradient", DeepPixels.Preview(source));
        using var saved = new MemoryStream();
        ProjectFile.Write(session.Document, saved); saved.Position = 0;
        var loaded = ProjectFile.Read(saved);
        var wide = Assert.IsType<SKBitmap>(DeepPixels.Source(loaded.ActiveLayer!.Pixels!));
        Assert.Equal(source.Info.ColorType, wide.ColorType);
        for (var x = 0; x < source.Width; x++) Assert.Equal(DeepPixels.Read(source, x, 1), DeepPixels.Read(wide, x, 1));
    }
}
