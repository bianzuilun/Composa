// Ported from Lolly (github.com/lolly-tools/lolly, tests/grade.test.ts at 12b26ff), MPL-2.0, used under the MIT licence by permission of Andy Fitzsimon, 2026-09-30.
using System.Globalization;
using System.Text.Json;
using Composa.Editing;
using Composa.Filters;
using Composa.IO;
using Composa.Model;
using Composa.Rendering;
using SkiaSharp;
using static Composa.Core.Tests.TestImages;

namespace Composa.Core.Tests;

public class ColorLookupTests
{
    // ── fixtures ─────────────────────────────────────────────────────────────

    /// <summary>The eight corners of a 2-point identity cube, red-fastest (the .cube row order).</summary>
    private static readonly string[] IdentityRows = ["0 0 0", "1 0 0", "0 1 0", "1 1 0", "0 0 1", "1 0 1", "0 1 1", "1 1 1"];

    private static readonly string IdentityCube2 = string.Join('\n', ["TITLE \"Identity\"", "LUT_3D_SIZE 2", "DOMAIN_MIN 0.0 0.0 0.0", "DOMAIN_MAX 1.0 1.0 1.0", .. IdentityRows]);

    /// <summary>An identity cube of n points as .cube text, red-fastest.</summary>
    private static string IdentityCubeText(int n)
    {
        var rows = new List<string> { $"LUT_3D_SIZE {n}" };
        for (var b = 0; b < n; b++)
            for (var g = 0; g < n; g++)
                for (var r = 0; r < n; r++)
                    rows.Add(FormattableString.Invariant($"{r / (double)(n - 1)} {g / (double)(n - 1)} {b / (double)(n - 1)}"));
        return string.Join('\n', rows);
    }

    /// <summary>
    /// A .3dl for an n-point grid: a line of n input levels, then n³ blue-fastest whole-number colors of an identity
    /// ramp on the given scale. The levels line is told apart by having more than three numbers, so n is at least 4.
    /// </summary>
    private static string TdlText(int n, int scale, bool mesh = true, string header = "")
    {
        var rows = new List<string>();
        if (header.Length > 0) rows.Add(header);
        if (mesh) rows.Add(string.Join(' ', Enumerable.Range(0, n).Select(i => (int)Math.Round(i / (double)(n - 1) * 1023))));
        int Q(int v) => (int)Math.Round(v / (double)(n - 1) * scale);
        for (var r = 0; r < n; r++)
            for (var g = 0; g < n; g++)
                for (var b = 0; b < n; b++)
                    rows.Add($"{Q(r)} {Q(g)} {Q(b)}");
        return string.Join('\n', rows);
    }

    private static ColorLattice Curves1D(params float[] points) => ColorLattice.ParseCube("LUT_1D_SIZE " + points.Length / 3 + "\n" +
        string.Join('\n', Enumerable.Range(0, points.Length / 3).Select(i => FormattableString.Invariant($"{points[i * 3]} {points[i * 3 + 1]} {points[i * 3 + 2]}"))));

    private static void Near(float expected, float actual, string what = "") => Assert.True(Math.Abs(expected - actual) < 1e-6, $"{what}: {actual} should be {expected}");

    private static void Near((float R, float G, float B) expected, (float R, float G, float B) actual)
    {
        Near(expected.R, actual.R, "red");
        Near(expected.G, actual.G, "green");
        Near(expected.B, actual.B, "blue");
    }

    private static SKColor Adjusted(Adjustment adjustment, SKColor color)
    {
        using var bitmap = Solid(2, 2, color);
        adjustment.Apply(bitmap);
        return bitmap.GetPixel(0, 0);
    }

    private static InvalidDataException Refused(Func<ColorLattice> parse) => Assert.Throws<InvalidDataException>(parse);

    // ── .cube parsing ────────────────────────────────────────────────────────

    [Fact]
    public void Cube_reads_size_title_domain_and_red_fastest_data()
    {
        var lut = ColorLattice.ParseCube(IdentityCube2);
        Assert.True(lut.HasCube);
        Assert.Equal(2, lut.Size);
        Assert.Equal(0, lut.CurveSize);
        Assert.Equal("Identity", lut.Title);
        Assert.Equal((0, 0, 0), lut.DomainMin);
        Assert.Equal((1, 1, 1), lut.DomainMax);
        Assert.Equal(24, lut.Cube.Length);
        Assert.Equal([1, 0, 0], lut.Cube.Slice(3, 3).ToArray());      // row 1 is (r=1, g=0, b=0): red advances first
        Assert.Equal([1, 1, 1], lut.Cube.Slice(21, 3).ToArray());     // the last row is the white corner
    }

    [Fact]
    public void Cube_reads_a_1d_table()
    {
        var lut = ColorLattice.ParseCube("LUT_1D_SIZE 3\n0 0 0\n0.5 0.25 0.75\n1 1 1\n");
        Assert.False(lut.HasCube);
        Assert.Equal(3, lut.CurveSize);
        Assert.Equal(9, lut.Curves.Length);
        Assert.Equal([0.5f, 0.25f, 0.75f], lut.Curves.Slice(3, 3).ToArray());
    }

    [Fact]
    public void Cube_honours_the_declared_domain()
    {
        var lut = ColorLattice.ParseCube(string.Join('\n', ["LUT_3D_SIZE 2", "DOMAIN_MIN -0.5 -0.5 -0.5", "DOMAIN_MAX 1.5 1.5 1.5", .. IdentityRows]));
        Assert.Equal((-0.5f, -0.5f, -0.5f), lut.DomainMin);
        Assert.Equal((1.5f, 1.5f, 1.5f), lut.DomainMax);
        // The declared domain is what sampling normalises through: 0.5 sits at the middle of -0.5..1.5, so an identity table returns 0.5.
        Near((0.5f, 0.5f, 0.5f), lut.Sample(0.5f, 0.5f, 0.5f));
        // And 0 sits a quarter of the way up, not at the black corner.
        Near(0.25f, lut.Sample(0, 0, 0).R);
        // Resolve spells the same thing as an input range.
        var range = ColorLattice.ParseCube(string.Join('\n', ["LUT_3D_SIZE 2", "LUT_3D_INPUT_RANGE -0.5 1.5", .. IdentityRows]));
        Assert.Equal(lut.DomainMin, range.DomainMin);
        Assert.Equal(lut.DomainMax, range.DomainMax);
    }

    [Fact]
    public void Cube_ignores_comments_blank_lines_unknown_keywords_and_crlf()
    {
        var lut = ColorLattice.ParseCube(string.Join("\r\n", ["# a comment", "", "LUT_3D_SIZE 2", "LUT_IN_VIDEO_RANGE", "   ", .. IdentityRows, "# trailing", ""]));
        Assert.Equal(2, lut.Size);
        Assert.Equal(24, lut.Cube.Length);
        Assert.Equal("Untitled look", ColorLattice.ParseCube("TITLE Untitled look\n" + IdentityCubeText(2)).Title);   // a title without quotes is the rest of the line
    }

    [Fact]
    public void Cube_reads_curves_and_a_cube_from_one_file()
    {
        // A 1D shaper before the 3D table, as Resolve writes: the curves' rows come first and feed the cube.
        var text = "LUT_1D_SIZE 2\nLUT_3D_SIZE 2\n0 0 0\n0.5 0.5 0.5\n" + string.Join('\n', IdentityRows);
        var lut = ColorLattice.ParseCube(text);
        Assert.Equal(2, lut.CurveSize);
        Assert.Equal(2, lut.Size);
        // The curves halve every channel, and the identity cube passes that on.
        Near((0.5f, 0.5f, 0.5f), lut.Sample(1, 1, 1));
        Near((0.25f, 0.1f, 0f), lut.Sample(0.5f, 0.2f, 0));
    }

    [Fact]
    public void Cube_without_a_size_line_is_not_a_cube()
    {
        Assert.Contains("no LUT_3D_SIZE or LUT_1D_SIZE", Refused(() => ColorLattice.ParseCube("0 0 0\n1 1 1\n")).Message);
        Assert.Equal("Line 1: a color comes before LUT_3D_SIZE.", Refused(() => ColorLattice.ParseCube("0 0 0\nLUT_3D_SIZE 2\n" + string.Join('\n', IdentityRows))).Message);
    }

    [Fact]
    public void Cube_that_ends_early_names_the_counts()
    {
        var text = string.Join('\n', ["LUT_3D_SIZE 2", .. IdentityRows.Take(5)]);
        Assert.Contains("ends after 5 of the 8 colors", Refused(() => ColorLattice.ParseCube(text)).Message);
    }

    [Fact]
    public void Cube_refuses_a_malformed_line_and_says_which()
    {
        string With(string line, int at) => string.Join('\n', ["TITLE \"x\"", "LUT_3D_SIZE 2", .. IdentityRows.Take(at), line, .. IdentityRows.Skip(at)]);
        Assert.StartsWith("Line 5: expected a color as three numbers", Refused(() => ColorLattice.ParseCube(With("0.1 0.2", 2))).Message);
        Assert.StartsWith("Line 5: expected a color as three numbers", Refused(() => ColorLattice.ParseCube(With("0.1 0.2 abc", 2))).Message);
        Assert.StartsWith("Line 5: expected a color as three numbers", Refused(() => ColorLattice.ParseCube(With("0.1 0.2 0.3 0.4", 2))).Message);
        Assert.StartsWith("Line 5: expected a color as three numbers", Refused(() => ColorLattice.ParseCube(With("0.1 NaN 0.3", 2))).Message);
        Assert.StartsWith("Line 5: \"COLOUR\" is not a .cube keyword", Refused(() => ColorLattice.ParseCube(With("COLOUR 1 2 3", 2))).Message);
        Assert.StartsWith("Line 11: more colors than the 8", Refused(() => ColorLattice.ParseCube(With("1 1 1", 8))).Message);
        Assert.StartsWith("Line 2: LUT_3D_SIZE needs a whole number", Refused(() => ColorLattice.ParseCube("TITLE \"x\"\nLUT_3D_SIZE two\n")).Message);
        Assert.StartsWith("Line 2: LUT_3D_SIZE must be between 2 and", Refused(() => ColorLattice.ParseCube("TITLE \"x\"\nLUT_3D_SIZE 1\n")).Message);
        Assert.StartsWith("Line 3: LUT_3D_SIZE is given twice", Refused(() => ColorLattice.ParseCube("TITLE \"x\"\nLUT_3D_SIZE 2\nLUT_3D_SIZE 2\n")).Message);
        Assert.StartsWith("Line 2: DOMAIN_MIN needs three numbers", Refused(() => ColorLattice.ParseCube("TITLE \"x\"\nDOMAIN_MIN 0 0\n")).Message);
        Assert.Contains("DOMAIN_MAX must be above DOMAIN_MIN", Refused(() => ColorLattice.ParseCube(string.Join('\n', ["LUT_3D_SIZE 2", "DOMAIN_MAX 0 0 0", .. IdentityRows]))).Message);
        // The size line is refused as soon as it is read, so the table is never allocated.
        Assert.StartsWith("Line 4: LUT_3D_SIZE must be between 2 and 144", Refused(() => ColorLattice.ParseCube($"\n\n\nLUT_3D_SIZE {DocumentLimits.MaxLookupSize + 1}\n0 0 0\n")).Message);
        Assert.StartsWith("Line 1: LUT_1D_SIZE must be between 2 and 65536", Refused(() => ColorLattice.ParseCube($"LUT_1D_SIZE {DocumentLimits.MaxLookupCurveSize + 1}\n")).Message);
        // The ceiling itself is allowed: a cube at the ceiling fails only on its row count.
        Assert.Contains("ends after 1 of the", Refused(() => ColorLattice.ParseCube($"LUT_3D_SIZE {DocumentLimits.MaxLookupSize}\n0 0 0\n")).Message);
    }

    [Fact]
    public void A_parsed_identity_cube_round_trips_through_the_sampler_at_a_realistic_size()
    {
        // 17 is the smallest size Export Look offers, so this is the coarsest real grid the sampler gets.
        var lut = ColorLattice.ParseCube(IdentityCubeText(17));
        Assert.Equal(17, lut.Size);
        foreach (var (r, g, b) in new[] { (0f, 0f, 0f), (1f, 1f, 1f), (0.3f, 0.61f, 0.94f), (0.5f, 0.5f, 0.5f) })
            Near((r, g, b), lut.Sample(r, g, b));
    }

    // ── .3dl parsing ─────────────────────────────────────────────────────────

    [Fact]
    public void Tdl_takes_its_size_from_the_levels_line_and_reorders_blue_fastest_to_red_fastest()
    {
        var lut = ColorLattice.Parse3dl(TdlText(4, 255));
        Assert.Equal(4, lut.Size);
        Assert.Equal((0, 0, 0), lut.DomainMin);
        Assert.Equal((1, 1, 1), lut.DomainMax);
        Assert.Equal(64 * 3, lut.Cube.Length);
        var third = 85 / 255f;
        var cube = lut.Cube;
        // Destination index 1 (red-fastest) is (r=1/3, g=0, b=0); in the source that color sat 16 rows in, because the source runs blue fastest.
        Near(third, cube[3], "red advances first");
        Assert.Equal(0, cube[4]);
        Assert.Equal(0, cube[5]);
        // Destination index 4 (one stride of n) is (0, 1/3, 0).
        Assert.Equal(0, cube[12]);
        Near(third, cube[13], "green advances at stride n");
        // Destination index 16 (n²) is (0, 0, 1/3): blue varies slowest in the output.
        Assert.Equal(0, cube[48]);
        Near(third, cube[50], "blue advances at stride n²");
    }

    [Fact]
    public void Tdl_detects_the_output_scale_from_the_data_peak_or_the_mesh_header()
    {
        foreach (var scale in new[] { 255, 1023, 4095, 65535 })
        {
            var lut = ColorLattice.Parse3dl(TdlText(4, scale));
            Assert.Equal([1, 1, 1], lut.Cube.Slice(189, 3).ToArray());   // with the right scale the white corner is exactly 1
            Assert.Equal([0, 0, 0], lut.Cube.Slice(0, 3).ToArray());
        }
        // A dark table never reaches its peak, so the header's output depth is what says 12 bits.
        var dark = ColorLattice.Parse3dl(TdlText(4, 2047, header: "3DMESH\nMesh 4 12"));
        Near(2047 / 4095f, dark.Cube[191], "white is read on a 12-bit scale");
    }

    [Fact]
    public void Tdl_skips_comments_and_keyword_lines()
    {
        Assert.Equal(4, ColorLattice.Parse3dl("# comment\n3DMESH\nMesh 1 10\n" + TdlText(4, 255)).Size);
    }

    [Fact]
    public void Tdl_without_a_levels_line_takes_the_cube_root_of_the_row_count()
    {
        var lut = ColorLattice.Parse3dl(TdlText(4, 1023, mesh: false));
        Assert.Equal(4, lut.Size);
        Assert.Equal([1, 1, 1], lut.Cube.Slice(189, 3).ToArray());
    }

    [Fact]
    public void Tdl_refuses_what_is_not_a_cube_and_says_which_line()
    {
        var full = TdlText(4, 255).Split('\n');
        Assert.Contains("ends after 9 of the 64 colors its 4 input levels", Refused(() => ColorLattice.Parse3dl(string.Join('\n', full.Take(10)))).Message);
        Assert.Contains("10 colors are not a whole cube", Refused(() => ColorLattice.Parse3dl(string.Join('\n', full.Skip(1).Take(10)))).Message);
        Assert.StartsWith("Line 3: expected a color as three whole numbers", Refused(() => ColorLattice.Parse3dl(string.Join('\n', [full[0], full[1], "1 2", .. full.Skip(2)]))).Message);
        Assert.StartsWith("Line 3: expected whole numbers", Refused(() => ColorLattice.Parse3dl(string.Join('\n', [full[0], full[1], "1 2 0.5", .. full.Skip(2)]))).Message);
        Assert.StartsWith("Line 66: more colors than the 4 input levels", Refused(() => ColorLattice.Parse3dl(string.Join('\n', [.. full, "0 0 0"]))).Message);
        // An outsized levels line never carries the rows to match, so it fails on the count before anything is allocated.
        var mesh = string.Join(' ', Enumerable.Range(0, DocumentLimits.MaxLookupSize + 1));
        Assert.Contains("ends after 2 of the", Refused(() => ColorLattice.Parse3dl($"{mesh}\n0 0 0\n1 1 1\n")).Message);
    }

    // ── routing by name ──────────────────────────────────────────────────────

    [Fact]
    public void Parse_routes_by_the_file_extension_only()
    {
        Assert.Equal(4, ColorLattice.Parse(TdlText(4, 255), "my-look.3dl").Size);
        Assert.Equal(4, ColorLattice.Parse(TdlText(4, 255), "LOOK.3DL").Size);
        Assert.Equal("Identity", ColorLattice.Parse(IdentityCube2).Title);
        Assert.Equal("Identity", ColorLattice.Parse(IdentityCube2, "look.cube").Title);
        // Unlike Lolly, a broken .cube reports its own line instead of falling back to the .3dl reader's message.
        Assert.StartsWith("Line 1: \"hello,\" is not a .cube keyword", Refused(() => ColorLattice.Parse("hello, this is not a LUT", "broken.cube")).Message);
    }

    // ── sampling ─────────────────────────────────────────────────────────────

    [Fact]
    public void Sampling_an_identity_lattice_is_the_identity_on_and_off_the_grid()
    {
        var lut = ColorLattice.Identity(5);
        foreach (var v in new[] { 0f, 0.125f, 0.25f, 0.37f, 0.5f, 0.63f, 0.75f, 1f }) Near((v, v, v), lut.Sample(v, v, v));
        Near((0.1f, 0.8f, 0.42f), lut.Sample(0.1f, 0.8f, 0.42f));   // off the diagonal too, where trilinear and tetrahedral disagree
    }

    [Fact]
    public void Sampling_returns_the_stored_corner_values_exactly_on_the_grid()
    {
        // A 2-point table whose only non-identity corner is red.
        var data = ColorLattice.Identity(2).Cube.ToArray();
        data[3] = 0.25f; data[4] = 0.5f; data[5] = 0.75f;
        var lut = ColorLattice.FromCube(2, data);
        Assert.Equal((0.25f, 0.5f, 0.75f), lut.Sample(1, 0, 0));
        Assert.Equal((0, 0, 0), lut.Sample(0, 0, 0));
        Assert.Equal((1, 1, 1), lut.Sample(1, 1, 1));
        // With two points, the whole table is one cell: white must still read the upper corner rather than run past the table.
        Near(0.5f, ColorLattice.Identity(2).Sample(0.5f, 0.5f, 0.5f).R);
    }

    [Fact]
    public void Sampling_clamps_inputs_outside_the_domain_to_the_table_edges()
    {
        var lut = ColorLattice.Identity(3);
        Assert.Equal((0, 0, 0), lut.Sample(-1, -1, -1));
        Assert.Equal((1, 1, 1), lut.Sample(2, 2, 2));
    }

    [Fact]
    public void Sampling_interpolates_curves_per_channel()
    {
        var lut = Curves1D(0, 0, 0, 0.25f, 0.5f, 0.75f, 1, 1, 1);
        Assert.Equal((0.25f, 0.5f, 0.75f), lut.Sample(0.5f, 0.5f, 0.5f));
        Near(0.125f, lut.Sample(0.25f, 0, 0).R, "midway to the middle point");
    }

    // ── the adjustment on pixels ─────────────────────────────────────────────

    [Fact]
    public void An_identity_lattice_leaves_every_color_within_one()
    {
        // 4096 colors spread over the cube, checked one by one after the 8-bit operation.
        var lookup = new ColorLookupAdjustment { Lattice = ColorLattice.Identity(33) };
        using var bitmap = Pixels.NewColor(64, 64);
        for (var y = 0; y < 64; y++)
            for (var x = 0; x < 64; x++)
                bitmap.SetPixel(x, y, new SKColor((byte)(x * 4), (byte)(y * 4), (byte)((x * 37 + y * 91) & 255)));
        using var before = bitmap.Copy();
        lookup.Apply(bitmap);
        for (var y = 0; y < 64; y++)
            for (var x = 0; x < 64; x++)
                AssertColor(before.GetPixel(x, y), bitmap.GetPixel(x, y), 1);
    }

    [Fact]
    public void A_known_lattice_gives_known_colors_and_leaves_alpha_alone()
    {
        var grey = ColorLattice.FromCube(2, Enumerable.Repeat(0.5f, 24).ToArray());   // everything goes to mid grey
        var lookup = new ColorLookupAdjustment { Lattice = grey };
        AssertColor(new SKColor(128, 128, 128), Adjusted(lookup, new SKColor(200, 30, 90)), 0);
        var half = Adjusted(lookup, new SKColor(200, 100, 40, 128));
        Assert.Equal(128, half.Alpha);
        AssertColor(new SKColor(128, 128, 128, 128), half);
        // The curves-only path takes the same route.
        var dim = new ColorLookupAdjustment { Lattice = Curves1D(0, 0, 0, 0.5f, 0.5f, 0.5f) };
        AssertColor(new SKColor(128, 128, 128), Adjusted(dim, SKColors.White), 0);
        // A domain wider than the pixels: 1.0 sits halfway up 0..2, so an identity table returns 0.5.
        var wide = ColorLattice.ParseCube(string.Join('\n', ["LUT_3D_SIZE 2", "DOMAIN_MAX 2 2 2", .. IdentityRows]));
        AssertColor(new SKColor(128, 128, 128), Adjusted(new ColorLookupAdjustment { Lattice = wide }, SKColors.White), 0);
    }

    [Fact]
    public void Amount_mixes_the_look_into_the_original()
    {
        var black = ColorLattice.FromCube(2, new float[24]);
        var lookup = new ColorLookupAdjustment { Lattice = black };
        AssertColor(new SKColor(100, 50, 20), Adjusted(lookup with { Amount = 50 }, new SKColor(200, 100, 40)), 0);
        AssertColor(new SKColor(200, 100, 40), Adjusted(lookup with { Amount = 0 }, new SKColor(200, 100, 40)), 0);
        Assert.True((lookup with { Amount = 0 }).IsIdentity);
        Assert.True(new ColorLookupAdjustment().IsIdentity);   // no lattice yet
        Assert.False(lookup.IsIdentity);
        // A table value past 1 is clamped on the way out rather than wrapping.
        var hot = ColorLattice.FromCube(2, Enumerable.Repeat(4f, 24).ToArray());
        AssertColor(SKColors.White, Adjusted(new ColorLookupAdjustment { Lattice = hot }, new SKColor(10, 20, 30)), 0);
    }

    // ── identity, equality and the writer ────────────────────────────────────

    [Fact]
    public void The_id_names_the_contents()
    {
        var a = ColorLattice.Identity(3);
        var b = ColorLattice.ParseCube(IdentityCubeText(3));
        Assert.Equal(a.Id, b.Id);
        Assert.Equal(64, a.Id.Length);
        var data = a.Cube.ToArray();
        data[0] = 0.01f;
        Assert.NotEqual(a.Id, ColorLattice.FromCube(3, data).Id);
        Assert.NotEqual(a.Id, ColorLattice.Identity(2).Id);
        // Two adjustments differ by their lattice alone, which record equality cannot see and ContentEquals must.
        var first = new ColorLookupAdjustment { Lattice = a, Source = "x" };
        Assert.True(first.ContentEquals(new ColorLookupAdjustment { Lattice = b, Source = "x" }));
        Assert.False(first.ContentEquals(new ColorLookupAdjustment { Lattice = ColorLattice.FromCube(3, data), Source = "x" }));
        Assert.False(first.ContentEquals(first with { Amount = 50 }));
        // The JSON carries the id and nothing of the table; a lattice attached afterwards takes over the id.
        var json = JsonSerializer.Serialize<Adjustment>(first);
        Assert.Contains(a.Id, json);
        Assert.DoesNotContain("lattice\"", json);
        var back = Assert.IsType<ColorLookupAdjustment>(JsonSerializer.Deserialize<Adjustment>(json));
        Assert.Null(back.Lattice);
        Assert.Equal(a.Id, back.LatticeId);
        Assert.True(back.IsIdentity);
        Assert.Equal(b.Id, (back with { Lattice = b }).LatticeId);
    }

    [Fact]
    public void Writing_a_cube_reads_back_the_same_table()
    {
        var lut = ColorLattice.ParseCube(string.Join('\n', ["LUT_1D_SIZE 2", "LUT_3D_SIZE 2", "DOMAIN_MIN -0.5 0 0", "DOMAIN_MAX 1.5 1 2", "0 0 0", "0.75 0.5 0.25", .. IdentityRows]));
        var text = lut.ToCube("A \"quoted\" look", "Made by a test");
        Assert.StartsWith("# Made by a test\nTITLE \"A 'quoted' look\"\nLUT_1D_SIZE 2\nLUT_3D_SIZE 2\nDOMAIN_MIN -0.500000 0.000000 0.000000\nDOMAIN_MAX 1.500000 1.000000 2.000000\n0.000000 0.000000 0.000000\n0.750000 0.500000 0.250000\n", text);
        var back = ColorLattice.ParseCube(text);
        Assert.Equal(lut.Id, back.Id);
        Assert.Equal("A 'quoted' look", back.Title);
        // Six decimals are the exchange precision: a table with more is read back within that.
        var fine = ColorLattice.FromCube(2, Enumerable.Range(0, 24).Select(i => i / 23.456789f).ToArray(), "Fine");
        var again = ColorLattice.ParseCube(fine.ToCube());
        Assert.Equal("Fine", again.Title);
        for (var i = 0; i < 24; i++) Assert.True(Math.Abs(fine.Cube[i] - again.Cube[i]) < 1e-6);
        Assert.Contains("LUT_3D_SIZE 3\n", ColorLattice.Identity(3).ToCube());
        Assert.DoesNotContain("TITLE", ColorLattice.Identity(3).ToCube());
    }

    // ── the project file ─────────────────────────────────────────────────────

    private static ColorLattice Warm()
    {
        var data = ColorLattice.Identity(3).Cube.ToArray();
        for (var i = 0; i < data.Length; i += 3) data[i] = Math.Min(1, data[i] * 1.1f + 0.0123456f);
        return ColorLattice.FromCube(3, data, "Warm");
    }

    private static string Manifest(MemoryStream stream)
    {
        stream.Position = 0;
        using var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read, leaveOpen: true);
        using var reader = new StreamReader(zip.GetEntry("manifest.json")!.Open());
        return reader.ReadToEnd();
    }

    [Fact]
    public void The_binary_form_keeps_the_lattice_exactly()
    {
        var lut = ColorLattice.ParseCube(string.Join('\n', ["TITLE \"Both\"", "LUT_1D_SIZE 2", "LUT_3D_SIZE 2", "DOMAIN_MIN -0.5 0 0", "DOMAIN_MAX 1.5 1 2", "0 0 0", "0.7654321 0.5 0.25", .. IdentityRows]));
        using var stream = new MemoryStream();
        lut.WriteTo(stream);
        stream.Position = 0;
        var back = ColorLattice.ReadFrom(stream);
        Assert.Equal(lut.Id, back.Id);
        Assert.Equal("Both", back.Title);
        Assert.Equal(lut.DomainMin, back.DomainMin);
        Assert.Equal(lut.DomainMax, back.DomainMax);
        Assert.True(lut.Cube.SequenceEqual(back.Cube));
        Assert.True(lut.Curves.SequenceEqual(back.Curves));
        Assert.Equal(stream.Length, stream.Position);
        // A cut-off or foreign entry is refused as damaged, not read as a smaller table.
        var bytes = stream.ToArray();
        Assert.Contains("damaged", Assert.Throws<InvalidDataException>(() => ColorLattice.ReadFrom(new MemoryStream(bytes[..^5]))).Message);
        Assert.Contains("damaged", Assert.Throws<InvalidDataException>(() => ColorLattice.ReadFrom(new MemoryStream("PNG\0\0\0\0\0"u8.ToArray()))).Message);
        Assert.Contains("damaged", Assert.Throws<InvalidDataException>(() => ColorLattice.ReadFrom(new MemoryStream())).Message);
    }

    [Fact]
    public void Lattices_round_trip_through_the_project_file_and_are_written_once()
    {
        var warm = Warm();
        var grey = ColorLattice.FromCube(2, Enumerable.Repeat(0.5f, 24).ToArray());
        var session = EditorSession.NewCanvas(10, 10, SKColors.White);
        session.AddAdjustmentLayer(new ColorLookupAdjustment { Lattice = warm, Source = "warm.cube", Amount = 60 });
        session.AddAdjustmentLayer(new ColorLookupAdjustment { Lattice = warm, Source = "warm.cube" });
        session.AddAdjustmentLayer(new ColorLookupAdjustment { Lattice = grey, Source = "Grey" });
        session.AddAdjustmentLayer(new ColorLookupAdjustment());   // nothing chosen yet
        using var stream = new MemoryStream();
        ProjectFile.Write(session.Document, stream);
        stream.Position = 0;
        using (var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read, leaveOpen: true))
            Assert.Equal([$"lookups/{grey.Id}.bin", $"lookups/{warm.Id}.bin"], zip.Entries.Select(e => e.FullName).Where(n => n.StartsWith("lookups/")).Order());
        var manifest = Manifest(stream);
        Assert.Contains($"\"version\": {ProjectFile.Version}", manifest);
        Assert.Contains(warm.Id, manifest);
        Assert.DoesNotContain("\"lattice\"", manifest);
        stream.Position = 0;
        var loaded = ProjectFile.Read(stream).AllLayers().Where(l => l.IsAdjustment).Select(l => Assert.IsType<ColorLookupAdjustment>(l.Adjustment)).ToList();
        Assert.Equal(warm.Id, loaded[0].Lattice!.Id);
        Assert.True(warm.Cube.SequenceEqual(loaded[0].Lattice!.Cube));
        Assert.Equal(("warm.cube", 60d), (loaded[0].Source, loaded[0].Amount));
        Assert.Same(loaded[0].Lattice, loaded[1].Lattice);          // one entry, one lattice in memory
        Assert.Equal(grey.Id, loaded[2].Lattice!.Id);
        Assert.Null(loaded[3].Lattice);
        Assert.True(loaded[3].IsIdentity);
        // The picture survives as well: the loaded document renders as the saved one did.
        using var before = DocumentRenderer.Flatten(session.Document);
        stream.Position = 0;
        using var after = DocumentRenderer.Flatten(ProjectFile.Read(stream));
        Assert.True(before.Bytes.AsSpan().SequenceEqual(after.Bytes));
    }

    [Fact]
    public void A_newer_format_and_a_missing_lattice_are_refused()
    {
        var session = EditorSession.NewCanvas(10, 10, SKColors.White);
        session.AddAdjustmentLayer(new ColorLookupAdjustment { Lattice = Warm() });
        using var stream = new MemoryStream();
        ProjectFile.Write(session.Document, stream);
        var bytes = stream.ToArray();

        MemoryStream Tampered(Action<System.IO.Compression.ZipArchive> change)
        {
            var copy = new MemoryStream(bytes.ToArray());
            using (var zip = new System.IO.Compression.ZipArchive(copy, System.IO.Compression.ZipArchiveMode.Update, leaveOpen: true)) change(zip);
            copy.Position = 0;
            return copy;
        }

        // What an older Composa says about this file is the message it has always had, now with 6 as its own ceiling.
        var newer = Tampered(zip =>
        {
            var entry = zip.GetEntry("manifest.json")!;
            string text;
            using (var reader = new StreamReader(entry.Open())) text = reader.ReadToEnd();
            entry.Delete();
            using var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open());
            writer.Write(text.Replace($"\"version\": {ProjectFile.Version}", $"\"version\": {ProjectFile.Version + 1}"));
        });
        Assert.Equal($"This project uses format version {ProjectFile.Version + 1}; this app supports up to version {ProjectFile.Version}.", Assert.Throws<InvalidDataException>(() => ProjectFile.Read(newer)).Message);
        var missing = Tampered(zip => zip.Entries.Single(e => e.FullName.StartsWith("lookups/")).Delete());
        Assert.Contains("lookup table inside the project is missing", Assert.Throws<InvalidDataException>(() => ProjectFile.Read(missing)).Message);
    }

    [Fact]
    public void Save_and_load_keep_the_lattice()
    {
        // The path Recovery and Ctrl+S take.
        var warm = Warm();
        var session = EditorSession.NewCanvas(10, 10, SKColors.White);
        session.AddAdjustmentLayer(new ColorLookupAdjustment { Lattice = warm, Source = "Warm" });
        var path = Path.Combine(Path.GetTempPath(), "composa-lookup-" + Guid.NewGuid().ToString("N")[..8] + ProjectFile.Extension);
        try
        {
            ProjectFile.Save(session.Document, path);
            var lookup = Assert.IsType<ColorLookupAdjustment>(ProjectFile.Load(path).AllLayers().Single(l => l.IsAdjustment).Adjustment);
            Assert.Equal(warm.Id, lookup.Lattice!.Id);
        }
        finally { TempFiles.Delete(path); }
    }

    // ── the bundled looks ────────────────────────────────────────────────────

    [Fact]
    public void The_bundled_looks_load_and_do_what_their_names_say()
    {
        Assert.Equal(["Fine Mono", "Muted Chrome", "Standard Slide", "Vivid Slide"], Looks.Names);
        foreach (var name in Looks.Names)
        {
            var look = Looks.Find(name)!;
            Assert.Equal(33, look.Size);
            Assert.Equal(name, look.Title);
            Assert.NotEqual("", Looks.Describe(name));
            Assert.Same(look, Looks.Find(name));   // parsed once
            // Every look keeps black dark and white light: film lifts its blacks a step or two, but no look inverts or greys out.
            var black = look.Sample(0, 0, 0);
            Assert.True(black.R < 0.03f && black.G < 0.03f && black.B < 0.03f, $"{name} black: {black}");
            var white = look.Sample(1, 1, 1);
            Assert.True(white.R > 0.98f && white.G > 0.98f && white.B > 0.98f, $"{name} white: {white}");
        }
        Assert.Null(Looks.Find("Acros"));
        Assert.Equal("", Looks.Describe("Acros"));
        // Fine Mono is neutral everywhere; Vivid Slide pushes a mid color further from grey than Standard Slide does.
        var mono = Looks.Find("Fine Mono")!;
        foreach (var (r, g, b) in new[] { (0.8f, 0.2f, 0.3f), (0.1f, 0.6f, 0.9f), (0.5f, 0.5f, 0.5f) })
        {
            var (mr, mg, mb) = mono.Sample(r, g, b);
            Assert.True(Math.Abs(mr - mg) < 0.01f && Math.Abs(mg - mb) < 0.01f, $"Fine Mono at {(r, g, b)}: {(mr, mg, mb)}");
        }
        static float Chroma((float R, float G, float B) c) => Math.Max(c.R, Math.Max(c.G, c.B)) - Math.Min(c.R, Math.Min(c.G, c.B));
        var leaf = (0.35f, 0.55f, 0.2f);
        Assert.True(Chroma(Looks.Find("Vivid Slide")!.Sample(leaf.Item1, leaf.Item2, leaf.Item3)) > Chroma(Looks.Find("Standard Slide")!.Sample(leaf.Item1, leaf.Item2, leaf.Item3)));
        // A look on a layer goes through the ordinary adjustment path and names itself.
        var adjustment = new ColorLookupAdjustment { Lattice = mono, Source = "Fine Mono" };
        var pixel = Adjusted(adjustment, new SKColor(200, 60, 90));
        Assert.True(Math.Abs(pixel.Red - pixel.Green) <= 3 && Math.Abs(pixel.Green - pixel.Blue) <= 3, pixel.ToString());
    }

    // ── Export Look ──────────────────────────────────────────────────────────

    [Fact]
    public void The_survey_bakes_what_is_a_function_of_color_and_names_what_is_not()
    {
        var session = EditorSession.NewCanvas(20, 20, SKColors.White);
        var curves = session.AddAdjustmentLayer(new CurvesAdjustment().WithChannel(0, [new(0, 0), new(128, 90), new(255, 255)]));
        var hidden = session.AddAdjustmentLayer(new ExposureAdjustment { Exposure = 1 });
        session.SetVisible(hidden, false);
        var identity = session.AddAdjustmentLayer(new BrightnessContrastAdjustment());
        var grain = session.AddAdjustmentLayer(new GrainAdjustment { Amount = 30 });
        var blur = session.AddAdjustmentLayer(new GaussianBlurAdjustment { Radius = 3 });
        var masked = session.AddAdjustmentLayer(new InvertAdjustment());
        session.AddMask(masked);
        var clipped = session.AddAdjustmentLayer(new InvertAdjustment());
        session.ToggleClippingMask(clipped);
        var lookup = session.AddAdjustmentLayer(new ColorLookupAdjustment { Lattice = Looks.Find("Fine Mono"), Source = "Fine Mono" });
        var (baked, leftOut) = LookBake.Survey(session.Document);
        Assert.Equal([curves.Id, lookup.Id], baked.Select(l => l.Id));
        Assert.Equal([(grain.Id, "changes from place to place"), (blur.Id, "reads the pixels around each pixel"), (masked.Id, "has a layer mask"), (clipped.Id, "is clipped to the layer below")],
            leftOut.Select(l => (l.Layer.Id, l.Why)));
        Assert.DoesNotContain(leftOut, l => l.Layer.Id == hidden.Id || l.Layer.Id == identity.Id);
    }

    [Fact]
    public void A_baked_look_applied_to_a_picture_matches_the_adjustments_applied_directly()
    {
        var session = EditorSession.NewCanvas(20, 20, SKColors.White);
        var curves = session.AddAdjustmentLayer(new CurvesAdjustment().WithChannel(0, [new(0, 20), new(110, 80), new(255, 240)]).WithChannel(2, [new(0, 0), new(255, 200)]));
        var hue = session.AddAdjustmentLayer(new HueSaturationAdjustment().WithShift(HueRange.Master, new HslShift(25, 30, 0)));
        session.SetOpacity(hue, 0.6);
        var map = session.AddAdjustmentLayer(new GradientMapAdjustment { Shadows = 0xFF203050, Highlights = 0xFFF0E0C0 });
        session.SetOpacity(map, 0.35);
        var lattice = LookBake.Bake(session.Document, 33, "Test look");
        Assert.Equal(33, lattice.Size);
        Assert.Equal("Test look", lattice.Title);

        using var direct = Gradient(64, 64);
        foreach (var layer in new[] { curves, hue, map })
        {
            using var adjusted = direct.Copy();
            layer.Adjustment!.Apply(adjusted);
            for (var y = 0; y < 64; y++)
                for (var x = 0; x < 64; x++)
                {
                    var below = direct.GetPixel(x, y);
                    var above = adjusted.GetPixel(x, y);
                    byte Mix(byte a, byte b) => (byte)Math.Round(a + (b - a) * layer.Opacity);
                    direct.SetPixel(x, y, new SKColor(Mix(below.Red, above.Red), Mix(below.Green, above.Green), Mix(below.Blue, above.Blue)));
                }
        }
        using var looked = Gradient(64, 64);
        new ColorLookupAdjustment { Lattice = lattice }.Apply(looked);
        var worst = 0;
        for (var y = 0; y < 64; y++)
            for (var x = 0; x < 64; x++)
            {
                var a = direct.GetPixel(x, y);
                var b = looked.GetPixel(x, y);
                worst = Math.Max(worst, Math.Max(Math.Abs(a.Red - b.Red), Math.Max(Math.Abs(a.Green - b.Green), Math.Abs(a.Blue - b.Blue))));
            }
        Assert.True(worst <= 4, $"The baked look differs from the adjustments by up to {worst}.");
        // A finer table is closer, a coarser one not much further off, and the bake is nothing without layers.
        Assert.True(LookBake.Bake(session.Document, 65).Sample(0.5f, 0.5f, 0.5f).R > 0);
        var none = LookBake.Bake(EditorSession.NewCanvas(4, 4, SKColors.White).Document, 17);
        var (nr, ng, nb) = none.Sample(0.25f, 0.5f, 0.75f);
        Assert.True(Math.Abs(nr - 0.25f) <= 1 / 255f && Math.Abs(ng - 0.5f) <= 1 / 255f && Math.Abs(nb - 0.75f) <= 1 / 255f, $"{(nr, ng, nb)}");   // the grid is laid out in 8 bits
        // What the export writes reads back as the same table, within the six decimals a .cube carries.
        var written = ColorLattice.ParseCube(lattice.ToCube("Test look", "Exported from Composa"));
        Assert.Equal("Test look", written.Title);
        for (var i = 0; i < lattice.Cube.Length; i++) Assert.True(Math.Abs(lattice.Cube[i] - written.Cube[i]) < 1e-6);
    }

    [Fact]
    public void A_camera_raw_grade_bakes_its_color_stages_and_leaves_the_spatial_groups_out()
    {
        var grade = new CameraRawSettings { Exposure = 0.4, Contrast = 20, Temperature = 15, Saturation = 25, Texture = 40, VignetteAmount = -30, GrainAmount = 20 }
            with { Detail = new CameraRawDetail { SharpenAmount = 50 }, Calibration = new CameraRawCalibration { BlueSaturation = 20 } };
        Assert.Equal([CameraRawGroup.Light, CameraRawGroup.Effects, CameraRawGroup.Detail], LookBake.LeftOutOf(grade));
        Assert.Equal(new[] { "Contrast", "Effects", "Detail" }, LookBake.LeftOutNames(grade));
        var colorOnly = LookBake.ColorOnly(grade);
        Assert.True(colorOnly.Adjusts(CameraRawGroup.Light) && colorOnly.Adjusts(CameraRawGroup.Calibration));
        Assert.False(colorOnly.Adjusts(CameraRawGroup.Effects) || colorOnly.Adjusts(CameraRawGroup.Detail));
        Assert.Equal(0, colorOnly.Contrast);
        Assert.True(LookBake.ColorOnly(new CameraRawSettings { Contrast = 20, Highlights = -30, Shadows = 50 }).IsIdentity);
        var lattice = LookBake.Bake(grade, 33, "Graded");
        Assert.Equal(("Graded", 33), (lattice.Title, lattice.Size));

        using var picture = Gradient(64, 64);
        using var direct = CameraRawPixels.Apply(picture, colorOnly);
        using var looked = picture.Copy();
        new ColorLookupAdjustment { Lattice = lattice }.Apply(looked);
        var worst = 0;
        for (var y = 0; y < 64; y++)
            for (var x = 0; x < 64; x++)
            {
                var a = direct.GetPixel(x, y);
                var b = looked.GetPixel(x, y);
                worst = Math.Max(worst, Math.Max(Math.Abs(a.Red - b.Red), Math.Max(Math.Abs(a.Green - b.Green), Math.Abs(a.Blue - b.Blue))));
            }
        // Contrast and saturation put kinks in the pipeline that 33 points round off; 8 steps is within what grading software accepts of a table.
        Assert.True(worst <= 8, $"The baked grade differs from the pipeline by up to {worst}.");
        // A grade of nothing but spatial groups bakes to the identity, within the grid's 8 bits.
        var spatial = new CameraRawSettings { Texture = 40, VignetteAmount = -30 } with { Optics = new CameraRawOptics { Distortion = 10 } };
        Assert.True(LookBake.ColorOnly(spatial).IsIdentity);
        var (r, g, b2) = LookBake.Bake(spatial, 17).Sample(0.25f, 0.5f, 0.75f);
        Assert.True(Math.Abs(r - 0.25f) <= 1 / 255f && Math.Abs(g - 0.5f) <= 1 / 255f && Math.Abs(b2 - 0.75f) <= 1 / 255f);
    }

    [Fact]
    public void Load_reads_a_file_by_its_name()
    {
        var folder = Path.Combine(Path.GetTempPath(), "composa-lut-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        var cube = Path.Combine(folder, "look.CUBE");
        var tdl = Path.Combine(folder, "look.3dl");
        try
        {
            File.WriteAllText(cube, IdentityCube2);
            Assert.Equal("Identity", ColorLattice.Load(cube).Title);
            File.WriteAllText(tdl, TdlText(4, 1023));
            Assert.Equal(4, ColorLattice.Load(tdl).Size);
        }
        finally
        {
            TempFiles.Delete(cube);
            TempFiles.Delete(tdl);
            try { Directory.Delete(folder); } catch (IOException) { }
        }
    }
}
