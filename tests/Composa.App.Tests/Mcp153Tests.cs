using System.Text.Json;
using Avalonia.Headless.XUnit;
using Composa.App.Mcp;
using Composa.Core.Tests;
using Composa.Editing;
using Composa.Filters;
using Composa.IO;
using Composa.Model;
using Composa.Rendering;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using SkiaSharp;

namespace Composa.App.Tests;

public partial class McpTests
{
    private static readonly string[] NewTools = ["liquify", "filter_progressive_blur", "filter_scanlines", "filter_camera_raw", "get_camera_raw_settings", "repeat_last_filter", "content_aware_fill",
        "convert_layer_depth", "layer_mask", "group_layers", "ungroup_layers", "canvas_size", "crop_canvas", "trim_canvas", "rotate_canvas", "set_layer_effects", "set_text", "set_shape",
        "inspect_import", "import_images", "get_capabilities", "set_print_proof", "render_view", "render_mask", "get_color_scopes", "set_workspace", "list_commands", "set_shortcut", "get_preferences", "set_preferences",
        "get_history", "go_to_history", "redo", "clone_stroke", "draw_gradient", "get_layer_details", "clipboard", "get_update_notice"];

    private static async Task<CallToolResult> Call(McpClient client,string name,Dictionary<string,object?>? arguments = null)
    {
        var result = await Pumped(client.CallToolAsync(name,arguments));
        Assert.True(result.IsError != true, $"{name}: {Text(result)}"); return result;
    }

    [AvaloniaFact]
    public async Task New_tools_are_discoverable_and_liquify_changes_pixels_with_undo_and_batch_parity()
    {
        var window = new MainWindow { Width = 1000,Height = 700 }; window.Show();
        using var host = new McpHost(window,PipeName()); Assert.True(await host.StartAsync());
        await using var client = await Pumped(McpClient.CreateAsync(Bridge(host.PipeName)));
        var listed = await client.ListToolsAsync();
        Assert.All(NewTools,name => Assert.Contains(listed,t => t.Name == name));
        var schema = listed.Single(t => t.Name == "paint_stroke").JsonSchema.GetRawText(); Assert.Contains("liquify",schema); Assert.Contains("blurRadius",schema);
        var capabilities = Text(await Call(client,"get_capabilities")); Assert.Contains("filter_camera_raw",capabilities);
        await Call(client,"new_document",new() { ["width"] = 64,["height"] = 32 });
        await Call(client,"draw_gradient",new() { ["startX"] = 0,["startY"] = 0,["endX"] = 64,["endY"] = 0 });
        var s = window.Session!; var original = s.ActiveLayer!.Pixels!; var value = original.GetPixel(32,16);
        var path = new double[][] { [20,16],[28,16],[36,16],[44,16] };
        await Call(client,"liquify",new() { ["points"] = path,["size"] = 28,["strength"] = 1 });
        Assert.Equal("Liquify",s.History.UndoName); Assert.NotEqual(value,s.ActiveLayer!.Pixels!.GetPixel(32,16)); Assert.Equal(value,original.GetPixel(32,16));
        await Call(client,"undo"); Assert.Same(original,s.ActiveLayer!.Pixels);
        await Call(client,"paint_strokes",new() { ["strokes"] = new[] { new { points = path,mode = "liquify",size = 28,opacity = 1 } } });
        Assert.NotEqual(value,s.ActiveLayer!.Pixels!.GetPixel(32,16)); await Call(client,"undo");
        var noStroke = s.History.CurrentId;
        var bad = await Pumped(client.CallToolAsync("liquify",new Dictionary<string,object?> { ["points"] = new[] { new[] { 1,1 } } }));
        Assert.True(bad.IsError); Assert.Equal(noStroke,s.History.CurrentId);
        await Call(client,"paint_stroke",new() { ["mode"] = "blur",["points"] = path,["blurRadius"] = 2 });
        await Call(client,"paint_stroke",new() { ["mode"] = "heal",["points"] = path,["size"] = 6 });
        await Call(client,"clone_stroke",new() { ["points"] = path,["sourceX"] = 2,["sourceY"] = 2 });
        Assert.Null(s.CloneSource);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Raw_grades_spatial_filters_repeat_and_masks_use_the_same_session_history()
    {
        var window = new MainWindow { Width = 1000,Height = 700 }; window.Show();
        using var host = new McpHost(window,PipeName()); Assert.True(await host.StartAsync());
        await using var client = await Pumped(McpClient.CreateAsync(Bridge(host.PipeName)));
        await Call(client,"new_document",new() { ["width"] = 32,["height"] = 24,["background"] = "#806040" });
        await Call(client,"convert_layer_depth",new() { ["depth"] = 16 });
        var s = window.Session!; var original = s.ActiveLayer!.Pixels!;
        using var defaults = JsonDocument.Parse(Text(await Call(client,"get_camera_raw_settings")));
        var grade = JsonSerializer.Deserialize<Dictionary<string,object?>>(defaults.RootElement.GetRawText())!; grade["exposure"] = 1; grade["shadows"] = 20;
        await Call(client,"filter_camera_raw",new() { ["settings"] = grade,["disabledGroups"] = new[] { "Detail" } });
        Assert.True(s.ActiveLayer!.Pixels!.GetPixel(2,2).Red > original.GetPixel(2,2).Red); Assert.True(DeepPixels.HasSource(s.ActiveLayer.Pixels));
        Assert.Equal("Camera Raw Filter",s.History.UndoName);
        var beforeRepeat = s.ActiveLayer.Pixels.GetPixel(2,2); await Call(client,"repeat_last_filter"); Assert.True(s.ActiveLayer.Pixels!.GetPixel(2,2).Red > beforeRepeat.Red);
        await Call(client,"undo"); await Call(client,"undo"); Assert.Same(original,s.ActiveLayer!.Pixels);
        await Call(client,"filter_camera_raw",new() { ["settings"] = grade,["resetGroups"] = new[] { "Light" } }); Assert.Equal(original.GetPixel(2,2),s.ActiveLayer!.Pixels!.GetPixel(2,2));
        await Call(client,"filter_progressive_blur",new() { ["startX"] = 0,["startY"] = 0,["endX"] = 32,["endY"] = 24,["toRadius"] = 2 });
        Assert.False(s.IsPreviewing); Assert.Equal(FilterSettings.DisplayName(FilterKind.ProgressiveBlur),s.History.UndoName);
        await Call(client,"filter_scanlines",new() { ["glow"] = 10,["dots"] = 40,["split"] = 2,["displace"] = 4,["blackLevel"] = 8 });
        Assert.Equal(FilterSettings.DisplayName(FilterKind.Scanlines),s.History.UndoName);
        var alpha = s.ActiveLayer!.Pixels!.GetPixel(2,2).Alpha;
        await Call(client,"filter_blur",new() { ["radius"] = 1,["preserveTransparency"] = true }); Assert.Equal(alpha,s.ActiveLayer.Pixels!.GetPixel(2,2).Alpha);
        await Call(client,"select_shape",new() { ["kind"] = "rectangle",["x"] = 0,["y"] = 0,["width"] = 12,["height"] = 24 });
        await Call(client,"layer_mask",new() { ["action"] = "add",["inverse"] = true });
        Assert.True(s.IsEditingMask); Assert.Equal((byte)0,s.ActiveLayer!.Mask!.GetPixel(2,2).Alpha); Assert.Equal((byte)255,s.ActiveLayer.Mask.GetPixel(20,2).Alpha);
        await Call(client,"layer_mask",new() { ["action"] = "solo" }); Assert.Equal(s.ActiveLayer.Id,s.SoloMaskLayerId);
        Assert.Single((await Call(client,"render_mask",new() { ["layer"] = s.ActiveLayer.Name })).Content.OfType<ImageContentBlock>());
        await Call(client,"layer_mask",new() { ["action"] = "pixels" }); Assert.Null(s.SoloMaskLayerId); Assert.False(s.IsEditingMask);
        await Call(client,"layer_mask",new() { ["action"] = "disable" }); Assert.False(s.ActiveLayer.MaskEnabled);
        var scopes = await Call(client,"get_color_scopes"); Assert.Single(scopes.Content.OfType<ImageContentBlock>());
        window.Close();
    }

    [AvaloniaFact]
    public async Task Native_import_export_print_color_and_folders_work_over_stdio()
    {
        var folder = Path.Combine(Path.GetTempPath(),"composa-mcp153-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        var window = new MainWindow { Width = 1000,Height = 700 }; window.Show();
        using var host = new McpHost(window,PipeName()); Assert.True(await host.StartAsync());
        await using var client = await Pumped(McpClient.CreateAsync(Bridge(host.PipeName)));
        try {
            var source = Path.Combine(folder,"source.png"); var wide = DeepPixels.New(16,8,true);
            for (var y = 0;y<8;y++) for (var x = 0;x<16;x++) DeepPixels.Write(wide,x,y,new SKColorF(2+x/100f,.3f,.2f,1));
            using var preview = DeepPixels.Preview(wide); ImageFiles.Save(preview,source,ExportFormat.Png16);
            await Call(client,"open_document",new() { ["path"] = source });
            var s = window.Session!; Assert.True(DeepPixels.HasSource(s.ActiveLayer!.Pixels!));
            var png = Path.Combine(folder,"export.png"); await Call(client,"export_image",new() { ["path"] = png,["bitDepth"] = 16 });
            Assert.Equal((byte)16,File.ReadAllBytes(png)[24]);
            var tiff = Path.Combine(folder,"export.tiff"); await Call(client,"export_image",new() { ["path"] = tiff,["bitDepth"] = 16,["width"] = 8 });
            using (var magick = new ImageMagick.MagickImage(tiff)) { Assert.Equal(16u,magick.Depth); Assert.Equal(8u,magick.Width); }
            var exr = Path.Combine(folder,"export.exr"); await Call(client,"export_image",new() { ["path"] = exr }); using (var decoded = ImageFiles.Load(exr)) Assert.True(DeepPixels.IsHdr(DeepPixels.Source(decoded)!));
            await Call(client,"set_print_proof",new() { ["proofColors"] = true,["gamutWarning"] = true }); Assert.True(s.View.ProofColors);
            var jpg = Path.Combine(folder,"cmyk.jpg"); await Call(client,"export_image",new() { ["path"] = jpg,["colorMode"] = "cmyk" });
            using (var magick = new ImageMagick.MagickImage(jpg)) { Assert.Equal(ImageMagick.ColorSpace.CMYK,magick.ColorSpace); Assert.NotNull(magick.GetColorProfile()); }
            await Call(client,"render_view");
            // A decoded-frame fixture exercises RAW routing/develop/Camera Raw without redistributing a camera file.
            // ImageMagick recognizes the PNG signature; the .dng suffix deliberately selects the app's RAW workflow.
            var raw = Path.Combine(folder,"decoded-frame.dng"); File.Copy(source,raw);
            await Call(client,"open_document",new() { ["path"] = raw,["rawSettings"] = new { exposure = -1,colorNoise = 10,luminanceNoise = 10,highlightRecovery = 20,lensDistortion = 1 },["cameraRaw"] = new { exposure = .25 } });
            Assert.True(DeepPixels.HasSource(window.Session!.ActiveLayer!.Pixels!)); Assert.True(window.Session.ActiveLayer.Pixels!.GetPixel(4,4).Red < 255);
            var look = Path.Combine(folder,"grade.cube"); await Call(client,"export_look",new() { ["path"] = look,["cameraRaw"] = new { exposure = 1,shadows = 20 } });
            Assert.Contains("LUT_3D_SIZE",File.ReadAllText(look));
            var psd = Path.Combine(folder,"import.psd"); var writer = new PsdWriter { Width = 16,Height = 8,Composite = preview };
            File.WriteAllBytes(psd,writer.Build()); await Call(client,"inspect_import",new() { ["path"] = psd }); await Call(client,"open_document",new() { ["path"] = psd,["allowConversions"] = true });
            var images = Path.Combine(folder,"inputs"); Directory.CreateDirectory(images); File.Copy(source,Path.Combine(images,"a.png"));
            using (var larger = Pixels.NewColor(32,24)) { larger.Erase(SKColors.Blue); ImageFiles.Save(larger,Path.Combine(images,"b.png"),ExportFormat.Png); }
            await Call(client,"import_images",new() { ["paths"] = new[] { images },["newDocument"] = true });
            Assert.Equal((32,24),(window.Session!.Document.Width,window.Session.Document.Height)); Assert.Equal(2,window.Session.Document.Layers.Count);
        } finally { window.Close(); Directory.Delete(folder,true); }
    }

    [AvaloniaFact]
    public async Task Workspace_live_content_preferences_and_shortcuts_are_controllable_without_dialogs()
    {
        var window = new MainWindow { Width = 1000,Height = 700 }; window.Show();
        using var host = new McpHost(window,PipeName()); Assert.True(await host.StartAsync());
        await using var client = await Pumped(McpClient.CreateAsync(Bridge(host.PipeName)));
        await Call(client,"new_document",new() { ["width"] = 1,["height"] = 1,["unit"] = "inches",["resolution"] = 300 }); var first = window.Session!;
        Assert.Equal((300,300,300d),(first.Document.Width,first.Document.Height,first.Document.Resolution));
        await Call(client,"add_shape",new() { ["kind"] = "rounded",["x"] = 20,["y"] = 20,["width"] = 40,["height"] = 30,["smoothCorners"] = true }); var shape = first.ActiveLayer!;
        Assert.True(shape.Shape!.SmoothCorners);
        await Call(client,"set_shape",new() { ["layer"] = shape.Name,["color"] = "#FF0000",["cornerRadius"] = 12 });
        await Call(client,"set_layer_effects",new() { ["layer"] = shape.Name,["effects"] = new { shadow = new { opacity = .2,distance = 3 } } });
        await Call(client,"transform_layer",new() { ["layer"] = shape.Name,["widthPercent"] = 200,["heightPercent"] = 150 }); Assert.NotNull(shape.Shape); Assert.NotNull(shape.Effects);
        await Call(client,"add_text",new() { ["text"] = "Before",["x"] = 10,["y"] = 10 }); var text = first.ActiveLayer!;
        await Call(client,"set_text",new() { ["layer"] = text.Name,["text"] = "After",["color"] = "#00FF00" }); Assert.Equal("After",text.Text!.Text);
        await Call(client,"canvas_size",new() { ["width"] = 320,["height"] = 310 }); Assert.NotNull(shape.Shape); Assert.NotNull(text.Text);
        await Call(client,"crop_canvas",new() { ["x"] = 0,["y"] = 0,["width"] = 300,["height"] = 300 });
        await Call(client,"rotate_canvas",new() { ["degrees"] = 90 });
        await Call(client,"new_document",new() { ["width"] = 32,["height"] = 24 });
        await Call(client,"set_workspace",new() { ["document"] = 1,["position"] = 2,["zoom"] = 3,["autoSelect"] = false,["rulers"] = true }); Assert.Same(first,window.Session); Assert.Same(first,window.Sessions[1]); Assert.Equal(3,window.Canvas.Zoom);
        await Call(client,"set_preferences",new() { ["language"] = "ja" }); Assert.Equal("ja",window.Settings.Language);
        var commands = Text(await Call(client,"list_commands",new() { ["search"] = "Delete" })); using var parsed = JsonDocument.Parse(commands); var id = parsed.RootElement[0].GetProperty("id").GetString()!;
        await Call(client,"set_shortcut",new() { ["id"] = id,["gesture"] = "" }); Assert.Equal("",window.Settings.Shortcuts[id]);
        await Call(client,"get_layer_details",new() { ["layer"] = shape.Name });
        await Call(client,"get_history"); var current = first.History.CurrentIndex; await Call(client,"go_to_history",new() { ["index"] = current-1 }); await Call(client,"redo"); Assert.Equal(current,first.History.CurrentIndex);
        await Call(client,"group_layers",new() { ["layers"] = new[] { shape.Name,text.Name } }); Assert.True(first.ActiveLayer!.IsGroup);
        await Call(client,"ungroup_layers",new() { ["layer"] = first.ActiveLayer.Name }); Assert.DoesNotContain(first.Document.Layers,l => l.IsGroup);
        window.Close();
    }
}
