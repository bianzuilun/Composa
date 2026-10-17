using Composa.Model;
using Composa.Rendering;

namespace Composa.Editing;

public sealed partial class EditorSession
{
    /// <summary>Changes a raster layer's storage depth, leaving its geometry, effects and mask untouched.</summary>
    public void ConvertLayerDepth(Layer layer, bool sixteenBit)
    {
        FinishInteraction();
        if (layer.IsLive || layer.Pixels is not { } pixels || layer.IsGroup || layer.IsAdjustment) return;
        var source = DeepPixels.Source(pixels);
        if (!sixteenBit && source == null || sixteenBit && source is { } wide && !DeepPixels.IsHdr(wide)) return;
        Apply(sixteenBit ? "Convert Layer to 16-bit" : "Convert Layer to 8-bit", () =>
        {
            if (sixteenBit) layer.Pixels = DeepPixels.Preview(DeepPixels.Convert(source ?? pixels));
            else
            {
                var copy = Pixels.Clone(pixels);
                DeepPixels.Detach(copy);
                layer.Pixels = copy;
            }
        });
        InvalidateAll();
        LayersChanged?.Invoke();
    }
}
