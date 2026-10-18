using System.IO.Pipes;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Composa.App.Mcp;
using Composa.Core.Tests;
using Composa.Editing;
using Composa.Filters;
using Composa.IO;
using Composa.Model;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using SkiaSharp;

namespace Composa.App.Tests;

/// <summary>
/// An agent reaches the editor through <c>composa --mcp</c>, a stdio bridge to the pipe the window listens on. These
/// tests go the whole way: the bridge is the built application, launched the way an MCP client launches it, and every
/// tool call lands on the window's dispatcher, which a headless test drives by hand.
/// </summary>
public partial class McpTests
{
    private static readonly string App = Path.Combine(AppContext.BaseDirectory, "composa.dll");

    private static string PipeName() => OperatingSystem.IsWindows()
        ? $"composa-test-{Guid.NewGuid():N}"
        : Path.Combine(Path.GetTempPath(), $"composa-test-{Guid.NewGuid():N}.sock");

    private static StdioClientTransport Bridge(string pipe) => new(new StdioClientTransportOptions
    {
        Name = "composa", Command = "dotnet", Arguments = [App, "--mcp"],
        EnvironmentVariables = new Dictionary<string, string?> { [McpPipe.Variable] = pipe }
    });

    /// <summary>The server's work is posted to the UI thread, which is this thread; it runs only while the test pumps.</summary>
    private static Task<T> Pumped<T>(ValueTask<T> task) => Pumped(task.AsTask());

    private static async Task<T> Pumped<T>(Task<T> task)
    {
        for (var i = 0; i < 3000 && !task.IsCompleted; i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10); }
        Dispatcher.UIThread.RunJobs();
        return await task;
    }

    private static async Task Pumped(Func<bool> until)
    {
        for (var i = 0; i < 1000 && !until(); i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10); }
        Dispatcher.UIThread.RunJobs();
    }

    private static string Text(CallToolResult result) => string.Concat(result.Content.OfType<TextContentBlock>().Select(t => t.Text));

    [AvaloniaFact]
    public async Task Tools_change_the_open_document_through_the_bridge_and_every_change_is_undoable()
    {
        var window = new MainWindow { Width = 1000, Height = 700 };
        window.Show();
        using var host = new McpHost(window, PipeName());
        Assert.True(await host.StartAsync());

        await using var client = await Pumped(McpClient.CreateAsync(Bridge(host.PipeName)));
        await Pumped(() => host.Connections == 1);
        var tools = await client.ListToolsAsync();
        Assert.Equal(
            new[] { "add_line", "add_shape", "add_text", "adjust_black_and_white", "adjust_brightness_contrast", "adjust_color_balance", "adjust_color_lookup", "adjust_curves", "adjust_exposure", "adjust_gradient_map",
             "adjust_hue_saturation", "adjust_invert", "adjust_levels", "delete_layer", "describe_document", "deselect", "duplicate_layer", "enhance_layer_resolution", "export_image", "export_look", "fill_layer", "filter_add_noise",
             "filter_bloom", "filter_blur", "filter_dither", "filter_lens_correction", "filter_motion_blur", "filter_painterly", "filter_remove_background", "filter_sharpen", "filter_tonal_contrast", "filter_vignette", "image_size", "list_documents",
             "modify_selection", "new_document", "new_layer", "open_document", "paint_stroke", "paint_strokes", "place_image", "render", "reorder_layer", "sample_color", "save_document", "select_all", "select_color_range", "select_inverse",
             "select_layer", "select_layer_pixels", "select_object", "select_shape", "select_subject", "select_wand", "set_layer", "trace_edges", "transform_layer", "undo" }
             .Concat(NewTools).Order(),
            tools.Select(t => t.Name).Order());

        var resources = await client.ListResourcesAsync();
        Assert.Equal(["composa://documents"], resources.Select(r => r.Uri));
        Assert.Equal(["composa://documents/{number}", "composa://documents/{number}/image"], (await client.ListResourceTemplatesAsync()).Select(r => r.UriTemplate).Order());
        var none = await Pumped(client.ReadResourceAsync("composa://documents"));
        Assert.Equal("No document is open.", Assert.IsType<TextResourceContents>(Assert.Single(none.Contents)).Text);

        var tooBig = await Pumped(client.CallToolAsync("new_document", new Dictionary<string, object?> { ["width"] = 40000, ["height"] = 10 }));
        Assert.Equal(true, tooBig.IsError);
        Assert.Contains(DocumentLimits.MaxSide.ToString(), Text(tooBig));
        var created = await Pumped(client.CallToolAsync("new_document", new Dictionary<string, object?> { ["width"] = 400, ["height"] = 300, ["background"] = "#FFFFFF" }));
        Assert.Equal("Created document 1: \"Untitled\" 400×300 px, now active.", Text(created));
        var session = window.Session!;
        Assert.Equal("Background", session.ActiveLayer!.Name);

        var listed = await Pumped(client.CallToolAsync("list_documents"));
        Assert.Contains("1: \"Untitled\" 400×300 px, 1 layers", Text(listed));

        var filled = await Pumped(client.CallToolAsync("fill_layer", new Dictionary<string, object?> { ["color"] = "#FF0000" }));
        Assert.NotEqual(true, filled.IsError);
        Assert.Equal(SKColors.Red, session.Document.Layers[0].Pixels!.GetPixel(5, 5));
        Assert.Equal("Fill", session.History.UndoName);

        var added = await Pumped(client.CallToolAsync("add_text", new Dictionary<string, object?> { ["text"] = "Hello", ["x"] = 20, ["y"] = 30, ["size"] = 48, ["color"] = "#0000FF" }));
        Assert.NotEqual(true, added.IsError);
        Assert.Equal(2, session.Document.Layers.Count);
        Assert.Equal("Hello", session.ActiveLayer!.Text!.Text);
        Assert.Equal(0xFF0000FFu, session.ActiveLayer.Text.Color);

        var described = Text(await Pumped(client.CallToolAsync("describe_document")));
        Assert.Contains("400×300 px", described);
        Assert.Matches("\\* \"Hello\" \\[[0-9a-f]{8}\\]: text \"Hello\"", described);
        Assert.Matches("- \"Background\" \\[[0-9a-f]{8}\\]: pixels", described);

        var rendered = await Pumped(client.CallToolAsync("render", new Dictionary<string, object?> { ["maxSide"] = 200 }));
        var image = Assert.Single(rendered.Content.OfType<ImageContentBlock>());
        Assert.Equal("image/png", image.MimeType);
        using var png = SKBitmap.Decode(image.DecodedData.ToArray());
        Assert.Equal(200, png.Width);
        Assert.Equal(150, png.Height);
        Assert.Equal(SKColors.Red, png.GetPixel(190, 140));

        var asResource = await Pumped(client.ReadResourceAsync("composa://documents/1"));
        var text = Assert.IsType<TextResourceContents>(Assert.Single(asResource.Contents));
        Assert.Equal("composa://documents/1", text.Uri);
        Assert.Contains("- \"Background\"", text.Text);
        var imageResource = await Pumped(client.ReadResourceAsync("composa://documents/1/image"));
        var blob = Assert.IsType<BlobResourceContents>(Assert.Single(imageResource.Contents));
        Assert.Equal("image/png", blob.MimeType);
        using (var fromResource = SKBitmap.Decode(blob.DecodedData.ToArray())) { Assert.Equal(400, fromResource.Width); Assert.Equal(SKColors.Red, fromResource.GetPixel(390, 290)); }
        var noSuch = await Assert.ThrowsAnyAsync<McpException>(async () => await Pumped(client.ReadResourceAsync("composa://documents/9")));
        Assert.Contains("no document 9", noSuch.Message);

        // The subject tools with the plain method, which is exact here: the blue text is what stands out from the red.
        var subject = await Pumped(client.CallToolAsync("select_subject", new Dictionary<string, object?> { ["detect"] = "plain" }));
        Assert.StartsWith("Selected the area at", Text(subject));
        var badDetect = await Pumped(client.CallToolAsync("select_subject", new Dictionary<string, object?> { ["detect"] = "magic" }));
        Assert.Equal(true, badDetect.IsError);
        Assert.Equal("Nothing is selected.", Text(await Pumped(client.CallToolAsync("deselect"))));
        Assert.Equal("Undid Deselect.", Text(await Pumped(client.CallToolAsync("undo"))));
        Assert.Equal("Undid Select Subject.", Text(await Pumped(client.CallToolAsync("undo"))));

        // Image Size by width alone keeps the proportions; nearest neighbour keeps the hard edge of the red fill.
        Assert.Equal("The document is now 200×150 px at 72 pixels/inch.", Text(await Pumped(client.CallToolAsync("image_size", new Dictionary<string, object?> { ["width"] = 200, ["resample"] = "nearest" }))));
        Assert.Equal((200, 150), (session.Document.Width, session.Document.Height));
        var badResample = await Pumped(client.CallToolAsync("image_size", new Dictionary<string, object?> { ["width"] = 300, ["resample"] = "magic" }));
        Assert.Equal(true, badResample.IsError);
        Assert.Equal("Undid Image Size.", Text(await Pumped(client.CallToolAsync("undo"))));
        Assert.Equal((400, 300), (session.Document.Width, session.Document.Height));
        // A text layer has settings to redraw from, so Enhance Resolution refuses it.
        var notRaster = await Pumped(client.CallToolAsync("enhance_layer_resolution", new Dictionary<string, object?> { ["layer"] = "Hello" }));
        Assert.Equal(true, notRaster.IsError);
        Assert.Contains("must be a raster layer shown larger than its own pixels", Text(notRaster));

        var picture = Path.Combine(Path.GetTempPath(), $"composa-place-{Guid.NewGuid():N}.png");
        using (var wide = new SKBitmap(800, 200)) { wide.Erase(SKColors.Lime); ImageFiles.Save(wide, picture, ExportFormat.Png); }
        try
        {
            var placed = await Pumped(client.CallToolAsync("place_image", new Dictionary<string, object?> { ["path"] = picture }));
            Assert.Equal($"Placed \"{Path.GetFileNameWithoutExtension(picture)}\" (800×200 px) as a layer at 0,100 size 400×100, now active.", Text(placed));
            Assert.Equal(3, session.Document.Layers.Count);                                          // Scaled down to fit and centered.
            var half = await Pumped(client.CallToolAsync("place_image", new Dictionary<string, object?> { ["path"] = picture, ["scale"] = 0.5, ["x"] = 100, ["y"] = 100 }));
            Assert.Contains("as a layer at 0,75 size 200×50", Text(half));                             // Half the fitted size, centered on 100,100.
            Assert.Equal("Undid Add Image.", Text(await Pumped(client.CallToolAsync("undo"))));
            var missing = await Pumped(client.CallToolAsync("place_image", new Dictionary<string, object?> { ["path"] = picture + ".missing" }));
            Assert.Equal(true, missing.IsError);
            Assert.Equal("Undid Add Image.", Text(await Pumped(client.CallToolAsync("undo"))));
        }
        finally { File.Delete(picture); }

        var refused = await Pumped(client.CallToolAsync("fill_layer", new Dictionary<string, object?> { ["color"] = "nonsense" }));
        Assert.Equal(true, refused.IsError);
        Assert.Contains("not a color", Text(refused));

        Assert.Equal("Undid Text.", Text(await Pumped(client.CallToolAsync("undo"))));
        Assert.Single(session.Document.Layers);

        // The layer tools, on a fresh text layer.
        await Pumped(client.CallToolAsync("add_text", new Dictionary<string, object?> { ["text"] = "Hello", ["x"] = 20, ["y"] = 30 }));
        var hello = session.ActiveLayer!;
        var set = await Pumped(client.CallToolAsync("set_layer", new Dictionary<string, object?> { ["layer"] = "hello", ["name"] = "Greeting", ["visible"] = false, ["opacity"] = 0.5, ["blend"] = "soft light" }));
        Assert.Equal("\"Greeting\": named \"Greeting\", hidden, opacity 50%, blend Soft Light.", Text(set));
        Assert.False(hello.Visible);
        Assert.Equal(0.5, hello.Opacity);
        Assert.Equal(BlendMode.SoftLight, hello.Blend);
        Assert.Equal("Blend Mode", session.History.UndoName);
        await Pumped(client.CallToolAsync("set_layer", new Dictionary<string, object?> { ["layer"] = "Greeting", ["visible"] = true }));
        var moved = await Pumped(client.CallToolAsync("transform_layer", new Dictionary<string, object?> { ["layer"] = "Greeting", ["x"] = 50, ["y"] = 60 }));
        Assert.StartsWith("\"Greeting\" is now at 50,60 size", Text(moved));
        Assert.Equal(50, hello.Transform.X);
        Assert.Equal("Background", session.Document.Layers[0].Name);
        Assert.Equal("Duplicated \"Greeting\" as \"Greeting copy\", now active.", Text(await Pumped(client.CallToolAsync("duplicate_layer", new Dictionary<string, object?> { ["layer"] = "Greeting" }))));
        Assert.Equal(3, session.Document.Layers.Count);
        await Pumped(client.CallToolAsync("reorder_layer", new Dictionary<string, object?> { ["layer"] = "Greeting copy", ["direction"] = "bottom" }));
        Assert.Equal("Greeting copy", session.Document.Layers[0].Name);
        Assert.Contains("already at the bottom", Text(await Pumped(client.CallToolAsync("reorder_layer", new Dictionary<string, object?> { ["layer"] = "Greeting copy", ["direction"] = "down" }))));
        var byId = await Pumped(client.CallToolAsync("select_layer", new Dictionary<string, object?> { ["layer"] = hello.Id.ToString("N")[..8] }));
        Assert.Equal("\"Greeting\" is the active layer.", Text(byId));
        Assert.Equal("Deleted \"Greeting copy\".", Text(await Pumped(client.CallToolAsync("delete_layer", new Dictionary<string, object?> { ["layer"] = "Greeting copy" }))));
        Assert.Equal(2, session.Document.Layers.Count);
        // Painting and shapes.
        await Pumped(client.CallToolAsync("select_layer", new Dictionary<string, object?> { ["layer"] = "Greeting" }));
        var stroke = await Pumped(client.CallToolAsync("paint_stroke", new Dictionary<string, object?> { ["points"] = new[] { new[] { 20.0, 200.0 }, new[] { 380.0, 200.0 } }, ["color"] = "#00FF00", ["size"] = 30, ["hardness"] = 1, ["layer"] = "Background" }));
        Assert.Equal("Painted a paint stroke of 2 points on \"Background\".", Text(stroke));
        Assert.Equal(SKColors.Lime, session.Document.Layers[0].Pixels!.GetPixel(200, 200));
        Assert.Equal("Brush", session.History.UndoName);
        Assert.Equal("Greeting", session.ActiveLayer!.Name);                                  // Painting on a named layer does not select it.
        var onText = await Pumped(client.CallToolAsync("paint_stroke", new Dictionary<string, object?> { ["points"] = new[] { new[] { 20.0, 20.0 } }, ["layer"] = "Greeting" }));
        Assert.Equal(true, onText.IsError);
        Assert.Contains("live text", Text(onText));
        // Many strokes at once, colors read back, and a gridded or partial render.
        var batch = await Pumped(client.CallToolAsync("paint_strokes", new Dictionary<string, object?>
        {
            ["strokes"] = new object[]
            {
                new { points = new[] { new[] { 20.0, 250.0 }, new[] { 380.0, 250.0 } }, color = "#00FF00", size = 20, hardness = 1.0 },
                new { points = new[] { new[] { 20.0, 280.0 }, new[] { 380.0, 280.0 } }, color = "#0000FF", size = 10, hardness = 1.0 }
            },
            ["layer"] = "Background"
        }));
        Assert.Equal("Painted 2 strokes on \"Background\".", Text(batch));
        Assert.Equal(SKColors.Lime, session.Document.Layers[0].Pixels!.GetPixel(200, 250));
        Assert.Equal(SKColors.Blue, session.Document.Layers[0].Pixels!.GetPixel(200, 280));
        Assert.Equal("Brush Strokes", session.History.UndoName);
        Assert.Equal("Greeting", session.ActiveLayer!.Name);
        Assert.Equal("Undid Brush Strokes.", Text(await Pumped(client.CallToolAsync("undo"))));
        Assert.Equal(SKColors.Red, session.Document.Layers[0].Pixels!.GetPixel(200, 280));
        var sampled = Text(await Pumped(client.CallToolAsync("sample_color", new Dictionary<string, object?> { ["points"] = new[] { new[] { 200.0, 200.0 }, new[] { 200.0, 20.0 } }, ["radius"] = 1, ["layer"] = "Background" })));
        Assert.Contains("200,200: #00FF00", sampled);
        Assert.Contains("200,20: #FF0000", sampled);
        var gridded = await Pumped(client.CallToolAsync("render", new Dictionary<string, object?> { ["maxSide"] = 200, ["grid"] = 100 }));
        Assert.Contains("grid every 100 px", Text(gridded));
        using (var view = SKBitmap.Decode(Assert.Single(gridded.Content.OfType<ImageContentBlock>()).DecodedData.ToArray()))
        {
            Assert.NotEqual(SKColors.Red, view.GetPixel(50, 60));                           // The line at canvas x 100 darkens the red.
            Assert.Equal(SKColors.Red, view.GetPixel(60, 60));
        }
        var part = await Pumped(client.CallToolAsync("render", new Dictionary<string, object?> { ["x"] = 0, ["y"] = 0, ["width"] = 200, ["height"] = 150 }));
        Assert.Contains("200×150 px view", Text(part));
        Assert.Contains("region at 0,0 size 200×150", Text(part));
        var halfRegion = await Pumped(client.CallToolAsync("render", new Dictionary<string, object?> { ["x"] = 10, ["width"] = 20 }));
        Assert.Equal(true, halfRegion.IsError);

        var shape = await Pumped(client.CallToolAsync("add_shape", new Dictionary<string, object?> { ["kind"] = "ellipse", ["x"] = 10, ["y"] = 10, ["width"] = 100, ["height"] = 50, ["color"] = "#FF00FF" }));
        Assert.Matches("Added ellipse \"Ellipse( \\d+)?\" at 10,10 size 100×50, now active\\.", Text(shape));
        Assert.Equal(ShapeKind.Ellipse, session.ActiveLayer!.Shape!.Kind);
        var line = await Pumped(client.CallToolAsync("add_line", new Dictionary<string, object?> { ["x1"] = 0, ["y1"] = 0, ["x2"] = 100, ["y2"] = 100, ["width"] = 6 }));
        Assert.Matches("Added line \"Line( \\d+)?\" from 0,0 to 100,100, now active\\.", Text(line));
        Assert.Equal(6, session.ActiveLayer!.Shape!.LineWidth);

        // Adjustments and filters.
        var inverted = await Pumped(client.CallToolAsync("adjust_invert", new Dictionary<string, object?> { ["layer"] = "Background" }));
        Assert.Equal("Applied Invert to \"Background\".", Text(inverted));
        Assert.Equal(new SKColor(255, 0, 255), session.Document.Layers[0].Pixels!.GetPixel(200, 200));   // The lime stroke, inverted.
        Assert.Equal("Invert", session.History.UndoName);
        var asLayer = await Pumped(client.CallToolAsync("adjust_brightness_contrast", new Dictionary<string, object?> { ["contrast"] = 30, ["asLayer"] = true, ["layer"] = "Background" }));
        Assert.Matches("Added adjustment layer \"Brightness/Contrast( \\d+)?\" above \"Background\", now active\\.", Text(asLayer));
        Assert.Equal(30, ((BrightnessContrastAdjustment)session.ActiveLayer!.Adjustment!).Contrast);
        var looked = await Pumped(client.CallToolAsync("adjust_color_lookup", new Dictionary<string, object?> { ["look"] = "Vivid Slide", ["amount"] = 80, ["asLayer"] = true, ["layer"] = "Background" }));
        Assert.Equal("Added adjustment layer \"Vivid Slide\" above \"Background\", now active.", Text(looked));
        var lookup = Assert.IsType<ColorLookupAdjustment>(session.ActiveLayer!.Adjustment);
        Assert.Equal((Looks.Find("Vivid Slide")!.Id, 80d), (lookup.LatticeId, lookup.Amount));
        Assert.Equal("Undid New Adjustment Layer.", Text(await Pumped(client.CallToolAsync("undo"))));       // One step, name and all.
        var badLook = await Pumped(client.CallToolAsync("adjust_color_lookup", new Dictionary<string, object?> { ["look"] = "Kodachrome" }));
        Assert.Equal(true, badLook.IsError);
        Assert.Contains("Fine Mono", Text(badLook));
        var noFile = await Pumped(client.CallToolAsync("adjust_color_lookup", new Dictionary<string, object?> { ["look"] = Path.Combine(Path.GetTempPath(), "composa-missing-look.cube") }));
        Assert.Equal(true, noFile.IsError);
        Assert.Contains("no file", Text(noFile));
        var onLive = await Pumped(client.CallToolAsync("adjust_exposure", new Dictionary<string, object?> { ["exposure"] = 1, ["layer"] = "Greeting" }));
        Assert.Equal(true, onLive.IsError);
        Assert.Contains("asLayer", Text(onLive));
        var blurred = await Pumped(client.CallToolAsync("filter_blur", new Dictionary<string, object?> { ["radius"] = 6, ["layer"] = "Background" }));
        Assert.Equal("Applied Gaussian Blur to \"Background\".", Text(blurred));
        Assert.Equal("Gaussian Blur", session.History.UndoName);
        var painted = await Pumped(client.CallToolAsync("filter_painterly", new Dictionary<string, object?> { ["style"] = "colorist wash", ["brushSize"] = 12, ["layer"] = "Background", ["seed"] = 5 }));
        Assert.Equal("Applied Painterly to \"Background\".", Text(painted));
        Assert.Equal("Painterly", session.History.UndoName);
        Assert.Equal("Undid Painterly.", Text(await Pumped(client.CallToolAsync("undo"))));
        var badStyle = await Pumped(client.CallToolAsync("filter_painterly", new Dictionary<string, object?> { ["style"] = "cubist" }));
        Assert.Equal(true, badStyle.IsError);
        var dithered = await Pumped(client.CallToolAsync("filter_dither", new Dictionary<string, object?> { ["style"] = "halftone dots", ["pixelSize"] = 1, ["colors"] = "two_colors", ["dark"] = "#102030", ["light"] = "#FFE080", ["layer"] = "Background" }));
        Assert.Equal("Applied Dither to \"Background\".", Text(dithered));
        Assert.Equal("Dither", session.History.UndoName);
        Assert.Equal("Undid Dither.", Text(await Pumped(client.CallToolAsync("undo"))));
        var badDither = await Pumped(client.CallToolAsync("filter_dither", new Dictionary<string, object?> { ["style"] = "atkinson", ["colors"] = "sepia" }));
        Assert.Equal(true, badDither.IsError);
        var badRange = await Pumped(client.CallToolAsync("adjust_hue_saturation", new Dictionary<string, object?> { ["hue"] = 30, ["range"] = "purples" }));
        Assert.Equal(true, badRange.IsError);
        var byColor = await Pumped(client.CallToolAsync("select_color_range", new Dictionary<string, object?> { ["colors"] = new[] { "#808080" }, ["fuzziness"] = 200 }));
        Assert.StartsWith("Selected the area", Text(byColor));
        Assert.Equal("Color Range", session.History.UndoName);
        var noneNear = await Pumped(client.CallToolAsync("select_color_range", new Dictionary<string, object?> { ["colors"] = new[] { "#010203" }, ["fuzziness"] = 0 }));
        Assert.StartsWith("No pixels are near", Text(noneNear));
        Assert.Null(session.Selection);

        // Selections.
        var rect = await Pumped(client.CallToolAsync("select_shape", new Dictionary<string, object?> { ["kind"] = "rectangle", ["x"] = 0, ["y"] = 0, ["width"] = 100, ["height"] = 80 }));
        Assert.Equal("Selected the area at 0,0 size 100×80.", Text(rect));
        Assert.Contains("selection at 0,0 size 100×80", Text(await Pumped(client.CallToolAsync("describe_document"))));
        await Pumped(client.CallToolAsync("fill_layer", new Dictionary<string, object?> { ["color"] = "#0000FF" }));                     // Background is active: fills inside the selection only.
        Assert.Equal(SKColors.Blue, session.Document.Layers[0].Pixels!.GetPixel(50, 40));
        Assert.NotEqual(SKColors.Blue, session.Document.Layers[0].Pixels!.GetPixel(300, 200));
        var traced = Text(await Pumped(client.CallToolAsync("trace_edges", new Dictionary<string, object?> { ["layer"] = "Background", ["minLength"] = 30, ["maxLines"] = 5 })));
        Assert.Matches("^[1-5] edges, longest first", traced);                               // The blue rectangle's outline, at least.
        Assert.Matches(@"\n\d+,\d+ \d+,\d+", traced);
        Assert.Equal("Selected the area at 0,0 size 110×90.", Text(await Pumped(client.CallToolAsync("modify_selection", new Dictionary<string, object?> { ["expand"] = 10 }))));
        Assert.Equal("Selected the area at 20,0 size 110×90.", Text(await Pumped(client.CallToolAsync("modify_selection", new Dictionary<string, object?> { ["moveX"] = 20 }))));
        var polygon = await Pumped(client.CallToolAsync("select_shape", new Dictionary<string, object?> { ["kind"] = "polygon", ["points"] = new[] { new[] { 200.0, 100.0 }, new[] { 300.0, 100.0 }, new[] { 250.0, 200.0 } }, ["mode"] = "add" }));
        Assert.Equal("Selected the area at 20,0 size 280×200.", Text(polygon));
        Assert.Equal("Selected the area at 0,0 size 400×300.", Text(await Pumped(client.CallToolAsync("select_inverse"))));
        Assert.Equal("Nothing is selected.", Text(await Pumped(client.CallToolAsync("deselect"))));
        Assert.Null(session.Document.Selection);
        Assert.Equal("Selected the area at 0,0 size 400×300.", Text(await Pumped(client.CallToolAsync("select_all"))));
        var wand = await Pumped(client.CallToolAsync("select_wand", new Dictionary<string, object?> { ["x"] = 50, ["y"] = 40, ["tolerance"] = 10, ["allLayers"] = false }));
        Assert.Equal("Selected the area at 0,0 size 100×80.", Text(wand));                            // The blue rectangle filled above, on the active Background.
        Assert.Equal(32, session.WandTolerance);                                                      // The tool's own tolerance is put back.
        var badMode = await Pumped(client.CallToolAsync("select_all", new Dictionary<string, object?> { ["document"] = 7 }));
        Assert.Equal(true, badMode.IsError);
        Assert.Equal("Nothing is selected.", Text(await Pumped(client.CallToolAsync("deselect"))));

        var unknown = await Pumped(client.CallToolAsync("set_layer", new Dictionary<string, object?> { ["layer"] = "Nope", ["visible"] = true }));
        Assert.Equal(true, unknown.IsError);
        Assert.Contains("no layer", Text(unknown));

        // Files: save, export and open.
        var folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"composa-files-{Guid.NewGuid():N}")).FullName;
        try
        {
            var never = await Pumped(client.CallToolAsync("save_document"));
            Assert.Equal(true, never.IsError);
            Assert.Contains("never been saved", Text(never));
            var notProject = await Pumped(client.CallToolAsync("save_document", new Dictionary<string, object?> { ["path"] = Path.Combine(folder, "work.png") }));
            Assert.Equal(true, notProject.IsError);
            Assert.Contains("export_image", Text(notProject));
            var relative = await Pumped(client.CallToolAsync("save_document", new Dictionary<string, object?> { ["path"] = "work.cmps" }));
            Assert.Equal(true, relative.IsError);
            var project = Path.Combine(folder, "work.cmps");
            Assert.Equal($"Saved \"work\" to {project}.", Text(await Pumped(client.CallToolAsync("save_document", new Dictionary<string, object?> { ["path"] = project }))));
            Assert.True(File.Exists(project));
            Assert.Equal(project, session.FilePath);
            Assert.False(session.IsModified);
            Assert.False(window.IsSaving(session));
            await Pumped(client.CallToolAsync("new_layer", new Dictionary<string, object?> { ["name"] = "Later" }));
            Assert.True(session.IsModified);
            Assert.Contains("unsaved changes", Text(await Pumped(client.CallToolAsync("list_documents"))));
            Assert.Equal($"Saved \"work\" to {project}.", Text(await Pumped(client.CallToolAsync("save_document"))));   // Its own file is replaced without asking.
            Assert.False(session.IsModified);
            var copy = Path.Combine(folder, "copy.cmps");
            File.WriteAllText(copy, "in the way");
            var taken = await Pumped(client.CallToolAsync("save_document", new Dictionary<string, object?> { ["path"] = copy }));
            Assert.Equal(true, taken.IsError);
            Assert.Contains("overwrite", Text(taken));
            Assert.NotEqual(true, (await Pumped(client.CallToolAsync("save_document", new Dictionary<string, object?> { ["path"] = copy, ["overwrite"] = true }))).IsError);
            Assert.Equal(copy, session.FilePath);
            Assert.Equal("copy", session.Title);

            var badExtension = await Pumped(client.CallToolAsync("export_image", new Dictionary<string, object?> { ["path"] = Path.Combine(folder, "flat.xyz") }));
            Assert.Equal(true, badExtension.IsError);
            Assert.Contains(".tiff", Text(badExtension));
            var jpeg = Path.Combine(folder, "flat.jpg");
            Assert.Equal($"Exported \"copy\" as a 400×300 px JPEG to {jpeg}.", Text(await Pumped(client.CallToolAsync("export_image", new Dictionary<string, object?> { ["path"] = jpeg, ["quality"] = 80 }))));
            using (var exported = SKBitmap.Decode(jpeg)) { Assert.Equal(400, exported.Width); Assert.Equal(300, exported.Height); }
            var occupied = await Pumped(client.CallToolAsync("export_image", new Dictionary<string, object?> { ["path"] = jpeg }));
            Assert.Equal(true, occupied.IsError);
            Assert.Contains("overwrite", Text(occupied));
            Assert.NotEqual(true, (await Pumped(client.CallToolAsync("export_image", new Dictionary<string, object?> { ["path"] = jpeg, ["overwrite"] = true }))).IsError);
            Assert.False(session.IsModified);                                                       // Exporting is not a change.

            var cube = Path.Combine(folder, "look.cube");
            var badSize = await Pumped(client.CallToolAsync("export_look", new Dictionary<string, object?> { ["path"] = cube, ["size"] = 40 }));
            Assert.Equal(true, badSize.IsError);
            Assert.Contains("17, 33, 65", Text(badSize));
            var notCube = await Pumped(client.CallToolAsync("export_look", new Dictionary<string, object?> { ["path"] = Path.Combine(folder, "look.3dl") }));
            Assert.Equal(true, notCube.IsError);
            var look = await Pumped(client.CallToolAsync("export_look", new Dictionary<string, object?> { ["path"] = cube, ["size"] = 17 }));
            Assert.StartsWith($"Exported the look of \"copy\" as a 17-point .cube to {cube}, baked from \"Brightness/Contrast", Text(look));   // The contrast layer added above.
            Assert.Equal(17, ColorLattice.Load(cube).Size);
            Assert.False(session.IsModified);
            var cubeTaken = await Pumped(client.CallToolAsync("export_look", new Dictionary<string, object?> { ["path"] = cube }));
            Assert.Equal(true, cubeTaken.IsError);
            Assert.Contains("overwrite", Text(cubeTaken));
            var fromFile = await Pumped(client.CallToolAsync("adjust_color_lookup", new Dictionary<string, object?> { ["look"] = cube, ["layer"] = "Background" }));
            Assert.Equal("Applied Color Lookup to \"Background\".", Text(fromFile));
            Assert.Equal("Undid Color Lookup.", Text(await Pumped(client.CallToolAsync("undo"))));

            var again = await Pumped(client.CallToolAsync("open_document", new Dictionary<string, object?> { ["path"] = copy }));
            Assert.Equal("Document 1 \"copy\" was already open, now active.", Text(again));
            Assert.Single(window.Sessions);
            var flat = await Pumped(client.CallToolAsync("open_document", new Dictionary<string, object?> { ["path"] = jpeg }));
            Assert.Equal($"Opened document 2: \"flat\" 400×300 px, 1 layers, now active.", Text(flat));
            Assert.Equal(2, window.Sessions.Count);
            Assert.Same(window.Sessions[1], window.Session);
            var reopened = await Pumped(client.CallToolAsync("open_document", new Dictionary<string, object?> { ["path"] = project }));
            Assert.Equal("Opened document 3: \"work\" 400×300 px, 6 layers, now active.", Text(reopened));  // Saved with "Later", before the save to copy.cmps.
            Assert.False(window.Sessions[2].IsModified);
            var missing = await Pumped(client.CallToolAsync("open_document", new Dictionary<string, object?> { ["path"] = Path.Combine(folder, "nothing.png") }));
            Assert.Equal(true, missing.IsError);
            var broken = Path.Combine(folder, "broken.cmps");
            File.WriteAllText(broken, "not a project");
            var unreadable = await Pumped(client.CallToolAsync("open_document", new Dictionary<string, object?> { ["path"] = broken }));
            Assert.Equal(true, unreadable.IsError);
            Assert.Contains("Couldn't open broken.cmps", Text(unreadable));
            Assert.Equal(3, window.Sessions.Count);

            // A GIMP file opens when nothing needs converting; one that would ask the person is refused with what it would ask.
            var plain = new XcfWriter { Version = 11, Width = 30, Height = 20 };
            plain.Layers.Add(new XcfWriterLayer { Name = "Only", Width = 30, Height = 20 }.Filled(SKColors.Olive));
            var plainPath = Path.Combine(folder, "plain.xcf");
            File.WriteAllBytes(plainPath, plain.Build());
            var opened = await Pumped(client.CallToolAsync("open_document", new Dictionary<string, object?> { ["path"] = plainPath }));
            Assert.NotEqual(true, opened.IsError);
            Assert.Contains("\"plain\"", Text(opened));
            Assert.Equal("Only", Assert.Single(window.Session!.Document.Layers).Name);
            var asks = new XcfWriter { Version = 11, Width = 30, Height = 20 };
            asks.Layers.Add(new XcfWriterLayer { Name = "Dissolved", Width = 30, Height = 20, Mode = 1 }.Filled(SKColors.Olive));
            var asksPath = Path.Combine(folder, "asks.xcf");
            File.WriteAllBytes(asksPath, asks.Build());
            var asksRefused = await Pumped(client.CallToolAsync("open_document", new Dictionary<string, object?> { ["path"] = asksPath }));
            Assert.Equal(true, asksRefused.IsError);
            Assert.Contains("Dissolved: Blend mode \"Dissolve\"", Text(asksRefused));
        }
        finally { Directory.Delete(folder, recursive: true); }

        await client.DisposeAsync();
        await Pumped(() => host.Connections == 0);
    }

    [AvaloniaFact]
    public async Task The_bridge_outlives_the_application_and_announces_its_tools_each_time_it_is_back()
    {
        var pipe = PipeName();
        await using var client = await McpClient.CreateAsync(Bridge(pipe));                 // Nothing listens yet.
        var changes = 0;
        var resourceChanges = 0;
        await using var handler = client.RegisterNotificationHandler(NotificationMethods.ToolListChangedNotification, (_, _) => { Interlocked.Increment(ref changes); return default; });
        await using var resourceHandler = client.RegisterNotificationHandler(NotificationMethods.ResourceListChangedNotification, (_, _) => { Interlocked.Increment(ref resourceChanges); return default; });
        Assert.True(client.ServerCapabilities.Tools?.ListChanged);
        Assert.True(client.ServerCapabilities.Resources?.ListChanged);
        Assert.Empty(await client.ListToolsAsync());
        Assert.Empty(await client.ListResourcesAsync());
        Assert.Empty(await client.ListResourceTemplatesAsync());
        var away = await Assert.ThrowsAnyAsync<McpException>(async () => await client.CallToolAsync("list_documents"));
        Assert.Contains("not running", away.Message);

        var window = new MainWindow { Width = 1000, Height = 700 };
        window.Show();
        var first = new McpHost(window, pipe);
        Assert.True(await first.StartAsync());
        await Pumped(() => changes >= 1);
        Assert.Contains("new_document", (await client.ListToolsAsync()).Select(t => t.Name));
        Assert.Contains("Created document 1", Text(await Pumped(client.CallToolAsync("new_document", new Dictionary<string, object?> { ["width"] = 64, ["height"] = 64 }))));

        first.Dispose();                                                                     // Composa quits.
        await Pumped(() => changes >= 2);
        Assert.Empty(await client.ListToolsAsync());

        using var second = new McpHost(window, pipe);                                        // Composa is started again.
        Assert.True(await second.StartAsync());
        await Pumped(() => changes >= 3);
        Assert.Equal(changes, resourceChanges);                                              // Resources are announced alongside the tools.
        Assert.Contains("1: \"Untitled\" 64×64 px", Text(await Pumped(client.CallToolAsync("list_documents"))));
        Assert.Contains("composa://documents", (await client.ListResourcesAsync()).Select(r => r.Uri));
    }

    [Fact]
    public async Task The_bridge_starts_the_application_once_when_asked_to_and_nothing_answers()
    {
        var toBridge = new AnonymousPipeServerStream(PipeDirection.Out);
        using var fromBridge = new AnonymousPipeServerStream(PipeDirection.In);
        var launched = 0;
        var bridge = new McpBridge(PipeName(),
            new StreamReader(new AnonymousPipeClientStream(PipeDirection.In, toBridge.ClientSafePipeHandle)),
            new StreamWriter(new AnonymousPipeClientStream(PipeDirection.Out, fromBridge.ClientSafePipeHandle)) { AutoFlush = true },
            () => Interlocked.Increment(ref launched));
        var run = bridge.RunAsync();
        for (var i = 0; i < 500 && Volatile.Read(ref launched) == 0; i++) await Task.Delay(10);
        await Task.Delay(1500);                                                              // Several more attempts fail meanwhile.
        Assert.Equal(1, launched);
        toBridge.Dispose();                                                                  // The client closes its end.
        Assert.Equal(0, await run);
    }

    [AvaloniaFact]
    public async Task A_second_window_leaves_the_pipe_to_the_first()
    {
        var window = new MainWindow { Width = 1000, Height = 700 };
        window.Show();
        using var first = new McpHost(window, PipeName());
        Assert.True(await first.StartAsync());
        using var second = new McpHost(window, first.PipeName);
        Assert.False(await second.StartAsync());
        await Pumped(() => first.Connections == 0);                       // The probe's connection has gone again.
    }
}
