using Avalonia;
using Composa.Rendering;
using SkiaSharp;

namespace Composa.App.Controls;

public sealed partial class CanvasView
{
    private SKBitmap? navigatorPixels;
    private bool navigatorStale = true;
    private SKRect navigatorRect;
    private bool navigatorDragging;

    private SKImage? CaptureNavigator(out Action<SKCanvas>? draw)
    {
        draw = null;
        if (session == null || !session.View.ShowNavigator || zoom < 3 || Bounds.Width < 200 || Bounds.Height < 160)
        { navigatorRect = SKRect.Empty; return null; }
        var document = session.Document;
        var scale = Math.Min(150.0 / document.Width, 110.0 / document.Height);
        var width = Math.Max(1, (int)Math.Ceiling(document.Width * scale));
        var height = Math.Max(1, (int)Math.Ceiling(document.Height * scale));
        if (navigatorStale || navigatorPixels == null || navigatorPixels.Width != width || navigatorPixels.Height != height)
        {
            navigatorPixels?.Dispose(); navigatorPixels = Pixels.NewColor(width, height);
            session.RenderView(navigatorPixels, new SKRectI(0, 0, width, height), new RenderView((float)scale, SKPoint.Empty));
            navigatorStale = false;
        }
        navigatorRect = new SKRect((float)Bounds.Width - width - 16, (float)Bounds.Height - height - 16, (float)Bounds.Width - 16, (float)Bounds.Height - 16);
        var rect = navigatorRect;
        var viewport = new SKRect((float)(-origin.X / UnitsPerPixel * scale), (float)(-origin.Y / UnitsPerPixel * scale),
            (float)((Bounds.Width - origin.X) / UnitsPerPixel * scale), (float)((Bounds.Height - origin.Y) / UnitsPerPixel * scale));
        viewport.Offset(rect.Left, rect.Top); viewport.Intersect(rect);
        var image = SKImage.FromBitmap(navigatorPixels);
        draw = drawing =>
        {
            using var background = new SKPaint { Color = new SKColor(25, 25, 25, 235) };
            drawing.DrawRect(new SKRect(rect.Left - 5, rect.Top - 5, rect.Right + 5, rect.Bottom + 5), background);
            DrawCheckerboard(drawing, rect);
            drawing.DrawImage(image, rect, new SKSamplingOptions(SKFilterMode.Linear));
            using var outline = new SKPaint { Color = SKColors.DeepSkyBlue, Style = SKPaintStyle.Stroke, StrokeWidth = 2, IsAntialias = true };
            drawing.DrawRect(viewport, outline);
        };
        return image;
    }

    private bool NavigateAt(Point position, bool begin)
    {
        if (session == null || (!navigatorDragging && !navigatorRect.Contains((float)position.X, (float)position.Y))) return false;
        navigatorDragging = begin || navigatorDragging;
        var x = Math.Clamp((position.X - navigatorRect.Left) / navigatorRect.Width, 0, 1) * session.Document.Width;
        var y = Math.Clamp((position.Y - navigatorRect.Top) / navigatorRect.Height, 0, 1) * session.Document.Height;
        origin = new Point(Bounds.Width / 2 - x * UnitsPerPixel, Bounds.Height / 2 - y * UnitsPerPixel);
        ClampOrigin(); InvalidateVisual(); ViewChanged?.Invoke();
        return true;
    }
}
