using Avalonia.Platform.Storage;
using Composa.App.Dialogs;
using Composa.IO;

namespace Composa.App;

public sealed partial class MainWindow
{
    private async Task ExportAs()
    {
        if (session is not { } target) return;
        using var source = target.Flatten();
        if (await ExportDialog.Show(this, source, EnsureColorProof, settings.CmykProfileName) is not { } options) return;
        var proof = options.Cmyk ? await EnsureColorProof() : null;
        if (options.Cmyk && proof == null) return;
        var extension = options.Format switch { ExportFormat.Jpeg => "jpg", ExportFormat.Tiff or ExportFormat.Tiff16 => "tiff", ExportFormat.Png16 => "png", _ => options.Format.ToString().ToLowerInvariant() };
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = L10n.Text("Export As"), SuggestedFileName = target.Title + "." + extension, DefaultExtension = extension,
            FileTypeChoices = [new FilePickerFileType(options.Format.ToString()) { Patterns = ["*." + extension] }]
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        try
        {
            await Busy(() => Task.Run(() =>
            {
                using var bitmap = ExportDialog.Resize(source, options.Width, options.Height);
                if (proof == null) ImageFiles.Save(bitmap, path, options.Format, options.Quality, target.Document.Resolution);
                else
                {
                    var bytes = proof.Export(bitmap, options.Format, options.Quality, target.Document.Resolution);
                    var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
                    try { File.WriteAllBytes(temporary, bytes); File.Move(temporary, path, overwrite: true); }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                }
                return true;
            }));
        }
        catch (Exception error) { ReportFailure(error); }
    }
}
