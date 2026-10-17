using SkiaSharp;

namespace Composa.Filters;

public static unsafe partial class CameraRawPixels
{
    private static void ApplyMeasuredColor(SKBitmap source, SKBitmap result, CameraRawSettings settings, double scale)
    {
        var stages = CameraRawTables.LightStages(source, settings, out var beforeShadows);
        var pixels = (byte*)result.GetPixels();
        int width = result.Width, height = result.Height, stride = result.RowBytes;
        if (settings.Calibration.ShadowTint != 0)
            Calibration(pixels, width, height, stride, settings.Calibration with { RedHue = 0, RedSaturation = 0, GreenHue = 0, GreenSaturation = 0, BlueHue = 0, BlueSaturation = 0 });
        if (settings.Shadows != 0)
        {
            if (beforeShadows > 0) ApplyLookup(result, CameraRawTables.Compose(stages.Take(beforeShadows).ToArray()));
            AdaptiveShadows(result, settings.Shadows, scale);
            stages.RemoveRange(0, beforeShadows);
        }
        ApplyLookup(result, ComposeMeasuredColor(stages, settings));
    }

    private static float[] ComposeMeasuredColor(List<CameraRawTables.Stage> stages, CameraRawSettings settings)
    {
        var colorStages = CameraRawTables.ColorStages(settings);
        var table = CameraRawTables.Compose(stages);
        if (settings.Curve.Adjusts)
        {
            var curve = settings.Curve;
            var tone = curve.LumaTable(curve.HasMeasuredDividers);
            float[] red = curve.ChannelTable(curve.Red), green = curve.ChannelTable(curve.Green), blue = curve.ChannelTable(curve.Blue);
            var refine = curve.RefineSaturation / 100;
            Parallel.For(0, CameraRawTables.Grid, band =>
            {
                var start = band * CameraRawTables.Grid * CameraRawTables.Grid * 3;
                var end = start + CameraRawTables.Grid * CameraRawTables.Grid * 3;
                for (var i = start; i < end; i += 3)
                {
                    double r = table[i], g = table[i + 1], b = table[i + 2];
                    double cr = LutAt(tone, r), cg = LutAt(tone, g), cb = LutAt(tone, b);
                    if (refine < 0)
                    {
                        double br = r, bg = g, bb = b;
                        ScaleLuminance(ref br, ref bg, ref bb, LutAt(tone, Rec709(r, g, b)));
                        cr += (br - cr) * -refine; cg += (bg - cg) * -refine; cb += (bb - cb) * -refine;
                    }
                    else if (refine > 0)
                    {
                        var luma = Rec709(cr, cg, cb);
                        cr = Clamp01(luma + (cr - luma) * (1 + refine)); cg = Clamp01(luma + (cg - luma) * (1 + refine)); cb = Clamp01(luma + (cb - luma) * (1 + refine));
                    }
                    table[i] = (float)LutAt(red, cr); table[i + 1] = (float)LutAt(green, cg); table[i + 2] = (float)LutAt(blue, cb);
                }
            });
        }
        if (colorStages.Count > 0) CameraRawTables.Compose(colorStages, table);
        return table;
    }

    /// <summary>A full-resolution preview's measured color stages. Spatial stages require the CPU pipeline.</summary>
    public static ColorLattice? PreviewLattice(SKBitmap source, CameraRawSettings settings)
    {
        settings = settings.Normalized();
        if (settings.Shadows != 0 || settings.Calibration.ShadowTint != 0 || settings.Adjusts(CameraRawGroup.Effects)
            || settings.Optics.Adjusts || settings.Detail.Adjusts) return null;
        var stages = CameraRawTables.LightStages(source, settings, out _);
        var table = ComposeMeasuredColor(stages, settings);
        var points = new float[table.Length]; var at = 0; var size = CameraRawTables.Grid;
        for (var blue = 0; blue < size; blue++) for (var green = 0; green < size; green++) for (var red = 0; red < size; red++)
        {
            var from = ((red * size + green) * size + blue) * 3;
            points[at++] = table[from]; points[at++] = table[from + 1]; points[at++] = table[from + 2];
        }
        return ColorLattice.FromCube(size, points, "Camera Raw preview");
    }

    private static void ApplyLookup(SKBitmap bitmap, float[] table)
    {
        var pixels = (byte*)bitmap.GetPixels();
        ForRows(bitmap.Height, y =>
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = pixels + (long)y * bitmap.RowBytes + x * 4;
                if (pixel[3] == 0) continue;
                Read(pixel, out var r, out var g, out var b);
                var color = CameraRawTables.Lookup(table, r, g, b);
                Write(pixel, color.R, color.G, color.B);
            }
        });
    }
}
