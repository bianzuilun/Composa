using System.ComponentModel;
using System.Text.Json.Serialization;
using Composa.Model;
using Composa.Painting;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SkiaSharp;

namespace Composa.App.Mcp;

/// <summary>The painting and shape tools: a brush stroke through given points, and the live shapes the Shape tool draws.</summary>
public sealed partial class ComposaTools
{
    [McpServerTool(Name = "paint_stroke")]
    [Description("Paints one brush stroke through the given canvas points on the active layer, or on the layer named without changing which layer is active, as a drag with the Brush tool would. A single point is a dab. Live text and shape layers cannot be painted on.")]
    public Task<string> PaintStroke(
        [Description("The stroke's points as [[x, y], [x, y], ...] in canvas pixels; a curve needs a point every few pixels")] double[][] points,
        [Description("A color as #rrggbb or #aarrggbb; ignored by the erase and smearing modes")] string color = "#000000",
        [Description("Brush diameter in pixels")] double size = 40,
        [Description("Edge hardness from 0 (soft) to 1 (hard)")] double hardness = 0.8,
        [Description("The most the stroke covers, 0 to 1; also Liquify/heal/blur/smudge/dodge/burn strength")] double opacity = 1,
        [Description("paint, erase, liquify (push pixels along the path), heal (texture-aware), blur, smudge, dodge or burn")] string mode = "paint",
        [Description("The layer to paint on; the active one when left out")] string? layer = null,
        int? document = null,
        [Description("Blur's Gaussian radius in canvas pixels, independent of size; 0.1 to 250")] double blurRadius = 5) => OnUi(() =>
    {
        var s = Editable(document);
        var brushMode = ParseBrushMode(mode);
        ValidatePoints(points);
        if (points.Length == 0 || points.Any(p => p.Length != 2)) throw new McpException("points is a list of [x, y] pairs with at least one pair.");
        if (size is < 1 or > 5000 || double.IsNaN(size)) throw new McpException("size is 1 to 5000 pixels.");
        // A stroke aimed at a layer paints there and leaves the selection alone, so what is added next still goes where it did.
        var active = s.ActiveLayer;
        if (layer != null) s.SelectLayer(Find(s, layer).Id);
        var target = s.ActiveLayer ?? throw new McpException("No layer is active.");
        var brush = new BrushSettings { Size = size, Hardness = Math.Clamp(hardness, 0, 1), Opacity = Math.Clamp(opacity, 0, 1), BlurRadius = FiniteRange(blurRadius, 0.1, 250, "blurRadius") };
        var path = points.Select(p => new SKPoint((float)p[0], (float)p[1])).ToList();
        string? problem;
        try { problem = s.PaintStroke(path, brush, ParseColor(color), brushMode); }
        finally { if (active != null && active != target) s.SelectLayer(active.Id); }
        if (problem != null) throw new McpException(problem);
        return $"Painted a {mode} stroke of {path.Count} point{(path.Count == 1 ? "" : "s")} on \"{target.Name}\".";
    });

    /// <summary>One stroke of a <c>paint_strokes</c> call, with the same choices as <c>paint_stroke</c>.</summary>
    public sealed class StrokeSpec
    {
        [JsonPropertyName("points"), Description("The stroke's points as [[x, y], [x, y], ...] in canvas pixels; a single point is a dab")]
        public double[][] Points { get; set; } = [];
        [JsonPropertyName("color"), Description("A color as #rrggbb or #aarrggbb")]
        public string Color { get; set; } = "#000000";
        [JsonPropertyName("size"), Description("Brush diameter in pixels")]
        public double Size { get; set; } = 40;
        [JsonPropertyName("hardness"), Description("Edge hardness from 0 (soft) to 1 (hard)")]
        public double Hardness { get; set; } = 0.8;
        [JsonPropertyName("opacity"), Description("The most the stroke covers, 0 to 1")]
        public double Opacity { get; set; } = 1;
        [JsonPropertyName("mode"), Description("paint, erase, liquify, heal, blur, smudge, dodge or burn")]
        public string Mode { get; set; } = "paint";
        [JsonPropertyName("blurRadius"), Description("Independent Blur radius, 0.1 to 250 canvas pixels")]
        public double BlurRadius { get; set; } = 5;
    }

    private const int MaxBatch = 2000;

    [McpServerTool(Name = "paint_strokes")]
    [Description("Paints many brush strokes in one call as one undoable step: block in a whole picture, or draw every line trace_edges found, without a round trip per stroke. Each stroke has its own points, color, size, hardness, opacity and mode, as paint_stroke takes them. Strokes are painted in the order given, so lay the big soft ones down first.")]
    public Task<string> PaintStrokes(
        [Description("The strokes, in painting order")] StrokeSpec[] strokes,
        [Description("The layer to paint on; the active one when left out")] string? layer = null,
        int? document = null) => OnUi(() =>
    {
        var s = Editable(document);
        if (strokes.Length == 0) throw new McpException("strokes is empty.");
        if (strokes.Length > MaxBatch) throw new McpException($"At most {MaxBatch} strokes go in one call; this one has {strokes.Length}.");
        var planned = new List<PlannedStroke>(strokes.Length);
        for (var i = 0; i < strokes.Length; i++)
        {
            var stroke = strokes[i];
            ValidatePoints(stroke.Points);
            if (stroke.Points.Length == 0 || stroke.Points.Any(p => p.Length != 2)) throw new McpException($"Stroke {i + 1}: points is a list of [x, y] pairs with at least one pair.");
            if (stroke.Size is < 1 or > 5000 || double.IsNaN(stroke.Size)) throw new McpException($"Stroke {i + 1}: size is 1 to 5000 pixels.");
            var brush = new BrushSettings { Size = stroke.Size, Hardness = Math.Clamp(stroke.Hardness, 0, 1), Opacity = Math.Clamp(stroke.Opacity, 0, 1), BlurRadius = FiniteRange(stroke.BlurRadius, 0.1, 250, "blurRadius") };
            planned.Add(new PlannedStroke(stroke.Points.Select(p => new SKPoint((float)p[0], (float)p[1])).ToList(), brush, ParseColor(stroke.Color), ParseBrushMode(stroke.Mode)));
        }
        var active = s.ActiveLayer;
        if (layer != null) s.SelectLayer(Find(s, layer).Id);
        var target = s.ActiveLayer ?? throw new McpException("No layer is active.");
        (int painted, string? problem) result;
        try { result = s.PaintStrokes(planned); }
        finally { if (active != null && active != target) s.SelectLayer(active.Id); }
        var (painted, problem) = result;
        if (problem != null) throw new McpException($"{problem} {painted} of {planned.Count} strokes were painted before that.");
        return $"Painted {painted} strokes on \"{target.Name}\".";
    });

    private static BrushMode ParseBrushMode(string mode) => mode.Trim().ToLowerInvariant() switch
    {
        "paint" => BrushMode.Paint, "erase" => BrushMode.Erase, "blur" => BrushMode.Blur,
        "smudge" => BrushMode.Smudge, "dodge" => BrushMode.Dodge, "burn" => BrushMode.Burn,
        "liquify" => BrushMode.Liquify, "heal" => BrushMode.Heal,
        _ => throw new McpException("mode is paint, erase, liquify, heal, blur, smudge, dodge or burn.")
    };

    [McpServerTool(Name = "add_shape")]
    [Description("Adds a live rectangle, rounded rectangle or ellipse as a new layer above the active one. Live shapes stay editable: transform_layer redraws them at the new size.")]
    public Task<string> AddShape(
        [Description("rectangle, rounded or ellipse")] string kind,
        [Description("Left edge in canvas pixels")] double x,
        [Description("Top edge in canvas pixels")] double y,
        double width,
        double height,
        [Description("Fill color as #rrggbb or #aarrggbb")] string color = "#000000",
        [Description("Corner radius in pixels, for a rounded rectangle")] double cornerRadius = 24,
        int? document = null,
        [Description("Continuous smooth corners for rounded rectangles")] bool smoothCorners = false) => OnUi(() =>
    {
        var s = Editable(document);
        var shapeKind = kind.Trim().ToLowerInvariant() switch
        {
            "rectangle" => ShapeKind.Rectangle, "rounded" or "rounded rectangle" or "roundedrectangle" => ShapeKind.RoundedRectangle,
            "ellipse" or "circle" => ShapeKind.Ellipse,
            _ => throw new McpException("kind is rectangle, rounded or ellipse; a line is added with add_line.")
        };
        if (width < 1 || height < 1) throw new McpException("The width and height must be at least 1 px.");
        var layer = s.AddShape(new ShapeStyle(shapeKind, (uint)ParseColor(color), Math.Max(0, cornerRadius)) { SmoothCorners = smoothCorners }, SKRect.Create((float)x, (float)y, (float)width, (float)height))
                    ?? throw new McpException($"That shape is too large: a shape covers at most {DocumentLimits.MaxSurfaceMegapixels} megapixels.");
        return $"Added {ShapeStyle.DisplayName(shapeKind).ToLowerInvariant()} \"{layer.Name}\" at {x:0},{y:0} size {width:0}×{height:0}, now active.";
    });

    [McpServerTool(Name = "add_line")]
    [Description("Adds a live straight line with round ends as a new layer above the active one.")]
    public Task<string> AddLine(
        double x1, double y1, double x2, double y2,
        [Description("Line color as #rrggbb or #aarrggbb")] string color = "#000000",
        [Description("Thickness in pixels")] double width = 4,
        int? document = null) => OnUi(() =>
    {
        var s = Editable(document);
        if (width < 1) throw new McpException("The width must be at least 1 px.");
        var layer = s.AddLine(new SKPoint((float)x1, (float)y1), new SKPoint((float)x2, (float)y2), ParseColor(color), width)
                    ?? throw new McpException("The two ends are the same point, or too far apart for one layer.");
        return $"Added line \"{layer.Name}\" from {x1:0},{y1:0} to {x2:0},{y2:0}, now active.";
    });
}
