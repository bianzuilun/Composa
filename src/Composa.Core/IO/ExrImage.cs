using System.IO.Compression;
using System.Text;
using Composa.Model;
using Composa.Rendering;
using SkiaSharp;

namespace Composa.IO;

/// <summary>OpenEXR scanline RGBA (half/float, uncompressed or ZIP), following the OpenEXR file layout specification.</summary>
public static class ExrImage
{
    private const int Magic = 20000630;
    private sealed record Channel(string Name, int Type);

    public static bool IsExr(Stream input)
    {
        if (!input.CanSeek) return false;
        var position = input.Position; Span<byte> magic = stackalloc byte[4];
        var length = input.Read(magic); input.Position = position;
        return length == 4 && System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(magic) == Magic;
    }

    public static byte[] Encode(SKBitmap preview)
    {
        var source = DeepPixels.Source(preview) ?? preview;
        using var output = new MemoryStream(); using var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);
        writer.Write(Magic); writer.Write(2);
        void Text(string value) { writer.Write(Encoding.ASCII.GetBytes(value)); writer.Write((byte)0); }
        void Attribute(string name, string type, Action<BinaryWriter> data)
        {
            using var buffer = new MemoryStream(); using (var values = new BinaryWriter(buffer, Encoding.UTF8, true)) data(values);
            Text(name); Text(type); writer.Write(checked((int)buffer.Length)); writer.Write(buffer.ToArray());
        }
        Attribute("channels", "chlist", values =>
        {
            foreach (var c in new[] { "A", "B", "G", "R" }) { values.Write(Encoding.ASCII.GetBytes(c)); values.Write((byte)0); values.Write(2); values.Write(0); values.Write(1); values.Write(1); }
            values.Write((byte)0);
        });
        Attribute("compression", "compression", values => values.Write((byte)0));
        foreach (var name in new[] { "dataWindow", "displayWindow" })
            Attribute(name, "box2i", values => { values.Write(0); values.Write(0); values.Write(source.Width - 1); values.Write(source.Height - 1); });
        Attribute("lineOrder", "lineOrder", values => values.Write((byte)0));
        Attribute("pixelAspectRatio", "float", values => values.Write(1f));
        Attribute("screenWindowCenter", "v2f", values => { values.Write(0f); values.Write(0f); });
        Attribute("screenWindowWidth", "float", values => values.Write(1f));
        writer.Write((byte)0);
        var rowBytes = checked(source.Width * 16); var offset = output.Position + source.Height * 8L;
        for (var y = 0; y < source.Height; y++) writer.Write(offset + y * (8L + rowBytes));
        for (var y = 0; y < source.Height; y++)
        {
            writer.Write(y); writer.Write(rowBytes);
            for (var c = 0; c < 4; c++) for (var x = 0; x < source.Width; x++)
            {
                var p = DeepPixels.Read(source, x, y);
                var value = c == 0 ? p.Alpha : Linear(p.Alpha > 0 ? (c == 1 ? p.Blue : c == 2 ? p.Green : p.Red) / p.Alpha : 0) * p.Alpha;
                writer.Write(float.IsFinite(value) ? value : 0);
            }
        }
        return output.ToArray();
    }

    public static SKBitmap Decode(Stream input)
    {
        using var reader = new BinaryReader(input, Encoding.UTF8, leaveOpen: true);
        if (reader.ReadInt32() != Magic) throw new InvalidDataException("This is not an OpenEXR image.");
        var version = reader.ReadInt32();
        if ((version & 0xff) != 2 || (version & ~0x4ff) != 0) throw new InvalidDataException("This OpenEXR layout needs an external HDR decoder (tiled, multipart or deep data).");
        var channels = new List<Channel>(); int minX = 0, minY = 0, width = 0, height = 0, compression = -1;
        while (true)
        {
            var name = Text(reader); if (name.Length == 0) break;
            var type = Text(reader); var length = reader.ReadInt32();
            if (length < 0 || length > 16 * 1024 * 1024) throw new InvalidDataException("Invalid OpenEXR attribute length.");
            var bytes = reader.ReadBytes(length); if (bytes.Length != length) throw new EndOfStreamException();
            using var attr = new BinaryReader(new MemoryStream(bytes));
            if (name == "channels" && type == "chlist")
            {
                while (attr.BaseStream.Position < length)
                {
                    var c = Text(attr); if (c.Length == 0) break;
                    var pixelType = attr.ReadInt32(); attr.ReadInt32(); var xs = attr.ReadInt32(); var ys = attr.ReadInt32();
                    if (pixelType is < 0 or > 2 || xs != 1 || ys != 1 || channels.Count >= 64) throw new InvalidDataException("Subsampled OpenEXR channels need an external HDR decoder.");
                    channels.Add(new Channel(c, pixelType));
                }
            }
            else if (name == "dataWindow" && type == "box2i" && length == 16)
            { minX = attr.ReadInt32(); minY = attr.ReadInt32(); width = checked(attr.ReadInt32() - minX + 1); height = checked(attr.ReadInt32() - minY + 1); }
            else if (name == "compression" && length == 1) compression = bytes[0];
        }
        if (!DocumentLimits.FitsSurface(width, height) || channels.Count == 0) throw new InvalidDataException("Invalid or oversized OpenEXR data window.");
        if (compression is not (0 or 2 or 3)) throw new InvalidDataException("This OpenEXR compression needs an external HDR decoder. Uncompressed, ZIPS and ZIP scanlines are supported directly.");
        var rows = compression == 3 ? 16 : 1;
        var offsets = new long[(height + rows - 1) / rows]; for (var i = 0; i < offsets.Length; i++) offsets[i] = reader.ReadInt64();
        var dataStart = input.Position;
        var rowBytes = checked(width * channels.Sum(c => c.Type == 1 ? 2 : 4));
        var result = DeepPixels.New(width, height, hdr: true); var seen = new bool[offsets.Length];
        try
        {
            foreach (var at in offsets)
            {
                if (at < dataStart || at > input.Length - 8) throw new InvalidDataException("Invalid OpenEXR chunk offset.");
                input.Position = at; var start = checked(reader.ReadInt32() - minY); var size = reader.ReadInt32();
                if (start < 0 || start >= height || start % rows != 0 || seen[start / rows]) throw new InvalidDataException("Invalid OpenEXR scanline block.");
                seen[start / rows] = true;
                var count = Math.Min(rows, height - start); var expected = checked(rowBytes * count);
                if (size < 0 || size > expected || at + 8 + size > input.Length) throw new InvalidDataException("Invalid OpenEXR chunk size.");
                var data = reader.ReadBytes(size);
                if (size != expected)
                {
                    if (compression == 0) throw new InvalidDataException("Truncated OpenEXR scanline.");
                    using var zipped = new ZLibStream(new MemoryStream(data), CompressionMode.Decompress);
                    var predicted = new byte[expected]; zipped.ReadExactly(predicted);
                    for (var i = 1; i < expected; i++) predicted[i] = unchecked((byte)(predicted[i - 1] + predicted[i] - 128));
                    data = new byte[expected]; var half = (expected + 1) / 2;
                    for (var i = 0; i < expected; i++) data[i] = predicted[(i & 1) == 0 ? i / 2 : half + i / 2];
                }
                using var pixels = new BinaryReader(new MemoryStream(data));
                for (var y = start; y < start + count; y++)
                {
                    var red = new float[width]; var green = new float[width]; var blue = new float[width]; var alpha = Enumerable.Repeat(1f, width).ToArray();
                    foreach (var channel in channels)
                    {
                        var target = channel.Name switch { "R" => red, "G" => green, "B" => blue, "A" => alpha, "Y" => red, _ => null };
                        for (var x = 0; x < width; x++)
                        {
                            var value = channel.Type switch { 1 => (float)BitConverter.UInt16BitsToHalf(pixels.ReadUInt16()), 2 => pixels.ReadSingle(), _ => pixels.ReadUInt32() };
                            if (target != null) target[x] = float.IsFinite(value) ? value : 0;
                        }
                    }
                    var monochrome = !channels.Any(c => c.Name == "R") && channels.Any(c => c.Name == "Y");
                    for (var x = 0; x < width; x++)
                    {
                        var a = Math.Clamp(alpha[x], 0, 1); var scale = a > 0 ? 1 / a : 0;
                        DeepPixels.Write(result, x, y, new SKColorF(Encoded(red[x] * scale) * a, Encoded((monochrome ? red[x] : green[x]) * scale) * a,
                            Encoded((monochrome ? red[x] : blue[x]) * scale) * a, a));
                    }
                }
            }
            return DeepPixels.Preview(result);
        }
        catch { result.Dispose(); throw; }
    }

    private static string Text(BinaryReader reader)
    {
        var bytes = new List<byte>();
        for (var i = 0; i < 256; i++) { var b = reader.ReadByte(); if (b == 0) return Encoding.ASCII.GetString(bytes.ToArray()); bytes.Add(b); }
        throw new InvalidDataException("Invalid OpenEXR attribute name.");
    }
    private static float Linear(float value) => value <= .04045f ? value / 12.92f : MathF.Pow((value + .055f) / 1.055f, 2.4f);
    private static float Encoded(float value) => value <= .0031308f ? value * 12.92f : 1.055f * MathF.Pow(value, 1 / 2.4f) - .055f;
}
