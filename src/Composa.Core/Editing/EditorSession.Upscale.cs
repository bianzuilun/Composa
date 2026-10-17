using Composa.Model;
using Composa.Rendering;
using Composa.Vision;
using SkiaSharp;

namespace Composa.Editing;

/// <summary>How Image Size resamples raster layers.</summary>
public enum ResampleMode
{
    /// <summary>Smooth when shrinking, Catmull-Rom when enlarging.</summary>
    Automatic,
    /// <summary>Hard pixel edges, for pixel art.</summary>
    Nearest,
    /// <summary>A model that invents detail while enlarging. Falls back to Automatic where it cannot run or when shrinking.</summary>
    Enhance
}

public sealed partial class EditorSession
{
    /// <summary>
    /// The slow half of Image Size with Enhance: every raster layer that would be resampled is enlarged by the model
    /// off the UI thread, from its committed pixels, which are immutable. The results go to <see cref="ResizeImage"/>,
    /// which uses one only while the layer still has the pixels it was made from, so an edit in between costs the
    /// enhancement and nothing else. Progress counts tiles across all layers. Call from the UI thread.
    /// </summary>
    public async Task<EnhancedLayers> PrepareEnhancedAsync(int width, int height, IProgress<(int Done, int Total)>? progress = null, CancellationToken cancellation = default)
    {
        width = Math.Clamp(width, 1, Document.MaxSide);
        height = Math.Clamp(height, 1, Document.MaxSide);
        double sx = (double)width / document.Width, sy = (double)height / document.Height;
        var model = UpscaleModels.General;
        var jobs = new List<(Layer Layer, SKBitmap Source, int Width, int Height)>();
        foreach (var layer in document.AllLayers())
        {
            if (layer.Pixels is not { } pixels || layer.Text != null || layer.Shape != null || !layer.Transform.IsPureTranslation(pixels.Width, pixels.Height)) continue;
            int w = Math.Max(1, (int)Math.Round(pixels.Width * sx)), h = Math.Max(1, (int)Math.Round(pixels.Height * sy));
            // Only an enlargement gains anything; the model's multiple must also fit a layer.
            if ((w <= pixels.Width && h <= pixels.Height) || !DocumentLimits.FitsSurface((long)pixels.Width * model.Scale, (long)pixels.Height * model.Scale)) continue;
            jobs.Add((layer, pixels, w, h));
        }
        var results = new Dictionary<Guid, (SKBitmap Source, SKBitmap Result)>();
        if (jobs.Count == 0) return new EnhancedLayers(results);
        var totals = jobs.Sum(j => Upscaling.PlanTiles(j.Source.Width, j.Source.Height).Count);
        var finished = 0;
        try
        {
            foreach (var (layer, source, w, h) in jobs)
            {
                var before = finished;
                // Do not queue these reports through another SynchronizationContext: a late tile
                // report could arrive after the final count, making progress run backwards.
                var perLayer = new TileProgress(p => progress?.Report((before + p.Done, totals)));
                var enlarged = await Task.Run(() =>
                {
                    using var big = Upscaler.Enlarge(source, model, perLayer, cancellation);
                    // The model always makes its multiple; the rest of the way to the target is plain resampling.
                    return big.Width == w && big.Height == h ? Pixels.Clone(big) : Resample(big, w, h);
                }, cancellation);
                finished += Upscaling.PlanTiles(source.Width, source.Height).Count;
                progress?.Report((finished, totals));
                results[layer.Id] = (source, enlarged);
                cancellation.ThrowIfCancellationRequested();
            }
        }
        catch
        {
            foreach (var (_, result) in results.Values) result.Dispose();
            throw;
        }
        return new EnhancedLayers(results);
    }

    private sealed class TileProgress(Action<(int Done, int Total)> report) : IProgress<(int Done, int Total)>
    {
        public void Report((int Done, int Total) value) => report(value);
    }
}

/// <summary>Enlarged pixels per layer id, with the committed bitmap each was made from.</summary>
public sealed class EnhancedLayers(Dictionary<Guid, (SKBitmap Source, SKBitmap Result)> results) : IDisposable
{
    public int Count => results.Count;

    /// <summary>The prepared pixels for a layer, when the layer still shows the pixels they were made from.</summary>
    public SKBitmap? For(Layer layer) =>
        results.TryGetValue(layer.Id, out var entry) && ReferenceEquals(entry.Source, layer.Pixels) ? entry.Result : null;

    /// <summary>Disposes the results nothing took, after <c>ResizeImage</c> has run or when it will not.</summary>
    public void DisposeUnused(Document document)
    {
        var inUse = document.AllLayers().Select(l => l.Pixels).ToHashSet();
        foreach (var (_, result) in results.Values) if (!inUse.Contains(result)) result.Dispose();
        results.Clear();
    }

    /// <summary>Discard an uncommitted preparation, for example when its progress window was cancelled.</summary>
    public void Dispose()
    {
        foreach (var (_, result) in results.Values) result.Dispose();
        results.Clear();
    }
}

public sealed partial class EditorSession
{
    /// <summary>
    /// Whether Layer > Enhance Resolution applies: a raster layer shown larger than its own pixels, so a picture
    /// placed small and scaled up has pixels to gain, whose enlargement fits a layer. Text and shapes are redrawn
    /// from their settings instead, and a layer shown at or below its size has nothing to gain.
    /// </summary>
    public bool CanEnhanceResolution(Layer? layer) =>
        layer is { Pixels: { } pixels, IsLive: false } && !layer.IsGroup && !layer.IsAdjustment
        && (layer.Transform.Width > pixels.Width + 0.5 || layer.Transform.Height > pixels.Height + 0.5)
        && DocumentLimits.FitsSurface((long)pixels.Width * UpscaleModels.General.Scale, (long)pixels.Height * UpscaleModels.General.Scale);

    /// <summary>
    /// The pixel size Enhance Resolution gives a layer: its size on the canvas, so every canvas pixel gets one of its
    /// own, but no more than the model's multiple of what it has.
    /// </summary>
    public static SKSizeI EnhancedResolutionSize(Layer layer)
    {
        var pixels = layer.Pixels!;
        var scale = UpscaleModels.General.Scale;
        return new SKSizeI(
            Math.Clamp((int)Math.Round(layer.Transform.Width), 1, pixels.Width * scale),
            Math.Clamp((int)Math.Round(layer.Transform.Height), 1, pixels.Height * scale));
    }

    /// <summary>
    /// The slow half of Enhance Resolution: the layer's committed pixels through the model off the UI thread, fitted to
    /// <see cref="EnhancedResolutionSize"/>. Hand the result to <see cref="EnhanceResolution"/>. Call from the UI thread.
    /// </summary>
    public async Task<EnhancedLayers> PrepareEnhancedResolutionAsync(Layer layer, IProgress<(int Done, int Total)>? progress = null, CancellationToken cancellation = default)
    {
        if (!CanEnhanceResolution(layer)) return new EnhancedLayers([]);
        var source = layer.Pixels!;
        var size = EnhancedResolutionSize(layer);
        var enlarged = await Task.Run(() =>
        {
            using var big = Upscaler.Enlarge(source, UpscaleModels.General, progress, cancellation);
            return big.Width == size.Width && big.Height == size.Height ? Pixels.Clone(big) : Resample(big, size.Width, size.Height);
        }, cancellation);
        return new EnhancedLayers(new Dictionary<Guid, (SKBitmap, SKBitmap)> { [layer.Id] = (source, enlarged) });
    }

    /// <summary>
    /// Layer > Enhance Resolution: the layer takes the prepared pixels and keeps its place and size on the canvas, so a
    /// logo placed large becomes crisp without the document changing. Its mask is resampled to match. False, and
    /// nothing changes, when the layer no longer shows the pixels the result was made from.
    /// </summary>
    public bool EnhanceResolution(Layer layer, EnhancedLayers enhanced)
    {
        if (enhanced.For(layer) is not { } pixels) return false;
        Apply("Enhance Resolution", () =>
        {
            layer.Pixels = pixels;
            if (layer.Mask is { } mask && (mask.Width != pixels.Width || mask.Height != pixels.Height)) layer.Mask = Resample(mask, pixels.Width, pixels.Height);
            // A layer that now has exactly one pixel per canvas pixel is a plain placement again, paintable at full resolution.
            var t = layer.Transform;
            if (t.Rotation == 0 && !t.FlipHorizontal && !t.FlipVertical && t.Distort == null && Math.Abs(t.Width - pixels.Width) < 0.5 && Math.Abs(t.Height - pixels.Height) < 0.5)
                layer.Transform = LayerTransform.Identity(pixels.Width, pixels.Height) with { X = Math.Round(t.X), Y = Math.Round(t.Y) };
        });
        Invalidate(AffectedArea(layer));
        LayersChanged?.Invoke();
        return true;
    }
}
