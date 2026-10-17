// Adapted from Compositor/Rendering/PatchPixels.c at v1.5.3 (MIT).
// Copyright (c) 2026 Wonder Assembly LLC. See packaging/THIRD-PARTY-NOTICES.txt.
using Composa.Model;
using Composa.Rendering;
using Composa.Selections;
using SkiaSharp;

namespace Composa.Painting;

/// <summary>Multiscale PatchMatch with texture features, weighted patch voting and seam adaptation.</summary>
internal static unsafe class TextureInpaint
{
    private const int Radius = 3, Channels = 6;

    private sealed class Level(int width, int height)
    {
        internal readonly int W = width, H = height;
        internal readonly float[] Color = new float[checked(width * height * Channels)];
        internal readonly bool[] Hole = new bool[width * height], Known = new bool[width * height], Source = new bool[width * height], Target = new bool[width * height];
        internal readonly byte[] Counts = new byte[width * height];
        internal readonly int[] Match = new int[width * height];
        internal readonly float[] Distance = new float[width * height];
        internal readonly List<int> Sources = [];
    }

    internal static bool TryFill(SKBitmap source, SKBitmap mask, out SKBitmap result, uint seed = 1)
    {
        result = null!;
        var hole = Geometry.Intersect(SelectionMask.Bounds(mask, 1), new SKRectI(0, 0, source.Width, source.Height));
        if (hole.IsEmpty) { result = Pixels.Clone(source); return true; }
        var span = Math.Max(hole.Width, hole.Height); var margin = Math.Max(64, span);
        var area = Geometry.Intersect(new SKRectI(hole.Left - margin, hole.Top - margin, hole.Right + margin, hole.Bottom + margin), new SKRectI(0, 0, source.Width, source.Height));
        if ((long)area.Width * area.Height > Inpaint.MaxArea) return false;
        var fine = new Level(area.Width, area.Height);
        for (var y = 0; y < fine.H; y++) for (var x = 0; x < fine.W; x++)
        {
            var p = y * fine.W + x;
            var pixel = (byte*)source.GetPixels() + (long)(area.Top + y) * source.RowBytes + (area.Left + x) * 4;
            fine.Hole[p] = ((byte*)mask.GetPixels())[(long)(area.Top + y) * mask.RowBytes + area.Left + x] > 0;
            fine.Known[p] = !fine.Hole[p] && pixel[3] == 255;
            for (var c = 0; c < 4; c++) fine.Color[p * Channels + c] = pixel[c];
        }
        Classify(fine);
        if (fine.Sources.Count == 0) return false;
        Texture(fine);
        var levels = new List<Level> { fine };
        for (var size = span; size > 28 && levels.Count < 12; size /= 2)
        {
            var last = levels[^1]; if (last.W < 56 || last.H < 56) break;
            var coarse = Downsample(last); if (coarse.Sources.Count == 0) break; levels.Add(coarse);
        }
        var coarsest = levels[^1];
        FillInward(coarsest);
        var random = seed | 1;
        for (var p = 0; p < coarsest.Target.Length; p++) if (coarsest.Target[p]) coarsest.Match[p] = coarsest.Sources[(int)(Next(ref random) % (uint)coarsest.Sources.Count)];
        Measure(coarsest); Settle(coarsest, 12, seed);
        for (var index = levels.Count - 2; index >= 0; index--)
        {
            Upsample(levels[index + 1], levels[index], seed + (uint)index);
            Measure(levels[index]); Vote(levels[index], 1); Measure(levels[index]);
            Settle(levels[index], index == 0 ? 4 : 6, seed + 0x1000u * (uint)(index + 1));
        }
        Vote(fine, 8); AdaptColor(fine);
        result = Pixels.Clone(source);
        for (var y = 0; y < fine.H; y++) for (var x = 0; x < fine.W; x++)
        {
            var p = y * fine.W + x; if (!fine.Hole[p]) continue;
            var target = (byte*)result.GetPixels() + (long)(area.Top + y) * result.RowBytes + (area.Left + x) * 4;
            var coverage = ((byte*)mask.GetPixels())[(long)(area.Top + y) * mask.RowBytes + area.Left + x] / 255f;
            var a = Math.Clamp(fine.Color[p * Channels + 3], 0, 255);
            for (var c = 0; c < 4; c++) target[c] = (byte)MathF.Round(target[c] * (1 - coverage) + Math.Clamp(fine.Color[p * Channels + c], 0, c == 3 ? 255 : a) * coverage);
        }
        Pixels.Invalidate(result); return true;
    }

    private static uint Next(ref uint state)
    { state ^= state << 13; state ^= state >> 17; state ^= state << 5; if (state == 0) state = 0x9e3779b9u; return state; }

    private static void Classify(Level level)
    {
        var w = level.W; var h = level.H; var stride = w + 1;
        var known = new int[checked(stride * (h + 1))]; var holes = new int[known.Length];
        for (var y = 0; y < h; y++)
        {
            int kr = 0, hr = 0;
            for (var x = 0; x < w; x++)
            {
                var p = y * w + x; kr += level.Known[p] ? 1 : 0; hr += level.Hole[p] ? 1 : 0;
                known[(y + 1) * stride + x + 1] = known[y * stride + x + 1] + kr;
                holes[(y + 1) * stride + x + 1] = holes[y * stride + x + 1] + hr;
            }
        }
        int Sum(int[] table, int x0, int y0, int x1, int y1) => table[y1 * stride + x1] - table[y0 * stride + x1] - table[y1 * stride + x0] + table[y0 * stride + x0];
        for (var y = 0; y < h; y++) for (var x = 0; x < w; x++)
        {
            var p = y * w + x;
            int x0 = Math.Max(0, x - Radius), y0 = Math.Max(0, y - Radius), x1 = Math.Min(w, x + Radius + 1), y1 = Math.Min(h, y + Radius + 1);
            var count = Sum(holes, x0, y0, x1, y1); level.Target[p] = count > 0;
            level.Counts[p] = (byte)(count + Sum(known, x0, y0, x1, y1));
            level.Source[p] = x >= Radius && y >= Radius && x + Radius < w && y + Radius < h && Sum(known, x0, y0, x1, y1) == 49;
            if (level.Source[p]) level.Sources.Add(p);
        }
    }

    private static float Distance(Level level, int p, int q, float best)
    {
        int w = level.W, px = p % w, py = p / w, qx = q % w, qy = q / w;
        var limit = best == float.MaxValue ? float.MaxValue : best * level.Counts[p]; var sum = 0f;
        for (var dy = -Radius; dy <= Radius; dy++)
        {
            var y = py + dy; if (y < 0 || y >= level.H) continue;
            for (var dx = -Radius; dx <= Radius; dx++)
            {
                var x = px + dx; if (x < 0 || x >= w) continue;
                var a = y * w + x; if (!level.Hole[a] && !level.Known[a]) continue;
                var b = (qy + dy) * w + qx + dx;
                for (var c = 0; c < Channels; c++) { var delta = level.Color[a * Channels + c] - level.Color[b * Channels + c]; sum += delta * delta; }
            }
            if (sum >= limit) return float.MaxValue;
        }
        return level.Counts[p] > 0 ? sum / level.Counts[p] : float.MaxValue;
    }

    private static void Measure(Level level) => Parallel.For(0, level.H, y =>
    { for (var x = 0; x < level.W; x++) { var p = y * level.W + x; if (level.Target[p]) level.Distance[p] = Distance(level, p, level.Match[p], float.MaxValue); } });

    private static void Sweep(Level level, bool forward, uint seed)
    {
        var before = (int[])level.Match.Clone(); var bands = level.H < 32 ? 1 : 16; var size = (level.H + bands - 1) / bands;
        Parallel.For(0, bands, band =>
        {
            var start = band * size; var end = Math.Min(level.H, start + size); var step = forward ? 1 : -1;
            var random = seed ^ unchecked((uint)band * 0x9e3779b9u) ^ 0x85ebca6bu;
            for (var iy = 0; iy < end - start; iy++) for (var ix = 0; ix < level.W; ix++)
            {
                int y = forward ? start + iy : end - 1 - iy, x = forward ? ix : level.W - 1 - ix, p = y * level.W + x;
                if (!level.Target[p]) continue;
                var best = level.Match[p]; var distance = level.Distance[p];
                void Try(int candidate)
                {
                    if (candidate < 0 || candidate >= level.Source.Length || !level.Source[candidate] || candidate == best) return;
                    var d = Distance(level, p, candidate, distance); if (d < distance) { distance = d; best = candidate; }
                }
                int nx = x - step, ny = y - step;
                if (nx >= 0 && nx < level.W && level.Target[y * level.W + nx])
                { var m = level.Match[y * level.W + nx]; if (m % level.W + step >= 0 && m % level.W + step < level.W) Try(m + step); }
                if (ny >= 0 && ny < level.H && level.Target[ny * level.W + x])
                { var m = ny >= start && ny < end ? level.Match[ny * level.W + x] : before[ny * level.W + x]; Try(m + step * level.W); }
                for (var reach = Math.Max(level.W, level.H); reach >= 1; reach /= 2)
                {
                    var cx = best % level.W + (int)(Next(ref random) % (uint)(reach * 2 + 1)) - reach;
                    var cy = best / level.W + (int)(Next(ref random) % (uint)(reach * 2 + 1)) - reach;
                    if (cx >= 0 && cy >= 0 && cx < level.W && cy < level.H) Try(cy * level.W + cx);
                }
                level.Match[p] = best; level.Distance[p] = distance;
            }
        });
    }

    private static float Spread(Level level, float sharpness = 1)
    {
        double sum = 0; var count = 0;
        for (var p = 0; p < level.Target.Length; p++) if (level.Target[p] && level.Distance[p] < float.MaxValue) { sum += level.Distance[p]; count++; }
        return (2 * (float)(count > 0 ? sum / count : 1) + 1) / sharpness;
    }

    private static bool Predict(Level level, int x, int y, float spread, Span<float> color)
    {
        color.Clear(); var weight = 0f;
        for (var dy = -Radius; dy <= Radius; dy++) for (var dx = -Radius; dx <= Radius; dx++)
        {
            int px = x - dx, py = y - dy;
            if (px < 0 || py < 0 || px >= level.W || py >= level.H) continue;
            var p = py * level.W + px; if (!level.Target[p] || level.Distance[p] == float.MaxValue) continue;
            var match = level.Match[p]; var sample = (match / level.W + dy) * level.W + match % level.W + dx;
            var w = MathF.Exp(-level.Distance[p] / spread);
            for (var c = 0; c < color.Length; c++) color[c] += level.Color[sample * Channels + c] * w;
            weight += w;
        }
        if (weight <= 0) return false;
        for (var c = 0; c < color.Length; c++) color[c] /= weight;
        return true;
    }

    private static void Vote(Level level, float sharpness)
    {
        var spread = Spread(level, sharpness);
        Parallel.For(0, level.H, y =>
        {
            Span<float> sum = stackalloc float[Channels];
            for (var x = 0; x < level.W; x++)
            { var p = y * level.W + x; if (level.Hole[p] && Predict(level, x, y, spread, sum)) sum.CopyTo(level.Color.AsSpan(p * Channels, Channels)); }
        });
    }

    private static void Settle(Level level, int iterations, uint seed)
    { for (var i = 0; i < iterations; i++) { Sweep(level, i % 2 == 0, unchecked(seed + (uint)i * 0x632be5abu)); Vote(level, 1); Measure(level); } }

    private static void Texture(Level level)
    {
        var w = level.W; var h = level.H; var stride = w + 1;
        var summed = new double[checked(stride * (h + 1) * 3)];
        float Luma(int p) => (level.Color[p * Channels] + level.Color[p * Channels + 1] + level.Color[p * Channels + 2]) / 3;
        for (var y = 0; y < h; y++)
        {
            double across = 0, down = 0, count = 0;
            for (var x = 0; x < w; x++)
            {
                var p = y * w + x;
                if (level.Known[p])
                {
                    count++;
                    if (x > 0 && x + 1 < w && level.Known[p - 1] && level.Known[p + 1]) across += Math.Abs(Luma(p + 1) - Luma(p - 1)) / 2;
                    if (y > 0 && y + 1 < h && level.Known[p - w] && level.Known[p + w]) down += Math.Abs(Luma(p + w) - Luma(p - w)) / 2;
                }
                var at = ((y + 1) * stride + x + 1) * 3; var above = (y * stride + x + 1) * 3;
                summed[at] = summed[above] + across; summed[at + 1] = summed[above + 1] + down; summed[at + 2] = summed[above + 2] + count;
            }
        }
        for (var y = 0; y < h; y++) for (var x = 0; x < w; x++)
        {
            var p = y * w + x; if (!level.Known[p]) continue;
            int x0 = Math.Max(0, x - 2), y0 = Math.Max(0, y - 2), x1 = Math.Min(w, x + 3), y1 = Math.Min(h, y + 3);
            double Sum(int c) => summed[(y1 * stride + x1) * 3 + c] - summed[(y0 * stride + x1) * 3 + c] - summed[(y1 * stride + x0) * 3 + c] + summed[(y0 * stride + x0) * 3 + c];
            var count = Math.Max(1, Sum(2)); level.Color[p * Channels + 4] = (float)(Sum(0) / count * 6); level.Color[p * Channels + 5] = (float)(Sum(1) / count * 6);
        }
    }

    private static Level Downsample(Level fine)
    {
        var coarse = new Level((fine.W + 1) / 2, (fine.H + 1) / 2);
        Span<float> all = stackalloc float[Channels], sure = stackalloc float[Channels];
        for (var y = 0; y < coarse.H; y++) for (var x = 0; x < coarse.W; x++)
        {
            all.Clear(); sure.Clear(); int present = 0, known = 0; var hole = false;
            for (var dy = 0; dy < 2; dy++) for (var dx = 0; dx < 2; dx++)
            {
                int fx = x * 2 + dx, fy = y * 2 + dy; if (fx >= fine.W || fy >= fine.H) continue;
                var p = fy * fine.W + fx; present++; known += fine.Known[p] ? 1 : 0; hole |= fine.Hole[p];
                for (var c = 0; c < Channels; c++) { all[c] += fine.Color[p * Channels + c]; if (fine.Known[p]) sure[c] += fine.Color[p * Channels + c]; }
            }
            var q = y * coarse.W + x; coarse.Hole[q] = hole; coarse.Known[q] = !hole && known == present;
            for (var c = 0; c < Channels; c++) coarse.Color[q * Channels + c] = known > 0 ? sure[c] / known : all[c] / present;
        }
        Classify(coarse); return coarse;
    }

    private static void FillInward(Level level)
    {
        var done = (bool[])level.Known.Clone(); var next = (bool[])done.Clone();
        Span<float> sum = stackalloc float[Channels];
        for (var changed = true; changed;)
        {
            changed = false; done.CopyTo(next, 0);
            for (var y = 0; y < level.H; y++) for (var x = 0; x < level.W; x++)
            {
                var p = y * level.W + x; if (!level.Hole[p] || done[p]) continue;
                sum.Clear(); var count = 0;
                for (var dy = -1; dy <= 1; dy++) for (var dx = -1; dx <= 1; dx++)
                {
                    int qx = x + dx, qy = y + dy; if (qx < 0 || qy < 0 || qx >= level.W || qy >= level.H || !done[qy * level.W + qx]) continue;
                    count++; for (var c = 0; c < Channels; c++) sum[c] += level.Color[(qy * level.W + qx) * Channels + c];
                }
                if (count == 0) continue;
                for (var c = 0; c < Channels; c++) level.Color[p * Channels + c] = sum[c] / count;
                next[p] = true; changed = true;
            }
            (done, next) = (next, done);
        }
    }

    private static void Upsample(Level coarse, Level fine, uint seed)
    {
        var random = seed | 1;
        for (var y = 0; y < fine.H; y++) for (var x = 0; x < fine.W; x++)
        {
            var p = y * fine.W + x; var c = y / 2 * coarse.W + x / 2;
            if (fine.Hole[p]) coarse.Color.AsSpan(c * Channels, Channels).CopyTo(fine.Color.AsSpan(p * Channels, Channels));
            if (!fine.Target[p]) continue;
            var match = coarse.Match[c]; int mx = 2 * (match % coarse.W) + (x & 1), my = 2 * (match / coarse.W) + (y & 1);
            var candidate = my * fine.W + mx;
            fine.Match[p] = coarse.Target[c] && mx < fine.W && my < fine.H && fine.Source[candidate] ? candidate : fine.Sources[(int)(Next(ref random) % (uint)fine.Sources.Count)];
        }
    }

    private static void AdaptColor(Level level)
    {
        var offsets = new float[level.W * level.H * 4]; var weight = new float[level.W * level.H]; var spread = Spread(level);
        Span<float> predicted = stackalloc float[4];
        for (var y = 0; y < level.H; y++) for (var x = 0; x < level.W; x++)
        {
            var p = y * level.W + x; if (!level.Known[p]) continue;
            var touches = false;
            for (var dy = -1; dy <= 1 && !touches; dy++) for (var dx = -1; dx <= 1; dx++)
            { int qx = x + dx, qy = y + dy; if (qx >= 0 && qy >= 0 && qx < level.W && qy < level.H && level.Hole[qy * level.W + qx]) { touches = true; break; } }
            if (!touches || !Predict(level, x, y, spread, predicted)) continue;
            weight[p] = 1;
            for (var c = 0; c < 4; c++) offsets[p * 4 + c] = level.Color[p * Channels + c] - predicted[c];
        }
        PushPull.Fill(offsets, weight, level.W, level.H, 4);
        for (var p = 0; p < level.Hole.Length; p++) if (level.Hole[p]) for (var c = 0; c < 4; c++) level.Color[p * Channels + c] += offsets[p * 4 + c];
    }
}
