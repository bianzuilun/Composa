using Avalonia.Controls;

namespace Composa.App.Controls;

public sealed partial class CanvasView
{
    private void OpenPendingEditMenu()
    {
        var menu = new ContextMenu();
        void Add(string title, Action action)
        {
            var item = new MenuItem { Header = L10n.Text(title) };
            item.Click += (_, _) => { action(); drag = Drag.None; guides.Clear(); UpdateCursor(); InvalidateVisual(); ToolStateChanged?.Invoke(); };
            menu.Items.Add(item);
        }
        if (HasPendingGradient)
        { Add("Apply", () => SettleGradient(true)); Add("Cancel", () => SettleGradient(false)); }
        else if (HasCrop)
        { Add("Apply", ApplyCrop); Add("Cancel", CancelCrop); }
        else if (session?.Transform != null)
        { Add("Apply", () => session.CommitTransform()); Add("Cancel", () => session.CancelTransform()); }
        if (menu.Items.Count > 0) menu.Open(this);
    }
}
