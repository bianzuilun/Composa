using Composa.Editing;
using Composa.Filters;
using Composa.Model;
using Composa.Rendering;
using SkiaSharp;

namespace Composa.App.Controls;

public sealed partial class CanvasView
{
    private CameraRawPreviewFrame? cameraRawPreview;
    public SKBitmap? CameraRawScopePreview => cameraRawPreview?.ScopePreview;

    /// <summary>Skia evaluates the measured color cube on the render canvas (GPU when the host provides one).
    /// Spatial grades, selections and layered documents keep the precision-aware asynchronous CPU path.</summary>
    public bool TryCameraRawPreview(EditorSession target, SKBitmap original, CameraRawSettings settings)
    {
        if (session != target || target.IsEditingMask || target.Selection != null || target.SoloMaskLayerId != null
            || target.Document.Layers.Count != 1 || target.ActiveLayer is not { IsLive: false, IsGroup: false, IsAdjustment: false, Visible: true } layer
            || layer.Mask != null || layer.Blend != BlendMode.Normal || layer.Effects is { IsEmpty: false }
            || target.View.ProofColors || target.View.GamutWarning) return false;
        var lattice = CameraRawPixels.PreviewLattice(original, settings);
        if (lattice == null) return false;
        var next = CameraRawPreviewFrame.Create(original, layer, lattice);
        if (next == null) return false;
        cameraRawPreview = next; navigatorStale = true; InvalidateVisual(); return true;
    }

    public void ClearCameraRawPreview()
    {
        if (cameraRawPreview == null) return;
        // A queued draw still holds its frame; its native resources leave with that frame, never on this UI mutation.
        cameraRawPreview = null; viewStale = true; navigatorStale = true; InvalidateVisual();
    }

    private sealed class CameraRawPreviewFrame
    {
        private const string Program = """
            uniform shader cube;
            uniform shader picture;
            float3 cell(float3 p) { return cube.eval(float2(p.g * 33.0 + p.r + 0.5, p.b + 0.5)).rgb; }
            half4 main(float2 coordinate) {
                half4 color = picture.eval(coordinate);
                if (color.a <= 0.0) return color;
                float3 rgb = color.rgb / color.a;
                float peak = max(1.0, max(rgb.r, max(rgb.g, rgb.b)));
                float3 position = clamp(rgb / peak, 0.0, 1.0) * 32.0;
                float3 base = min(floor(position), float3(31.0));
                float3 f = position - base;
                float3 lower = mix(mix(cell(base), cell(base + float3(1,0,0)), f.r),
                                   mix(cell(base + float3(0,1,0)), cell(base + float3(1,1,0)), f.r), f.g);
                float3 upper = mix(mix(cell(base + float3(0,0,1)), cell(base + float3(1,0,1)), f.r),
                                   mix(cell(base + float3(0,1,1)), cell(base + float3(1,1,1)), f.r), f.g);
                return half4(mix(lower, upper, f.b) * peak * color.a, color.a);
            }
            """;
        private static readonly Lazy<SKRuntimeEffect?> Effect = new(() => SKRuntimeEffect.CreateShader(Program, out _));
        private readonly SKBitmap source;
        private readonly SKBitmap cubePixels;
        private readonly SKImage image;
        private readonly SKShader filter;
        private readonly SKMatrix placement;
        private readonly float opacity;
        private readonly ColorLattice lattice;
        private SKBitmap? scopePreview;

        private CameraRawPreviewFrame(SKBitmap source, SKBitmap cubePixels, SKImage image, SKShader filter, Layer layer, ColorLattice lattice)
        {
            this.source = source; this.cubePixels = cubePixels; this.filter = filter; this.lattice = lattice;
            this.image = image; placement = layer.Matrix; opacity = (float)layer.Opacity;
        }

        internal static CameraRawPreviewFrame? Create(SKBitmap original, Layer layer, ColorLattice lattice)
        {
            if (Effect.Value is not { } effect) return null;
            var pixels = DeepPixels.New(33 * 33, 33, hdr: true);
            var points = lattice.Cube;
            for (var b = 0; b < 33; b++) for (var g = 0; g < 33; g++) for (var r = 0; r < 33; r++)
            {
                var at = ((b * 33 + g) * 33 + r) * 3;
                DeepPixels.Write(pixels, g * 33 + r, b, new SKColorF(points[at], points[at + 1], points[at + 2], 1));
            }
            using var cube = SKImage.FromPixels(pixels.PeekPixels());
            using var shader = cube.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, new SKSamplingOptions(SKFilterMode.Nearest));
            var source = DeepPixels.Source(original) ?? original;
            var image = SKImage.FromPixels(source.PeekPixels());
            using var picture = image.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, new SKSamplingOptions(SKFilterMode.Linear));
            using var children = new SKRuntimeEffectChildren(effect) { ["cube"] = shader, ["picture"] = picture };
            using var uniforms = new SKRuntimeEffectUniforms(effect);
            var filter = effect.ToShader(uniforms, children);
            if (filter == null) { image.Dispose(); pixels.Dispose(); return null; }
            return new CameraRawPreviewFrame(source, pixels, image, filter, layer, lattice);
        }

        internal void Draw(SKCanvas canvas, SKMatrix view)
        {
            canvas.Save(); canvas.Concat(in view); canvas.Concat(in placement);
            using var paint = new SKPaint { Shader = filter, Color = SKColors.White.WithAlpha((byte)Math.Round(opacity * 255)) };
            canvas.DrawRect(0, 0, source.Width, source.Height, paint);
            canvas.Restore(); GC.KeepAlive(source); GC.KeepAlive(cubePixels);
        }

        internal SKBitmap ScopePreview
        {
            get
            {
                if (scopePreview != null) return scopePreview;
                var scale = Math.Min(1, 384.0 / Math.Max(source.Width, source.Height));
                var preview = Pixels.NewColor(Math.Max(1, (int)(source.Width * scale)), Math.Max(1, (int)(source.Height * scale)));
                using (var canvas = new SKCanvas(preview))
                { canvas.Scale((float)scale); canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Linear)); }
                new ColorLookupAdjustment { Lattice = lattice }.Apply(preview);
                return scopePreview = preview;
            }
        }
    }
}
