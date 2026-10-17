using Composa.Filters;
using Composa.Rendering;
using SkiaSharp;

namespace Composa.IO;

/// <summary>ICC conversion provided by the installed or bundled ImageMagick. PDF returns raw CMYK samples.</summary>
public interface IColorConverter
{
    byte[] ConvertColor(byte[] png, byte[] rgbProfile, byte[] cmykProfile, bool proof, ExportFormat format, int quality, double resolution);
    byte[] DecodeCmyk(byte[] samples, int width, int height, int depth, byte[]? profile);
}

/// <summary>An immutable print profile and its RGB-to-CMYK-to-RGB proof lattice. Never changes document pixels.</summary>
public sealed class CmykProof
{
    private readonly byte[] rgbProfile;
    private readonly byte[] cmykProfile;
    private readonly IColorConverter converter;
    public string Name { get; }
    public ColorLattice Lattice { get; }

    public CmykProof(string name, byte[] rgbProfile, byte[] cmykProfile, IColorConverter? converter = null)
    {
        if (cmykProfile.Length < 128 || cmykProfile.Length > 16 * 1024 * 1024 || !cmykProfile.AsSpan(16, 4).SequenceEqual("CMYK"u8) || !cmykProfile.AsSpan(36, 4).SequenceEqual("acsp"u8))
            throw new InvalidDataException("Choose a valid CMYK ICC profile.");
        Name = name; this.rgbProfile = rgbProfile.ToArray(); this.cmykProfile = cmykProfile.ToArray();
        this.converter = converter ?? ImageMagick.Current as IColorConverter ?? throw new InvalidOperationException("CMYK conversion needs ImageMagick with ICC color management.");
        const int size = 33;
        using var grid = Pixels.NewColor(size * size, size);
        for (var b = 0; b < size; b++) for (var g = 0; g < size; g++) for (var r = 0; r < size; r++)
            grid.SetPixel(g * size + r, b, new SKColor((byte)Math.Round(r * 255.0 / (size - 1)), (byte)Math.Round(g * 255.0 / (size - 1)), (byte)Math.Round(b * 255.0 / (size - 1))));
        var png = Convert(ImageFiles.Encode(grid, ExportFormat.Png), proof: true, ExportFormat.Png, 100, 72);
        using var roundtrip = ImageFiles.Load(new MemoryStream(png));
        if (roundtrip.Width != grid.Width || roundtrip.Height != grid.Height) throw new InvalidDataException("The proof conversion changed the image dimensions.");
        var points = new float[size * size * size * 3]; var at = 0;
        for (var b = 0; b < size; b++) for (var g = 0; g < size; g++) for (var r = 0; r < size; r++)
        {
            var p = roundtrip.GetPixel(g * size + r, b); points[at++] = p.Red / 255f; points[at++] = p.Green / 255f; points[at++] = p.Blue / 255f;
        }
        Lattice = ColorLattice.FromCube(size, points, name);
    }

    private byte[] Convert(byte[] png, bool proof, ExportFormat format, int quality, double resolution) =>
        converter.ConvertColor(png, rgbProfile, cmykProfile, proof, format, quality, resolution);

    public byte[] Export(SKBitmap bitmap, ExportFormat format, int quality = 90, double resolution = 72)
    {
        if (format is not (ExportFormat.Jpeg or ExportFormat.Tiff or ExportFormat.Pdf)) throw new ArgumentException("CMYK export supports JPEG, TIFF and PDF.");
        using var paper = Pixels.NewColor(bitmap.Width, bitmap.Height);
        using (var canvas = new SKCanvas(paper)) { canvas.Clear(SKColors.White); canvas.DrawBitmap(bitmap, 0, 0); }
        var bytes = Convert(ImageFiles.Encode(paper, ExportFormat.Png), false, format, quality, resolution);
        return format == ExportFormat.Pdf ? RasterExport.CmykPdf(bitmap.Width, bitmap.Height, bytes, cmykProfile, resolution) : bytes;
    }

    public unsafe void Apply(SKBitmap bitmap, SKRectI area, bool proofColors, bool gamutWarning)
    {
        area = Composa.Model.Geometry.Intersect(area, new SKRectI(0, 0, bitmap.Width, bitmap.Height));
        Parallel.For(area.Top, area.Bottom, y =>
        {
            var row = (byte*)bitmap.GetPixels() + (long)y * bitmap.RowBytes;
            for (var x = area.Left; x < area.Right; x++)
            {
                var p = row + x * 4; var alpha = p[3]; if (alpha == 0) continue;
                float r = p[0] / (float)alpha, g = p[1] / (float)alpha, b = p[2] / (float)alpha;
                var mapped = Lattice.Sample(r, g, b);
                if (gamutWarning && Math.Max(Math.Abs(r - mapped.R), Math.Max(Math.Abs(g - mapped.G), Math.Abs(b - mapped.B))) > 10 / 255f)
                { p[0] = (byte)alpha; p[1] = 0; p[2] = (byte)alpha; }
                else if (proofColors)
                { p[0] = (byte)Math.Clamp(MathF.Round(mapped.R * alpha), 0, alpha); p[1] = (byte)Math.Clamp(MathF.Round(mapped.G * alpha), 0, alpha); p[2] = (byte)Math.Clamp(MathF.Round(mapped.B * alpha), 0, alpha); }
            }
        });
        Pixels.Invalidate(bitmap);
    }
}
