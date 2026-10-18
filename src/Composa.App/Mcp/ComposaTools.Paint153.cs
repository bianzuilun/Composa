using System.ComponentModel;
using System.Text.Json;
using Composa.Editing;
using Composa.Painting;
using Composa.Rendering;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SkiaSharp;

namespace Composa.App.Mcp;

public sealed partial class ComposaTools
{
    [McpServerTool(Name = "clone_stroke")]
    [Description("Clone Stamp along canvas points, sampling sourceX/sourceY at the first point and following its displacement. Handles scaled layers. sampleAllLayers reads the composite; otherwise the layer. Leaves interactive clone source/settings unchanged. One undoable stroke.")]
    public Task<string> CloneStroke(double[][] points,double sourceX,double sourceY,double size = 40,double hardness = 0.8,double opacity = 1,bool sampleAllLayers = false,string? layer = null,int? document = null) => OnUi(() => {
        ValidatePoints(points); ValidatePoints([[sourceX,sourceY]]); var s = Editable(document); var target = Target(s,layer);
        var brush = new BrushSettings { Size = FiniteRange(size,1,5000,"size"), Hardness = FiniteRange(hardness,0,1,"hardness"), Opacity = FiniteRange(opacity,0,1,"opacity") };
        var problem = s.PaintCloneStroke(points.Select(p => new SKPoint((float)p[0],(float)p[1])).ToList(),brush,new SKPoint((float)sourceX,(float)sourceY),sampleAllLayers);
        if (problem != null) throw new McpException(problem); return $"Cloned a stroke on \"{target.Name}\".";
    });

    [McpServerTool(Name = "draw_gradient")]
    [Description("Draws a linear or radial gradient between canvas points, respecting selection/mask and native precision. radial=true uses start as center, end as radius. toTransparent ignores endColor. opacity 0..1. One undoable edit.")]
    public Task<string> DrawGradient(double startX,double startY,double endX,double endY,string startColor = "#000000",string endColor = "#FFFFFF",bool radial = false,bool toTransparent = false,double opacity = 1,string? layer = null,int? document = null) => OnUi(() => {
        ValidatePoints([[startX,startY],[endX,endY]]); FiniteRange(opacity,0,1,"opacity"); var first = ParseColor(startColor); var last = ParseColor(endColor);
        var s = Editable(document); var target = Target(s,layer); if (!s.CanEditPixels) throw new McpException("Select a raster layer or mask.");
        var saved = (s.Foreground,s.Background,s.GradientRadial,s.GradientToTransparent,s.GradientOpacity);
        try {
            (s.Foreground,s.Background,s.GradientRadial,s.GradientToTransparent,s.GradientOpacity) = (first,last,radial,toTransparent,opacity);
            var original = s.BeginGradient(target);
            try { s.DrawGradient(target,original,new SKPoint((float)startX,(float)startY),new SKPoint((float)endX,(float)endY)); s.Commit(); }
            catch { s.Cancel(); throw; }
            s.NotifyLayersChanged(); return $"Drew gradient on \"{target.Name}\".";
        } finally { (s.Foreground,s.Background,s.GradientRadial,s.GradientToTransparent,s.GradientOpacity) = saved; }
    });

    [McpServerTool(Name = "get_layer_details", ReadOnly = true, Idempotent = true)]
    [Description("Reads a layer's exact transform, depth, mask state, live text/shape and effects settings. Use these records with set_text, set_shape, set_layer_effects and transform_layer.")]
    public Task<string> GetLayerDetails(string layer,int? document = null) => OnUi(() => {
        var s = Session(document); var target = Find(s,layer); var source = target.Pixels == null ? null : DeepPixels.Source(target.Pixels);
        return JsonSerializer.Serialize(new { id = Id(target),target.Name,target.Transform,depth = source == null ? "8-bit" : DeepPixels.IsHdr(source) ? "HDR float" : "16-bit",
            target.Text,target.Shape,target.Effects,hasMask = target.Mask != null,target.MaskEnabled,editingMask = s.IsEditingMask && s.ActiveLayer == target,soloMask = s.SoloMaskLayerId == target.Id },McpJson);
    });

    [McpServerTool(Name = "clipboard")]
    [Description("Copies/cuts/pastes through Composa's clipboard. action: copy, copy_merged, cut, paste, paste_in_place or paste_into. Paste on the welcome screen creates a canvas from the image; internal copies retain native precision. Operates on the active document; use set_workspace to activate a tab.")]
    public Task<string> Clipboard(string action) => OnUi(async () => {
        if (window.IsDragging || window.Session?.IsInteracting == true) throw new McpException("Finish the current canvas interaction first.");
        if (action is not ("copy" or "copy_merged" or "cut" or "paste" or "paste_in_place" or "paste_into")) throw new McpException("Unknown clipboard action.");
        await window.McpClipboard(action); return $"Clipboard {action}; {window.Sessions.Count} documents open.";
    });
}
