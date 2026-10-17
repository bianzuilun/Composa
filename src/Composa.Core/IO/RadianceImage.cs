using System.Globalization;
using System.Text;
using Composa.Model;
using Composa.Rendering;
using SkiaSharp;

namespace Composa.IO;

/// <summary>Radiance RGBE scanlines, flat and run-length encoded, retained as extended-sRGB floats.</summary>
public static class RadianceImage
{
    public static bool Matches(Stream input)
    {
        if (!input.CanSeek) return false;
        var at = input.Position; Span<byte> magic = stackalloc byte[10]; var count = input.Read(magic); input.Position = at;
        return count >= 6 && (magic[..6].SequenceEqual("#?RGBE"u8) || count == 10 && magic.SequenceEqual("#?RADIANCE"u8));
    }

    public static SKBitmap Decode(Stream input)
    {
        using var reader = new BinaryReader(input, Encoding.ASCII, true);
        var magic = Line(reader); if (magic is not ("#?RGBE" or "#?RADIANCE")) throw new InvalidDataException("This is not a Radiance image.");
        var format = false;
        for (var count = 0; ; count++)
        {
            if (count > 4096) throw new InvalidDataException("The Radiance header is too large.");
            var line = Line(reader); if (line.Length == 0) break;
            if (line.StartsWith("FORMAT=", StringComparison.Ordinal))
            { if (line != "FORMAT=32-bit_rle_rgbe") throw new InvalidDataException("Only RGBE Radiance images are supported."); format = true; }
        }
        if (!format) throw new InvalidDataException("The Radiance image has no RGBE format declaration.");
        var resolution = Line(reader).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (resolution.Length != 4 || !Axis(resolution[0]) || !Axis(resolution[2]) || resolution[0][1] == resolution[2][1]
            || !int.TryParse(resolution[1], NumberStyles.None, CultureInfo.InvariantCulture, out var major)
            || !int.TryParse(resolution[3], NumberStyles.None, CultureInfo.InvariantCulture, out var minor)) throw new InvalidDataException("The Radiance image dimensions are invalid.");
        var width = resolution[0][1] == 'X' ? major : minor; var height = resolution[0][1] == 'Y' ? major : minor;
        if (!DocumentLimits.FitsSurface(width, height)) throw new InvalidDataException($"The image exceeds {DocumentLimits.MaxSide:N0} pixels a side or {DocumentLimits.MaxSurfaceMegapixels} megapixels.");
        var bitmap = DeepPixels.New(width, height, hdr: true);
        void Pixel(int index, ReadOnlySpan<byte> rgbe)
        {
            int first = index / minor, second = index % minor;
            int Position(string axis, int value, int size) => axis[1] == 'X' ? axis[0] == '+' ? value : size - 1 - value : axis[0] == '-' ? value : size - 1 - value;
            var a = Position(resolution[0], first, major); var b = Position(resolution[2], second, minor);
            int x = resolution[0][1] == 'X' ? a : b, y = resolution[0][1] == 'Y' ? a : b;
            var scale = rgbe[3] == 0 ? 0 : MathF.ScaleB(1, rgbe[3] - 136);
            static float Encoded(float linear) => linear <= .0031308f ? linear * 12.92f : 1.055f * MathF.Pow(linear, 1 / 2.4f) - .055f;
            DeepPixels.Write(bitmap, x, y, new SKColorF(Encoded(rgbe[0] * scale), Encoded(rgbe[1] * scale), Encoded(rgbe[2] * scale), 1));
        }
        try
        {
            var row = new byte[checked(minor * 4)]; var header = reader.ReadBytes(4);
            if (header.Length != 4) throw new EndOfStreamException();
            bool RunLength(byte[] h) => minor is >= 8 and <= 32767 && h[0] == 2 && h[1] == 2 && (h[2] & 128) == 0 && (h[2] << 8 | h[3]) == minor;
            if (!RunLength(header))
            {
                var total = checked(width * height); var previous = new byte[4]; var at = 0; var shift = 0;
                while (at < total)
                {
                    if (header[0] == 1 && header[1] == 1 && header[2] == 1)
                    {
                        if (at == 0 || shift > 24) throw new InvalidDataException("Invalid Radiance repeat run.");
                        var count = (long)header[3] << shift;
                        if (count <= 0 || count > total - at) throw new InvalidDataException("Invalid Radiance repeat length.");
                        for (var i = 0L; i < count; i++) Pixel(at++, previous);
                        shift += 8;
                    }
                    else { Pixel(at++, header); header.CopyTo(previous, 0); shift = 0; }
                    if (at == total) break;
                    header = reader.ReadBytes(4); if (header.Length != 4) throw new EndOfStreamException();
                }
            }
            else for (var y = 0; y < major; y++)
            {
                if (y > 0) { header = reader.ReadBytes(4); if (header.Length != 4 || !RunLength(header)) throw new InvalidDataException("Invalid Radiance scanline header."); }
                for (var c = 0; c < 4; c++)
                {
                    var x = 0;
                    while (x < minor)
                    {
                        var code = reader.ReadByte(); var count = code > 128 ? code - 128 : code;
                        if (count == 0 || count > minor - x) throw new InvalidDataException("Invalid Radiance scanline run.");
                        if (code > 128) { var value = reader.ReadByte(); for (var i = 0; i < count; i++) row[(x++ * 4) + c] = value; }
                        else for (var i = 0; i < count; i++) row[(x++ * 4) + c] = reader.ReadByte();
                    }
                }
                for (var x = 0; x < minor; x++) Pixel(y * minor + x, row.AsSpan(x * 4, 4));
            }
            return DeepPixels.Preview(bitmap);
        }
        catch { bitmap.Dispose(); throw; }
    }

    private static bool Axis(string axis) => axis.Length == 2 && axis[0] is '+' or '-' && axis[1] is 'X' or 'Y';
    private static string Line(BinaryReader reader)
    {
        var text = new StringBuilder();
        for (var i = 0; i < 8192; i++) { var value = reader.ReadByte(); if (value == 10) return text.ToString().TrimEnd('\r'); text.Append((char)value); }
        throw new InvalidDataException("The Radiance header line is too long.");
    }
}
