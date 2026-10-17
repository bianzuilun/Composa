using Composa.Editing;
using Composa.Filters;
using Composa.IO;
using Composa.Model;
using Composa.Rendering;
using SkiaSharp;

namespace Composa.Core.Tests;

public class CompositorPortTests
{
    [Fact]
    public void Mask_from_selection_can_be_inverted_and_solo_is_only_a_view()
    {
        var session = EditorSession.NewCanvas(16, 12, SKColors.Red);
        session.SelectRect(new SKRect(0, 0, 8, 12));
        var layer = session.ActiveLayer!;
        session.AddMask(layer, hideAll: true);
        Assert.Equal((byte)0, layer.Mask!.GetPixel(2, 6).Alpha);
        Assert.Equal((byte)255, layer.Mask.GetPixel(12, 6).Alpha);
        var state = session.History.CurrentId;
        session.ToggleSoloMask(layer);
        using var view = Pixels.NewColor(16, 12);
        session.RenderView(view, session.Document.Bounds, RenderView.Identity);
        Assert.Equal(SKColors.Black, view.GetPixel(2, 6));
        Assert.Equal(SKColors.White, view.GetPixel(12, 6));
        using var exported = session.Flatten();
        Assert.Equal(SKColors.Red, exported.GetPixel(12, 6));
        Assert.Equal(state, session.History.CurrentId);
        session.ToggleSoloMask(layer);
        Assert.Null(session.SoloMaskLayerId);
        session.Undo();
        Assert.Null(session.ActiveLayer!.Mask);
        session.Redo();
        Assert.NotNull(session.ActiveLayer!.Mask);
    }

    [Fact]
    public void Tiff_and_pdf_have_real_container_headers_and_keep_print_resolution()
    {
        using var bitmap = Pixels.NewColor(30, 20);
        bitmap.Erase(new SKColor(200, 30, 10, 128));
        var tiff = ImageFiles.Encode(bitmap, ExportFormat.Tiff, resolution: 300);
        Assert.Equal(new byte[] { 73, 73, 42, 0 }, tiff[..4]);
        Assert.Equal(ExportFormat.Tiff, ImageFiles.FormatFor("image.tiff"));
        var pdf = System.Text.Encoding.ASCII.GetString(ImageFiles.Encode(bitmap, ExportFormat.Pdf, resolution: 300));
        Assert.StartsWith("%PDF-1.4", pdf);
        Assert.Contains("/MediaBox [0 0 7.2 4.8]", pdf);
        Assert.Contains("/SMask 5 0 R", pdf);
        Assert.Contains("/Count 1", pdf);
        Assert.EndsWith("%%EOF\n", pdf);
    }
    [Fact]
    public void Physical_canvas_units_round_trip_at_print_resolution()
    {
        Assert.Equal(300, CanvasUnits.ToPixels(1, CanvasUnit.Inches, 300));
        Assert.Equal(300, CanvasUnits.ToPixels(2.54, CanvasUnit.Centimeters, 300));
        Assert.Equal(300, CanvasUnits.ToPixels(25.4, CanvasUnit.Millimeters, 300));
        Assert.Equal(2.54, CanvasUnits.FromPixels(300, CanvasUnit.Centimeters, 300), 8);
        Assert.Equal(1920, CanvasUnits.ToPixels(1920, CanvasUnit.Pixels, 300));
        Assert.Throws<ArgumentOutOfRangeException>(() => CanvasUnits.ToPixels(double.NaN, CanvasUnit.Inches, 300));
    }

    [Fact]
    public void Smooth_corners_remain_live_and_persist_through_project_files()
    {
        var style = new ShapeStyle(ShapeKind.RoundedRectangle, 0xFFFF0000, 12) { SmoothCorners = true };
        using var smooth = EditorSession.RenderShape(style, 80, 60);
        using var circular = EditorSession.RenderShape(style with { SmoothCorners = false }, 80, 60);
        Assert.Equal((byte)0, smooth.GetPixel(0, 0).Alpha);
        Assert.Equal(SKColors.Red, smooth.GetPixel(40, 30));
        Assert.False(smooth.Bytes.SequenceEqual(circular.Bytes));
        var session = EditorSession.NewCanvas(100, 100);
        session.AddShape(style, new SKRect(5, 5, 85, 65));
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cmps");
        try
        {
            ProjectFile.Save(session.Document, path);
            var loaded = ProjectFile.Load(path);
            Assert.True(loaded.ActiveLayer!.Shape!.SmoothCorners);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Gaussian_preserve_transparency_keeps_every_original_alpha_and_size()
    {
        using var source = Pixels.NewColor(20, 20);
        using (var canvas = new SKCanvas(source))
        using (var paint = new SKPaint { Color = new SKColor(220, 30, 10, 128) }) canvas.DrawRect(3, 3, 12, 12, paint);
        var original = source.Bytes;
        var (result, x, y) = ImageFilters.Run(source, new FilterSettings { Kind = FilterKind.GaussianBlur, Radius = 3, PreserveTransparency = true });
        using (result)
        {
            Assert.Equal((20, 20, 0, 0), (result.Width, result.Height, x, y));
            for (var yy = 0; yy < 20; yy++) for (var xx = 0; xx < 20; xx++) Assert.Equal(source.GetPixel(xx, yy).Alpha, result.GetPixel(xx, yy).Alpha);
        }
        Assert.Equal(original, source.Bytes);
    }

    [Fact]
    public void Progressive_blur_is_unchanged_before_the_zero_radius_endpoint_and_is_undoable()
    {
        var session = EditorSession.NewCanvas(48, 16, SKColors.White);
        var source = Pixels.NewColor(48, 16);
        for (var y = 0; y < 16; y++) for (var x = 0; x < 48; x++) source.SetPixel(x, y, x % 2 == 0 ? SKColors.Black : SKColors.White);
        session.AddImageLayer("stripes", source);
        var original = source.Bytes;
        session.ApplyFilter(new FilterSettings { Kind = FilterKind.ProgressiveBlur, ProgressiveStart = new SKPoint(24, 0), ProgressiveEnd = new SKPoint(40, 0), ProgressiveFrom = 0, ProgressiveTo = 4 });
        Assert.Equal(source.GetPixel(4, 8), session.ActiveLayer!.Pixels!.GetPixel(4, 8));
        Assert.InRange(session.ActiveLayer.Pixels.GetPixel(42, 8).Red, (byte)60, (byte)195);
        Assert.Equal(original, source.Bytes);
        session.Undo();
        Assert.Same(source, session.ActiveLayer.Pixels);
    }

    [Fact]
    public void Scanlines_preserve_alpha_and_change_output_with_displacement_split_and_glow()
    {
        using var source = Pixels.NewColor(32, 32);
        for (var y = 0; y < 32; y++) for (var x = 0; x < 32; x++) source.SetPixel(x, y, new SKColor((byte)(x * 8), (byte)(y * 8), 180, 160));
        var original = source.Bytes;
        using var lines = ScanlinesPixels.Apply(source, new ScanlinesSettings());
        using var changed = ScanlinesPixels.Apply(source, new ScanlinesSettings { Dots = 70, Displace = 8, Smoothness = 80, Glow = 30, Split = 2, Colors = DitherColors.Original });
        Assert.False(lines.Bytes.SequenceEqual(changed.Bytes));
        for (var y = 0; y < 32; y++) for (var x = 0; x < 32; x++) Assert.Equal(source.GetPixel(x, y).Alpha, changed.GetPixel(x, y).Alpha);
        Assert.Equal(original, source.Bytes);
        Assert.Equal(4, new ScanlinesSettings { Spacing = 4 }.Normalized().Spacing);
        Assert.Equal(2, new ScanlinesSettings { Spacing = -1 }.Normalized().Spacing);
    }

    [Fact]
    public void Folder_import_is_ordered_filters_non_images_and_does_not_descend_into_subfolders()
    {
        var root = Path.Combine(Path.GetTempPath(), "composa-import-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "b.hif"), "");
            File.WriteAllText(Path.Combine(root, "a.png"), "");
            File.WriteAllText(Path.Combine(root, "readme.txt"), "");
            Directory.CreateDirectory(Path.Combine(root, "nested"));
            File.WriteAllText(Path.Combine(root, "nested/c.png"), "");
            Assert.Equal(new[] { "a.png", "b.hif" }, ImportPaths.Expand([root]).Select(Path.GetFileName));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
