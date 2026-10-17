using Composa.IO;
using Composa.Rendering;
using SkiaSharp;
using Magick = ImageMagick;

namespace Composa.App.Tests;

public class NativeColorWorkflowTests
{
    [Fact]
    public void Cmyk_exports_are_four_channel_files_and_pdf_embeds_its_print_profile()
    {
        var backend = BundledImageMagick.TryLoad()!; Assert.NotNull(backend);
        var profile = Magick.ColorProfiles.CoatedFOGRA39.ToByteArray();
        var proof = new CmykProof("FOGRA39", Magick.ColorProfiles.SRGB.ToByteArray(), profile, backend);
        using var picture = Pixels.NewColor(24, 16); picture.Erase(new SKColor(230, 20, 50));
        using (var canvas = new SKCanvas(picture))
        using (var clear = new SKPaint { Color = SKColors.Transparent, BlendMode = SKBlendMode.Src }) canvas.DrawRect(0, 0, 8, 8, clear);
        foreach (var format in new[] { ExportFormat.Jpeg, ExportFormat.Tiff })
        {
            var bytes = proof.Export(picture, format, 95, 300);
            using var decoded = new Magick.MagickImage(bytes);
            Assert.Equal(Magick.ColorSpace.CMYK, decoded.ColorSpace); Assert.NotNull(decoded.GetColorProfile());
            Assert.Equal((24u, 16u), (decoded.Width, decoded.Height));
            decoded.TransformColorSpace(Magick.ColorProfiles.SRGB);
            using var rgb = ImageFiles.Load(new MemoryStream(decoded.ToByteArray(Magick.MagickFormat.Png)));
            var paper = rgb.GetPixel(2, 2); Assert.True(paper.Red > 220 && paper.Green > 220 && paper.Blue > 220);
        }
        var pdf = proof.Export(picture, ExportFormat.Pdf, resolution: 300); var text = System.Text.Encoding.Latin1.GetString(pdf);
        Assert.StartsWith("%PDF-1.4", text); Assert.Contains("/ICCBased 5 0 R", text); Assert.Contains("/N 4", text);
        Assert.True(pdf.AsSpan().IndexOf(profile) >= 0);
        var unchanged = picture.GetPixel(3, 3);
        using var shown = Pixels.Clone(picture); proof.Apply(shown, new SKRectI(0, 0, 24, 16), true, true);
        Assert.Equal(unchanged, picture.GetPixel(3, 3));
    }

    [Fact]
    public void Sixteen_bit_tiff_is_decoded_without_reducing_its_sample_steps()
    {
        var backend = BundledImageMagick.TryLoad()!; Assert.NotNull(backend);
        var wide = DeepPixels.New(32, 2);
        for (var y = 0; y < 2; y++) for (var x = 0; x < 32; x++) DeepPixels.Write(wide, x, y, new SKColorF((30000 + x) / 65535f, .5f, .25f, 1));
        using var preview = DeepPixels.Preview(wide);
        var path = Path.Combine(Path.GetTempPath(), "composa-wide-" + Guid.NewGuid().ToString("N") + ".tiff");
        try
        {
            ImageFiles.Save(preview, path, ExportFormat.Tiff16, resolution: 300);
            using var decoded = ImageFiles.Load(new MemoryStream(backend.Convert(path, ImageMagickOutput.Png)));
            var result = Assert.IsType<SKBitmap>(DeepPixels.Source(decoded));
            for (var x = 0; x < 32; x++) Assert.InRange(Math.Abs(DeepPixels.Read(result, x, 1).Red - (30000 + x) / 65535f), 0, 2 / 65535f);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void The_bundled_hdri_decoder_preserves_exr_values_above_white()
    {
        var backend = BundledImageMagick.TryLoad()!; Assert.NotNull(backend);
        var wide = DeepPixels.New(4, 2, hdr: true); wide.Erase(SKColors.Black);
        for (var y = 0; y < 2; y++) for (var x = 0; x < 4; x++) DeepPixels.Write(wide, x, y, new SKColorF(3, .5f, .25f, 1));
        using var preview = DeepPixels.Preview(wide);
        var path = Path.Combine(Path.GetTempPath(), "composa-hdr-" + Guid.NewGuid().ToString("N") + ".exr");
        try
        {
            ImageFiles.Save(preview, path, ExportFormat.Exr);
            using var decoded = ImageFiles.Load(new MemoryStream(backend.Convert(path, ImageMagickOutput.Exr)));
            var source = Assert.IsType<SKBitmap>(DeepPixels.Source(decoded));
            Assert.InRange(DeepPixels.Read(source, 1, 1).Red, 2.99f, 3.01f);
        }
        finally { File.Delete(path); }
    }
}
