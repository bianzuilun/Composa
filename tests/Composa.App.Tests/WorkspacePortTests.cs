using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Composa.App.Controls;
using Composa.App.Dialogs;
using Composa.Editing;
using Composa.Filters;
using SkiaSharp;

namespace Composa.App.Tests;

public class WorkspacePortTests
{
    [AvaloniaFact]
    public void Command_search_works_without_a_document_and_fullscreen_exits_on_escape()
    {
        var window = new MainWindow(); window.Show();
        try
        {
            window.KeyPressQwerty(PhysicalKey.F, RawInputModifiers.Control);
            Dispatcher.UIThread.RunJobs();
            var dialog = Assert.Single(window.OwnedWindows);
            Assert.Equal("Search Commands", dialog.Title);
            var search = dialog.GetVisualDescendants().OfType<TextBox>().Single();
            search.Text = "new canvas"; Dispatcher.UIThread.RunJobs();
            Assert.Single(dialog.GetVisualDescendants().OfType<ListBox>().Single().Items);
            dialog.Close(false); Dispatcher.UIThread.RunJobs();
            window.ToggleCanvasOnly();
            Assert.Equal(WindowState.FullScreen, window.WindowState);
            var root = (Grid)window.Content!;
            Assert.False(root.Children.Single(child => Grid.GetRow(child) == 2).IsVisible);
            Assert.False(root.Children.Single(child => Grid.GetRow(child) == 7).IsVisible);
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Assert.NotEqual(WindowState.FullScreen, window.WindowState);
            Assert.True(root.Children.Single(child => Grid.GetRow(child) == 2).IsVisible);
            Assert.True(root.Children.Single(child => Grid.GetRow(child) == 7).IsVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Document_tabs_reorder_by_dragging_and_keep_the_active_document()
    {
        var window = new MainWindow(); window.Show();
        try
        {
            var first = EditorSession.NewCanvas(20, 20); first.SuggestedName = "First";
            var second = EditorSession.NewCanvas(20, 20); second.SuggestedName = "Second";
            var third = EditorSession.NewCanvas(20, 20); third.SuggestedName = "Third";
            window.AddSession(first); window.AddSession(second); window.AddSession(third);
            Dispatcher.UIThread.RunJobs();
            Border Tab(EditorSession item) => window.GetVisualDescendants().OfType<Border>().Single(border => ReferenceEquals(border.Tag, item));
            var from = Tab(first).TranslatePoint(new Point(24, 12), window)!.Value;
            var target = Tab(third);
            var to = target.TranslatePoint(new Point(target.Bounds.Width - 5, 12), window)!.Value;
            window.MouseDown(from, MouseButton.Left);
            window.MouseMove(to, RawInputModifiers.LeftMouseButton);
            window.MouseUp(to, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { second, third, first }, window.Sessions);
            Assert.Same(first, window.Session);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Navigator_click_pans_at_high_zoom_without_editing_the_document()
    {
        var window = new MainWindow { Width = 1280, Height = 800 }; window.Show();
        try
        {
            var session = EditorSession.NewCanvas(1000, 800, SKColors.White);
            window.AddSession(session);
            Dispatcher.UIThread.RunJobs();
            window.Canvas.ZoomTo(4);
            Assert.True(Screenshots.Save(window, "port-navigator"));
            var before = window.Canvas.ToScreen(SKPoint.Empty);
            var state = session.History.CurrentId;
            var at = window.Canvas.TranslatePoint(new Point(window.Canvas.Bounds.Width - 30, window.Canvas.Bounds.Height - 30), window)!.Value;
            window.MouseDown(at, MouseButton.Left); window.MouseUp(at, MouseButton.Left);
            Assert.NotEqual(before, window.Canvas.ToScreen(SKPoint.Empty));
            Assert.Equal(4, window.Canvas.Zoom);
            Assert.Equal(state, session.History.CurrentId);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Camera_raw_header_reset_updates_the_grade_and_visible_sliders()
    {
        var window = new MainWindow(); window.Show();
        using var pixels = Composa.Rendering.Pixels.NewColor(20, 20); pixels.Erase(SKColors.Gray);
        CameraRawSettings? preview = null;
        var task = CameraRawDialog.Show(window, new CameraRawSettings { Exposure = 1, Shadows = 20 }, pixels, value => preview = value, () => pixels);
        Dispatcher.UIThread.RunJobs();
        var dialog = Assert.Single(window.OwnedWindows);
        var light = dialog.GetVisualDescendants().OfType<Expander>().First();
        var reset = ((Grid)light.Header!).Children.OfType<Button>().Single(button => Equals(button.Tag, "group-reset"));
        reset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var exposure = dialog.GetVisualDescendants().OfType<SliderField>().Single(field => field.Label == "Exposure");
        Assert.Equal(0, exposure.Value);
        dialog.Close(true); Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, task.Result!.Exposure);
        Assert.Equal(0, task.Result.Shadows);
        window.Close();
        _ = preview;
    }
}
