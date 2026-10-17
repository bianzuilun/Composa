using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Composa.Editing;

namespace Composa.App;

public sealed partial class MainWindow
{
    private EditorSession? pressedTab;
    private Point tabPressPoint;
    private bool draggingTab;
    private ScrollViewer? tabScroll;
    private Button? tabOverflow;

    private void UpdateTabOverflow()
    {
        if (tabOverflow != null && tabScroll != null)
            tabOverflow.IsVisible = tabScroll.Extent.Width > tabScroll.Viewport.Width + 1;
    }

    private void OpenTabOverflow()
    {
        if (tabOverflow == null) return;
        var menu = new ContextMenu();
        foreach (var item in sessions)
        {
            var entry = new MenuItem { Header = item.Title + (item.IsModified ? " •" : ""), IsChecked = item == session };
            entry.Click += (_, _) => SetSession(item);
            menu.Items.Add(entry);
        }
        menu.Open(tabOverflow);
    }

    private void MoveTab(object? sender, PointerEventArgs e)
    {
        if (pressedTab == null || tabScroll == null) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) { pressedTab = null; e.Pointer.Capture(null); return; }
        var travel = e.GetPosition(this) - tabPressPoint;
        if (!draggingTab && Math.Abs(travel.X) < 5 && Math.Abs(travel.Y) < 5) return;
        draggingTab = true;
        var pointer = e.GetPosition(tabScroll);
        var edge = pointer.X < 24 ? -24 : pointer.X > tabScroll.Bounds.Width - 24 ? 24 : 0;
        if (edge != 0) tabScroll.Offset = new Vector(Math.Clamp(tabScroll.Offset.X + edge, 0, Math.Max(0, tabScroll.Extent.Width - tabScroll.Viewport.Width)), 0);
        var x = e.GetPosition(tabs).X;
        var target = tabs.Children.OfType<Border>().FirstOrDefault(tab => x >= tab.Bounds.Left && x <= tab.Bounds.Right);
        if (target?.Tag is EditorSession other && other != pressedTab)
        {
            var from = sessions.IndexOf(pressedTab);
            var to = sessions.IndexOf(other);
            // Cross the neighbour's midpoint before swapping, to avoid oscillation after its frame moves.
            if (from >= 0 && to >= 0 && (from < to ? x > target.Bounds.Center.X : x < target.Bounds.Center.X))
            {
                sessions.RemoveAt(from); sessions.Insert(to, pressedTab); RebuildTabs();
            }
        }
        e.Handled = true;
    }
}
