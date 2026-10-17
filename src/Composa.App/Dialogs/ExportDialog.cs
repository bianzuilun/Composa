using Avalonia.Controls;
using Avalonia.Layout;
using Composa.IO;
using Composa.Model;
using Composa.Rendering;
using SkiaSharp;

namespace Composa.App.Dialogs;

public sealed record ExportOptions(ExportFormat Format, int Width, int Height, int Quality);

public static class ExportDialog
{
    public static async Task<ExportOptions?> Show(Window owner, SKBitmap source)
    {
        var format = ExportFormat.Png; var quality = 90; var width = source.Width; var height = source.Height;
        var locked = true; var updating = false;
        var image = new Image { Source = Ui.ToAvaloniaBitmap(source, 480), MaxWidth = 480, MaxHeight = 320, Stretch = Avalonia.Media.Stretch.Uniform };
        var info = Ui.Label("", Palette.Secondary);
        NumericUpDown widthBox = null!, heightBox = null!;
        void Refresh() { info.Text = $"{width} × {height} px · {format}"; }
        widthBox = Ui.Number(width, 1, DocumentLimits.MaxSide, value =>
        {
            width = (int)value;
            if (!updating && locked) { updating = true; heightBox.Value = (decimal)Math.Clamp(Math.Round(width * (double)source.Height / source.Width), 1, DocumentLimits.MaxSide); updating = false; }
            Refresh();
        }, width: 130);
        heightBox = Ui.Number(height, 1, DocumentLimits.MaxSide, value =>
        {
            height = (int)value;
            if (!updating && locked) { updating = true; widthBox.Value = (decimal)Math.Clamp(Math.Round(height * (double)source.Width / source.Height), 1, DocumentLimits.MaxSide); updating = false; }
            Refresh();
        }, width: 130);
        var formats = Ui.Combo(Enum.GetValues<ExportFormat>(), format, value => value.ToString(), value => { format = value; Refresh(); }, 130);
        var preview = Ui.TextButton(L10n.Text("Preview"), () =>
        {
            if (!DocumentLimits.FitsSurface(width, height)) { info.Text = L10n.Text("The export exceeds the supported pixel budget."); return; }
            using var sized = Resize(source, width, height);
            var encoded = ImageFiles.Encode(sized, format, quality);
            // PDF/TIFF show the same rendered pixels; JPEG/WebP show their actual compression.
            using var decoded = format is ExportFormat.Pdf or ExportFormat.Tiff ? Pixels.Clone(sized) : ImageFiles.Load(new MemoryStream(encoded));
            image.Source = Ui.ToAvaloniaBitmap(decoded, 480);
            info.Text = $"{width} × {height} px · {encoded.Length / 1024.0:N1} KiB";
        });
        var body = Ui.Column(12, image, Ui.Row(10, formats, Ui.Label(L10n.Text("Width")), widthBox, Ui.Label(L10n.Text("Height")), heightBox),
            Ui.Check(L10n.Text("Keep proportions"), locked, value => locked = value),
            Ui.SliderField(L10n.Text("Quality"), quality, 1, 100, value => quality = (int)value, width: 300), Ui.Row(12, preview, info));
        Refresh();
        return await new DialogWindow("Export As", body, "Export").Ask(owner) && DocumentLimits.FitsSurface(width, height)
            ? new ExportOptions(format, width, height, quality) : null;
    }

    public static SKBitmap Resize(SKBitmap source, int width, int height)
    {
        if (!DocumentLimits.FitsSurface(width, height)) throw new ArgumentOutOfRangeException(nameof(width));
        var result = Pixels.NewColor(width, height);
        using var canvas = new SKCanvas(result);
        using var image = SKImage.FromBitmap(source);
        canvas.DrawImage(image, new SKRect(0, 0, width, height), new SKSamplingOptions(SKCubicResampler.Mitchell));
        return result;
    }
}
