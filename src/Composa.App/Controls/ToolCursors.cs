using Avalonia;
using Avalonia.Input;
using SkiaSharp;

namespace Composa.App.Controls;

/// <summary>Cross-platform tool cursors, cached for the lifetime of the application.</summary>
internal static class ToolCursors
{
    internal static readonly Cursor ZoomIn = Magnifier(false);
    internal static readonly Cursor ZoomOut = Magnifier(true);
    internal static readonly Cursor ClosedHand = Make(canvas =>
    {
        using var path = SKPath.ParseSvgPathData("M9 24 L6 18 Q4 13 7 13 L10 16 L10 9 Q10 6 13 8 L14 6 Q17 5 18 8 Q21 6 23 10 Q26 10 26 14 L25 22 L22 27 L12 27 Z");
        using var outer = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Stroke, StrokeWidth = 3, IsAntialias = true };
        using var fill = new SKPaint { Color = SKColors.White, IsAntialias = true };
        canvas.DrawPath(path, outer); canvas.DrawPath(path, fill);
        using var crease = new SKPaint { Color = SKColors.Black, StrokeWidth = 1, IsAntialias = true };
        canvas.DrawLine(14, 10, 14, 15, crease); canvas.DrawLine(18, 10, 18, 15, crease); canvas.DrawLine(22, 12, 22, 16, crease);
    }, new PixelPoint(16, 16));

    private static Cursor Magnifier(bool minus) => Make(canvas =>
    {
        using var outer = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Stroke, StrokeWidth = 4, StrokeCap = SKStrokeCap.Round, IsAntialias = true };
        using var inner = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Stroke, StrokeWidth = 2, StrokeCap = SKStrokeCap.Round, IsAntialias = true };
        foreach (var paint in new[] { outer, inner })
        {
            canvas.DrawCircle(12, 12, 8, paint); canvas.DrawLine(18, 18, 25, 25, paint);
            canvas.DrawLine(8, 12, 16, 12, paint);
            if (!minus) canvas.DrawLine(12, 8, 12, 16, paint);
        }
    }, new PixelPoint(12, 12));

    private static Cursor Make(Action<SKCanvas> draw, PixelPoint hotspot)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(32, 32, SKColorType.Rgba8888, SKAlphaType.Premul));
        bitmap.Erase(SKColors.Transparent); using (var canvas = new SKCanvas(bitmap)) draw(canvas);
        using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = encoded.AsStream();
        using var image = new Avalonia.Media.Imaging.Bitmap(stream);
        return new Cursor(image, hotspot);
    }
}
