using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;

namespace Composa.App.Dialogs;

public static class CommandPalette
{
    public static async Task<Shortcut?> Show(Window owner, IEnumerable<Shortcut> shortcuts)
    {
        var all = shortcuts.Where(s => !s.Hidden).ToList();
        var search = new TextBox { PlaceholderText = L10n.Text("Search commands and tools"), MinWidth = 500 };
        var list = new ListBox { Height = 340, HorizontalAlignment = HorizontalAlignment.Stretch };
        var matches = new List<Shortcut>();
        var dialog = new DialogWindow("Search Commands", Ui.Column(12, search, list), "Run");
        void Refresh()
        {
            var words = (search.Text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            matches = all.Where(s => words.All(word => L10n.Text(s.Title).Contains(word, StringComparison.OrdinalIgnoreCase) || s.Title.Contains(word, StringComparison.OrdinalIgnoreCase))).ToList();
            list.ItemsSource = matches.Select(s => L10n.Text(s.Title) + "    " + Shortcut.Label(s.Gesture)).ToArray();
            list.SelectedIndex = matches.Count > 0 ? 0 : -1;
            dialog.CanAccept = matches.Count > 0;
        }
        search.TextChanged += (_, _) => Refresh();
        search.KeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Up or Key.Down) || matches.Count == 0) return;
            list.SelectedIndex = Math.Clamp(list.SelectedIndex + (e.Key == Key.Down ? 1 : -1), 0, matches.Count - 1);
            e.Handled = true;
        };
        dialog.Opened += (_, _) => search.Focus();
        Refresh();
        return await dialog.Ask(owner) && list.SelectedIndex >= 0 ? matches[list.SelectedIndex] : null;
    }
}
