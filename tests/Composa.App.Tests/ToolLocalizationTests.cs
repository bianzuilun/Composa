using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Composa.App.Controls;
using Composa.Editing;
using Composa.Model;
using SkiaSharp;

namespace Composa.App.Tests;

public class ToolLocalizationTests
{
    [AvaloniaTheory]
    [InlineData("zh-CN")]
    [InlineData("ja")]
    public void Every_tool_group_and_its_selected_tool_are_localized(string language)
    {
        var previous = L10n.Culture.Name;
        MainWindow? window = null;
        try
        {
            L10n.Initialize(language);
            window = new MainWindow();
            window.Show();
            var session = EditorSession.NewCanvas(100, 100, SKColors.White);
            window.AddSession(session);
            foreach (var tool in new[] { Tool.Marquee, Tool.Lasso, Tool.Wand, Tool.Brush, Tool.Smear, Tool.Shape })
            {
                var button = window.RailButton(tool);
                button.OpenGroup();
                Dispatcher.UIThread.RunJobs();
                Assert.True(button.IsGroupOpen);
                var items = button.GroupItems.ToArray();
                Assert.NotEmpty(items);
                foreach (var item in items)
                {
                    var choice = Assert.IsType<ToolChoice>(item.Tag);
                    Assert.NotEqual(choice.Name, L10n.Text(choice.Name));
                    Assert.Equal(L10n.Text(choice.Name), item.Header);
                    Assert.Equal(choice.Key(), item.InputGesture);
                    item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    Dispatcher.UIThread.RunJobs();
                    Assert.Equal(tool, session.Tool);
                    Assert.Equal(choice.Name, button.Current!.Name);
                    Assert.StartsWith(L10n.Text(choice.Name), Assert.IsType<string>(ToolTip.GetTip(button)));
                    var options = window.GetVisualDescendants().OfType<StackPanel>().Single(panel => panel.Classes.Contains("options"));
                    Assert.Contains(options.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == L10n.Text(choice.Name));
                }
                button.OpenGroup();
                Dispatcher.UIThread.RunJobs();
                Assert.True(Screenshots.Save(TopLevel.GetTopLevel(button.GroupItems[0])!, $"tools-{language}-{tool}"));
            }
            Assert.StartsWith(language == "ja" ? "ズーム" : "缩放", Assert.IsType<string>(ToolTip.GetTip(window.RailButton(Tool.Zoom))));
            Assert.True(Screenshots.Save(window, "tools-options-" + language));
        }
        finally
        {
            if (window?.Session is { } session) session.MarkSaved("tool-localization-test.cmps");
            window?.Close();
            L10n.Initialize(previous);
        }
    }
}
