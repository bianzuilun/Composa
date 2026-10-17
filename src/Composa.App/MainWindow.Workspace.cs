using Avalonia.Controls;
using Composa.App.Dialogs;

namespace Composa.App;

public sealed partial class MainWindow
{
    private bool canvasOnly;
    private WindowState beforeCanvasOnly;
    private readonly List<Control> workspaceChrome = [];

    private async Task SearchCommands()
    {
        if (await CommandPalette.Show(this, AllShortcuts.Where(shortcut => session != null || shortcut.Group != "Tools and Canvas")) is { } chosen) Execute(chosen);
    }

    public void ToggleCanvasOnly()
    {
        if (canvas.IsDragging || session?.IsEditingText == true) return;
        canvasOnly = !canvasOnly;
        if (canvasOnly) { beforeCanvasOnly = WindowState; WindowState = WindowState.FullScreen; }
        else WindowState = beforeCanvasOnly;
        foreach (var control in workspaceChrome) control.IsVisible = !canvasOnly;
        canvas.Focus();
    }
}
