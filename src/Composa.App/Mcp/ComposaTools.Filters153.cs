using System.ComponentModel;
using System.Text.Json;
using Composa.Filters;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SkiaSharp;

namespace Composa.App.Mcp;

public sealed partial class ComposaTools
{
    [McpServerTool(Name = "liquify")]
    [Description("Liquify: pushes pixels along canvas points [[x,y],...], using the untouched stroke source to retain detail. Needs at least two different points. One undoable stroke; size is brush diameter, strength is 0 to 1. Raster layers or masks only.")]
    public Task<string> Liquify(double[][] points, double size = 40, double strength = 0.5, double hardness = 0.8, string? layer = null, int? document = null)
    {
        if (points.Length < 2 || points.Any(p => p.Length != 2) || points.All(p => p.SequenceEqual(points[0])))
            throw new McpException("Liquify needs at least two different [x,y] points.");
        return PaintStroke(points, size: size, hardness: hardness, opacity: strength, mode: "liquify", layer: layer, document: document);
    }

    [McpServerTool(Name = "filter_progressive_blur")]
    [Description("Progressive Blur: blur radius changes between two endpoints in canvas coordinates, equivalent to dragging the filter's handles. fromRadius/toRadius are 0 to 250 pixels. Keeps native precision and respects the selection.")]
    public Task<string> FilterProgressiveBlur(double startX, double startY, double endX, double endY, double fromRadius = 0, double toRadius = 20, string? layer = null, int? document = null)
    {
        ValidatePoints([[startX, startY], [endX, endY]]);
        if (startX == endX && startY == endY) throw new McpException("The blur endpoints must be different.");
        return Filter(document, layer, new FilterSettings { Kind = FilterKind.ProgressiveBlur,
            ProgressiveStart = new SKPoint((float)startX, (float)startY), ProgressiveEnd = new SKPoint((float)endX, (float)endY),
            ProgressiveFrom = FiniteRange(fromRadius, 0, 250, "fromRadius"), ProgressiveTo = FiniteRange(toRadius, 0, 250, "toRadius") });
    }

    [McpServerTool(Name = "filter_scanlines")]
    [Description("CRT Scanlines with every control: spacing 2–32, thickness 5–100, glow/dots/smoothness/threshold/blackLevel 0–100, wobble 0–64, displace/density/contrast -100–100, split 0–16. colors is black_white, two_colors or original; dark/light are hex colors.")]
    public Task<string> FilterScanlines(int spacing = 4, double thickness = 70, double glow = 0, double dots = 0,
        double wobble = 0, double displace = 0, double smoothness = 50, double threshold = 0, int split = 0,
        double density = 0, double contrast = 0, double blackLevel = 0, string colors = "black_white",
        string dark = "#000000", string light = "#FFFFFF", string? layer = null, int? document = null)
    {
        var palette = colors.Trim().ToLowerInvariant() switch {
            "black_white" => DitherColors.BlackWhite, "two_colors" => DitherColors.TwoColors, "original" => DitherColors.Original,
            _ => throw new McpException("colors is black_white, two_colors or original.") };
        return Filter(document, layer, new FilterSettings { Kind = FilterKind.Scanlines, Scanlines = new ScanlinesSettings {
            Spacing = spacing, Thickness = thickness, Glow = glow, Dots = dots, Wobble = wobble, Displace = displace,
            Smoothness = smoothness, Threshold = threshold, Split = split, Density = density, Contrast = contrast,
            BlackLevel = blackLevel, Colors = palette, Dark = (uint)ParseColor(dark), Light = (uint)ParseColor(light)
        }.Normalized() });
    }

    [McpServerTool(Name = "filter_camera_raw")]
    [Description("Applies a complete Camera Raw grade at native precision, with measured color and regional Shadows. settings has Exposure (-5..5), Temperature/Tint/Contrast/Highlights/Shadows/Whites/Blacks/Vibrance/Saturation (-100..100), effects, Curve (0..1 point coordinates), Mixer (eight-family arrays), Grading, Detail, Optics and Calibration. Omitted settings start neutral. resetGroups resets complete groups; disabledGroups omits them. Group names: Light, Color, Effects, Curve, Mixer, Grading, Detail, Optics, Calibration. get_camera_raw_settings returns defaults/last grade. No dialog is opened.")]
    public Task<string> FilterCameraRaw(CameraRawSettings settings, string[]? resetGroups = null, string[]? disabledGroups = null, int seed = 1, string? layer = null, int? document = null)
    {
        ValidateGrade(settings);
        var grade = settings.Normalized();
        foreach (var name in resetGroups ?? []) grade = grade.ResetGroup(RawGroup(name));
        foreach (var name in disabledGroups ?? []) grade = grade.Without(RawGroup(name));
        return Filter(document, layer, new FilterSettings { Kind = FilterKind.CameraRaw, CameraRaw = grade, Seed = (uint)seed });
    }

    [McpServerTool(Name = "get_camera_raw_settings", ReadOnly = true, Idempotent = true)]
    [Description("Returns the complete default or last accepted Camera Raw grade, with every nested control. Use as input to filter_camera_raw after changing the desired fields. Curve coordinates are 0..1; mixer arrays have eight entries: Reds, Oranges, Yellows, Greens, Aquas, Blues, Purples, Magentas.")]
    public Task<string> GetCameraRawSettings(bool last = false) => OnUi(() => JsonSerializer.Serialize(last ? window.LastCameraRaw : new CameraRawSettings(), McpJson));

    [McpServerTool(Name = "repeat_last_filter")]
    [Description("Repeats the last accepted UI or MCP filter with its settings on the target layer. Each repeat is undoable.")]
    public Task<string> RepeatFilter(string? layer = null, int? document = null) => OnUi(async () => {
        var settings = window.LastFilter ?? throw new McpException("No filter has been accepted yet.");
        return await Filter(document, layer, settings);
    });

    [McpServerTool(Name = "content_aware_fill")]
    [Description("Fills the current selection using surrounding texture (PatchMatch), as Edit > Content-Aware Fill. Raster layer only; one undoable edit.")]
    public Task<string> ContentAwareFill(string? layer = null, int? document = null) => OnUi(() => {
        var s = Editable(document); var target = Target(s, layer);
        if (!s.CanEditPixels || s.IsEditingMask || s.Selection == null) throw new McpException("Select an area on a raster layer first; masks cannot be healed.");
        s.ContentAwareFill(); return $"Content-aware filled the selection on \"{target.Name}\".";
    });

    private static readonly JsonSerializerOptions McpJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    private static CameraRawGroup RawGroup(string name) => Enum.TryParse<CameraRawGroup>(name, true, out var group) && Enum.IsDefined(group)
        ? group : throw new McpException($"Unknown Camera Raw group {name}.");

    private static void ValidateGrade(CameraRawSettings grade)
    {
        if (grade is null || grade.Curve is null || grade.Mixer is null || grade.Grading is null || grade.Detail is null || grade.Optics is null || grade.Calibration is null
            || grade.Curve.Rgb is null || grade.Curve.Red is null || grade.Curve.Green is null || grade.Curve.Blue is null
            || grade.Mixer.Hue is null || grade.Mixer.Saturation is null || grade.Mixer.Luminance is null
            || grade.Grading.Shadows is null || grade.Grading.Midtones is null || grade.Grading.Highlights is null || grade.Grading.Global is null)
            throw new McpException("Camera Raw groups and arrays cannot be null; omit a field to use its default.");
    }

    private static double FiniteRange(double value, double min, double max, string name) => double.IsFinite(value) && value >= min && value <= max
        ? value : throw new McpException($"{name} must be a finite number from {min} to {max}.");

    private static void ValidatePoints(double[][] points)
    {
        if (points.Length == 0 || points.Any(p => p is null || p.Length != 2 || p.Any(v => !double.IsFinite(v) || Math.Abs(v) > DocumentLimitCoordinate)))
            throw new McpException("points must contain finite [x,y] canvas coordinates.");
    }
    private const double DocumentLimitCoordinate = Composa.Model.DocumentLimits.MaxSide * 2.0;
}
