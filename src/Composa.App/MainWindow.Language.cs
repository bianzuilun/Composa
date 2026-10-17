using Avalonia.Controls;
using Composa.App.Dialogs;

namespace Composa.App;

public sealed partial class MainWindow
{
    private MenuItem BuildLanguageMenu()
    {
        var menu = new MenuItem { Header = L10n.Get("Language.Menu") };
        var choices = new (string Code, string Label)[]
        {
            ("system", L10n.Get("Language.System")), ("en", "English"),
            ("zh-CN", "简体中文"), ("ja", "日本語")
        };
        var selected = choices.Any(choice => choice.Code == settings.Language) ? settings.Language : "system";
        foreach (var choice in choices)
        {
            var item = new MenuItem
            {
                Header = choice.Label, ToggleType = MenuItemToggleType.Radio,
                IsChecked = choice.Code == selected
            };
            item.Click += async (_, _) =>
            {
                settings.Language = choice.Code;
                settings.Save();
                foreach (var other in menu.Items.OfType<MenuItem>()) other.IsChecked = other == item;
                await Prompts.Alert(this, L10n.Get("Language.RestartTitle"), L10n.Get("Language.RestartMessage"));
            };
            menu.Items.Add(item);
        }
        return menu;
    }
}
