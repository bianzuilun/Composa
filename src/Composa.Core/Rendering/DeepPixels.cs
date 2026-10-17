using System.Runtime.CompilerServices;
using SkiaSharp;

namespace Composa.Rendering;

/// <summary>
/// Immutable high precision source pixels behind an RGBA8888 editing preview. Copies keep their source depth;
/// paint only replaces pixels whose preview changed. The native source is uint16 RGBA or extended-sRGB floats.
/// This follows Compositor's DeepLayer/DeepPixels approach; see THIRD-PARTY-NOTICES.txt.
/// </summary>
public static unsafe class DeepPixels
{
    private sealed class Entry(SKBitmap wide, byte[]? basis = null, int basisStride = 0)
    {
        internal readonly SKBitmap Wide = wide;
        internal readonly byte[]? Basis = basis;
        internal readonly int BasisStride = basisStride;
        internal SKBitmap? Painted;
    }
    private static readonly ConditionalWeakTable<SKBitmap, Entry> Sources = new();
    static DeepPixels() => Pixels.Invalidated += preview =>
    {
        if (Sources.TryGetValue(preview, out var entry)) lock (entry) entry.Painted = null;
    };

    public static bool HasSource(SKBitmap preview) => Sources.TryGetValue(preview, out _);
    public static bool IsWide(SKBitmap pixels) => pixels.ColorType is SKColorType.Rgba16161616 or SKColorType.RgbaF16 or SKColorType.RgbaF16Clamped or SKColorType.RgbaF32;
    public static bool IsHdr(SKBitmap pixels) => pixels.ColorType is SKColorType.RgbaF16 or SKColorType.RgbaF32;

    /// <summary>The original source with any new paint over it, shared and immutable. Never dispose or mutate it.</summary>
    public static SKBitmap? Source(SKBitmap preview)
    {
        if (!Sources.TryGetValue(preview, out var entry)) return null;
        lock (entry)
        {
            if (entry.Basis == null) return entry.Wide;
            if (entry.Painted != null) return entry.Painted;
            var changed = false;
            for (var y = 0; y < preview.Height && !changed; y++)
                changed = !new ReadOnlySpan<byte>((byte*)preview.GetPixels() + (long)y * preview.RowBytes, preview.Width * 4)
                    .SequenceEqual(entry.Basis.AsSpan(y * entry.BasisStride, preview.Width * 4));
            if (!changed) return entry.Wide;
            var result = Pixels.Clone(entry.Wide);
            Parallel.For(0, preview.Height, y =>
            {
                var painted = (byte*)preview.GetPixels() + (long)y * preview.RowBytes;
                var basis = y * entry.BasisStride;
                for (var x = 0; x < preview.Width; x++)
                {
                    var at = x * 4;
                    if (painted[at] == entry.Basis[basis + at] && painted[at + 1] == entry.Basis[basis + at + 1] && painted[at + 2] == entry.Basis[basis + at + 2] && painted[at + 3] == entry.Basis[basis + at + 3]) continue;
                    Write(result, x, y, new SKColorF(painted[at] / 255f, painted[at + 1] / 255f, painted[at + 2] / 255f, painted[at + 3] / 255f));
                }
            });
            return entry.Painted = result;
        }
    }

    /// <summary>Associate a newly created preview with a newly created immutable wide source.</summary>
    public static void Attach(SKBitmap preview, SKBitmap source)
    {
        if (preview.ColorType != SKColorType.Rgba8888 || !IsWide(source) || preview.Width != source.Width || preview.Height != source.Height)
            throw new ArgumentException("The wide source and preview must have matching dimensions and supported pixel formats.");
        Sources.Remove(preview); Sources.Add(preview, new Entry(source));
    }

    internal static SKBitmap CopyLink(SKBitmap source, SKBitmap copy)
    {
        if (Source(source) is { } wide) Sources.Add(copy, new Entry(wide, source.Bytes, source.RowBytes));
        return copy;
    }

    public static void Detach(SKBitmap preview) => Sources.Remove(preview);

    internal static void Collect(SKBitmap preview, HashSet<SKBitmap> into)
    {
        if (!Sources.TryGetValue(preview, out var entry)) return;
        into.Add(entry.Wide);
        if (entry.Painted != null) into.Add(entry.Painted);
    }

    public static SKBitmap New(int width, int height, bool hdr = false)
    {
        using var colorSpace = SKColorSpace.CreateSrgb();
        var bitmap = new SKBitmap(new SKImageInfo(width, height, hdr ? SKColorType.RgbaF32 : SKColorType.Rgba16161616, SKAlphaType.Premul, colorSpace));
        bitmap.Erase(SKColors.Transparent);
        return bitmap;
    }

    public static SKBitmap Convert(SKBitmap source, bool hdr = false)
    {
        var result = New(source.Width, source.Height, hdr);
        using var drawing = new SKCanvas(result);
        drawing.Clear(SKColors.Transparent);
        drawing.DrawBitmap(source, 0, 0);
        return result;
    }

    internal static void CopyWide(SKBitmap source, SKBitmap target)
    {
        if (source.Width != target.Width || source.Height != target.Height || !IsWide(source) || !IsWide(target)) throw new ArgumentException("Matching wide surfaces are required.");
        if (source.ColorType == target.ColorType) { Pixels.CopyPixels(source, target); return; }
        using (var canvas = new SKCanvas(target))
        using (var paint = new SKPaint { BlendMode = SKBlendMode.Src }) canvas.DrawBitmap(source, 0, 0, paint);
        Pixels.Invalidate(target);
    }

    /// <summary>Mirror a geometric raster edit in a float surface and associate its full precision result.</summary>
    internal static void PreserveDraw(SKBitmap source, SKBitmap result, Action<SKCanvas, SKBitmap> draw)
    {
        if (Source(source) is not { } original) return;
        var wide = New(result.Width, result.Height, hdr: true);
        try
        {
            using (var canvas = new SKCanvas(wide)) draw(canvas, original);
            if (!IsHdr(original))
            {
                var integer = Convert(wide); wide.Dispose(); wide = integer;
            }
            Attach(result, wide);
        }
        catch { wide.Dispose(); throw; }
    }

    internal static SKBitmap? MixSelection(SKBitmap original, SKBitmap modified, SKBitmap selection)
    {
        var oldWide = Source(original); var newWide = Source(modified);
        if (oldWide == null && newWide == null) return null;
        var first = oldWide ?? original; var second = newWide ?? modified;
        var result = New(original.Width, original.Height, IsHdr(first) || IsHdr(second));
        Parallel.For(0, result.Height, y =>
        {
            for (var x = 0; x < result.Width; x++)
            {
                var cover = x < selection.Width && y < selection.Height ? selection.GetPixel(x, y).Alpha / 255f : 0;
                var a = Read(first, x, y); var b = Read(second, x, y);
                Write(result, x, y, new SKColorF(a.Red + (b.Red - a.Red) * cover, a.Green + (b.Green - a.Green) * cover,
                    a.Blue + (b.Blue - a.Blue) * cover, a.Alpha + (b.Alpha - a.Alpha) * cover));
            }
        });
        return result;
    }

    /// <summary>Quantize only the editing copy. Fixed per-channel dither prevents banding in the displayed gradient.</summary>
    public static SKBitmap Preview(SKBitmap wide)
    {
        if (!IsWide(wide)) throw new ArgumentException("A wide color bitmap is required.");
        var result = Pixels.NewColor(wide.Width, wide.Height);
        Parallel.For(0, wide.Height, y =>
        {
            var row = (byte*)result.GetPixels() + (long)y * result.RowBytes;
            for (var x = 0; x < wide.Width; x++)
            {
                var p = Read(wide, x, y);
                var alpha = (byte)Math.Clamp(MathF.Round(p.Alpha * 255), 0, 255);
                row[x * 4] = (byte)Math.Clamp(MathF.Floor(p.Red * 255 + Noise(x, y, 0)), 0, alpha);
                row[x * 4 + 1] = (byte)Math.Clamp(MathF.Floor(p.Green * 255 + Noise(x, y, 1)), 0, alpha);
                row[x * 4 + 2] = (byte)Math.Clamp(MathF.Floor(p.Blue * 255 + Noise(x, y, 2)), 0, alpha);
                row[x * 4 + 3] = alpha;
            }
        });
        Attach(result, wide);
        return result;
    }

    internal static float Noise(int x, int y, int channel)
    {
        unchecked
        {
            var hash = (uint)x * 0x9E3779B1u ^ (uint)y * 0x85EBCA77u ^ (uint)(channel + 1) * 0xC2B2AE3Du;
            hash ^= hash >> 15; hash *= 0x2C1B3C6Du; hash ^= hash >> 12; hash *= 0x297A2D39u; hash ^= hash >> 15;
            return (hash >> 8) / (float)(1 << 24);
        }
    }

    /// <summary>Read premultiplied channels without reducing them to bytes.</summary>
    public static SKColorF Read(SKBitmap bitmap, int x, int y)
    {
        var pixel = (byte*)bitmap.GetPixels() + (long)y * bitmap.RowBytes + (long)x * bitmap.BytesPerPixel;
        if (bitmap.ColorType == SKColorType.Rgba16161616)
        {
            var p = (ushort*)pixel; return new SKColorF(p[0] / 65535f, p[1] / 65535f, p[2] / 65535f, p[3] / 65535f);
        }
        if (bitmap.ColorType is SKColorType.RgbaF16 or SKColorType.RgbaF16Clamped)
        {
            var p = (Half*)pixel; return new SKColorF((float)p[0], (float)p[1], (float)p[2], (float)p[3]);
        }
        if (bitmap.ColorType == SKColorType.RgbaF32)
        {
            var p = (float*)pixel; return new SKColorF(p[0], p[1], p[2], p[3]);
        }
        if (bitmap.ColorType == SKColorType.Rgba8888) return new SKColorF(pixel[0] / 255f, pixel[1] / 255f, pixel[2] / 255f, pixel[3] / 255f);
        throw new ArgumentException("Unsupported color format.");
    }

    /// <summary>Write an uncommitted wide bitmap. Floating-point colors may exceed white; coverage stays in 0…1.</summary>
    public static void Write(SKBitmap bitmap, int x, int y, SKColorF color)
    {
        var pixel = (byte*)bitmap.GetPixels() + (long)y * bitmap.RowBytes + (long)x * bitmap.BytesPerPixel;
        var alpha = Math.Clamp(color.Alpha, 0, 1);
        if (bitmap.ColorType == SKColorType.Rgba16161616)
        {
            var p = (ushort*)pixel;
            p[0] = (ushort)MathF.Round(Math.Clamp(color.Red, 0, alpha) * 65535); p[1] = (ushort)MathF.Round(Math.Clamp(color.Green, 0, alpha) * 65535);
            p[2] = (ushort)MathF.Round(Math.Clamp(color.Blue, 0, alpha) * 65535); p[3] = (ushort)MathF.Round(alpha * 65535); return;
        }
        if (bitmap.ColorType is SKColorType.RgbaF16 or SKColorType.RgbaF16Clamped)
        {
            var p = (Half*)pixel; p[0] = (Half)color.Red; p[1] = (Half)color.Green; p[2] = (Half)color.Blue; p[3] = (Half)alpha; return;
        }
        if (bitmap.ColorType == SKColorType.RgbaF32)
        {
            var p = (float*)pixel; p[0] = color.Red; p[1] = color.Green; p[2] = color.Blue; p[3] = alpha; return;
        }
        throw new ArgumentException("A wide color bitmap is required.");
    }
}
