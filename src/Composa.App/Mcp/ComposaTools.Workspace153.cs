using System.ComponentModel;
using System.Text.Json;
using Avalonia.Input;
using Composa.App.Dialogs;
using Composa.Editing;
using Composa.Filters;
using Composa.Model;
using Composa.Rendering;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SkiaSharp;

namespace Composa.App.Mcp;

public sealed partial class ComposaTools
{
    [McpServerTool(Name = "get_capabilities", ReadOnly = true, Idempotent = true)]
    [Description("Lists this running Composa's actual MCP tools and feature routes. Call before planning an edit, especially after an upgrade. tools/list supplies the full input schemas. Operations are shared across Claude, Codex and DeepSeek Harness; no client-specific editing protocol is required.")]
    public Task<string> GetCapabilities() => OnUi(() => JsonSerializer.Serialize(new {
        tools = Collection().Select(t => t.ProtocolTool.Name).Order().ToArray(),
        workflows = new {
            liquify = "liquify or paint_stroke/paint_strokes mode=liquify", healing = "paint_stroke mode=heal; content_aware_fill",
            raw = "inspect_import; open_document/place_image/import_images with rawSettings and cameraRaw",
            precision = "convert_layer_depth; describe_document; export_image bitDepth=16 or .exr",
            color = "set_print_proof; render_view; export_image colorMode=cmyk",
            cameraRaw = "get_camera_raw_settings; filter_camera_raw; get_color_scopes",
            filters = "filter_progressive_blur; filter_scanlines; filter_blur preserveTransparency; repeat_last_filter",
            liveContent = "add_shape smoothCorners; set_shape; set_text; set_layer_effects; transform_layer; image_size; canvas_size; crop_canvas; trim_canvas",
            masks = "layer_mask; render_mask; select_layer_pixels", workspace = "set_workspace; list_commands; set_shortcut; get_preferences; set_preferences",
            clipboard = "clipboard; import_images newDocument=true for file drops"
        },
        automatic = new[] { "Native precision composition/adjustments and source retention", "Texture-aware healing and stable smudge sampling", "Bounded scopes sampling", "GPU preview is a UI rendering optimization with automatic CPU fallback", "Selected-layer reveal, tab overflow, cursor and option-bar gestures are UI behaviors; direct MCP tools operate by coordinates/IDs" }
    },McpJson));

    [McpServerTool(Name = "set_print_proof")]
    [Description("Sets CMYK proof colors/gamut warning and optional CMYK ICC profile without changing document pixels. profilePath is an absolute .icc/.icm path; omitted uses the saved profile/default FOGRA39. Settings persist. render_view shows proof; export_image exports real CMYK.")]
    public async Task<string> SetPrintProof(bool? proofColors = null,bool? gamutWarning = null,string? profilePath = null,int? document = null)
    {
        var profile = profilePath == null ? null : await ReadProfile(profilePath);
        return await OnUi(async () => {
            var s = Editable(document); var proof = await window.McpColorProof(profile,profilePath == null ? null : System.IO.Path.GetFileNameWithoutExtension(profilePath));
            if (!window.Sessions.Contains(s) || s.IsInteracting) throw new McpException("The document changed interaction during ICC preparation.");
            s.View = s.View with { ProofColors = proofColors ?? s.View.ProofColors, GamutWarning = gamutWarning ?? s.View.GamutWarning };
            s.InvalidateAll(); if (s == window.Session) window.RememberMcpView();
            return $"CMYK profile: {proof.Name}; proof={s.View.ProofColors}; gamutWarning={s.View.GamutWarning}.";
        });
    }

    [McpServerTool(Name = "render_view", ReadOnly = true, Idempotent = true)]
    [Description("Returns the document's flattened preview with current CMYK proof/gamut warning applied. Does not alter exports or document pixels. render returns unproofed RGB; render_mask shows a mask.")]
    public Task<CallToolResult> RenderView(int maxSide = 1024,int? document = null) => OnUi(() => {
        var s = Session(document); using var image = Pixels.Clone(s.Composite());
        window.Canvas.ColorProof?.Apply(image,new SKRectI(0,0,image.Width,image.Height),s.View.ProofColors,s.View.GamutWarning);
        return ImageResult(image,maxSide,"Document view with current print proof.");
    });

    [McpServerTool(Name = "render_mask", ReadOnly = true, Idempotent = true)]
    [Description("Shows one layer's mask as white coverage on black, at its native pixel dimensions, independently of canvas solo-mask UI.")]
    public Task<CallToolResult> RenderMask(string layer,int maxSide = 1024,int? document = null) => OnUi(() => {
        var target = Find(Session(document),layer); var mask = target.Mask ?? throw new McpException("The layer has no mask.");
        using var image = Pixels.NewColor(mask.Width,mask.Height); image.Erase(SKColors.Black);
        using (var canvas = new SKCanvas(image)) using (var paint = new SKPaint { Color = SKColors.White }) canvas.DrawBitmap(mask,0,0,paint);
        return ImageResult(image,maxSide,$"Mask of {target.Name}.");
    });

    [McpServerTool(Name = "get_color_scopes", ReadOnly = true, Idempotent = true)]
    [Description("Returns RGB/luminance histogram bins and bounded vectorscope samples for the document or a layer, plus an optional vectorscope image. Samples native sources; HDR is clipped only for display scopes, never in the document.")]
    public Task<CallToolResult> GetColorScopes(string? layer = null,bool includeImage = true,int? document = null) => OnUi(() => {
        var s = Session(document); var pixels = layer == null ? s.Composite() : Find(s,layer).Pixels ?? throw new McpException("The layer has no pixels.");
        var histogram = Histogram.Of(pixels); var scope = Vectorscope.Of(DeepPixels.Source(pixels) ?? pixels);
        var text = JsonSerializer.Serialize(new { red = histogram.Channel(0), green = histogram.Channel(1), blue = histogram.Channel(2), luminance = histogram.Channel(3), scope.Samples, scope.Maximum },McpJson);
        if (!includeImage) return new CallToolResult { Content = [new TextContentBlock { Text = text }] };
        using var plot = scope.Plot(); var result = ImageResult(plot,128,text); return result;
    });

    [McpServerTool(Name = "set_workspace")]
    [Description("Activates/reorders a document tab and changes workspace view without editing pixels. position is a 1-based tab index; zoom is a scale (3=300%, up to 64), fit fits the canvas. canvasOnly controls fullscreen canvas mode. View/tool preferences persist as in the UI. Direct zoom replaces middle-button gestures.")]
    public Task<string> SetWorkspace(int? document = null,int? position = null,double? zoom = null,bool fit = false,bool? canvasOnly = null,
        bool? autoSelect = null,bool? transformControls = null,bool? pixelGrid = null,bool? navigator = null,bool? rulers = null,bool? guides = null,bool? snap = null) => OnUi(() => {
        var s = Editable(document);
        if (position is { } at && (at < 1 || at > window.Sessions.Count)) throw new McpException("position is a 1-based open tab index.");
        if (zoom is { } z) FiniteRange(z,0.001,64,"zoom");
        if (zoom != null && fit) throw new McpException("Give zoom or fit, not both.");
        window.ActivateMcpDocument(s);
        if (position is { } p) window.ReorderMcpDocument(s,p-1);
        if (zoom is { } value) window.Canvas.ZoomTo(value); if (fit) window.Canvas.Fit();
        if (canvasOnly is { } only && only != window.CanvasOnly) window.ToggleCanvasOnly();
        if (autoSelect is { } auto) window.Canvas.AutoSelect = auto;
        if (transformControls is { } transform) window.Canvas.ShowTransformControls = transform;
        if (pixelGrid is { } pixel) window.Canvas.ShowPixelGrid = pixel;
        s.View = s.View with { ShowNavigator = navigator ?? s.View.ShowNavigator, ShowRulers = rulers ?? s.View.ShowRulers, ShowGuides = guides ?? s.View.ShowGuides, Snap = snap ?? s.View.Snap };
        window.RememberMcpView(); return $"Active document {window.Sessions.ToList().IndexOf(s)+1}; zoom {window.Canvas.Zoom*100:0.##}%; canvasOnly={window.CanvasOnly}.";
    });

    [McpServerTool(Name = "list_commands", ReadOnly = true, Idempotent = true)]
    [Description("Searches the same command catalog as the command palette, returning stable IDs, titles, groups, active/default shortcuts and enabled state. Use structured MCP tools to perform editing without modal dialogs; set_shortcut clears/rebinds these IDs.")]
    public Task<string> ListCommands(string search = "") => OnUi(() => JsonSerializer.Serialize(window.McpCommands.Where(c => !c.Hidden && (c.Title.Contains(search,StringComparison.OrdinalIgnoreCase) || c.Id.Contains(search,StringComparison.OrdinalIgnoreCase)))
        .Select(c => new { c.Id,c.Title,c.Group,gesture = c.Gesture?.ToString(),defaultGesture = c.Default?.ToString(),enabled = c.Enabled?.Invoke() ?? true }),McpJson));

    [McpServerTool(Name = "set_shortcut")]
    [Description("Rebinds a command ID from list_commands and saves it to Settings. gesture is e.g. Ctrl+Shift+P; empty clears it; reset=true restores the default. Conflicting custom bindings are refused. Custom bindings take priority over newly introduced defaults.")]
    public Task<string> SetShortcut(string id,string gesture = "",bool reset = false) => OnUi(() => {
        if (!window.McpCommands.Any(c => c.Id == id)) throw new McpException("Unknown command ID; use list_commands.");
        KeyGesture? key = null;
        if (!reset && gesture.Length > 0) { try { key = KeyGesture.Parse(gesture); } catch { throw new McpException("Invalid key gesture."); } }
        if (key != null && window.McpCommands.Any(c => c.Id != id && Equals(c.Gesture,key) && window.Settings.Shortcuts.ContainsKey(c.Id))) throw new McpException("Another custom command already uses that gesture.");
        window.SetMcpShortcut(id,key,reset); return $"Shortcut {id} {(reset ? "reset" : gesture.Length == 0 ? "cleared" : gesture)}.";
    });

    [McpServerTool(Name = "get_preferences", ReadOnly = true, Idempotent = true)]
    [Description("Reads language, print profile and view preferences. Language choices are system, en, zh-CN and ja; language changes take effect on restart.")]
    public Task<string> GetPreferences() => OnUi(() => JsonSerializer.Serialize(new { window.Settings.Language,window.Settings.CmykProfileName,window.Settings.View,window.Settings.AutoSelect,window.Settings.ShowTransformControls,window.Settings.ShowPixelGrid },McpJson));

    [McpServerTool(Name = "set_preferences")]
    [Description("Saves language selection: system (follow OS), en (English), zh-CN (Simplified Chinese), ja (Japanese). Restart is required; this tool does not quit the editor.")]
    public Task<string> SetPreferences(string language) => OnUi(() => {
        if (language is not ("system" or "en" or "zh-CN" or "ja")) throw new McpException("language is system, en, zh-CN or ja.");
        window.Settings.Language = language; window.Settings.Save(); return "Language saved; restart Composa to apply it.";
    });

    [McpServerTool(Name = "get_update_notice", ReadOnly = true, Idempotent = true)]
    [Description("Reads the current update strip and its expandable Release body without checking the network, downloading or installing anything.")]
    public Task<string> GetUpdateNotice() => OnUi(() => JsonSerializer.Serialize(new { phase = window.UpdateNotice.State.ToString(),window.UpdateNotice.Message,window.UpdateNotice.ReleaseBody },McpJson));

    [McpServerTool(Name = "get_history", ReadOnly = true, Idempotent = true)]
    [Description("Lists history step indices, names and current position; go_to_history scrubs directly with a single restore.")]
    public Task<string> GetHistory(int? document = null) => OnUi(() => { var s = Session(document); return JsonSerializer.Serialize(new { current = s.History.CurrentIndex, steps = s.History.Steps.Select((step,i) => new { index = i,step.Name,step.Id }) },McpJson); });

    [McpServerTool(Name = "go_to_history")]
    [Description("Restores the history step index from get_history, with one restore as in the History panel.")]
    public Task<string> GoToHistory(int index,int? document = null) => OnUi(() => { var s = Editable(document); if (index < 0 || index >= s.History.Steps.Count) throw new McpException("Invalid history index."); s.GoToHistory(index); return $"History position {s.History.CurrentIndex}."; });

    [McpServerTool(Name = "redo")]
    [Description("Restores the last undone editing step.")]
    public Task<string> Redo(int? document = null) => OnUi(() => { var s = Editable(document); if (!s.CanRedo) throw new McpException("Nothing to redo."); var name = s.History.RedoName; s.Redo(); return $"Redid {name}."; });

    private static CallToolResult ImageResult(SKBitmap pixels,int maxSide,string description)
    {
        var scale = Math.Min(1,Math.Clamp(maxSide,16,4096)/(double)Math.Max(pixels.Width,pixels.Height));
        using var sized = EditorSession.Resample(pixels,Math.Max(1,(int)Math.Round(pixels.Width*scale)),Math.Max(1,(int)Math.Round(pixels.Height*scale)));
        using var data = sized.Encode(SKEncodedImageFormat.Png,100);
        return new CallToolResult { Content = [new TextContentBlock { Text = description },ImageContentBlock.FromBytes(data.ToArray(),"image/png")] };
    }
}
