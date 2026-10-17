using Composa.Editing;
using Composa.Filters;
using Composa.IO;
using Composa.IO.Psd;
using Composa.Model;
using Composa.Painting;
using Composa.Rendering;
using SkiaSharp;

namespace Composa.Core.Tests;

public class PrecisionWorkflowTests
{
    private static SKBitmap Ramp(int width = 256, int height = 4)
    {
        var wide = DeepPixels.New(width, height);
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
        { var value = (24000 + x) / 65535f; DeepPixels.Write(wide, x, y, new SKColorF(value, value, value, 1)); }
        return DeepPixels.Preview(wide);
    }

    [Fact]
    public void Native_composition_and_adjustment_layers_keep_sub_byte_steps()
    {
        using var preview = Ramp(); var document = new Document(256, 4);
        var layer = Layer.Raster("Fine gradient", preview); document.Layers.Add(layer); document.SetActive(layer.Id);
        using var flat = DocumentRenderer.Flatten(document);
        var source = DeepPixels.Source(preview)!; var pixels = Assert.IsType<SKBitmap>(DeepPixels.Source(flat));
        for (var x = 20; x < 220; x++) Assert.InRange(Math.Abs(DeepPixels.Read(source, x, 2).Red - DeepPixels.Read(pixels, x, 2).Red), 0, 2 / 65535f);
        var session = new EditorSession(document);
        session.AddAdjustmentLayer(new LevelsAdjustment().WithRange(0, new LevelsRange { InputBlack = 90, InputWhite = 98 }));
        using var graded = session.Flatten(); var gradedSource = Assert.IsType<SKBitmap>(DeepPixels.Source(graded));
        var distinct = Enumerable.Range(0, 256).Select(x => DeepPixels.Read(gradedSource, x, 2).Red).Distinct().Count();
        Assert.True(distinct > 200, $"Only {distinct} values survived a steep Levels adjustment.");
    }

    [Fact]
    public void Native_blur_and_resize_keep_detail_below_one_byte()
    {
        using var original = Ramp();
        using var resized = EditorSession.Resample(original, 512, 8);
        var resizedSource = Assert.IsType<SKBitmap>(DeepPixels.Source(resized));
        Assert.True(Enumerable.Range(20, 450).Select(x => DeepPixels.Read(resizedSource, x, 3).Red).Distinct().Count() > 150);
        using var blurred = ImageFilters.Run(original, new FilterSettings { Kind = FilterKind.GaussianBlur, Radius = 1, ClampEdges = true }).Result;
        var blurredSource = Assert.IsType<SKBitmap>(DeepPixels.Source(blurred));
        Assert.NotEqual(DeepPixels.Read(blurredSource, 100, 2).Red, DeepPixels.Read(blurredSource, 101, 2).Red);
        Assert.InRange(Math.Abs(DeepPixels.Read(DeepPixels.Source(original)!, 100, 2).Red - DeepPixels.Read(blurredSource, 100, 2).Red), 0, 4 / 65535f);
    }

    [Fact]
    public void A_sixteen_bit_gradient_is_created_at_native_precision_and_depth_conversion_undoes()
    {
        var session = EditorSession.NewCanvas(2048, 2);
        var layer = session.ActiveLayer!; session.ConvertLayerDepth(layer, true);
        session.Foreground = SKColors.Black; session.Background = SKColors.White;
        var before = session.BeginGradient(layer);
        session.DrawGradient(layer, before, SKPoint.Empty, new SKPoint(2048, 0)); session.Commit();
        var source = Assert.IsType<SKBitmap>(DeepPixels.Source(session.ActiveLayer!.Pixels!));
        Assert.True(Enumerable.Range(0, 2048).Select(x => DeepPixels.Read(source, x, 1).Red).Distinct().Count() > 1000);
        session.ConvertLayerDepth(session.ActiveLayer!, false);
        Assert.Null(DeepPixels.Source(session.ActiveLayer!.Pixels!));
        session.Undo(); Assert.Same(source, DeepPixels.Source(session.ActiveLayer!.Pixels!));
    }

    [Fact]
    public void Hdr_exr_and_project_round_trips_retain_highlights_and_coverage()
    {
        var wide = DeepPixels.New(3, 2, hdr: true);
        var colors = new[] { new SKColorF(.003f, .12f, 3.75f, 1), new SKColorF(1.5f, .25f, .1f, .5f), new SKColorF(0, 0, 0, 0) };
        for (var y = 0; y < 2; y++) for (var x = 0; x < 3; x++) DeepPixels.Write(wide, x, y, colors[x]);
        using var preview = DeepPixels.Preview(wide);
        void Check(SKBitmap bitmap)
        {
            var source = Assert.IsType<SKBitmap>(DeepPixels.Source(bitmap)); Assert.True(DeepPixels.IsHdr(source));
            for (var x = 0; x < 3; x++)
            {
                var p = DeepPixels.Read(source, x, 1);
                Assert.InRange(Math.Abs(p.Red - colors[x].Red), 0, .00002f); Assert.InRange(Math.Abs(p.Blue - colors[x].Blue), 0, .00002f);
                Assert.Equal(colors[x].Alpha, p.Alpha);
            }
        }
        using var exr = new MemoryStream(ExrImage.Encode(preview)); using var decoded = ImageFiles.Load(exr); Check(decoded);
        var document = new Document(3, 2); document.Layers.Add(Layer.Raster("HDR", preview));
        using var project = new MemoryStream(); ProjectFile.Write(document, project); project.Position = 0;
        var loaded = ProjectFile.Read(project); Check(loaded.Layers.Single().Pixels!);
        using var flattened = DocumentRenderer.Flatten(loaded); Check(flattened);
    }

    [Fact]
    public void Negative_exposure_recovers_hdr_highlights_without_clipping_the_source()
    {
        var wide = DeepPixels.New(1, 1, hdr: true); DeepPixels.Write(wide, 0, 0, new SKColorF(2, 2, 2, 1));
        using var original = DeepPixels.Preview(wide); using var adjusted = Pixels.Clone(original);
        new ExposureAdjustment { Exposure = -3 }.Apply(adjusted);
        var result = DeepPixels.Read(DeepPixels.Source(adjusted)!, 0, 0);
        Assert.InRange(result.Red, .7f, 1f); Assert.Equal(2f, DeepPixels.Read(DeepPixels.Source(original)!, 0, 0).Red);
    }

    [Fact]
    public void Radiance_flat_and_run_length_scanlines_preserve_hdr_and_orientation()
    {
        foreach (var rle in new[] { false, true })
        {
            using var stream = new MemoryStream();
            stream.Write(System.Text.Encoding.ASCII.GetBytes("#?RADIANCE\nFORMAT=32-bit_rle_rgbe\n\n+Y 2 -X 8\n"));
            for (var y = 0; y < 2; y++)
            {
                if (rle)
                {
                    stream.Write(new byte[] { 2, 2, 0, 8 });
                    foreach (var c in new byte[] { (byte)(y == 0 ? 192 : 64), 32, 16, 130 }) stream.Write(new byte[] { 136, c });
                }
                else for (var x = 0; x < 8; x++) stream.Write(new byte[] { (byte)(y == 0 ? 192 : 64), 32, 16, 130 });
            }
            stream.Position = 0; using var image = ImageFiles.Load(stream); var source = DeepPixels.Source(image)!;
            Assert.Equal((8, 2), (image.Width, image.Height));
            var bright = DeepPixels.Read(source, 4, 1).Red; Assert.True(bright > 1.5f);
            Assert.InRange(DeepPixels.Read(source, 4, 0).Red, .999f, 1.001f);
        }
    }

    [Fact]
    public void Photoshop_sixteen_bit_flat_and_tagged_layers_keep_the_samples()
    {
        foreach (var name in new[] { "flat-raw.psd", "flat-zip-prediction.psd", "layer-lr16.psd", "layer-lr16-with-length.psd" })
        {
            var import = PsdImport.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Psd16", name));
            try
            {
                var layer = Assert.Single(import.Layers); var source = Assert.IsType<SKBitmap>(DeepPixels.Source(layer.Pixels!));
                for (var x = 0; x < 13; x++) Assert.InRange(Math.Abs(DeepPixels.Read(source, x, 5).Red - (20000 + x * 301 + 5 * 19) / 65535f), 0, 2 / 65535f);
            }
            finally { import.Discard(); }
        }
    }

    [Fact]
    public void Raw_development_keeps_sixteen_bit_steps_and_does_not_modify_decoded_samples()
    {
        var samples = Enumerable.Range(0, 256).SelectMany(x => new[] { (ushort)(30000 + x), (ushort)(30000 + x), (ushort)(30000 + x) }).ToArray();
        var saved = samples.ToArray(); var raw = new RawImage(256, 1, samples);
        using var image = raw.Develop(new RawDevelopSettings()); var source = Assert.IsType<SKBitmap>(DeepPixels.Source(image));
        for (var x = 0; x < 256; x++) Assert.Equal((30000 + x) / 65535f, DeepPixels.Read(source, x, 0).Red);
        Assert.Equal(saved, raw.Samples);
    }

    [Fact]
    public void Nonuniform_resize_preserves_live_text_effects_and_its_mapping_after_editing()
    {
        var session = EditorSession.NewCanvas(160, 100);
        var text = session.AddText(new SKPoint(30, 30), new TextStyle { Text = "Text", Size = 20 });
        session.SetTransform(text, text.Transform with { Rotation = 25 });
        session.AddEffect(text, LayerEffectKind.DropShadow);
        var effects = text.Effects; var matrix = text.Matrix;
        var expected = matrix.MapPoint(10, 10); expected = new SKPoint(expected.X * 2, expected.Y * 1.5f);
        session.ResizeImage(320, 150);
        Assert.NotNull(text.Text); Assert.Same(effects, text.Effects);
        var actual = text.Matrix.MapPoint(10, 10); Assert.InRange(SKPoint.Distance(expected, actual), 0, .001f);
        session.Apply("Edit Text", () => session.SetText(text, text.Text! with { Text = "Longer text" }));
        var edited = text.Matrix.MapPoint(10, 10); Assert.InRange(SKPoint.Distance(actual, edited), 0, .001f);
        session.Undo(); Assert.Equal("Text", session.ActiveLayer!.Text!.Text);
    }

    [Fact]
    public void Content_aware_fill_keeps_texture_and_never_changes_pixels_outside_the_mask()
    {
        using var source = Pixels.NewColor(64, 64); using var mask = Pixels.NewMask(64, 64);
        for (var y = 0; y < 64; y++) for (var x = 0; x < 64; x++) source.SetPixel(x, y, (x / 2 + y / 2) % 2 == 0 ? SKColors.White : SKColors.Black);
        using (var drawing = new SKCanvas(mask)) drawing.DrawRect(26, 27, 12, 10, new SKPaint { Color = SKColors.White });
        using var filled = Inpaint.Fill(source, mask);
        for (var y = 0; y < 64; y++) for (var x = 0; x < 64; x++) if (mask.GetPixel(x, y).Alpha == 0) Assert.Equal(source.GetPixel(x, y), filled.GetPixel(x, y));
        var values = Enumerable.Range(28, 8).SelectMany(x => Enumerable.Range(29, 6).Select(y => (double)filled.GetPixel(x, y).Red)).ToArray();
        var mean = values.Average(); var variance = values.Average(v => (v - mean) * (v - mean)); Assert.True(variance > 1000, $"Texture variance is {variance}.");
    }

    [Fact]
    public async Task An_async_filter_is_adopted_as_one_undoable_edit()
    {
        var session = EditorSession.NewCanvas(256, 4); session.AddImageLayer("Fine", Ramp());
        var original = session.ActiveLayer!.Pixels!;
        Assert.True(session.BeginFilter(FilterKind.GaussianBlur));
        await session.PreviewFilterAsync(new FilterSettings { Kind = FilterKind.GaussianBlur, Radius = 1, PreserveTransparency = true });
        session.CommitPreview(); Assert.NotNull(DeepPixels.Source(session.ActiveLayer!.Pixels!));
        session.Undo(); Assert.Same(original, session.ActiveLayer!.Pixels);
    }

    [Fact]
    public void Vectorscope_sampling_is_bounded_and_transparent_pixels_do_not_count()
    {
        using var gray = Pixels.NewColor(64, 32); gray.Erase(SKColors.Gray);
        var density = Vectorscope.Of(gray);
        Assert.Equal(2048, density.Samples); Assert.Equal(2048, density.Maximum);
        Assert.Equal(1, density.Counts.ToArray().Count(count => count > 0));
        gray.Erase(SKColors.Transparent); Assert.Equal(0, Vectorscope.Of(gray).Samples);
        using var large = Pixels.NewColor(1280, 1000); large.Erase(SKColors.Red);
        Assert.InRange(Vectorscope.Of(large).Samples, 1, 131072);
    }
}
