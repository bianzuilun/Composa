using SkiaSharp;

namespace Composa.Rendering;

/// <summary>Continuous corners, matching Compositor 1.5.3's cubic–arc–cubic construction.</summary>
public static class ShapePaths
{
    // Adapted from Compositor Document/ShapeTool.swift at 554104cf (MIT, Wonder Assembly LLC).
    public static SKPath Rectangle(SKRect rect, double requestedRadius, bool smooth)
    {
        var radius = Math.Clamp(double.IsFinite(requestedRadius) ? requestedRadius : 0, 0, Math.Min(rect.Width, rect.Height) / 2);
        var path = new SKPath();
        if (radius <= 0) { path.AddRect(rect); return path; }
        var smoothing = smooth ? Math.Min(0.6, Math.Max(0, Math.Min(rect.Width, rect.Height) / (2 * radius) - 1)) : 0;
        if (smoothing <= 0) { path.AddRoundRect(rect, (float)radius, (float)radius); return path; }
        var reach = (1 + smoothing) * radius;
        var angle = smoothing * Math.PI / 4;
        var tangent = radius * Math.Tan(angle / 2);
        var c = tangent * Math.Cos(angle);
        var d = tangent * Math.Sin(angle);
        var arcLength = radius * Math.Sqrt(2) * Math.Sin((1 - smoothing) * Math.PI / 4);
        var b = (reach - arcLength - c - d) / 3;
        var a = 2 * b;
        var corners = new[] { new SKPoint(rect.Right, rect.Top), new SKPoint(rect.Right, rect.Bottom), new SKPoint(rect.Left, rect.Bottom), new SKPoint(rect.Left, rect.Top) };
        for (var i = 0; i < 4; i++)
        {
            var cos = Math.Cos(i * Math.PI / 2); var sin = Math.Sin(i * Math.PI / 2);
            SKPoint P(double x, double y) => new((float)(corners[i].X + x * cos - y * sin), (float)(corners[i].Y + x * sin + y * cos));
            var start = P(-reach, 0);
            if (i == 0) path.MoveTo(start); else path.LineTo(start);
            path.CubicTo(P(-reach + a, 0), P(-reach + a + b, 0), P(-reach + a + b + c, d));
            var arcEnd = P(-d, reach - a - b - c);
            path.ArcTo((float)radius, (float)radius, i * 90, SKPathArcSize.Small, SKPathDirection.Clockwise, arcEnd.X, arcEnd.Y);
            path.CubicTo(P(0, reach - a - b), P(0, reach - a), P(0, reach));
        }
        path.Close();
        return path;
    }
}
