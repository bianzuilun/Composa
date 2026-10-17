using Composa.Rendering;
using SkiaSharp;

namespace Composa.IO.Psd;

/// <summary>
/// Unpacks channel planes as the Photoshop file format stores them: raw, PackBits run lengths (the usual), or
/// zlib with and without per-row prediction. Written from Adobe's published Photoshop File Formats Specification.
/// </summary>
internal static class PsdChannels
{
    public const int Raw = 0, Rle = 1, Zip = 2, ZipWithPrediction = 3;

    /// <summary>
    /// One plane of <paramref name="width"/> × <paramref name="height"/> bytes from one channel's data. A Large Document
    /// (<paramref name="largeDocument"/>, PSB) stores each PackBits row's byte count in 4 bytes rather than 2. With a
    /// <paramref name="crop"/> only that part of the plane comes back: raw rows are copied in part and PackBits rows
    /// outside it are skipped by their byte counts, so pixels beyond the canvas never cost memory. A zip plane has to be
    /// inflated whole before it can be cut, so there the saving is the color image, not the plane.
    /// </summary>
    public static byte[] Decode(int compression, int width, int height, ReadOnlySpan<byte> data, bool largeDocument = false, PsdCrop? crop = null)
    {
        var expected = (long)width * height;
        if (expected == 0) return [];
        if (crop is { } part)
        {
            if (part.X < 0 || part.Y < 0 || part.Width < 0 || part.Height < 0 || part.X + part.Width > width || part.Y + part.Height > height) throw PsdException.Truncated();
            if (part.Width == 0 || part.Height == 0) return [];
            if (compression == Raw)
            {
                if (data.Length < expected) throw PsdException.Truncated();
                return Slice(data, width, part);
            }
            if (compression == Rle)
            {
                var cursor = new PsdCursor(data);
                var counts = new int[height];
                for (var row = 0; row < height; row++) counts[row] = RowCount(ref cursor, largeDocument);
                return UnpackRows(data[cursor.Offset..], counts, width, part);
            }
            return Slice(Decode(compression, width, height, data, largeDocument), width, part);
        }
        switch (compression)
        {
            case Raw:
                if (data.Length < expected) throw PsdException.Truncated();
                return data[..(int)expected].ToArray();
            case Rle:
            {
                var cursor = new PsdCursor(data);
                var counts = new int[height];
                for (var row = 0; row < height; row++) counts[row] = RowCount(ref cursor, largeDocument);
                var plane = new byte[expected];
                UnpackRows(data[cursor.Offset..], counts, width, height, plane, 0);
                return plane;
            }
            case Zip:
            case ZipWithPrediction:
            {
                var plane = Inflate(data, expected);
                if (compression == ZipWithPrediction)
                    for (var row = 0; row < height; row++)
                    {
                        var start = row * width;
                        for (var x = 1; x < width; x++) plane[start + x] = (byte)(plane[start + x] + plane[start + x - 1]);
                    }
                return plane;
            }
            default:
                throw new PsdException("This Photoshop file uses a layer compression method that isn't supported.");
        }
    }

    /// <summary>Sixteen-bit planes retain big-endian samples; ZIP prediction adds whole 16-bit samples.</summary>
    public static byte[] Decode16(int compression, int width, int height, ReadOnlySpan<byte> data, bool largeDocument = false, PsdCrop? crop = null)
    {
        var byteWidth = checked(width * 2);
        var byteCrop = crop is { } part ? new PsdCrop(part.X * 2, part.Y, part.Width * 2, part.Height) : (PsdCrop?)null;
        if (compression != ZipWithPrediction) return Decode(compression, byteWidth, height, data, largeDocument, byteCrop);
        var plane = Inflate(data, (long)byteWidth * height);
        for (var y = 0; y < height; y++)
        {
            ushort previous = 0;
            for (var x = 0; x < width; x++)
            {
                var at = y * byteWidth + x * 2;
                var sample = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(plane.AsSpan(at, 2));
                previous = unchecked((ushort)(previous + sample));
                System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(plane.AsSpan(at, 2), previous);
            }
        }
        return byteCrop is { } cut ? Slice(plane, byteWidth, cut) : plane;
    }

    /// <summary>A PackBits row's byte count: 2 bytes in a PSD, 4 in a PSB.</summary>
    public static int RowCount(ref PsdCursor cursor, bool largeDocument)
    {
        if (!largeDocument) return cursor.U16();
        var count = cursor.U32();
        if (count > int.MaxValue) throw PsdException.Truncated();
        return (int)count;
    }

    /// <summary>The rows of a plane inside the crop, each cut to the crop's columns.</summary>
    private static byte[] Slice(ReadOnlySpan<byte> plane, int width, PsdCrop crop)
    {
        var result = new byte[crop.Width * crop.Height];
        for (var row = 0; row < crop.Height; row++) plane.Slice((crop.Y + row) * width + crop.X, crop.Width).CopyTo(result.AsSpan(row * crop.Width, crop.Width));
        return result;
    }

    /// <summary>PackBits rows through a crop: rows above and below it are stepped over by their byte counts, the rest unpacked one at a time and cut.</summary>
    private static byte[] UnpackRows(ReadOnlySpan<byte> data, int[] counts, int width, PsdCrop crop)
    {
        var plane = new byte[crop.Width * crop.Height];
        var row = new byte[width];
        var offset = 0;
        for (var y = 0; y < counts.Length; y++)
        {
            if (y >= crop.Y && y < crop.Y + crop.Height)
            {
                UnpackRows(data[offset..], counts.AsSpan(y, 1), width, 1, row, 0);
                row.AsSpan(crop.X, crop.Width).CopyTo(plane.AsSpan((y - crop.Y) * crop.Width, crop.Width));
            }
            offset += counts[y];
            if (offset > data.Length) throw PsdException.Truncated();
        }
        return plane;
    }

    /// <summary>PackBits rows into <paramref name="plane"/> from <paramref name="planeOffset"/>; returns the bytes consumed.</summary>
    public static int UnpackRows(ReadOnlySpan<byte> data, ReadOnlySpan<int> counts, int width, int height, byte[] plane, long planeOffset)
    {
        var offset = 0;
        for (var row = 0; row < height; row++)
        {
            var end = offset + counts[row];
            if (end > data.Length) throw PsdException.Truncated();
            var target = planeOffset + (long)row * width;
            var written = 0;
            while (written < width)
            {
                if (offset >= end) throw PsdException.Truncated();
                var n = (sbyte)data[offset++];
                if (n >= 0)
                {
                    var count = n + 1;
                    if (written + count > width || offset + count > end) throw PsdException.Truncated();
                    data.Slice(offset, count).CopyTo(plane.AsSpan((int)(target + written), count));
                    offset += count;
                    written += count;
                }
                else if (n != -128)
                {
                    var count = 1 - n;
                    if (written + count > width || offset >= end) throw PsdException.Truncated();
                    plane.AsSpan((int)(target + written), count).Fill(data[offset++]);
                    written += count;
                }
            }
            offset = end;
        }
        return offset;
    }

    private static byte[] Inflate(ReadOnlySpan<byte> data, long expected)
    {
        var plane = new byte[expected];
        try
        {
            using var input = new MemoryStream(data.ToArray());
            using var zlib = new System.IO.Compression.ZLibStream(input, System.IO.Compression.CompressionMode.Decompress);
            var read = 0;
            while (read < plane.Length)
            {
                var got = zlib.Read(plane, read, plane.Length - read);
                if (got <= 0) break;
                read += got;
            }
            if (read < plane.Length) throw PsdException.Truncated();
        }
        catch (InvalidDataException) { throw PsdException.Truncated(); }
        return plane;
    }

    /// <summary>Premultiplied RGBA pixels from straight planes; a missing alpha means opaque.</summary>
    public static SKBitmap ColorImage(int width, int height, byte[]? red, byte[]? green, byte[]? blue, byte[]? alpha)
    {
        var count = (long)width * height;
        if ((red != null && red.Length < count) || (green != null && green.Length < count) || (blue != null && blue.Length < count) || (alpha != null && alpha.Length < count))
            throw PsdException.Truncated();
        var bitmap = Pixels.NewColor(width, height);
        var pixels = bitmap.GetPixelSpan();
        for (long i = 0, p = 0; i < count; i++, p += 4)
        {
            var a = alpha == null ? (byte)255 : alpha[i];
            pixels[(int)p] = Premultiply(red == null ? (byte)0 : red[i], a);
            pixels[(int)p + 1] = Premultiply(green == null ? (byte)0 : green[i], a);
            pixels[(int)p + 2] = Premultiply(blue == null ? (byte)0 : blue[i], a);
            pixels[(int)p + 3] = a;
        }
        Pixels.Invalidate(bitmap);
        return bitmap;
    }

    private static byte Premultiply(byte value, byte alpha) => alpha == 255 ? value : (byte)((value * alpha + 127) / 255);

    public static SKBitmap ColorImage16(int width, int height, byte[]? red, byte[]? green, byte[]? blue, byte[]? alpha)
    {
        var bitmap = DeepPixels.New(width, height);
        float Sample(byte[]? plane, int i, float missing) => plane == null ? missing : System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(plane.AsSpan(i * 2, 2)) / 65535f;
        try
        {
            for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
            {
                var i = y * width + x; var a = Sample(alpha, i, 1);
                DeepPixels.Write(bitmap, x, y, new SKColorF(Sample(red, i, 0) * a, Sample(green, i, 0) * a, Sample(blue, i, 0) * a, a));
            }
            return DeepPixels.Preview(bitmap);
        }
        catch { bitmap.Dispose(); throw; }
    }

    internal static SKBitmap RgbProfile(SKBitmap preview, byte[]? profile)
    {
        if (profile == null) return preview;
        using var space = SKColorSpace.CreateIcc(profile);
        if (space == null || space.IsSrgb) return preview;
        var source = DeepPixels.Source(preview) ?? preview;
        using var tagged = new SKBitmap(source.Info.WithColorSpace(space));
        Pixels.CopyPixels(source, tagged);
        if (DeepPixels.IsWide(source))
        {
            var converted = DeepPixels.Preview(DeepPixels.Convert(tagged)); preview.Dispose(); return converted;
        }
        using var srgb = SKColorSpace.CreateSrgb();
        using var output = new SKBitmap(preview.Info.WithColorSpace(srgb));
        using (var canvas = new SKCanvas(output)) canvas.DrawBitmap(tagged, 0, 0);
        Pixels.CopyPixels(output, preview); Pixels.Invalidate(preview); return preview;
    }

    public static SKBitmap CmykImage(int width, int height, int depth, byte[]? cyan, byte[]? magenta, byte[]? yellow, byte[]? black, byte[]? alpha, byte[]? profile)
    {
        if (ImageMagick.Current is not IColorConverter converter) throw new PsdException("CMYK Photoshop files need ImageMagick with ICC color management.");
        var bytes = depth / 8; var count = checked(width * height); var samples = new byte[checked(count * 4 * bytes)];
        byte[]?[] planes = [cyan, magenta, yellow, black];
        for (var i = 0; i < count; i++) for (var c = 0; c < 4; c++)
        {
            var plane = planes[c];
            if (depth == 8) samples[i * 4 + c] = plane == null ? (byte)0 : (byte)(255 - plane[i]);
            else
            {
                var value = plane == null ? (ushort)0 : (ushort)(65535 - System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(plane.AsSpan(i * 2, 2)));
                System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(samples.AsSpan((i * 4 + c) * 2, 2), value);
            }
        }
        var rgb = converter.DecodeCmyk(samples, width, height, depth, profile);
        using var stream = new MemoryStream(rgb);
        var bitmap = ImageFiles.Load(stream);
        if (bitmap.Width != width || bitmap.Height != height) { bitmap.Dispose(); throw PsdException.Truncated(); }
        if (alpha == null) return bitmap;
        if (DeepPixels.Source(bitmap) is { } wide)
        {
            var masked = DeepPixels.Convert(wide);
            for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
            {
                var i = y * width + x;
                var a = depth == 16 ? System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(alpha.AsSpan(i * 2, 2)) / 65535f : alpha[i] / 255f;
                var p = DeepPixels.Read(wide, x, y); DeepPixels.Write(masked, x, y, new SKColorF(p.Red * a, p.Green * a, p.Blue * a, a));
            }
            bitmap.Dispose(); return DeepPixels.Preview(masked);
        }
        var target = bitmap.GetPixelSpan();
        for (var i = 0; i < count; i++)
        {
            var a = depth == 16 ? alpha[i * 2] : alpha[i];
            for (var c = 0; c < 3; c++) target[i * 4 + c] = Premultiply(target[i * 4 + c], a);
            target[i * 4 + 3] = a;
        }
        Pixels.Invalidate(bitmap); return bitmap;
    }

    /// <summary>An Alpha8 mask from one gray plane.</summary>
    public static SKBitmap MaskImage(int width, int height, byte[] gray)
    {
        if (gray.Length < (long)width * height) throw PsdException.Truncated();
        var mask = Pixels.NewMask(width, height);
        var target = mask.GetPixelSpan();
        for (var y = 0; y < height; y++) gray.AsSpan(y * width, width).CopyTo(target.Slice(y * mask.RowBytes, width));
        Pixels.Invalidate(mask);
        return mask;
    }
}
