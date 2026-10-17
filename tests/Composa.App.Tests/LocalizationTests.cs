using System.Globalization;
using System.Resources;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Composa.App.Controls;
using Composa.Editing;
using SkiaSharp;

namespace Composa.App.Tests;

public class LocalizationTests
{
    [Theory]
    [InlineData("system", "zh-CN", "zh-CN")]
    [InlineData("system", "zh-SG", "zh-CN")]
    [InlineData("system", "zh-TW", "en")]
    [InlineData("system", "ja-JP", "ja")]
    [InlineData("system", "fr-FR", "en")]
    [InlineData("en", "ja-JP", "en")]
    [InlineData("zh-CN", "en-US", "zh-CN")]
    [InlineData("ja", "zh-CN", "ja")]
    [InlineData("invalid", "ja-JP", "ja")]
    public void Language_resolution_is_explicit_and_has_an_English_fallback(string preference, string system, string expected) =>
        Assert.Equal(expected, L10n.ResolveCulture(preference, CultureInfo.GetCultureInfo(system)).Name);

    [Fact]
    public void Language_round_trips_and_old_settings_follow_the_system()
    {
        foreach (var code in new[] { "system", "en", "zh-CN", "ja" })
            Assert.Equal(code, JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(new Settings { Language = code }))!.Language);
        Assert.Equal("system", JsonSerializer.Deserialize<Settings>("{}")!.Language);
    }

    [Fact]
    public void Catalogs_have_identical_keys_unique_English_and_matching_format_arguments()
    {
        var resources = new ResourceManager("Composa.App.Localization.Strings", typeof(L10n).Assembly);
        var english = resources.GetResourceSet(CultureInfo.InvariantCulture, true, false)!
            .Cast<System.Collections.DictionaryEntry>().ToDictionary(e => (string)e.Key, e => (string)e.Value!);
        Assert.Equal(english.Count, english.Values.Distinct().Count());
        foreach (var code in new[] { "zh-CN", "ja" })
        {
            var translated = resources.GetResourceSet(CultureInfo.GetCultureInfo(code), true, false)!
                .Cast<System.Collections.DictionaryEntry>().ToDictionary(e => (string)e.Key, e => (string)e.Value!);
            Assert.Equal(english.Keys.Order(), translated.Keys.Order());
            foreach (var (key, source) in english)
            {
                Assert.False(string.IsNullOrWhiteSpace(translated[key]));
                Assert.Equal(System.Text.CompositeFormat.Parse(source).MinimumArgumentCount,
                    System.Text.CompositeFormat.Parse(translated[key]).MinimumArgumentCount);
            }
        }
    }

    [AvaloniaTheory]
    [InlineData("zh-CN", "文件(_F)", "新建画布…")]
    [InlineData("ja", "ファイル(_F)", "新規キャンバス…")]
    public void Localized_window_preserves_ids_user_names_and_requires_restart(string code, string fileLabel, string newLabel)
    {
        var previous = L10n.Culture.Name;
        MainWindow? window = null;
        try
        {
            L10n.Initialize(code);
            window = new MainWindow();
            window.Show();
            var menu = window.GetVisualDescendants().OfType<Menu>().Single();
            var file = (MenuItem)menu.Items[0]!;
            Assert.Equal(fileLabel, file.Header);
            Assert.Equal(newLabel, ((MenuItem)file.Items[0]!).Header);
            var session = EditorSession.NewCanvas(64, 64, SKColors.White);
            window.AddSession(session);
            session.AddBlankLayer();
            session.Rename(session.ActiveLayer!, "Save");
            Assert.Equal("Save", session.ActiveLayer!.Name);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == "Save");
            Assert.Equal(Icons.Layers, HistoryPanel.IconFor("New Layer"));
            Assert.Equal("History", window.GetVisualDescendants().OfType<SideDock>().Single().Section("History").Title);
            var help = (MenuItem)menu.Items[^1]!;
            var languages = help.Items.OfType<MenuItem>().Single(item => Equals(item.Header, L10n.Get("Language.Menu")));
            Assert.Equal(4, languages.Items.Count);
            var english = languages.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "English"));
            english.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal("en", window.Settings.Language);
            Assert.Equal(code, L10n.Culture.Name);
            Assert.Equal(fileLabel, file.Header);
            Assert.True(english.IsChecked);
            Assert.Single(languages.Items.OfType<MenuItem>().Where(item => item.IsChecked));
            Screenshots.Save(window, "localization-" + code);
            foreach (var dialog in window.OwnedWindows.ToArray()) dialog.Close();
            Dispatcher.UIThread.RunJobs();
            window.KeyPressQwerty(PhysicalKey.F1, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            var shortcuts = Assert.Single(window.OwnedWindows);
            shortcuts.GetVisualDescendants().OfType<TextBox>().First().Text = L10n.Get("Common.Save");
            Dispatcher.UIThread.RunJobs();
            var recorder = shortcuts.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Ctrl+S"));
            recorder.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            shortcuts.KeyPressQwerty(PhysicalKey.K, RawInputModifiers.Control);
            shortcuts.Close(true);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Ctrl+K", window.Settings.Shortcuts["Save"]);
            Assert.False(window.Settings.Shortcuts.ContainsKey(L10n.Get("Common.Save")));
        }
        finally
        {
            if (window?.Session is { } remaining) remaining.MarkSaved("localization-test.cmps");
            window?.Close();
            L10n.Initialize(previous);
        }
    }
}
