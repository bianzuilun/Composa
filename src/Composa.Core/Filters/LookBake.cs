using Composa.Model;
using Composa.Rendering;
using SkiaSharp;

namespace Composa.Filters;

/// <summary>
/// Export Look: the document's adjustment layers baked into one lookup table. An identity lattice is laid out as a
/// bitmap, run through each adjustment layer's operation and opacity from the bottom up exactly as the renderer runs
/// the picture through them, and read back as a lattice. Only what is a pure function of a pixel's color can be
/// baked: a layer with a mask, a clipped layer, a layer inside a group (it changes the group, not the picture), and
/// an adjustment that reads neighbouring pixels or its position are left out, and <see cref="Survey"/> says which.
/// Opacity mixes the result back as the renderer does; the renderer applies no blend mode to an adjustment layer, so
/// neither does the bake. A Camera Raw grade bakes the same way through its own pipeline, without the groups that
/// read neighbours or the position (<see cref="SpatialGroups"/>).
/// </summary>
public static class LookBake
{
    /// <summary>The sizes offered: 17 points for a rough look, 33 as grading software exchanges, 65 for a fine one.</summary>
    public static readonly int[] Sizes = [17, 33, 65];

    /// <summary>A layer that could not be baked, and why, in words that follow its name.</summary>
    public readonly record struct LeftOut(Layer Layer, string Why);

    /// <summary>The visible top-level adjustment layers, bottom to top, split into those baked and those left out.</summary>
    public static (List<Layer> Baked, List<LeftOut> LeftOut) Survey(Document document)
    {
        var baked = new List<Layer>();
        var leftOut = new List<LeftOut>();
        foreach (var layer in document.AllLayers())
        {
            if (!layer.IsAdjustment || layer.Adjustment is not { } adjustment || !layer.Visible || layer.Opacity <= 0 || adjustment.IsIdentity) continue;
            if (!document.IsEffectivelyVisible(layer)) continue;
            var why = layer.Mask != null && layer.MaskEnabled ? "has a layer mask"
                : layer.Clipped ? "is clipped to the layer below"
                : document.ParentOf(layer.Id) != null ? "is inside a group"
                : adjustment.SamplingMargin > 0 ? "reads the pixels around each pixel"
                : adjustment.DependsOnPosition ? "changes from place to place"
                : null;
            if (why == null) baked.Add(layer); else leftOut.Add(new LeftOut(layer, why));
        }
        return (baked, leftOut);
    }

    /// <summary>Bakes the layers <see cref="Survey"/> found bakeable into a lattice of <paramref name="size"/> points per axis.</summary>
    public static ColorLattice Bake(Document document, int size, string title = "") => Bake(Survey(document).Baked, size, title);

    /// <summary>Bakes <paramref name="layers"/>, bottom to top, each applied at its opacity.</summary>
    public static ColorLattice Bake(IEnumerable<Layer> layers, int size, string title = "") => Through(size, grid =>
    {
        foreach (var layer in layers)
        {
            if (layer.Adjustment is not { } adjustment || adjustment.IsIdentity || layer.Opacity <= 0) continue;
            using var adjusted = Pixels.Clone(grid);
            adjustment.Apply(adjusted);
            Mix(grid, adjusted, layer.Opacity);
        }
        return grid;
    }, title);

    /// <summary>The Camera Raw groups a table cannot hold: they read neighbouring pixels or the position.</summary>
    public static readonly CameraRawGroup[] SpatialGroups = [CameraRawGroup.Effects, CameraRawGroup.Detail, CameraRawGroup.Optics];

    /// <summary>The Camera Raw groups of <paramref name="grade"/> that would be left out of a bake because they adjust something and are spatial.</summary>
    public static List<CameraRawGroup> LeftOutOf(CameraRawSettings grade)
    {
        var groups = SpatialGroups.Where(grade.Adjusts).ToList();
        if (grade.Contrast != 0 || grade.Highlights != 0 || grade.Shadows != 0) groups.Insert(0, CameraRawGroup.Light);
        return groups;
    }

    /// <summary>Names the particular Light controls omitted, instead of implying the whole Light group was removed.</summary>
    public static IEnumerable<string> LeftOutNames(CameraRawSettings grade)
    {
        if (grade.Contrast != 0) yield return "Contrast";
        if (grade.Highlights != 0) yield return "Highlights";
        if (grade.Shadows != 0) yield return "Shadows";
        foreach (var group in SpatialGroups.Where(grade.Adjusts)) yield return group.ToString();
    }

    /// <summary>The grade with <see cref="SpatialGroups"/> taken out: what a table can hold of it.</summary>
    public static CameraRawSettings ColorOnly(CameraRawSettings grade) =>
        SpatialGroups.Aggregate(grade, (settings, group) => settings.Without(group)) with { Contrast = 0, Highlights = 0, Shadows = 0 };

    /// <summary>Bakes a Camera Raw grade's color stages, <see cref="ColorOnly"/>, through the pipeline itself.</summary>
    public static ColorLattice Bake(CameraRawSettings grade, int size, string title = "") =>
        Through(size, grid => CameraRawPixels.Apply(grid, ColorOnly(grade)), title);

    /// <summary>
    /// Lays the identity lattice out as a bitmap, every color one opaque pixel (red along a row within a green column,
    /// blue down the rows), hands it to <paramref name="apply"/>, which returns the bitmap to read back (the same one
    /// changed in place, or a new one), and reads the result into a lattice.
    /// </summary>
    public static unsafe ColorLattice Through(int size, Func<SKBitmap, SKBitmap> apply, string title = "")
    {
        if (size < 2 || size > DocumentLimits.MaxLookupSize) throw new ArgumentOutOfRangeException(nameof(size));
        var last = size - 1;
        using var grid = Pixels.NewColor(size * size, size);
        var pixels = (byte*)grid.GetPixels();
        for (var b = 0; b < size; b++)
            for (var g = 0; g < size; g++)
                for (var r = 0; r < size; r++)
                {
                    var p = pixels + (long)b * grid.RowBytes + (g * size + r) * 4;
                    p[0] = (byte)Math.Round(r * 255.0 / last);
                    p[1] = (byte)Math.Round(g * 255.0 / last);
                    p[2] = (byte)Math.Round(b * 255.0 / last);
                    p[3] = 255;
                }
        var result = apply(grid);
        try
        {
            if (result.Width != grid.Width || result.Height != grid.Height || result.ColorType != SKColorType.Rgba8888) throw new InvalidOperationException("The bake changed the grid's shape.");
            var read = (byte*)result.GetPixels();
            var data = new float[size * size * size * 3];
            var k = 0;
            for (var b = 0; b < size; b++)
                for (var g = 0; g < size; g++)
                    for (var r = 0; r < size; r++)
                    {
                        var p = read + (long)b * result.RowBytes + (g * size + r) * 4;
                        data[k++] = p[0] / 255f;
                        data[k++] = p[1] / 255f;
                        data[k++] = p[2] / 255f;
                    }
            return ColorLattice.FromCube(size, data, title);
        }
        finally { if (!ReferenceEquals(result, grid)) result.Dispose(); }
    }

    /// <summary>Mixes <paramref name="above"/> into <paramref name="grid"/> by <paramref name="opacity"/>, as the renderer mixes an adjustment layer back.</summary>
    private static unsafe void Mix(SKBitmap grid, SKBitmap above, double opacity)
    {
        opacity = Math.Clamp(opacity, 0, 1);
        var below = (byte*)grid.GetPixels();
        var changed = (byte*)above.GetPixels();
        for (var y = 0; y < grid.Height; y++)
        {
            var d = below + (long)y * grid.RowBytes;
            var s = changed + (long)y * above.RowBytes;
            for (var i = 0; i < grid.Width * 4; i++)
                d[i] = opacity >= 1 ? s[i] : (byte)Math.Round(d[i] + (s[i] - d[i]) * opacity);
        }
    }
}
