using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Composa.Model;
using Composa.Rendering;
using SkiaSharp;

namespace Composa.IO;

/// <summary>16-bit RGBA PNG output. The encoder reads source samples directly, never through GetPixel's byte conversion.</summary>
public static class Png16
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];
    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(value =>
    {
        var crc = (uint)value;
        for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : 0xEDB88320u);
        return crc;
    }).ToArray();

    internal static bool IsSixteenBit(Stream stream)
    {
        if (!stream.CanSeek) return false;
        var position = stream.Position;
        Span<byte> header = stackalloc byte[26];
        var read = 0;
        try
        {
            while (read < header.Length)
            {
                var count = stream.Read(header[read..]);
                if (count == 0) break;
                read += count;
            }
            return read == header.Length && header[..8].SequenceEqual(Signature) && header[24] == 16;
        }
        finally { stream.Position = position; }
    }

    /// <summary>Decode the standard 16-bit PNG color layouts, including Adam7, to exact integer samples.
    /// Skia's public codec API cannot decode directly to RGBA16-unorm, so going through half floats would lose bits.</summary>
    internal static unsafe SKBitmap Decode(Stream stream, SKColorSpace? colorSpace)
    {
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        if (!reader.ReadBytes(8).AsSpan().SequenceEqual(Signature)) throw new InvalidDataException("Invalid PNG signature.");
        using var imageData = new MemoryStream();
        int width = 0, height = 0, channels = 0, interlace = 0, colorType = 0;
        byte[]? transparent = null;
        var finished = false;
        while (!finished)
        {
            var lengthBytes = reader.ReadBytes(4);
            if (lengthBytes.Length != 4) throw new InvalidDataException("Truncated PNG chunk.");
            var length = BinaryPrimitives.ReadInt32BigEndian(lengthBytes);
            var name = reader.ReadBytes(4);
            if (name.Length != 4 || length < 0 || length > stream.Length - stream.Position - 4) throw new InvalidDataException("Invalid PNG chunk length.");
            var type = Encoding.ASCII.GetString(name);
            var relevant = type is "IHDR" or "IDAT" or "tRNS" or "IEND";
            var limit = width == 0 ? 1024L : (long)width * height * 16 + 1_048_576;
            if (relevant && length > limit) throw new InvalidDataException("PNG chunk exceeds the image budget.");
            if (!relevant) { stream.Seek(length + 4L, SeekOrigin.Current); continue; }
            var bytes = reader.ReadBytes(length);
            var crcBytes = reader.ReadBytes(4);
            if (bytes.Length != length || crcBytes.Length != 4 || BinaryPrimitives.ReadUInt32BigEndian(crcBytes) != Crc(name, bytes)) throw new InvalidDataException("Damaged PNG chunk.");
            switch (type)
            {
                case "IHDR":
                    if (width != 0 || bytes.Length != 13 || bytes[8] != 16 || bytes[10] != 0 || bytes[11] != 0 || bytes[12] > 1) throw new InvalidDataException("Invalid 16-bit PNG header.");
                    width = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(0, 4)); height = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(4, 4));
                    if (!DocumentLimits.FitsSurface(width, height)) throw new InvalidDataException("PNG dimensions exceed the image budget.");
                    colorType = bytes[9]; interlace = bytes[12];
                    channels = colorType switch { 0 => 1, 2 => 3, 4 => 2, 6 => 4, _ => throw new InvalidDataException("Unsupported 16-bit PNG color type.") };
                    break;
                case "IDAT":
                    if (width == 0 || imageData.Length + bytes.Length > limit) throw new InvalidDataException("Invalid PNG image data.");
                    imageData.Write(bytes); break;
                case "tRNS":
                    if (width == 0 || colorType is not (0 or 2) || bytes.Length != (colorType == 0 ? 2 : 6)) throw new InvalidDataException("Invalid PNG transparency.");
                    transparent = bytes; break;
                case "IEND":
                    if (bytes.Length != 0) throw new InvalidDataException("Invalid PNG end chunk.");
                    finished = true; break;
            }
        }
        if (width == 0 || imageData.Length == 0) throw new InvalidDataException("PNG contains no image.");
        imageData.Position = 0;
        using var decoded = new ZLibStream(imageData, CompressionMode.Decompress);
        var result = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba16161616, SKAlphaType.Premul, colorSpace));
        try
        {
            int[] startsX = interlace == 0 ? [0] : [0, 4, 0, 2, 0, 1, 0], startsY = interlace == 0 ? [0] : [0, 0, 4, 0, 2, 0, 1];
            int[] stepsX = interlace == 0 ? [1] : [8, 8, 4, 4, 2, 2, 1], stepsY = interlace == 0 ? [1] : [8, 8, 8, 4, 4, 2, 2];
            for (var pass = 0; pass < startsX.Length; pass++)
            {
                int columns = Math.Max(0, (width - startsX[pass] + stepsX[pass] - 1) / stepsX[pass]), rows = Math.Max(0, (height - startsY[pass] + stepsY[pass] - 1) / stepsY[pass]);
                if (columns == 0 || rows == 0) continue;
                var bpp = channels * 2;
                var previous = new byte[checked(columns * bpp)]; var current = new byte[previous.Length];
                for (var y = 0; y < rows; y++)
                {
                    var filter = decoded.ReadByte();
                    if (filter is < 0 or > 4) throw new InvalidDataException("Invalid PNG row filter.");
                    decoded.ReadExactly(current);
                    for (var i = 0; i < current.Length; i++)
                    {
                        int left = i < bpp ? 0 : current[i - bpp], above = previous[i], upperLeft = i < bpp ? 0 : previous[i - bpp];
                        var predictor = filter switch { 0 => 0, 1 => left, 2 => above, 3 => (left + above) / 2, _ => Paeth(left, above, upperLeft) };
                        current[i] = unchecked((byte)(current[i] + predictor));
                    }
                    var row = (ushort*)((byte*)result.GetPixels() + (long)(startsY[pass] + y * stepsY[pass]) * result.RowBytes);
                    for (var x = 0; x < columns; x++)
                    {
                        var samples = current.AsSpan(x * bpp, bpp);
                        ushort r = BinaryPrimitives.ReadUInt16BigEndian(samples), g = r, b = r, alpha = 65535;
                        if (colorType is 2 or 6) { g = BinaryPrimitives.ReadUInt16BigEndian(samples[2..]); b = BinaryPrimitives.ReadUInt16BigEndian(samples[4..]); }
                        if (colorType is 4 or 6) alpha = BinaryPrimitives.ReadUInt16BigEndian(samples[^2..]);
                        else if (transparent != null && (colorType == 0 ? r == BinaryPrimitives.ReadUInt16BigEndian(transparent) : samples.SequenceEqual(transparent))) alpha = 0;
                        var target = row + (startsX[pass] + x * stepsX[pass]) * 4;
                        target[0] = (ushort)(((uint)r * alpha + 32767) / 65535); target[1] = (ushort)(((uint)g * alpha + 32767) / 65535);
                        target[2] = (ushort)(((uint)b * alpha + 32767) / 65535); target[3] = alpha;
                    }
                    (current, previous) = (previous, current);
                }
            }
            if (decoded.ReadByte() != -1) throw new InvalidDataException("PNG has excess decompressed data.");
            return result;
        }
        catch { result.Dispose(); throw; }
    }

    private static int Paeth(int left, int above, int upperLeft)
    {
        var p = left + above - upperLeft;
        var a = Math.Abs(p - left); var b = Math.Abs(p - above); var c = Math.Abs(p - upperLeft);
        return a <= b && a <= c ? left : b <= c ? above : upperLeft;
    }

    public static byte[] Encode(SKBitmap bitmap, double resolution = 72)
    {
        var source = DeepPixels.Source(bitmap) ?? bitmap;
        using var output = new MemoryStream();
        output.Write(Signature);
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), source.Width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), source.Height);
        header[8] = 16; header[9] = 6; // RGBA, standard compression/filtering, no interlace.
        Chunk(output, "IHDR", header);
        Chunk(output, "sRGB", [0]);
        var physical = new byte[9];
        var perMeter = (uint)Math.Round((double.IsFinite(resolution) ? Math.Clamp(resolution, 1, 9600) : 72) / .0254);
        BinaryPrimitives.WriteUInt32BigEndian(physical.AsSpan(0, 4), perMeter);
        BinaryPrimitives.WriteUInt32BigEndian(physical.AsSpan(4, 4), perMeter); physical[8] = 1;
        Chunk(output, "pHYs", physical);
        using var packed = new MemoryStream();
        using (var compressed = new ZLibStream(packed, CompressionLevel.Optimal, leaveOpen: true))
        {
            var row = new byte[checked(source.Width * 8 + 1)];
            for (var y = 0; y < source.Height; y++)
            {
                for (var x = 0; x < source.Width; x++)
                {
                    var p = DeepPixels.Read(source, x, y);
                    var alpha = Math.Clamp(p.Alpha, 0, 1);
                    ushort Straight(float value) => alpha <= 0 ? (ushort)0 : (ushort)MathF.Round(Math.Clamp(value / alpha, 0, 1) * 65535);
                    var at = row.AsSpan(1 + x * 8, 8);
                    BinaryPrimitives.WriteUInt16BigEndian(at[..2], Straight(p.Red));
                    BinaryPrimitives.WriteUInt16BigEndian(at[2..4], Straight(p.Green));
                    BinaryPrimitives.WriteUInt16BigEndian(at[4..6], Straight(p.Blue));
                    BinaryPrimitives.WriteUInt16BigEndian(at[6..8], (ushort)MathF.Round(alpha * 65535));
                }
                compressed.Write(row);
            }
        }
        // Keep chunks reasonably sized without a second copy of the compressed image.
        var bytes = packed.GetBuffer();
        for (var offset = 0; offset < packed.Length;)
        {
            var length = (int)Math.Min(65536, packed.Length - offset);
            Chunk(output, "IDAT", bytes.AsSpan(offset, length)); offset += length;
        }
        Chunk(output, "IEND", []);
        return output.ToArray();
    }

    private static void Chunk(Stream stream, string type, ReadOnlySpan<byte> bytes)
    {
        Span<byte> integer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(integer, bytes.Length); stream.Write(integer);
        var name = Encoding.ASCII.GetBytes(type); stream.Write(name); stream.Write(bytes);
        BinaryPrimitives.WriteUInt32BigEndian(integer, Crc(name, bytes)); stream.Write(integer);
    }

    private static uint Crc(ReadOnlySpan<byte> name, ReadOnlySpan<byte> bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in name) crc = CrcTable[(crc ^ value) & 255] ^ (crc >> 8);
        foreach (var value in bytes) crc = CrcTable[(crc ^ value) & 255] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }
}
