using System.ComponentModel;
using System.Text.Json;
using Composa.Editing;
using Composa.Filters;
using Composa.IO;
using Composa.IO.Psd;
using Composa.IO.Xcf;
using Composa.Model;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SkiaSharp;

namespace Composa.App.Mcp;

public sealed partial class ComposaTools
{
    [McpServerTool(Name = "inspect_import", ReadOnly = true, Idempotent = true)]
    [Description("Inspects an image/Photoshop/GIMP/RAW file without changing documents. Lists conversions before open_document/place_image with allowConversions=true. RAW returns dimensions and available develop controls.")]
    public async Task<string> InspectImport(string path)
    {
        path = Existing(path);
        return await Task.Run(() => {
            if (PsdImport.IsPsd(path)) { var data = PsdImport.Load(path); try { return JsonSerializer.Serialize(new { data.Width, data.Height, conversions = data.Conversions },McpJson); } finally { data.Discard(); } }
            if (XcfImport.IsXcf(path)) { var data = XcfImport.Load(path); try { return JsonSerializer.Serialize(new { data.Width, data.Height, conversions = data.Conversions },McpJson); } finally { data.Discard(); } }
            if (RawImporter.IsRaw(path)) { var raw = RawImporter.Decode(path); return JsonSerializer.Serialize(new { raw.Width, raw.Height, rawSettings = new RawDevelopSettings(), cameraRaw = new CameraRawSettings() },McpJson); }
            using var pixels = SvgImporter.IsSvg(path) ? SvgImporter.Render(path) : ImageFiles.Load(path);
            var source = Composa.Rendering.DeepPixels.Source(pixels);
            return JsonSerializer.Serialize(new { pixels.Width,pixels.Height,precision = source == null ? "8-bit" : Composa.Rendering.DeepPixels.IsHdr(source) ? "HDR float" : "16-bit" },McpJson);
        });
    }

    private async Task<string> OpenSpecial(string path, bool allowConversions, RawDevelopSettings? rawSettings, CameraRawSettings? cameraRaw)
    {
        await OnUi(() => { if (window.IsDragging || window.Session?.IsInteracting == true) throw new McpException("Finish the current canvas interaction first."); return true; });
        EditorSession opened;
        byte[]? cmykProfile = null;
        string report = "";
        if (PsdImport.IsPsd(path)) {
            var data = await Task.Run(() => PsdImport.Load(path));
            try { report = ConversionReport(data.Conversions,allowConversions); cmykProfile = data.CmykProfile; opened = EditorSession.OpenPhotoshop(data,Path.GetFileNameWithoutExtension(path)); }
            catch { data.Discard(); throw; }
        } else if (XcfImport.IsXcf(path)) {
            var data = await Task.Run(() => XcfImport.Load(path));
            try { report = ConversionReport(data.Conversions,allowConversions); opened = EditorSession.OpenGimp(data,Path.GetFileNameWithoutExtension(path)); }
            catch { data.Discard(); throw; }
        } else {
            var pixels = await DecodeRaw(path,rawSettings,cameraRaw);
            var doc = new Document(pixels.Width,pixels.Height); var layer = Layer.Raster(Path.GetFileNameWithoutExtension(path),pixels);
            doc.Layers.Add(layer); doc.SetActive(layer.Id); opened = new EditorSession(doc) { SuggestedName = Path.GetFileNameWithoutExtension(path) };
        }
        return await OnUi(() => {
            if (window.IsDragging || window.Session?.IsInteracting == true) {
                foreach (var layer in opened.Document.AllLayers()) { layer.Pixels?.Dispose(); layer.Mask?.Dispose(); }
                throw new McpException("A canvas interaction started during import; no document was added.");
            }
            window.AddSession(opened); window.Settings.AddRecent(path);
            window.RememberMcpProfile(cmykProfile,Path.GetFileNameWithoutExtension(path)+" ICC");
            return $"Opened document {window.Sessions.Count}: \"{opened.Title}\" {opened.Document.Width}×{opened.Document.Height} px, {opened.Document.AllLayers().Count()} layers, now active." + report;
        });
    }

    private async Task<string> PlaceSpecial(string path, double? x, double? y, bool fit, double scale, int? document, bool allowConversions, RawDevelopSettings? rawSettings, CameraRawSettings? cameraRaw)
    {
        var s = await OnUi(() => Editable(document));
        var budget = await OnUi(() => DocumentLimits.DocumentPixelBudget - s.Document.RasterPixels());
        var center = x is { } cx && y is { } cy ? new SKPoint((float)cx,(float)cy) : (SKPoint?)null;
        if (PsdImport.IsPsd(path)) {
            if (scale != 1) throw new McpException("Place a Photoshop folder at its native size, then transform its individual layers.");
            var data = await Task.Run(() => PsdImport.Load(path,budget)); var consumed = false;
            try {
                var report = ConversionReport(data.Conversions,allowConversions);
                return await OnUi(() => { EnsureImportTarget(s,budget); var added = s.PlacePhotoshop(data,Path.GetFileNameWithoutExtension(path),center); consumed = true; window.RememberMcpProfile(data.CmykProfile,Path.GetFileNameWithoutExtension(path)+" ICC"); return $"Placed Photoshop folder \"{added.Name}\"." + report; });
            } finally { if (!consumed) data.Discard(); }
        }
        if (XcfImport.IsXcf(path)) {
            if (scale != 1) throw new McpException("Place a GIMP folder at its native size, then transform its individual layers.");
            var data = await Task.Run(() => XcfImport.Load(path,budget)); var consumed = false;
            try {
                var report = ConversionReport(data.Conversions,allowConversions);
                return await OnUi(() => { EnsureImportTarget(s,budget); var added = s.PlaceGimp(data,Path.GetFileNameWithoutExtension(path),center); consumed = true; return $"Placed GIMP folder \"{added.Name}\"." + report; });
            } finally { if (!consumed) data.Discard(); }
        }
        var pixels = await DecodeRaw(path,rawSettings,cameraRaw); var used = false;
        try { return await OnUi(() => { EnsureImportTarget(s,budget); if ((long)pixels.Width*pixels.Height > budget) throw new McpException("The RAW frame exceeds the remaining document pixel budget."); var added = s.AddImageLayer(Path.GetFileNameWithoutExtension(path),pixels,center,fit,scale); used = true; return $"Placed RAW layer \"{added.Name}\" at native 16-bit precision."; }); }
        finally { if (!used) pixels.Dispose(); }
    }

    [McpServerTool(Name = "import_images")]
    [Description("Imports files or the supported images directly in folders, sorted by name, as layers. newDocument=true creates a canvas fitted to all imported layers, equivalent to dropping files on the welcome/tab area. Folders are not recursive. Reports each failure explicitly; successful layers are undoable. RAW and optional Camera Raw settings apply to RAW files; Photoshop/GIMP conversions require allowConversions.")]
    public async Task<string> ImportImages(string[] paths, bool newDocument = false, bool allowConversions = false, RawDevelopSettings? rawSettings = null, CameraRawSettings? cameraRaw = null, int? document = null)
    {
        if (paths.Length == 0) throw new McpException("Give at least one path.");
        var expanded = ImportPaths.Expand(paths.Select(Absolute)).ToList();
        if (expanded.Count == 0) throw new McpException("No supported image files were found.");
        var messages = new List<string>(); EditorSession? target = newDocument ? null : await OnUi(() => Editable(document));
        foreach (var path in expanded) {
            try {
                if (target == null) { messages.Add(await OpenDocument(path,allowConversions,rawSettings,cameraRaw)); target = await OnUi(() => window.Session!); }
                else {
                    var number = await OnUi(() => window.Sessions.ToList().IndexOf(target)+1);
                    messages.Add(await PlaceImage(path,fit: !newDocument,document: number,allowConversions: allowConversions,rawSettings: rawSettings,cameraRaw: cameraRaw));
                }
            } catch (Exception error) { messages.Add($"FAILED {Path.GetFileName(path)}: {error.Message}"); }
        }
        if (target != null && newDocument) await OnUi(() => { EnsureImportTarget(target,0); target.RevealAll(); return true; });
        return string.Join("\n",messages);
    }

    private void EnsureImportTarget(EditorSession target,long originalBudget)
    {
        if (!window.Sessions.Contains(target) || window.IsDragging || target.IsInteracting) throw new McpException("The target document closed or started an interaction during import.");
        if (originalBudget > 0 && DocumentLimits.DocumentPixelBudget-target.Document.RasterPixels() < originalBudget) throw new McpException("The document grew during import; retry with its current pixel budget.");
    }

    private static string ConversionReport(IEnumerable<ImportConversion> conversions,bool allow)
    {
        var report = string.Join("; ",conversions.Select(c => $"{c.LayerName}: {c.Message}"));
        if (report.Length > 0 && !allow) throw new McpException("Import needs conversions. Inspect with inspect_import, then set allowConversions=true to accept: " + report);
        return report.Length == 0 ? "" : " Conversions: " + report;
    }

    private static async Task<SKBitmap> DecodeRaw(string path,RawDevelopSettings? settings,CameraRawSettings? grade)
    {
        if (grade != null) ValidateGrade(grade);
        var raw = settings ?? new RawDevelopSettings();
        FiniteRange(raw.Exposure,-3,3,"exposure"); FiniteRange(raw.Temperature,-100,100,"temperature"); FiniteRange(raw.Tint,-100,100,"tint");
        FiniteRange(raw.LuminanceNoise,0,100,"luminanceNoise"); FiniteRange(raw.ColorNoise,0,100,"colorNoise"); FiniteRange(raw.HighlightRecovery,0,100,"highlightRecovery"); FiniteRange(raw.LensDistortion,-100,100,"lensDistortion");
        return await Task.Run(() => {
            var pixels = RawImporter.Decode(path).Develop(raw);
            if (grade == null || grade.IsIdentity) return pixels;
            try { return ImageFilters.Run(pixels,new FilterSettings { Kind = FilterKind.CameraRaw,CameraRaw = grade.Normalized() }).Result; }
            finally { pixels.Dispose(); }
        });
    }

    private static string Existing(string path) { path = Absolute(path); if (!File.Exists(path)) throw new McpException($"There is no file at {path}."); return path; }
    private static async Task<byte[]> ReadProfile(string path)
    {
        path = Existing(path); var size = new FileInfo(path).Length;
        if (size < 128 || size > 16*1024*1024) throw new McpException("ICC profiles must be 128 bytes to 16 MiB.");
        return await File.ReadAllBytesAsync(path);
    }
    private static void WriteExport(string path,byte[] bytes)
    {
        var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try { File.WriteAllBytes(temp,bytes); File.Move(temp,path,overwrite: true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
