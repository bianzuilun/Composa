using System.ComponentModel;
using Composa.Editing;
using Composa.Filters;
using Composa.IO;
using Composa.IO.Psd;
using Composa.IO.Xcf;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Composa.App.Mcp;

/// <summary>
/// Files: opening, saving and exporting. These go through the window's own file commands with the dialogs cut out, so
/// a save from an agent is written in the background exactly as Ctrl+S is and close and quit wait for it. A file the
/// document did not come from is never overwritten unless the agent says so.
/// </summary>
public sealed partial class ComposaTools
{
    [McpServerTool(Name = "open_document")]
    [Description("Opens a project or image as a new tab, including native 16-bit/HDR PNG/TIFF/EXR/HDR, HEIF/HIF, Photoshop PSD/PSB, GIMP XCF and camera RAW. Photoshop/GIMP conversion reports require allowConversions=true; inspect_import lists them without editing. RAW uses rawSettings and optional cameraRaw, with no modal dialog. Existing projects are activated.")]
    public async Task<string> OpenDocument([Description("Absolute path of the file")] string path, bool allowConversions = false, RawDevelopSettings? rawSettings = null, CameraRawSettings? cameraRaw = null)
    {
        path = Absolute(path);
        if (!File.Exists(path)) throw new McpException($"There is no file at {path}.");
        if (PsdImport.IsPsd(path) || XcfImport.IsXcf(path) || RawImporter.IsRaw(path))
            return await OpenSpecial(path, allowConversions, rawSettings, cameraRaw);
        return await OnUi(async () =>
        {
            if (window.IsDragging) throw new McpException("The person is dragging on the canvas; try again in a moment.");
            var before = window.Sessions.Count;
            EditorSession s;
            try { s = await window.OpenPath(path) ?? throw new McpException($"{Path.GetFileName(path)} was not opened."); }
            catch (Exception error) when (error is not McpException) { throw new McpException($"Couldn't open {Path.GetFileName(path)}: {error.Message}"); }
            var number = window.Sessions.ToList().IndexOf(s) + 1;
            return window.Sessions.Count == before
                ? $"Document {number} \"{s.Title}\" was already open, now active."
                : $"Opened document {number}: \"{s.Title}\" {s.Document.Width}×{s.Document.Height} px, {s.Document.AllLayers().Count()} layers, now active.";
        });
    }

    [McpServerTool(Name = "save_document")]
    [Description("Saves the document as a Composa project (.cmps) with all its layers, in the background as Ctrl+S does. Without a path it saves to the file the document came from.")]
    public Task<string> SaveDocument(
        [Description("Absolute path ending in .cmps; leave it out to save to the document's own file")] string? path = null,
        [Description("Replace a file that exists at a new path; the document's own file is always replaced")] bool overwrite = false,
        int? document = null) => OnUi(async () =>
    {
        var s = Editable(document);                             // Text being typed is committed first, so it is in what gets saved.
        if (path == null)
        {
            path = s.FilePath ?? throw new McpException($"\"{s.Title}\" has never been saved; give a path.");
        }
        else
        {
            path = Absolute(path);
            if (!path.EndsWith(ProjectFile.Extension, StringComparison.OrdinalIgnoreCase))
                throw new McpException($"A project file ends in {ProjectFile.Extension}; export_image writes an image file.");
            Fresh(path, s.FilePath, overwrite);
        }
        if (await window.SaveTo(s, path) is { } error) throw new McpException($"Couldn't save {Path.GetFileName(path)}: {error.Message}");
        return $"Saved \"{s.Title}\" to {path}.";
    });

    [McpServerTool(Name = "export_image")]
    [Description("Exports PNG/JPEG/WebP/TIFF/PDF/EXR, optionally resized without changing the document. bitDepth=16 writes native PNG/TIFF; EXR retains floating HDR. colorMode=cmyk supports 8-bit JPEG/TIFF/PDF with the selected ICC profile. Omitted dimensions keep the canvas; one dimension keeps its aspect ratio. save_document keeps layers.")]
    public async Task<string> ExportImage(
        [Description("Absolute path ending in .png, .jpg, .webp, .tif, .tiff, .pdf or .exr")] string path,
        [Description("JPEG and WebP quality from 1 to 100")] int quality = 90,
        [Description("Replace a file that exists at the path")] bool overwrite = false,
        int? document = null, int bitDepth = 8, string colorMode = "rgb", int? width = null, int? height = null, string? profilePath = null)
    {
        path = Absolute(path);
        var format = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => ExportFormat.Png,
            ".jpg" or ".jpeg" => ExportFormat.Jpeg,
            ".webp" => ExportFormat.Webp,
            ".tif" or ".tiff" => ExportFormat.Tiff, ".pdf" => ExportFormat.Pdf, ".exr" => ExportFormat.Exr,
            _ => throw new McpException("The path must end in .png, .jpg, .webp, .tif, .tiff, .pdf or .exr; save_document writes a project file.")
        };
        if (quality is < 1 or > 100) throw new McpException("quality is from 1 to 100.");
        Fresh(path, null, overwrite);
        if (bitDepth is not (8 or 16)) throw new McpException("bitDepth is 8 or 16; EXR always uses floating-point HDR.");
        var cmyk = colorMode.Trim().ToLowerInvariant() switch { "rgb" => false, "cmyk" => true, _ => throw new McpException("colorMode is rgb or cmyk.") };
        if (cmyk && (bitDepth != 8 || format is not (ExportFormat.Jpeg or ExportFormat.Tiff or ExportFormat.Pdf))) throw new McpException("CMYK supports 8-bit JPEG, TIFF or PDF.");
        if (bitDepth == 16) format = format switch { ExportFormat.Png => ExportFormat.Png16, ExportFormat.Tiff => ExportFormat.Tiff16, ExportFormat.Exr => format, _ => throw new McpException("16-bit export supports PNG or TIFF; EXR is floating point.") };
        var profile = profilePath == null ? null : await ReadProfile(profilePath);
        var (title, snapshot, resolution) = await OnUi(() => { var s = Editable(document); return (s.Title, s.Document.Clone(), s.Document.Resolution); });
        var proof = cmyk ? await OnUi(() => window.McpColorProof(profile, profilePath == null ? null : Path.GetFileNameWithoutExtension(profilePath))) : null;
        using var flat = await Task.Run(() => Composa.Rendering.DocumentRenderer.Flatten(snapshot));
        var w = width ?? (height is { } h ? Math.Max(1,(int)Math.Round(flat.Width * (double)h / flat.Height)) : flat.Width);
        var hOut = height ?? (width is { } tw ? Math.Max(1,(int)Math.Round(flat.Height * (double)tw / flat.Width)) : flat.Height);
        CheckSurface(w,hOut);
        try
        {
            await Task.Run(() => {
                using var sized = w == flat.Width && hOut == flat.Height ? Composa.Rendering.Pixels.Clone(flat) : EditorSession.Resample(flat,w,hOut);
                if (proof == null) ImageFiles.Save(sized,path,format,quality,resolution);
                else WriteExport(path,proof.Export(sized,format,quality,resolution));
            });
            return $"Exported \"{title}\" as a {w}×{hOut} px {format.ToString().ToUpperInvariant()} to {path}.";
        }
        catch (Exception error) when (error is not McpException) { throw new McpException($"Couldn't export {Path.GetFileName(path)}: {error.Message}"); }
    }

    [McpServerTool(Name = "export_look")]
    [Description("Bakes the document's visible adjustment layers into a .cube lookup table any editor can load, as File > Export Look does. Only what is a function of a pixel's color goes in: a masked, clipped or grouped layer and grain, noise and blurs are left out and named in the result. The document is untouched.")]
    public async Task<string> ExportLook(
        [Description("Absolute path ending in .cube")] string path,
        [Description("Points per axis: 17, 33 or 65")] int size = 33,
        [Description("Replace a file that exists at the path")] bool overwrite = false,
        int? document = null, CameraRawSettings? cameraRaw = null)
    {
        path = Absolute(path);
        if (!path.EndsWith(".cube", StringComparison.OrdinalIgnoreCase)) throw new McpException("The path must end in .cube.");
        if (!LookBake.Sizes.Contains(size)) throw new McpException($"size is one of {string.Join(", ", LookBake.Sizes)}.");
        Fresh(path, null, overwrite);
        if (cameraRaw != null)
        {
            ValidateGrade(cameraRaw);
            var grade = cameraRaw.Normalized();
            var baked = await Task.Run(() => LookBake.Bake(grade,size,"Camera Raw").ToCube("Camera Raw"));
            await File.WriteAllTextAsync(path,baked);
            return $"Exported Camera Raw look to {path}. Left out spatial/adaptive controls: {string.Join(", ",LookBake.LeftOutNames(grade))}.";
        }
        // The bake reads the layers, so it runs where they are edited; the file is written off that thread.
        var (title, text, names, leftOut) = await OnUi(() =>
        {
            var s = Session(document);
            var (baked, skipped) = LookBake.Survey(s.Document);
            var reasons = skipped.Select(l => $"\"{l.Layer.Name}\" {l.Why}").ToList();
            if (baked.Count == 0)
                throw new McpException(reasons.Count > 0 ? $"None of the adjustment layers can be baked into a look: {string.Join("; ", reasons)}." : "There is no adjustment layer to bake into a look.");
            var baking = string.Join(", ", baked.Select(l => $"\"{l.Name}\""));
            return (s.Title, LookBake.Bake(baked, size, s.Title).ToCube(s.Title, $"Exported from Composa: {string.Join(", ", baked.Select(l => l.Name))}"), baking, reasons);
        });
        try { await Task.Run(() => File.WriteAllText(path, text)); }
        catch (Exception error) when (error is not McpException) { throw new McpException($"Couldn't write {Path.GetFileName(path)}: {error.Message}"); }
        return $"Exported the look of \"{title}\" as a {size}-point .cube to {path}, baked from {names}." + (leftOut.Count > 0 ? $" Left out: {string.Join("; ", leftOut)}." : "");
    }

    private static string Absolute(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path)) throw new McpException("Give an absolute path.");
        return Path.GetFullPath(path);
    }

    /// <summary>Refuses a path that would replace a file the agent did not ask to replace; a document's own file is fair game.</summary>
    private static void Fresh(string path, string? own, bool overwrite)
    {
        if (overwrite || !File.Exists(path)) return;
        if (own != null && string.Equals(Path.GetFullPath(own), path, StringComparison.Ordinal)) return;
        throw new McpException($"There is already a file at {path}; pass overwrite: true to replace it.");
    }
}
