using System.ComponentModel;
using Composa.Editing;
using Composa.Model;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Composa.App.Mcp;

/// <summary>
/// The layer tools. A layer is named by its name, or by the id <c>describe_document</c> shows in brackets when two
/// layers share a name. None of these change which layer is active except <c>select_layer</c>, <c>duplicate_layer</c>
/// (the copy becomes active, as in the Layers panel) and <c>delete_layer</c>.
/// </summary>
public sealed partial class ComposaTools
{
    [McpServerTool(Name = "select_layer")]
    [Description("Makes a layer the active one: what fill_layer acts on and where the tools that add layers put them.")]
    public Task<string> SelectLayer(string layer, int? document = null) => OnUi(() =>
    {
        var s = Editable(document);
        var target = Find(s, layer);
        s.SelectLayer(target.Id);
        return $"\"{target.Name}\" is the active layer.";
    });

    [McpServerTool(Name = "set_layer")]
    [Description("Changes a layer's name, visibility, opacity or blend mode. Give only what should change.")]
    public Task<string> SetLayer(
        string layer,
        string? name = null,
        bool? visible = null,
        [Description("0 to 1")] double? opacity = null,
        [Description("A blend mode as the Layers panel names it, such as Multiply or Soft Light")] string? blend = null,
        int? document = null) => OnUi(() =>
    {
        var s = Editable(document);
        var target = Find(s, layer);
        var changes = new List<string>();
        if (name != null) { s.Rename(target, name); changes.Add($"named \"{target.Name}\""); }
        if (visible is { } shown) { s.SetVisible(target, shown); changes.Add(shown ? "shown" : "hidden"); }
        if (opacity is { } o)
        {
            if (o is < 0 or > 1 || double.IsNaN(o)) throw new McpException("opacity is a number from 0 to 1.");
            s.Begin("Opacity");
            s.SetOpacity(target, o);
            s.Commit();
            s.NotifyLayersChanged();
            changes.Add($"opacity {o * 100:0}%");
        }
        if (blend != null) { s.SetBlend(target, ParseBlend(blend)); changes.Add($"blend {target.Blend.DisplayName()}"); }
        if (changes.Count == 0) throw new McpException("Nothing to change: give a name, visible, opacity or blend.");
        return $"\"{target.Name}\": {string.Join(", ", changes)}.";
    });

    [McpServerTool(Name = "transform_layer")]
    [Description("Moves, resizes or rotates a layer by setting its frame in canvas pixels. Give only what should change. Text is laid out again at the new size and a shape is redrawn, so both stay sharp. Consecutive calls on one layer fold into one undo step, as the transform bar's fields do.")]
    public Task<string> TransformLayer(
        string layer,
        [Description("Left edge")] double? x = null,
        [Description("Top edge")] double? y = null,
        double? width = null,
        double? height = null,
        [Description("Degrees; positive turns clockwise")] double? rotation = null,
        int? document = null,
        [Description("Width as percentage of source pixel width; cannot combine with width")] double? widthPercent = null,
        [Description("Height as percentage of source pixel height; cannot combine with height")] double? heightPercent = null) => OnUi(() =>
    {
        var s = Editable(document);
        var target = Find(s, layer);
        if (target.Pixels == null) throw new McpException($"\"{target.Name}\" is a {Kind(target)} layer and has no frame of its own.");
        var t = target.Transform;
        if (widthPercent != null && width != null || heightPercent != null && height != null) throw new McpException("Give each dimension in pixels or percent.");
        if (widthPercent is { } wp) width = target.Pixels.Width * FiniteRange(wp,0.01,100000,"widthPercent") / 100;
        if (heightPercent is { } hp) height = target.Pixels.Height * FiniteRange(hp,0.01,100000,"heightPercent") / 100;
        foreach (var value in new[] { x,y,width,height,rotation }) if (value is { } v && !double.IsFinite(v)) throw new McpException("Transform values must be finite.");
        var next = t with { X = x ?? t.X, Y = y ?? t.Y, Width = width ?? t.Width, Height = height ?? t.Height, Rotation = rotation ?? t.Rotation };
        if (next.Width < 1 || next.Height < 1 || next.Width > DocumentLimits.MaxSide || next.Height > DocumentLimits.MaxSide)
            throw new McpException($"The width and height must be between 1 and {DocumentLimits.MaxSide} px.");
        s.SetTransform(target, next);
        var b = target.Bounds;
        return $"\"{target.Name}\" is now at {b.Left:0},{b.Top:0} size {b.Width:0}×{b.Height:0}" + (next.Rotation != 0 ? $", rotated {next.Rotation:0.#}°" : "") + ".";
    });

    [McpServerTool(Name = "duplicate_layer")]
    [Description("Copies a layer (a folder with everything in it) right above the original. The copy becomes the active layer.")]
    public Task<string> DuplicateLayer(string layer, int? document = null) => OnUi(() =>
    {
        var s = Editable(document);
        var target = Find(s, layer);
        s.SelectLayer(target.Id);
        s.DuplicateSelectedLayers();
        return $"Duplicated \"{target.Name}\" as \"{s.ActiveLayer!.Name}\", now active.";
    });

    [McpServerTool(Name = "delete_layer", Destructive = true)]
    [Description("Deletes a layer, or a folder with everything in it. Undo brings it back.")]
    public Task<string> DeleteLayer(string layer, int? document = null) => OnUi(() =>
    {
        var s = Editable(document);
        var target = Find(s, layer);
        s.SelectLayer(target.Id);
        s.DeleteSelectedLayers();
        if (s.Document.Find(target.Id) != null) throw new McpException($"\"{target.Name}\" could not be deleted.");
        return $"Deleted \"{target.Name}\".";
    });

    [McpServerTool(Name = "reorder_layer")]
    [Description("Moves a layer up or down one step among its siblings, or to the top or bottom of them.")]
    public Task<string> ReorderLayer(
        string layer,
        [Description("up, down, top or bottom")] string direction,
        int? document = null) => OnUi(() =>
    {
        var s = Editable(document);
        var target = Find(s, layer);
        var siblings = s.Document.SiblingsOf(target.Id)!;
        var index = siblings.IndexOf(target);
        var (neighbour, drop) = direction.Trim().ToLowerInvariant() switch
        {
            "up" => (index + 1 < siblings.Count ? siblings[index + 1] : null, LayerDrop.Above),
            "down" => (index > 0 ? siblings[index - 1] : null, LayerDrop.Below),
            "top" => (index + 1 < siblings.Count ? siblings[^1] : null, LayerDrop.Above),
            "bottom" => (index > 0 ? siblings[0] : null, LayerDrop.Below),
            _ => throw new McpException("direction is up, down, top or bottom.")
        };
        if (neighbour == null) return $"\"{target.Name}\" is already at the {(drop == LayerDrop.Above ? "top" : "bottom")}.";
        s.MoveLayers([target], neighbour, drop);
        return $"\"{target.Name}\" is now {siblings.Count - 1 - siblings.IndexOf(target)} from the top of its {siblings.Count} siblings.";
    });

    // ---- Naming layers --------------------------------------------------------------------------------------------

    /// <summary>The short id describe_document shows, for when two layers share a name.</summary>
    private static string Id(Layer layer) => layer.Id.ToString("N")[..8];

    private static Layer Find(EditorSession s, string layer)
    {
        layer = layer.Trim();
        var all = s.Document.AllLayers().ToList();
        if (all.Where(l => Id(l) == layer).ToList() is [var byId]) return byId;
        var byName = all.Where(l => string.Equals(l.Name, layer, StringComparison.OrdinalIgnoreCase)).ToList();
        if (byName.Count == 1) return byName[0];
        if (byName.Count > 1) throw new McpException($"{byName.Count} layers are named \"{layer}\"; use an id instead: {string.Join(", ", byName.Select(Id))}.");
        throw new McpException($"There is no layer \"{layer}\"; describe_document lists them with their ids.");
    }

    private static BlendMode ParseBlend(string blend)
    {
        var compact = blend.Replace(" ", "").Replace("(Add)", "");
        foreach (var mode in Enum.GetValues<BlendMode>())
            if (string.Equals(mode.ToString(), compact, StringComparison.OrdinalIgnoreCase) || string.Equals(mode.DisplayName(), blend.Trim(), StringComparison.OrdinalIgnoreCase)) return mode;
        throw new McpException($"\"{blend}\" is not a blend mode. The modes are {string.Join(", ", Enum.GetValues<BlendMode>().Select(m => m.DisplayName()))}.");
    }
}
