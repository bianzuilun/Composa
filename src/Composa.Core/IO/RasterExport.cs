using System.Globalization;
using System.IO.Compression;
using System.Text;
using SkiaSharp;

namespace Composa.IO;

/// <summary>Portable RGB TIFF and one-page PDF export, without an external converter.</summary>
public static class RasterExport
{
    private static double Dpi(double resolution) => double.IsFinite(resolution) ? Math.Clamp(resolution, 1, 9600) : 72;

    public static byte[] Tiff(SKBitmap bitmap, double resolution = 72)
    {
        const ushort fields = 14;
        const int directory = 8;
        var bits = directory + 2 + fields * 12 + 4;
        var xResolution = bits + 8;
        var yResolution = xResolution + 8;
        var pixels = yResolution + 8;
        var length = checked(bitmap.Width * bitmap.Height * 4);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write((byte)'I'); writer.Write((byte)'I'); writer.Write((ushort)42); writer.Write((uint)directory);
        writer.Write(fields);
        void Field(ushort tag, ushort type, uint count, uint value)
        {
            writer.Write(tag); writer.Write(type); writer.Write(count); writer.Write(value);
        }
        Field(256, 4, 1, (uint)bitmap.Width); Field(257, 4, 1, (uint)bitmap.Height);
        Field(258, 3, 4, (uint)bits); Field(259, 3, 1, 1); Field(262, 3, 1, 2);
        Field(273, 4, 1, (uint)pixels); Field(277, 3, 1, 4); Field(278, 4, 1, (uint)bitmap.Height);
        Field(279, 4, 1, (uint)length); Field(282, 5, 1, (uint)xResolution); Field(283, 5, 1, (uint)yResolution);
        Field(284, 3, 1, 1); Field(296, 3, 1, 2); Field(338, 3, 1, 2); // Unassociated alpha.
        writer.Write(0u);
        for (var i = 0; i < 4; i++) writer.Write((ushort)8);
        for (var i = 0; i < 2; i++) { writer.Write((uint)Math.Round(Dpi(resolution) * 1000)); writer.Write(1000u); }
        for (var y = 0; y < bitmap.Height; y++) for (var x = 0; x < bitmap.Width; x++)
        {
            var p = bitmap.GetPixel(x, y);
            writer.Write(p.Red); writer.Write(p.Green); writer.Write(p.Blue); writer.Write(p.Alpha);
        }
        writer.Flush();
        return stream.ToArray();
    }

    public static byte[] Pdf(SKBitmap bitmap, double resolution = 72)
    {
        using var rgb = new MemoryStream(); using var alpha = new MemoryStream();
        using (var colors = new ZLibStream(rgb, CompressionLevel.Optimal, leaveOpen: true))
        using (var coverage = new ZLibStream(alpha, CompressionLevel.Optimal, leaveOpen: true))
        {
            var row = new byte[checked(bitmap.Width * 3)]; var mask = new byte[bitmap.Width];
            for (var y = 0; y < bitmap.Height; y++)
            {
                for (var x = 0; x < bitmap.Width; x++)
                {
                    var p = bitmap.GetPixel(x, y);
                    row[x * 3] = p.Red; row[x * 3 + 1] = p.Green; row[x * 3 + 2] = p.Blue; mask[x] = p.Alpha;
                }
                colors.Write(row); coverage.Write(mask);
            }
        }
        var width = (bitmap.Width * 72 / Dpi(resolution)).ToString("0.#####", CultureInfo.InvariantCulture);
        var height = (bitmap.Height * 72 / Dpi(resolution)).ToString("0.#####", CultureInfo.InvariantCulture);
        using var output = new MemoryStream();
        var offsets = new List<long> { 0 };
        void Text(string text) => output.Write(Encoding.ASCII.GetBytes(text));
        void Object(int id, string body) { offsets.Add(output.Position); Text($"{id} 0 obj\n{body}\nendobj\n"); }
        void Stream(int id, string attributes, byte[] bytes)
        {
            offsets.Add(output.Position); Text($"{id} 0 obj\n<< {attributes} /Length {bytes.Length} >>\nstream\n");
            output.Write(bytes); Text("\nendstream\nendobj\n");
        }
        Text("%PDF-1.4\n");
        Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        Object(3, $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {width} {height}] /Resources << /XObject << /Image 4 0 R >> >> /Contents 6 0 R >>");
        Stream(4, $"/Type /XObject /Subtype /Image /Width {bitmap.Width} /Height {bitmap.Height} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /FlateDecode /SMask 5 0 R", rgb.ToArray());
        Stream(5, $"/Type /XObject /Subtype /Image /Width {bitmap.Width} /Height {bitmap.Height} /ColorSpace /DeviceGray /BitsPerComponent 8 /Filter /FlateDecode", alpha.ToArray());
        Stream(6, "", Encoding.ASCII.GetBytes($"q\n{width} 0 0 {height} 0 0 cm\n/Image Do\nQ\n"));
        var xref = output.Position;
        Text("xref\n0 7\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1)) Text(offset.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
        Text($"trailer\n<< /Size 7 /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return output.ToArray();
    }
}
