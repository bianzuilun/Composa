using System.ComponentModel;
using Composa.Editing;
using Composa.Model;
using Composa.Rendering;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SkiaSharp;

namespace Composa.App.Mcp;

public sealed partial class ComposaTools
{
    [McpServerTool(Name = "convert_layer_depth")]
    [Description("Converts a raster layer to 8 or 16 bits/channel as Layer > Bit Depth. HDR converted to integer is clipped to SDR. Geometry, effects and mask remain; undo restores the original native precision.")]
    public Task<string> ConvertLayerDepth(int depth, string? layer = null, int? document = null) => OnUi(() => {
        if (depth is not (8 or 16)) throw new McpException("depth is 8 or 16.");
        var s = Editable(document); var target = Target(s, layer);
        if (target.IsLive || target.Pixels == null) throw new McpException("Depth conversion requires a raster layer.");
        s.ConvertLayerDepth(target, depth == 16); return $"\"{target.Name}\" is {depth}-bit.";
    });

    [McpServerTool(Name = "layer_mask")]
    [Description("Creates/edits a layer mask. action: add (current selection, or all white; inverse=true hides the selection/all), enable, disable, delete, apply, edit, pixels, solo or unsolo. edit/pixels select the brush/filter target. solo previews coverage without affecting export. Existing masks are never silently replaced.")]
    public Task<string> LayerMask(string action, string? layer = null, bool inverse = false, int? document = null) => OnUi(() => {
        var s = Editable(document); var target = Target(s, layer); var key = action.Trim().ToLowerInvariant();
        if (key == "add") { if (target.Mask != null) throw new McpException("The layer already has a mask."); s.AddMask(target, inverse); }
        else if (key == "pixels") { if (s.SoloMaskLayerId is { } solo && s.Document.Find(solo) is { } shown) s.ToggleSoloMask(shown); s.EditingMask = false; }
        else {
            if (target.Mask == null) throw new McpException("The layer has no mask; add one first.");
            switch (key) {
                case "enable": s.SetMaskEnabled(target, true); break;
                case "disable": s.SetMaskEnabled(target, false); break;
                case "delete": s.DeleteMask(target); break;
                case "apply": if (target.Pixels == null || target.IsLive) throw new McpException("Rasterize the layer before applying its mask."); s.ApplyMask(target); break;
                case "edit": s.EditingMask = true; break;
                case "solo": if (s.SoloMaskLayerId != target.Id) s.ToggleSoloMask(target); break;
                case "unsolo": if (s.SoloMaskLayerId == target.Id) s.ToggleSoloMask(target); break;
                default: throw new McpException("action is add, enable, disable, delete, apply, edit, pixels, solo or unsolo.");
            }
        }
        s.InvalidateAll(); s.NotifyLayersChanged(); return $"Mask {key} on \"{target.Name}\"; editing {(s.IsEditingMask ? "mask" : "pixels")}.";
    });

    [McpServerTool(Name = "group_layers")]
    [Description("Groups the named layers in one undoable step; all must be siblings. The new folder becomes active.")]
    public Task<string> GroupLayers(string[] layers, string? name = null, int? document = null) => OnUi(() => {
        var s = Editable(document);
        if (layers.Length == 0) throw new McpException("Give at least one layer.");
        var targets = layers.Select(l => Find(s, l)).Distinct().ToList();
        if (targets.Select(l => s.Document.ParentOf(l.Id)).Distinct().Count() != 1) throw new McpException("Grouped layers must be siblings.");
        s.SelectLayer(targets[0].Id); foreach (var target in targets.Skip(1)) s.SelectLayer(target.Id, extend: true);
        s.GroupSelectedLayers();
        if (name != null) s.Rename(s.ActiveLayer!, name);
        return $"Grouped {targets.Count} layers as \"{s.ActiveLayer!.Name}\".";
    });

    [McpServerTool(Name = "ungroup_layers")]
    [Description("Removes a folder and preserves its children, as Layer > Ungroup Layers. Undo restores the folder.")]
    public Task<string> UngroupLayers(string layer, int? document = null) => OnUi(() => {
        var s = Editable(document); var target = Find(s, layer);
        if (!target.IsGroup) throw new McpException("The layer is not a folder.");
        s.Ungroup(target); return $"Ungrouped \"{target.Name}\".";
    });

    [McpServerTool(Name = "canvas_size")]
    [Description("Resizes the canvas without resampling layers. width/height use pixels, inches, centimeters or millimeters at the document DPI. anchor is Center, TopLeft, Top, TopRight, Left, Right, BottomLeft, Bottom or BottomRight. Text, shapes, effects and native precision stay editable.")]
    public Task<string> CanvasSizeTool(double width, double height, string unit = "pixels", string anchor = "Center", int? document = null) => OnUi(() => {
        var s = Editable(document); var (w,h) = CanvasSize(width, height, unit, s.Document.Resolution);
        if (!Enum.TryParse<Anchor>(anchor, true, out var a) || !Enum.IsDefined(a)) throw new McpException("Unknown anchor.");
        s.ResizeCanvas(w,h,a); return $"Canvas is now {w}×{h} px.";
    });

    [McpServerTool(Name = "crop_canvas")]
    [Description("Crops to x,y,width,height in canvas pixels, retaining editable text/shapes/effects and native precision. Areas outside the canvas are allowed within document limits.")]
    public Task<string> CropCanvas(int x, int y, int width, int height, int? document = null) => OnUi(() => {
        CheckSurface(width, height); var s = Editable(document);
        if (Math.Abs((long)x) > DocumentLimits.MaxSide || Math.Abs((long)y) > DocumentLimits.MaxSide) throw new McpException("Crop origin is too far from the canvas.");
        s.Crop(SKRectI.Create(x,y,width,height)); return $"Cropped to {width}×{height} px at {x},{y}.";
    });

    [McpServerTool(Name = "trim_canvas")]
    [Description("Trims unused edges, preserving live layers and effects. basedOn: TransparentPixels, TopLeftPixelColor or BottomRightPixelColor. tolerance 0..255.")]
    public Task<string> TrimCanvas(string basedOn = "TransparentPixels", int tolerance = 0, bool top = true, bool bottom = true, bool left = true, bool right = true, int? document = null) => OnUi(() => {
        if (!Enum.TryParse<TrimBasedOn>(basedOn,true,out var basis) || !Enum.IsDefined(basis)) throw new McpException("Unknown trim basis.");
        var s = Editable(document); var changed = s.Trim(new TrimOptions { BasedOn = basis, Tolerance = (byte)Math.Clamp(tolerance,0,255), Top = top, Bottom = bottom, Left = left, Right = right });
        return changed ? $"Trimmed to {s.Document.Width}×{s.Document.Height} px." : "There was nothing to trim.";
    });

    [McpServerTool(Name = "rotate_canvas")]
    [Description("Rotates the canvas and every layer by 90, 180 or 270 degrees clockwise. Preserves live content and precision.")]
    public Task<string> RotateCanvas(int degrees = 90, int? document = null) => OnUi(() => {
        if (degrees is not (90 or 180 or 270 or -90)) throw new McpException("degrees is 90, 180, 270 or -90.");
        var s = Editable(document); s.RotateCanvas(degrees != -90 && degrees != 270); if (degrees == 180) s.RotateCanvas(true);
        return $"Rotated canvas {degrees}°.";
    });

    [McpServerTool(Name = "set_layer_effects")]
    [Description("Replaces a layer's live effects using the full Stroke, Shadow, ColorOverlay, InnerShadow, OuterGlow and InnerGlow records. Opacity is 0..1; colors are ARGB integers. An empty record removes all effects. Effects remain editable through resizing.")]
    public Task<string> SetLayerEffects(string layer, LayerEffects effects, int? document = null) => OnUi(() => {
        var s = Editable(document); var target = Find(s, layer);
        if (!EditorSession.CanHaveEffects(target)) throw new McpException("The layer cannot have effects.");
        s.Apply("Layer Effects", () => s.SetEffects(target, effects)); s.NotifyLayersChanged(); return $"Updated effects on \"{target.Name}\".";
    });

    [McpServerTool(Name = "set_text")]
    [Description("Edits an existing live text layer without opening the typing UI. Give only fields to change; replacing characters keeps surviving letters' color/font runs. Resizing stays editable.")]
    public Task<string> SetText(string layer, string? text = null, double? size = null, string? color = null, string? font = null, bool? bold = null, bool? italic = null, int? document = null) => OnUi(() => {
        var s = Editable(document); var target = Find(s,layer); var style = target.Text ?? throw new McpException("The layer is not live text.");
        if (text != null) style = style.WithReplacedCharacters(0, style.Text.Length, text.Length) with { Text = text };
        if (color != null) style = style.WithColor((uint)ParseColor(color),0,style.Text.Length);
        s.SetText(target, style with { Size = size ?? style.Size, FontFamily = font ?? style.FontFamily, Bold = bold ?? style.Bold, Italic = italic ?? style.Italic });
        return $"Updated text \"{target.Name}\".";
    });

    [McpServerTool(Name = "set_shape")]
    [Description("Changes a live shape's fill, corner radius and continuous smooth corners without rasterizing it. Fill color is hex. Effects and placement remain.")]
    public Task<string> SetShape(string layer, string? color = null, double? cornerRadius = null, bool? smoothCorners = null, int? document = null) => OnUi(() => {
        var s = Editable(document); var target = Find(s,layer); var shape = target.Shape ?? throw new McpException("The layer is not a live shape.");
        var next = shape with { Fill = color == null ? shape.Fill : (uint)ParseColor(color), CornerRadius = cornerRadius == null ? shape.CornerRadius : FiniteRange(cornerRadius.Value,0,DocumentLimits.MaxSide,"cornerRadius"), SmoothCorners = smoothCorners ?? shape.SmoothCorners };
        s.Apply("Shape", () => { target.Pixels = EditorSession.RenderShape(next, target.Pixels!.Width, target.Pixels.Height); target.Shape = next; });
        s.InvalidateAll(); s.NotifyLayersChanged(); return $"Updated shape \"{target.Name}\".";
    });

    private static (int Width,int Height) CanvasSize(double width, double height, string unit, double dpi)
    {
        FiniteRange(dpi,1,9600,"resolution");
        if (!Enum.TryParse<CanvasUnit>(unit,true,out var u) || !Enum.IsDefined(u)) throw new McpException("unit is pixels, inches, centimeters or millimeters.");
        var factor = u == CanvasUnit.Pixels ? 1 : dpi / CanvasUnits.PerInch(u);
        FiniteRange(width * factor,1,DocumentLimits.MaxSide,"width in pixels"); FiniteRange(height * factor,1,DocumentLimits.MaxSide,"height in pixels");
        var w = (int)Math.Round(width*factor); var h = (int)Math.Round(height*factor); CheckSurface(w,h); return (w,h);
    }

    private static void CheckSurface(int width,int height)
    {
        if (!DocumentLimits.FitsSurface(width,height)) throw new McpException($"A surface is at most {DocumentLimits.MaxSide} pixels per side and {DocumentLimits.MaxSurfaceMegapixels} megapixels.");
    }
}
