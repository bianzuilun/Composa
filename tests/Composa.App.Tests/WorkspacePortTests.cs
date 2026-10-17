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
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Assert.NotEqual(WindowState.FullScreen, window.WindowState);
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
