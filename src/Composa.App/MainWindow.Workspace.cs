using Avalonia.Controls;
using Composa.App.Dialogs;

namespace Composa.App;

public sealed partial class MainWindow
{
    private bool canvasOnly;
    private WindowState beforeCanvasOnly;
    private readonly List<Control> workspaceChrome = [];
    private readonly Dictionary<Control, bool> chromeVisibility = [];

    private async Task SearchCommands()
    {
        if (await CommandPalette.Show(this, AllShortcuts.Where(shortcut => session != null || shortcut.Group != "Tools and Canvas")) is { } chosen) Execute(chosen);
    }

    public void ToggleCanvasOnly()
    {
        if (canvas.IsDragging || session?.IsEditingText == true) return;
        canvasOnly = !canvasOnly;
        if (canvasOnly)
        {
            beforeCanvasOnly = WindowState;
            chromeVisibility.Clear();
            foreach (var control in workspaceChrome) { chromeVisibility[control] = control.IsVisible; control.IsVisible = false; }
            WindowState = WindowState.FullScreen;
        }
        else
        {
            WindowState = beforeCanvasOnly;
            foreach (var (control, visible) in chromeVisibility) control.IsVisible = visible;
            chromeVisibility.Clear();
        }
        canvas.Focus();
    }
}
