using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Composa.Editing;
using Composa.Filters;
using Composa.Rendering;
using SkiaSharp;

namespace Composa.App.Tests;

public class PrecisionWorkspaceTests
{
    [AvaloniaFact]
    public void Shader_preview_matches_measured_color_without_changing_the_document()
    {
        var window = new MainWindow { Width = 1280, Height = 800 }; window.Show();
        var session = EditorSession.NewCanvas(200, 120, new SKColor(60, 100, 150)); window.AddSession(session);
        try
        {
            Dispatcher.UIThread.RunJobs(); var original = session.ActiveLayer!.Pixels!;
            var settings = new CameraRawSettings { Exposure = .5, Contrast = 20, Temperature = 12, Saturation = 15 };
            session.BeginFilter(FilterKind.CameraRaw);
            Assert.True(window.Canvas.TryCameraRawPreview(session, original, settings));
            Assert.Same(original, session.ActiveLayer!.Pixels);
            Assert.True(Screenshots.Save(window, "camera-raw-measured-shader"));
            using var screenshot = SKBitmap.Decode(Path.Combine(Screenshots.Folder, "camera-raw-measured-shader.png"));
            using var expected = CameraRawPixels.Apply(original, settings);
            var point = window.Canvas.TranslatePoint(window.Canvas.ToScreen(new SKPoint(61, 41)), window)!.Value;
            var shown = screenshot.GetPixel((int)Math.Round(point.X * window.RenderScaling), (int)Math.Round(point.Y * window.RenderScaling));
            var color = expected.GetPixel(61, 41);
            Assert.InRange(Math.Abs(shown.Red - color.Red), 0, 3); Assert.InRange(Math.Abs(shown.Green - color.Green), 0, 3); Assert.InRange(Math.Abs(shown.Blue - color.Blue), 0, 3);
            Assert.NotNull(window.Canvas.CameraRawScopePreview);
            Assert.False(window.Canvas.TryCameraRawPreview(session, original, settings with { Shadows = 20 }));
        }
        finally { window.Canvas.ClearCameraRawPreview(); session.CancelPreview(); window.Close(); }
    }

    [AvaloniaFact]
    public void Print_commands_and_delete_are_rebindable_without_hidden_redo_collisions()
    {
        var window = new MainWindow(); window.Show();
        try
        {
            window.AddSession(EditorSession.NewCanvas(20, 20));
            var menus = window.GetVisualDescendants().OfType<Menu>().Single();
            MenuItem Find(string name) => menus.Items.OfType<MenuItem>().SelectMany(top => Flatten(top)).Single(item => Equals(item.Header, name));
            Assert.Equal(new KeyGesture(Key.Y, KeyModifiers.Control), Find("Proof Colors").InputGesture);
            Assert.Equal(new KeyGesture(Key.Y, KeyModifiers.Control | KeyModifiers.Shift), Find("Gamut Warning").InputGesture);
            Assert.Equal(new KeyGesture(Key.Delete), Find("Clear").InputGesture);
            Assert.True(Screenshots.Save(window, "precision-workspace"));
        }
        finally { window.Close(); }

        static IEnumerable<MenuItem> Flatten(MenuItem item)
        { yield return item; foreach (var child in item.Items.OfType<MenuItem>()) foreach (var descendant in Flatten(child)) yield return descendant; }
    }

    [AvaloniaFact]
    public void Layer_depth_badges_and_conversion_commands_follow_undo()
    {
        var window = new MainWindow(); window.Show();
        var session = EditorSession.NewCanvas(120, 80, SKColors.Gray); window.AddSession(session);
        try
        {
            session.ConvertLayerDepth(session.ActiveLayer!, true); Dispatcher.UIThread.RunJobs();
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "16-bit");
            Assert.True(Screenshots.Save(window, "precision-layer-depth"));
            session.ConvertLayerDepth(session.ActiveLayer!, false); session.Undo();
            Assert.NotNull(DeepPixels.Source(session.ActiveLayer!.Pixels!));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Update_notice_displays_release_body_in_the_window()
    {
        var window = new MainWindow(); window.Show();
        try
        {
            window.UpdateNotice.Show(new ReleaseVersion(1, 5, 3, ""), false, "Added 16-bit layers and print proofing.");
            var details = window.UpdateNotice.GetVisualDescendants().OfType<Expander>().Single();
            Assert.True(details.IsVisible); details.IsExpanded = true;
            Assert.Contains(window.UpdateNotice.GetVisualDescendants().OfType<SelectableTextBlock>(), text => text.Text!.Contains("16-bit layers"));
            Assert.True(Screenshots.Save(window, "update-release-body"));
        }
        finally { window.Close(); }
    }
}
