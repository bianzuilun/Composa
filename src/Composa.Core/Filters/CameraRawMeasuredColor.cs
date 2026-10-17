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
        var colorStages = CameraRawTables.ColorStages(settings);
        if (stages.Count == 0 && !settings.Curve.Adjusts && colorStages.Count == 0) return;
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
        ApplyLookup(result, table);
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
