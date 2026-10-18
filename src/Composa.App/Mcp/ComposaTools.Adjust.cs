using System.ComponentModel;
using Composa.Editing;
using Composa.Filters;
using Composa.Vision;
using Composa.Model;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SkiaSharp;

namespace Composa.App.Mcp;

/// <summary>
/// The Image menu's adjustments and the Filter menu's filters. An adjustment is baked into the active layer's pixels,
/// or added as an adjustment layer above it with <c>asLayer</c>, which affects everything below and stays editable.
/// A filter always changes pixels. Both respect a selection. Camera Raw's complete grade is exposed separately.
/// </summary>
public sealed partial class ComposaTools
{
    private const string AsLayer = "Add an adjustment layer above the layer instead of changing its pixels; it then affects every layer below and can be edited later";
    private const string TargetLayer = "The layer to work on; the active one when left out";

    // ---- Adjustments ------------------------------------------------------------------------------------------------

    [McpServerTool(Name = "adjust_brightness_contrast")]
    [Description("Brightness/Contrast, each -100 to 100.")]
    public Task<string> AdjustBrightnessContrast(double brightness = 0, double contrast = 0, [Description(AsLayer)] bool asLayer = false, [Description(TargetLayer)] string? layer = null, int? document = null) =>
        Adjust(document, layer, asLayer, new BrightnessContrastAdjustment { Brightness = Math.Clamp(brightness, -100, 100), Contrast = Math.Clamp(contrast, -100, 100) });

    [McpServerTool(Name = "adjust_exposure")]
    [Description("Exposure in stops (-20 to 20), offset for the shadows (-0.5 to 0.5) and gamma (0.01 to 9.99, 1 is unchanged).")]
    public Task<string> AdjustExposure(double exposure = 0, double offset = 0, double gamma = 1, [Description(AsLayer)] bool asLayer = false, [Description(TargetLayer)] string? layer = null, int? document = null) =>
        Adjust(document, layer, asLayer, new ExposureAdjustment { Exposure = Math.Clamp(exposure, -20, 20), Offset = Math.Clamp(offset, -0.5, 0.5), Gamma = Math.Clamp(gamma, 0.01, 9.99) });

    [McpServerTool(Name = "adjust_hue_saturation")]
    [Description("Hue/Saturation: hue -180 to 180 degrees, saturation and lightness -100 to 100, on all colors or one range of them. Colorize tints the whole layer with the hue (0 to 360) and saturation.")]
    public Task<string> AdjustHueSaturation(
        double hue = 0, double saturation = 0, double lightness = 0,
        [Description("master, reds, yellows, greens, cyans, blues or magentas")] string range = "master",
        bool colorize = false,
        [Description(AsLayer)] bool asLayer = false, [Description(TargetLayer)] string? layer = null, int? document = null)
    {
        if (!Enum.TryParse<HueRange>(range.Trim(), true, out var hueRange)) throw new McpException("range is master, reds, yellows, greens, cyans, blues or magentas.");
        var shift = new HslShift(colorize ? Math.Clamp(hue, 0, 360) : Math.Clamp(hue, -180, 180), Math.Clamp(saturation, -100, 100), Math.Clamp(lightness, -100, 100));
        return Adjust(document, layer, asLayer, new HueSaturationAdjustment { Colorize = colorize }.WithShift(hueRange, shift));
    }

    [McpServerTool(Name = "adjust_levels")]
    [Description("Levels: input black and white points (0 to 255), midtone gamma (0.1 to 9.99, above 1 brightens) and output black and white, on all channels or one.")]
    public Task<string> AdjustLevels(
        double inputBlack = 0, double inputWhite = 255, double gamma = 1, double outputBlack = 0, double outputWhite = 255,
        [Description("rgb, red, green or blue")] string channel = "rgb",
        [Description(AsLayer)] bool asLayer = false, [Description(TargetLayer)] string? layer = null, int? document = null)
    {
        var range = new LevelsRange
        {
            InputBlack = Math.Clamp(inputBlack, 0, 253), InputWhite = Math.Clamp(inputWhite, Math.Clamp(inputBlack, 0, 253) + 2, 255),
            Gamma = Math.Clamp(gamma, 0.1, 9.99), OutputBlack = Math.Clamp(outputBlack, 0, 255), OutputWhite = Math.Clamp(outputWhite, 0, 255)
        };
        return Adjust(document, layer, asLayer, new LevelsAdjustment().WithRange(Channel(channel), range));
    }

    [McpServerTool(Name = "adjust_curves")]
    [Description("Curves: control points as [[input, output], ...] with values 0 to 255, on all channels or one. [[0,0],[255,255]] is a straight line; [[0,0],[64,48],[192,208],[255,255]] adds contrast.")]
    public Task<string> AdjustCurves(
        double[][] points,
        [Description("rgb, red, green or blue")] string channel = "rgb",
        [Description(AsLayer)] bool asLayer = false, [Description(TargetLayer)] string? layer = null, int? document = null)
    {
        if (points.Length < 2 || points.Any(p => p.Length != 2)) throw new McpException("points is a list of at least two [input, output] pairs.");
        var curve = points.Select(p => new CurvePoint(Math.Clamp(p[0], 0, 255), Math.Clamp(p[1], 0, 255)));
        return Adjust(document, layer, asLayer, new CurvesAdjustment().WithChannel(Channel(channel), curve));
    }

    [McpServerTool(Name = "adjust_black_and_white")]
    [Description("Black & White: how light each color range comes out (-200 to 300, Photoshop's defaults when left out), with an optional tint.")]
    public Task<string> AdjustBlackAndWhite(
        double reds = 40, double yellows = 60, double greens = 40, double cyans = 60, double blues = 20, double magentas = 80,
        bool tint = false, [Description("0 to 360")] double tintHue = 40, [Description("0 to 100")] double tintSaturation = 20,
        [Description(AsLayer)] bool asLayer = false, [Description(TargetLayer)] string? layer = null, int? document = null) =>
        Adjust(document, layer, asLayer, new BlackAndWhiteAdjustment
        {
            Reds = reds, Yellows = yellows, Greens = greens, Cyans = cyans, Blues = blues, Magentas = magentas,
            Tint = tint, TintHue = Math.Clamp(tintHue, 0, 360), TintSaturation = Math.Clamp(tintSaturation, 0, 100)
        });

    [McpServerTool(Name = "adjust_color_balance")]
    [Description("Color Balance: for the shadows, midtones and highlights, three values -100 to 100 as [cyan/red, magenta/green, yellow/blue]; negative goes to the first color.")]
    public Task<string> AdjustColorBalance(
        double[]? shadows = null, double[]? midtones = null, double[]? highlights = null, bool preserveLuminosity = true,
        [Description(AsLayer)] bool asLayer = false, [Description(TargetLayer)] string? layer = null, int? document = null)
    {
        static double[] Three(double[]? values, string name) =>
            values == null ? new double[3] : values.Length == 3 ? values.Select(v => Math.Clamp(v, -100, 100)).ToArray() : throw new McpException($"{name} needs three values: cyan/red, magenta/green, yellow/blue.");
        return Adjust(document, layer, asLayer, new ColorBalanceAdjustment
        {
            Shadows = Three(shadows, "shadows"), Midtones = Three(midtones, "midtones"), Highlights = Three(highlights, "highlights"), PreserveLuminosity = preserveLuminosity
        });
    }

    [McpServerTool(Name = "adjust_gradient_map")]
    [Description("Gradient Map: maps the layer's tones onto a gradient from the shadows color to the highlights color.")]
    public Task<string> AdjustGradientMap(string shadows = "#000000", string highlights = "#FFFFFF", bool reversed = false, [Description(AsLayer)] bool asLayer = false, [Description(TargetLayer)] string? layer = null, int? document = null) =>
        Adjust(document, layer, asLayer, new GradientMapAdjustment { Shadows = (uint)ParseColor(shadows), Highlights = (uint)ParseColor(highlights), Reversed = reversed });

    [McpServerTool(Name = "adjust_color_lookup")]
    [Description("Color Lookup: grades the layer through a lookup table, either a bundled look by name (Fine Mono, Muted Chrome, Standard Slide or Vivid Slide) or a .cube or .3dl file at an absolute path. Amount, 0 to 100, mixes the look into the original. An adjustment layer made this way is named after the look.")]
    public Task<string> AdjustColorLookup(
        [Description("A bundled look's name, or an absolute path to a .cube or .3dl file")] string look,
        [Description("How much of the look shows, 0 to 100")] double amount = 100,
        [Description(AsLayer)] bool asLayer = false, [Description(TargetLayer)] string? layer = null, int? document = null)
    {
        ColorLattice lattice;
        string source;
        if (Looks.Find(look) is { } bundled) { lattice = bundled; source = look; }
        else if (Path.IsPathRooted(look))
        {
            var path = Path.GetFullPath(look);
            if (!File.Exists(path)) throw new McpException($"There is no file at {path}.");
            try { lattice = ColorLattice.Load(path); }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException) { throw new McpException($"Couldn't read {Path.GetFileName(path)}: {error.Message}"); }
            source = Path.GetFileName(path);
        }
        else throw new McpException($"\"{look}\" is neither a bundled look ({string.Join(", ", Looks.Names)}) nor an absolute path to a .cube or .3dl file.");
        return Adjust(document, layer, asLayer, new ColorLookupAdjustment { Lattice = lattice, Source = source, Amount = Math.Clamp(amount, 0, 100) });
    }

    [McpServerTool(Name = "adjust_invert")]
    [Description("Invert the layer's colors.")]
    public Task<string> AdjustInvert([Description(AsLayer)] bool asLayer = false, [Description(TargetLayer)] string? layer = null, int? document = null) =>
        Adjust(document, layer, asLayer, new InvertAdjustment());

    // ---- Filters ------------------------------------------------------------------------------------------------------

    [McpServerTool(Name = "filter_blur")]
    [Description("Gaussian Blur by a radius in pixels.")]
    public Task<string> FilterBlur([Description("0.1 to 250")] double radius = 8, [Description(TargetLayer)] string? layer = null, int? document = null,
        [Description("Keep the layer's original transparency while blurring its colors")] bool preserveTransparency = false) =>
        Filter(document, layer, new FilterSettings { Kind = FilterKind.GaussianBlur, Radius = Math.Clamp(radius, 0.1, 250), PreserveTransparency = preserveTransparency });

    [McpServerTool(Name = "filter_motion_blur")]
    [Description("Motion Blur over a distance in pixels at an angle in degrees.")]
    public Task<string> FilterMotionBlur([Description("1 to 2000")] double distance = 30, [Description("-180 to 180")] double angle = 0, [Description(TargetLayer)] string? layer = null, int? document = null) =>
        Filter(document, layer, new FilterSettings { Kind = FilterKind.MotionBlur, Radius = Math.Clamp(distance, 1, 2000), Angle = Math.Clamp(angle, -180, 180) });

    [McpServerTool(Name = "filter_add_noise")]
    [Description("Add Noise: amount 0 to 100, even or bell-shaped (gaussian), grey or colored.")]
    public Task<string> FilterAddNoise(double amount = 20, bool gaussian = false, bool monochrome = true, [Description(TargetLayer)] string? layer = null, int? document = null) =>
        Filter(document, layer, new FilterSettings { Kind = FilterKind.AddNoise, Amount = Math.Clamp(amount, 0, 100), Gaussian = gaussian, Monochrome = monochrome });

    [McpServerTool(Name = "filter_sharpen")]
    [Description("Sharpen: strength 0 to 100 over a radius in pixels.")]
    public Task<string> FilterSharpen(double amount = 60, [Description("0.1 to 250")] double radius = 2, [Description(TargetLayer)] string? layer = null, int? document = null) =>
        Filter(document, layer, new FilterSettings { Kind = FilterKind.Sharpen, Amount = Math.Clamp(amount, 0, 100), Radius = Math.Clamp(radius, 0.1, 250) });

    [McpServerTool(Name = "filter_vignette")]
    [Description("Vignette: darkens (or colors) the edges. Amount 0 to 100, midpoint 0 to 100 from the middle outward, roundness -100 (follows the frame) to 100 (a circle), feather 0 to 100, highlights 0 to 100 spares bright pixels. On an empty layer it fills the whole canvas.")]
    public Task<string> FilterVignette(double amount = 35, double midpoint = 50, double roundness = 100, double feather = 60, double highlights = 25, string color = "#000000", [Description(TargetLayer)] string? layer = null, int? document = null) =>
        Filter(document, layer, new FilterSettings
        {
            Kind = FilterKind.Vignette, VignetteAmount = Math.Clamp(amount, 0, 100), VignetteMidpoint = Math.Clamp(midpoint, 0, 100), VignetteRoundness = Math.Clamp(roundness, -100, 100),
            VignetteFeather = Math.Clamp(feather, 0, 100), VignetteHighlights = Math.Clamp(highlights, 0, 100), VignetteColor = (uint)ParseColor(color)
        });

    [McpServerTool(Name = "filter_bloom")]
    [Description("Bloom / Glow: makes bright areas glow. Amount 0 to 100 and a blur radius in pixels.")]
    public Task<string> FilterBloom(double amount = 40, double radius = 24, [Description(TargetLayer)] string? layer = null, int? document = null) =>
        Filter(document, layer, new FilterSettings { Kind = FilterKind.BloomGlow, BloomAmount = Math.Clamp(amount, 0, 100), BloomRadius = Math.Clamp(radius, 0.1, 250) });

    [McpServerTool(Name = "filter_tonal_contrast")]
    [Description("Tonal Contrast: local contrast. Amount 0 to 100, detail radius in pixels, and how much the shadows, midtones and highlights get, -100 to 100.")]
    public Task<string> FilterTonalContrast(double amount = 50, double radius = 16, double shadows = 40, double midtones = 60, double highlights = 30, [Description(TargetLayer)] string? layer = null, int? document = null) =>
        Filter(document, layer, new FilterSettings
        {
            Kind = FilterKind.TonalContrast, TonalAmount = Math.Clamp(amount, 0, 100), TonalRadius = Math.Clamp(radius, 0.1, 250),
            TonalShadows = Math.Clamp(shadows, -100, 100), TonalMidtones = Math.Clamp(midtones, -100, 100), TonalHighlights = Math.Clamp(highlights, -100, 100)
        });

    [McpServerTool(Name = "filter_lens_correction")]
    [Description("Lens Correction: distortion from -100 (pincushion) to 100 (corrects barrel distortion).")]
    public Task<string> FilterLensCorrection(double distortion = 0, [Description(TargetLayer)] string? layer = null, int? document = null) =>
        Filter(document, layer, new FilterSettings { Kind = FilterKind.LensCorrection, Distortion = Math.Clamp(distortion, -100, 100) });

    [McpServerTool(Name = "filter_remove_background")]
    [Description("Remove Background. With detect 'any' or 'person' a model run on this machine finds the subject and the layer gets a mask hiding everything else, which can be painted on afterwards; with 'plain' the near-uniform backdrop connected to the layer's edges is erased, and tolerance 0 to 100 says how different a pixel may be from it and still go. Left out, detect follows the choice in Composa's Object Selection options. A model that is not available here falls back to 'plain'.")]
    public Task<string> FilterRemoveBackground(double tolerance = 20, [Description("any, person or plain")] string? detect = null, [Description(TargetLayer)] string? layer = null, int? document = null) => OnUi(async () =>
    {
        var s = Editable(document);
        var target = Target(s, layer);
        if (!s.CanEditPixels) throw new McpException($"\"{target.Name}\" is a {Kind(target)} layer, so a filter cannot change its pixels. Rasterize it first.");
        var choice = ParseDetect(detect) ?? s.Detect;
        var settings = new FilterSettings { Kind = FilterKind.RemoveBackground, Amount = Math.Clamp(tolerance, 0, 100), Detect = choice };
        if (!await s.ApplyRemoveBackgroundAsync(settings)) throw new McpException($"The model found no subject in \"{target.Name}\", so nothing changed.");
        return s.IsEditingMask || SubjectFinder.Resolve(choice) == SubjectDetect.Backdrop
            ? $"Removed the plain backdrop of \"{target.Name}\"." + (SubjectFinder.FallbackReason(choice) is { } reason ? " " + reason : "")
            : $"Masked \"{target.Name}\" to the {SubjectFinder.DisplayName(choice).ToLowerInvariant()} the model found; the mask can be painted on.";
    });

    [McpServerTool(Name = "filter_painterly")]
    [Description("Painterly: repaints the layer in brush strokes that follow the picture's edges, the largest brush first and each smaller one only where the picture still differs, so a photo becomes a painting that is still recognizably the same picture. Style impressionist (faithful), expressionist (long bending strokes, colors drift), colorist_wash (thin overlapping washes) or pointillist (dots). brushSize is the largest brush's diameter in pixels, 0 fits it to the picture; passes is how many brushes, each half the size, 1 to 4; detail 0 to 100 says how closely to follow the picture. Gaps between strokes stay transparent, so a paper-colored layer below gives a painting on paper. The same seed paints the same strokes; 0 picks one.")]
    public Task<string> FilterPainterly(string style = "impressionist", double brushSize = 0, int passes = 3, double detail = 50, int seed = 0, [Description(TargetLayer)] string? layer = null, int? document = null)
    {
        var key = style.Trim().Replace("_", "").Replace(" ", "");
        if (!Enum.TryParse<PainterlyStyle>(key, true, out var painterlyStyle)) throw new McpException("style is impressionist, expressionist, colorist_wash or pointillist.");
        return Filter(document, layer, new FilterSettings
        {
            Kind = FilterKind.Painterly, Seed = seed == 0 ? (uint)Random.Shared.Next(1, int.MaxValue) : (uint)seed,
            Painterly = new PainterlySettings { Style = painterlyStyle, BrushSize = brushSize, Passes = passes, Detail = detail }.Normalized()
        });
    }

    [McpServerTool(Name = "filter_dither")]
    [Description("Dither: turns the layer into dithered pixels, from the classic Mac's Atkinson look to Bayer grids, halftone dots, lines and diamonds, old Mac fill patterns and ASCII drawn as readable text. Colors can be black and white, two colors you pick, or the image's own.")]
    public Task<string> FilterDither(
        [Description("atkinson, floyd_steinberg, bayer2, bayer4, bayer8, halftone_dots, halftone_lines, halftone_diamonds, mac_patterns or ascii")] string style = "atkinson",
        [Description("Each dithered pixel covers this many layer pixels on a side, 1 to 32, for chunky old-screen pixels; ascii ignores it")] int pixelSize = 2,
        [Description("square, or dot for round pixels like an LED screen")] string pixelShape = "square",
        [Description("Halftone cell size in dithered pixels, 4 to 64")] int cellSize = 8,
        [Description("Halftone screen angle in degrees, -90 to 90")] double angle = 45,
        [Description("ASCII line height in pixels, 6 to 64")] int textSize = 14,
        [Description("ASCII characters to draw with, in any order; each spot gets the one whose ink best matches its tone")] string characters = DitherSettings.DefaultCharacters,
        [Description("Tones per channel for diffusion and Bayer styles, 2 to 8; 2 is pure 1-bit")] int tones = 2,
        [Description("How much of each pixel's error passes to its neighbors in the diffusion styles, 0 to 100")] double diffusion = 100,
        [Description("More ink (positive) or less before dithering, -100 to 100")] double density = 0,
        [Description("Flatter or punchier before dithering, -100 to 100")] double contrast = 0,
        [Description("black_white, two_colors or original")] string colors = "black_white",
        [Description("The dark color for two_colors, as #RRGGBB or a name")] string dark = "#000000",
        [Description("The light color for two_colors")] string light = "#FFFFFF",
        [Description("Halftone, pattern and ASCII marks stand for the light tones, drawn in the light color on the dark, like a glowing screen")] bool lightOnDark = true,
        [Description(TargetLayer)] string? layer = null, int? document = null)
    {
        if (!DitherSettings.TryParseStyle(style, out var ditherStyle)) throw new McpException("style is atkinson, floyd_steinberg, bayer2, bayer4, bayer8, halftone_dots, halftone_lines, halftone_diamonds, mac_patterns or ascii.");
        var shape = pixelShape.Trim().ToLowerInvariant() switch { "square" => DitherPixelShape.Square, "dot" or "dots" or "round" => DitherPixelShape.Dot, _ => throw new McpException("pixelShape is square or dot.") };
        var palette = colors.Trim().ToLowerInvariant().Replace("_", "").Replace(" ", "").Replace("&", "") switch
        {
            "blackwhite" or "blackandwhite" or "bw" => DitherColors.BlackWhite,
            "twocolors" or "two" => DitherColors.TwoColors,
            "original" => DitherColors.Original,
            _ => throw new McpException("colors is black_white, two_colors or original.")
        };
        return Filter(document, layer, new FilterSettings
        {
            Kind = FilterKind.Dither,
            Dither = new DitherSettings
            {
                Style = ditherStyle, PixelSize = pixelSize, PixelShape = shape, CellSize = cellSize, Angle = angle, TextSize = textSize, Characters = characters,
                Levels = tones, Diffusion = diffusion, Density = density, Contrast = contrast, Colors = palette,
                Dark = (uint)ParseColor(dark), Light = (uint)ParseColor(light), LightOnDark = lightOnDark
            }.Normalized()
        });
    }

    // ---- Shared -------------------------------------------------------------------------------------------------------

    private static int Channel(string channel) => channel.Trim().ToLowerInvariant() switch
    {
        "rgb" or "master" or "all" => 0, "red" or "r" => 1, "green" or "g" => 2, "blue" or "b" => 3,
        _ => throw new McpException("channel is rgb, red, green or blue.")
    };

    private Task<string> Adjust(int? document, string? layer, bool asLayer, Adjustment adjustment) => OnUi(() =>
    {
        var s = Editable(document);
        var target = Target(s, layer);
        if (asLayer)
        {
            // A look names its layer, as the dialog does; the rename stays inside the layer's own step.
            var look = adjustment as ColorLookupAdjustment;
            var added = s.AddAdjustmentLayer(adjustment, commit: look is not { Source.Length: > 0 });
            if (look is { Source.Length: > 0 }) { added.Name = look.Source; s.Commit(); s.NotifyLayersChanged(); }
            return $"Added adjustment layer \"{added.Name}\" above \"{target.Name}\", now active.";
        }
        if (!s.CanEditPixels)
            throw new McpException($"\"{target.Name}\" is a {Kind(target)} layer, so its pixels cannot be adjusted in place. Set asLayer for an adjustment layer above it, or rasterize it first.");
        s.Adjust(adjustment);
        return $"Applied {adjustment.DisplayName} to \"{target.Name}\".";
    });

    private Task<string> Filter(int? document, string? layer, FilterSettings settings) => OnUi(async () =>
    {
        var s = Editable(document);
        var target = Target(s, layer);
        if (!s.CanEditPixels) throw new McpException($"\"{target.Name}\" is a {Kind(target)} layer, so a filter cannot change its pixels. Rasterize it first.");
        if (!s.BeginFilter(settings.Kind)) throw new McpException("The layer cannot be filtered.");
        try { await s.PreviewFilterAsync(settings); s.CommitPreview(); }
        catch { s.CancelPreview(); throw; }
        window.RememberMcpFilter(settings);
        return $"Applied {FilterSettings.DisplayName(settings.Kind)} to \"{target.Name}\".";
    });

    /// <summary>The named layer, made active as the menus need it to be, or the active layer.</summary>
    private static Layer Target(EditorSession s, string? layer)
    {
        if (layer != null) s.SelectLayer(Find(s, layer).Id);
        return s.ActiveLayer ?? throw new McpException("No layer is active.");
    }
}
