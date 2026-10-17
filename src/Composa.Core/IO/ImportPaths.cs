namespace Composa.IO;

public static class ImportPaths
{
    /// <summary>Folders contribute their supported image files in deterministic name order; subfolders are not traversed.</summary>
    public static IEnumerable<string> Expand(IEnumerable<string> paths)
    {
        var extensions = ImageFiles.ImportExtensions.Concat(RawImporter.Extensions).ToArray();
        foreach (var path in paths)
        {
            if (!Directory.Exists(path)) { yield return path; continue; }
            foreach (var file in Directory.EnumerateFiles(path).Order(StringComparer.OrdinalIgnoreCase))
                if (extensions.Any(extension => file.EndsWith(extension, StringComparison.OrdinalIgnoreCase))) yield return file;
        }
    }
}
