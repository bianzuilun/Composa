using Avalonia.Platform.Storage;
using Composa.IO;

namespace Composa.App;

public sealed partial class MainWindow
{
    private CmykProof? colorProof;

    private async Task<CmykProof?> EnsureColorProof()
    {
        if (colorProof != null) return colorProof;
        try
        {
            var rgb = global::ImageMagick.ColorProfiles.SRGB.ToByteArray();
            var cmyk = settings.CmykProfile ?? global::ImageMagick.ColorProfiles.CoatedFOGRA39.ToByteArray();
            var name = settings.CmykProfileName;
            colorProof = await Busy(() => Task.Run(() => new CmykProof(name, rgb, cmyk)));
            canvas.SetColorProof(colorProof);
            return colorProof;
        }
        catch (Exception error) { ReportFailure(error); return null; }
    }

    private async Task ChooseCmykProfile()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L10n.Text("CMYK Profile"), AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("ICC profile") { Patterns = ["*.icc", "*.icm"] }]
        });
        if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
        try
        {
            var length = new FileInfo(path).Length;
            if (length is < 128 or > 16 * 1024 * 1024) throw new InvalidDataException(L10n.Text("Choose a valid CMYK ICC profile."));
            var profile = await File.ReadAllBytesAsync(path); var name = Path.GetFileNameWithoutExtension(path);
            var proof = await Busy(() => Task.Run(() => new CmykProof(name, global::ImageMagick.ColorProfiles.SRGB.ToByteArray(), profile)));
            settings.CmykProfile = profile; settings.CmykProfileName = name; settings.Save();
            colorProof = proof; canvas.SetColorProof(proof);
        }
        catch (Exception error) { ReportFailure(error); }
    }

    private async Task ToggleProof(bool gamut = false)
    {
        if (session is not { } target || await EnsureColorProof() == null || target != session) return;
        target.View = gamut ? target.View with { GamutWarning = !target.View.GamutWarning } : target.View with { ProofColors = !target.View.ProofColors };
        target.InvalidateAll(); RememberToolSettings();
    }
}
