using System.ComponentModel;
using System.Reflection;
using System.Text;
using Avalonia.Threading;
using Composa.Editing;
using Composa.IO;
using Composa.IO.Psd;
using Composa.Model;
using Composa.Rendering;
using Composa.Selections;
using Composa.Vision;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SkiaSharp;

namespace Composa.App.Mcp;

/// <summary>
/// The tools an agent gets. Each one runs on the UI thread and goes through <see cref="EditorSession"/>, so what an
/// agent does is one undoable step, refreshes the window through the session's events and can be taken back with
/// Ctrl+Z like anything else. Documents are addressed by their tab number, the way <c>list_documents</c> reports them.
/// </summary>
public sealed partial class ComposaTools(MainWindow window)
{
    public McpServerPrimitiveCollection<McpServerTool> Collection()
    {
        var tools = new McpServerPrimitiveCollection<McpServerTool>();
        foreach (var method in typeof(ComposaTools).GetMethods(BindingFlags.Public | BindingFlags.Instance))
            if (method.GetCustomAttribute<McpServerToolAttribute>() != null) tools.Add(McpServerTool.Create(method, this));
        return tools;
    }

    public McpServerResourceCollection Resources()
    {
        var resources = new McpServerResourceCollection();
        foreach (var method in typeof(ComposaTools).GetMethods(BindingFlags.Public | BindingFlags.Instance))
            if (method.GetCustomAttribute<McpServerResourceAttribute>() != null) resources.Add(McpServerResource.Create(method, this));
        return resources;
    }

    [McpServerTool(Name = "list_documents", ReadOnly = true, Idempotent = true)]
    [Description("The documents open in Composa, numbered as their tabs are. Other tools take that number as `document`; leave it out for the active one.")]
    public Task<string> ListDocuments() => OnUi(() =>
    {
        var sessions = window.Sessions;
        if (sessions.Count == 0) return "No document is open.";
        var text = new StringBuilder();
        for (var i = 0; i < sessions.Count; i++)
        {
            var s = sessions[i];
            text.Append(s == window.Session ? "* " : "  ").Append(i + 1).Append(": \"").Append(s.Title).Append("\" ")
                .Append(s.Document.Width).Append('×').Append(s.Document.Height).Append(" px, ")
                .Append(s.Document.AllLayers().Count()).Append(" layers").Append(s.IsModified ? ", unsaved changes" : "").AppendLine();
        }
        return text.ToString().TrimEnd();
    });

    [McpServerTool(Name = "describe_document", ReadOnly = true, Idempotent = true)]
    [Description("The canvas and the layer stack of a document, top layer first. The active layer is marked with *; the id in brackets names a layer when two share a name.")]
    public Task<string> DescribeDocument(int? document = null) => OnUi(() =>
    {
        var s = Session(document);
        var doc = s.Document;
        var text = new StringBuilder();
        text.Append('"').Append(s.Title).Append("\": ").Append(doc.Width).Append('×').Append(doc.Height).Append(" px at ")
            .Append(doc.Resolution.ToString("0.#")).Append(" ppi");
        if (doc.Selection != null && !SelectionMask.Bounds(doc.Selection).IsEmpty)
        {
            var b = SelectionMask.Bounds(doc.Selection);
            text.Append(", selection at ").Append(b.Left).Append(',').Append(b.Top).Append(" size ").Append(b.Width).Append('×').Append(b.Height);
        }
        text.AppendLine().AppendLine("Layers, top first:");
        Describe(text, doc, doc.Layers, 0);
        return text.ToString().TrimEnd();
    });

    private static void Describe(StringBuilder text, Document doc, List<Layer> layers, int depth)
    {
        for (var i = layers.Count - 1; i >= 0; i--)
        {
            var layer = layers[i];
            text.Append(' ', depth * 2).Append(doc.ActiveLayerId == layer.Id ? "* " : "- ").Append('"').Append(layer.Name).Append("\" [").Append(Id(layer)).Append("]: ").Append(Kind(layer));
            if (layer.Text != null) text.Append(" \"").Append(layer.Text.Text.Replace("\n", "\\n")).Append('"');
            if (layer.Pixels != null)
            {
                var source = DeepPixels.Source(layer.Pixels);
                text.Append(source == null ? ", 8-bit" : DeepPixels.IsHdr(source) ? ", HDR float" : ", 16-bit");
                var b = layer.Bounds;
                text.Append(" at ").Append(b.Left.ToString("0")).Append(',').Append(b.Top.ToString("0")).Append(" size ")
                    .Append(b.Width.ToString("0")).Append('×').Append(b.Height.ToString("0"));
            }
            if (!layer.Visible) text.Append(", hidden");
            if (layer.Opacity < 1) text.Append(", opacity ").Append((layer.Opacity * 100).ToString("0")).Append('%');
            if (layer.Blend != BlendMode.Normal) text.Append(", blend ").Append(layer.Blend.DisplayName());
            if (layer.Mask != null) text.Append(", masked");
            if (layer.Clipped) text.Append(", clipped to the layer below");
            if (layer.Effects != null) text.Append(", with effects");
            text.AppendLine();
            if (layer.IsGroup) Describe(text, doc, layer.Children, depth + 1);
        }
    }

    private static string Kind(Layer layer) =>
        layer.IsGroup ? "group" : layer.IsAdjustment ? $"{layer.Adjustment?.DisplayName ?? "adjustment"} adjustment" :
        layer.Text != null ? "text" : layer.Shape != null ? $"{ShapeStyle.DisplayName(layer.Shape.Kind).ToLowerInvariant()} shape" : "pixels";

    [McpServerTool(Name = "image_size")]
    [Description("Image > Image Size: resamples the whole document to a new size in pixels. Give width, height or both; one alone keeps the proportions. resample is automatic (smooth when shrinking, sharp when enlarging), nearest (hard pixel blocks, for pixel art) or enhance (a model run on this machine enlarges photo layers four times and fits the result, inventing fine detail; about a second per 65,000 pixels of each layer; where the model is not available it resamples as automatic and says so). resolution sets pixels per inch.")]
    public Task<string> ImageSize(int? width = null, int? height = null, string resample = "automatic", double? resolution = null, int? document = null) => OnUi(async () =>
    {
        var s = Editable(document);
        var doc = s.Document;
        if (width is null && height is null && resolution is null) throw new McpException("Give a width, a height or a resolution.");
        var w = width ?? (height is { } h0 ? Math.Max(1, (int)Math.Round((double)h0 * doc.Width / doc.Height)) : doc.Width);
        var h = height ?? (width is { } w0 ? Math.Max(1, (int)Math.Round((double)w0 * doc.Height / doc.Width)) : doc.Height);
        if (w < 1 || h < 1 || w > DocumentLimits.MaxSide || h > DocumentLimits.MaxSide) throw new McpException($"The size must be 1 to {DocumentLimits.MaxSide} pixels a side.");
        var mode = resample.ToLowerInvariant() switch
        {
            "automatic" or "auto" or "" => ResampleMode.Automatic,
            "nearest" or "nearest_neighbor" or "nearest neighbor" or "pixel" => ResampleMode.Nearest,
            "enhance" or "ai" or "model" => ResampleMode.Enhance,
            _ => throw new McpException($"Unknown resample \"{resample}\": use automatic, nearest or enhance.")
        };
        var note = mode == ResampleMode.Enhance && !UpscaleModels.IsAvailable ? " " + UpscaleModels.UnavailableReason + " The picture was resampled as automatic does." : "";
        if (!await window.ResizeImage(s, w, h, resolution ?? doc.Resolution, mode)) throw new McpException("Image Size was cancelled.");
        return $"The document is now {doc.Width}×{doc.Height} px at {doc.Resolution:0.##} pixels/inch.{note}";
    });

    [McpServerTool(Name = "enhance_layer_resolution")]
    [Description("Layer > Enhance Resolution: gives a raster layer that is shown larger than its own pixels (placed small and scaled up) enough pixels for its size on the canvas, through a model run on this machine that invents fine detail, up to four times what it has. The layer keeps its place and size on the canvas. Refused for text, shapes, folders and adjustment layers, and for a layer shown at or below its own size. About a second per 65,000 pixels of the layer.")]
    public Task<string> EnhanceLayerResolution([Description(TargetLayer)] string? layer = null, int? document = null) => OnUi(async () =>
    {
        var s = Editable(document);
        var target = layer != null ? Find(s, layer) : s.ActiveLayer ?? throw new McpException("No layer is active.");
        if (!s.CanEnhanceResolution(target)) throw new McpException($"\"{target.Name}\" cannot be enhanced: it must be a raster layer shown larger than its own pixels.");
        if (!UpscaleModels.IsAvailable) throw new McpException(UpscaleModels.UnavailableReason!);
        var before = target.Pixels!;
        var enhanced = await s.PrepareEnhancedResolutionAsync(target);
        try
        {
            if (!s.EnhanceResolution(target, enhanced)) throw new McpException($"\"{target.Name}\" changed while the model ran, so nothing was changed.");
        }
        finally { enhanced.DisposeUnused(s.Document); }
        return $"\"{target.Name}\" now has {target.Pixels!.Width}×{target.Pixels.Height} px, up from {before.Width}×{before.Height}, at the same place and size on the canvas.";
    });

    [McpServerTool(Name = "new_document")]
    [Description("Creates a new document in a new tab and makes it the active one.")]
    public Task<string> NewDocument(
        [Description("Width in the specified unit")] double width,
        [Description("Height in the specified unit")] double height,
        [Description("Background color as #rrggbb; leave it out for a transparent canvas")] string? background = null,
        [Description("pixels, inches, centimeters or millimeters")] string unit = "pixels",
        [Description("Pixels per inch, 1 to 9600; 72 for screen or 300 for print")] double resolution = 72) => OnUi(() =>
    {
        if (window.IsDragging) throw new McpException("The person is dragging on the canvas; try again in a moment.");
        var (w, h) = CanvasSize(width, height, unit, resolution);
        if (!DocumentLimits.FitsSurface(w, h))
            throw new McpException($"A canvas is at most {DocumentLimits.MaxSide} px on a side and {DocumentLimits.MaxSurfaceMegapixels} megapixels; {width}×{height} px is not.");
        var s = EditorSession.NewCanvas(w, h, background == null ? null : ParseColor(background));
        s.Document.Resolution = resolution;
        window.AddSession(s);
        return $"Created document {window.Sessions.Count}: \"{s.Title}\" {w}×{h} px, now active.";
    });

    [McpServerTool(Name = "place_image")]
    [Description("Places an image as a layer, including 16-bit/HDR and developed RAW. Photoshop/GIMP layers arrive in a folder, preserving their structure; inspect_import lists required conversions. rawSettings and cameraRaw develop RAW without a dialog.")]
    public async Task<string> PlaceImage(
        [Description("Absolute path of the image file")] string path,
        [Description("Canvas x of the image's center; leave both out to center it on the canvas")] double? x = null,
        [Description("Canvas y of the image's center")] double? y = null,
        [Description("Scale the image down to fit the canvas (never up)")] bool fit = true,
        [Description("Multiplies the placed size: 0.5 places it at half the size it would otherwise get")] double scale = 1,
        int? document = null, bool allowConversions = false, RawDevelopSettings? rawSettings = null, Composa.Filters.CameraRawSettings? cameraRaw = null)
    {
        path = Existing(path);
        if (scale is <= 0 or > 16 || double.IsNaN(scale)) throw new McpException("scale must be above 0 and at most 16.");
        if (PsdImport.IsPsd(path) || Composa.IO.Xcf.XcfImport.IsXcf(path) || RawImporter.IsRaw(path)) return await PlaceSpecial(path,x,y,fit,scale,document,allowConversions,rawSettings,cameraRaw);
        var target = await OnUi(() => Editable(document));
        var (canvas, budget) = await OnUi(() => (new SKSizeI(target.Document.Width, target.Document.Height), DocumentLimits.DocumentPixelBudget - target.Document.RasterPixels()));
        // Decoding is the slow part and needs no document, so it runs off the UI thread, as the window's own import does.
        // An SVG is drawn at the size it will be placed at, so a scaled one is as sharp as a full-size one.
        var svgFit = new SKSizeI(Math.Max(1, (int)Math.Round(canvas.Width * Math.Min(scale, 1))), Math.Max(1, (int)Math.Round(canvas.Height * Math.Min(scale, 1))));
        var pixels = await Task.Run(() => SvgImporter.IsSvg(path) ? SvgImporter.Render(path, svgFit, budget) : ImageFiles.Load(path));
        if ((long)pixels.Width * pixels.Height > budget)
        {
            pixels.Dispose();
            throw new McpException($"{Path.GetFileName(path)} is {pixels.Width}×{pixels.Height} px, more than the {budget / 1_000_000} megapixels the document can still hold.");
        }
        var used = false;
        try { return await OnUi(() =>
        {
            EnsureImportTarget(target,budget); var s = target;
            var center = x is { } cx && y is { } cy ? new SKPoint((float)cx, (float)cy) : (SKPoint?)null;
            // The SVG was already drawn at the scaled size; scaling it again would shrink it twice.
            var layer = s.AddImageLayer(Path.GetFileNameWithoutExtension(path), pixels, center, fit, SvgImporter.IsSvg(path) && scale <= 1 ? 1 : scale);
            used = true;
            var b = layer.Bounds;
            return $"Placed \"{layer.Name}\" ({pixels.Width}×{pixels.Height} px) as a layer at {b.Left:0},{b.Top:0} size {b.Width:0}×{b.Height:0}, now active.";
        }); } finally { if (!used) pixels.Dispose(); }
    }

    [McpServerTool(Name = "new_layer")]
    [Description("Adds an empty, transparent layer the size of the canvas above the active layer and makes it the active layer.")]
    public Task<string> NewLayer(
        [Description("The layer's name; leave it out for the next free \"Layer n\"")] string? name = null,
        int? document = null) => OnUi(() =>
    {
        var s = Editable(document);
        var layer = s.AddBlankLayer();
        if (!string.IsNullOrWhiteSpace(name)) s.Rename(layer, name.Trim());
        return $"Added layer \"{layer.Name}\", now active.";
    });

    [McpServerTool(Name = "fill_layer")]
    [Description("Fills the active layer with a color, within the selection when there is one. A text layer is recolored instead of filled.")]
    public Task<string> FillLayer(
        [Description("A color as #rrggbb or #aarrggbb")] string color,
        int? document = null) => OnUi(() =>
    {
        var s = Editable(document);
        if (s.ActiveLayer is not { } layer) throw new McpException("No layer is active.");
        if (!s.CanFill) throw new McpException($"\"{layer.Name}\" cannot be filled: it is a {Kind(layer)} layer.");
        s.Fill(ParseColor(color));
        return $"Filled \"{layer.Name}\" with {color}.";
    });

    [McpServerTool(Name = "add_text")]
    [Description("Adds a text layer above the active layer. x and y are the top-left corner of the text in canvas pixels.")]
    public Task<string> AddText(
        [Description("The text; a newline starts a new line")] string text,
        [Description("Left edge in canvas pixels")] double x,
        [Description("Top edge in canvas pixels")] double y,
        [Description("Font size in pixels")] double size = 72,
        [Description("A color as #rrggbb or #aarrggbb")] string color = "#000000",
        [Description("Font family; leave it out for the default")] string? font = null,
        bool bold = false,
        bool italic = false,
        int? document = null) => OnUi(() =>
    {
        var s = Editable(document);
        if (string.IsNullOrEmpty(text)) throw new McpException("The text is empty.");
        var style = s.TextDefaults with
        {
            Text = text, Size = size, Color = (uint)ParseColor(color), Bold = bold, Italic = italic,
            FontFamily = string.IsNullOrWhiteSpace(font) ? s.TextDefaults.FontFamily : font.Trim()
        };
        var layer = s.AddText(new SKPoint((float)x, (float)y), style);
        return $"Added text layer \"{layer.Name}\" at {x:0},{y:0}, now active.";
    });

    [McpServerTool(Name = "undo")]
    [Description("Takes back the last step in the document, whoever made it.")]
    public Task<string> Undo(int? document = null) => OnUi(() =>
    {
        var s = Editable(document);
        if (!s.CanUndo) throw new McpException("There is nothing to undo.");
        var name = s.History.UndoName;
        s.Undo();
        return $"Undid {name}.";
    });

    [McpServerTool(Name = "render", ReadOnly = true, Idempotent = true)]
    [Description("The document as it looks now, flattened to a PNG. Call it to see the result of your changes. A grid labels canvas coordinates so you can read where things are instead of estimating; a region (x, y, width, height) shows part of the canvas at full size, for working on detail such as an eye.")]
    public Task<CallToolResult> Render(
        [Description("The longest side of the image in pixels; the picture is scaled down to fit, never up")] int maxSide = 1024,
        [Description("Draw a labelled grid every this many canvas pixels; 0 for none")] int grid = 0,
        [Description("Left of a region to render instead of the whole canvas, in canvas pixels")] double? x = null,
        [Description("Top of the region")] double? y = null,
        [Description("Width of the region")] double? width = null,
        [Description("Height of the region")] double? height = null,
        int? document = null) => OnUi(() =>
    {
        var s = Session(document);
        SKRectI? region = null;
        if (x != null || y != null || width != null || height != null)
        {
            if (x == null || y == null || width == null || height == null) throw new McpException("A region needs x, y, width and height.");
            region = SKRectI.Create((int)Math.Floor(x.Value), (int)Math.Floor(y.Value), (int)Math.Ceiling(width.Value), (int)Math.Ceiling(height.Value));
        }
        var (png, description) = Png(s, maxSide, region, Math.Max(0, grid));
        return new CallToolResult { Content = [new TextContentBlock { Text = description }, ImageContentBlock.FromBytes(png, "image/png")] };
    });

    /// <summary>The composite, or a region of it, scaled to fit <paramref name="maxSide"/> as a PNG, with a line saying what it shows.</summary>
    private static (byte[] Png, string Description) Png(EditorSession s, int maxSide, SKRectI? region = null, int grid = 0)
    {
        var composite = s.Composite();                                      // Owned by the session: never disposed here.
        var whole = new SKRectI(0, 0, composite.Width, composite.Height);
        var area = region is { } r ? SKRectI.Intersect(r, whole) : whole;
        if (area.IsEmpty) throw new McpException($"That region lies outside the {composite.Width}×{composite.Height} px canvas.");
        var scale = Math.Min(1.0, (double)Math.Clamp(maxSide, 16, 4096) / Math.Max(area.Width, area.Height));
        var size = new SKSizeI(Math.Max(1, (int)Math.Round(area.Width * scale)), Math.Max(1, (int)Math.Round(area.Height * scale)));
        using var picture = Pixels.NewColor(size.Width, size.Height);
        using (var canvas = new SKCanvas(picture))
        {
            canvas.Scale((float)scale);
            using var paint = new SKPaint { IsAntialias = true };
            using var image = SKImage.FromBitmap(composite);
            canvas.DrawImage(image, SKRect.Create(area.Left, area.Top, area.Width, area.Height), SKRect.Create(0, 0, area.Width, area.Height),
                             new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), paint);
            if (grid > 0) DrawGrid(canvas, area, (float)scale, grid);
        }
        using var encoded = SKImage.FromBitmap(picture);
        using var data = encoded.Encode(SKEncodedImageFormat.Png, 100);
        var description = $"{picture.Width}×{picture.Height} px view of the {composite.Width}×{composite.Height} px canvas";
        if (region != null) description += $", region at {area.Left},{area.Top} size {area.Width}×{area.Height}";
        if (grid > 0) description += $", grid every {grid} px labelled in canvas coordinates";
        return (data.ToArray(), description + ".");
    }

    /// <summary>Lines every <paramref name="grid"/> canvas pixels with their canvas coordinate along the top and left edges, drawn on the canvas already scaled to the output.</summary>
    private static void DrawGrid(SKCanvas canvas, SKRectI area, float scale, int grid)
    {
        canvas.ResetMatrix();
        using var line = new SKPaint { Color = new SKColor(0, 0, 0, 120), StrokeWidth = 1, IsAntialias = false };
        using var halo = new SKPaint { Color = new SKColor(255, 255, 255, 200), IsAntialias = true };
        using var text = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        using var font = new SKFont(SKTypeface.Default, 11);
        // Labels need room: when lines fall closer than that in the output, only every nth is labelled.
        var every = Math.Max(1, (int)Math.Ceiling(28 / (grid * scale)));
        float outWidth = area.Width * scale, outHeight = area.Height * scale;
        for (var gx = (int)Math.Ceiling(area.Left / (double)grid) * grid; gx <= area.Right; gx += grid)
        {
            var ox = (float)Math.Round((gx - area.Left) * scale) + 0.5f;
            canvas.DrawLine(ox, 0, ox, outHeight, line);
            if (gx / grid % every != 0) continue;
            var label = gx.ToString();
            var width = font.MeasureText(label);
            canvas.DrawRect(ox + 1, 0, width + 4, 13, halo);
            canvas.DrawText(label, ox + 3, 10, SKTextAlign.Left, font, text);
        }
        for (var gy = (int)Math.Ceiling(area.Top / (double)grid) * grid; gy <= area.Bottom; gy += grid)
        {
            var oy = (float)Math.Round((gy - area.Top) * scale) + 0.5f;
            canvas.DrawLine(0, oy, outWidth, oy, line);
            if (gy / grid % every != 0) continue;
            var label = gy.ToString();
            var width = font.MeasureText(label);
            canvas.DrawRect(0, oy + 1, width + 4, 13, halo);
            canvas.DrawText(label, 2, oy + 11, SKTextAlign.Left, font, text);
        }
    }

    // ---- Plumbing -----------------------------------------------------------------------------------------------

    private static async Task<T> OnUi<T>(Func<T> work) => await Dispatcher.UIThread.InvokeAsync(work);

    /// <summary>For work that awaits on the UI thread, such as a save that writes in the background.</summary>
    private static async Task<T> OnUi<T>(Func<Task<T>> work) => await Dispatcher.UIThread.InvokeAsync(work);

    private EditorSession Session(int? document)
    {
        var sessions = window.Sessions;
        if (sessions.Count == 0) throw new McpException("No document is open in Composa.");
        if (document is { } number)
        {
            if (number < 1 || number > sessions.Count) throw new McpException($"There is no document {number}; list_documents shows {sessions.Count}.");
            return sessions[number - 1];
        }
        return window.Session ?? sessions[0];
    }

    /// <summary>A session ready for an edit: not mid-drag, and with any text being typed committed first, as a menu command would.</summary>
    private EditorSession Editable(int? document)
    {
        if (window.IsDragging) throw new McpException("The person is dragging on the canvas; try again in a moment.");
        var s = Session(document);
        if (s.IsEditingText) s.FinishText();
        if (s.IsInteracting) throw new McpException("An edit is still open in the window; try again in a moment.");
        return s;
    }

    private static SKColor ParseColor(string color) =>
        SKColor.TryParse(color, out var parsed) ? parsed : throw new McpException($"\"{color}\" is not a color; use #rrggbb or #aarrggbb.");
}
