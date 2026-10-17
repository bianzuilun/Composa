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
        if (await ExportDialog.Show(this, source) is not { } options) return;
        var extension = options.Format switch { ExportFormat.Jpeg => "jpg", ExportFormat.Tiff => "tiff", _ => options.Format.ToString().ToLowerInvariant() };
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
                ImageFiles.Save(bitmap, path, options.Format, options.Quality, target.Document.Resolution);
                return true;
            }));
        }
        catch (Exception error) { ReportFailure(error); }
    }
}
