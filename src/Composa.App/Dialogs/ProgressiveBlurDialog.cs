using Avalonia.Controls;
using Avalonia.Threading;
using Composa.App.Controls;
using Composa.Filters;
using SkiaSharp;

namespace Composa.App.Dialogs;

public static class ProgressiveBlurDialog
{
    public static Task<FilterSettings?> Show(Window owner, CanvasView canvas, SKRect area, Action<FilterSettings> preview)
    {
        var completion = new TaskCompletionSource<FilterSettings?>();
        var current = new FilterSettings
        {
            Kind = FilterKind.ProgressiveBlur,
            ProgressiveStart = new SKPoint(area.MidX, area.Top + area.Height * 0.35f),
            ProgressiveEnd = new SKPoint(area.MidX, area.Top + area.Height * 0.65f)
        };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        var accepted = false;
        void Schedule() { timer.Stop(); timer.Start(); canvas.InvalidateVisual(); }
        timer.Tick += (_, _) => { timer.Stop(); preview(current); };
        var from = Ui.SliderField(L10n.Text("From"), 0, 0, 250, value => { current = current with { ProgressiveFrom = value }; Schedule(); }, 0.1, "0.0", 300);
        var to = Ui.SliderField(L10n.Text("To"), 20, 0, 250, value => { current = current with { ProgressiveTo = value }; Schedule(); }, 0.1, "0.0", 300);
        var dialog = new Window
        {
            Title = L10n.Text("Progressive Blur"), SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20), Spacing = 12,
            Children =
            {
                Ui.Label(L10n.Text("Drag either endpoint on the canvas to position the blur.")), from, to,
                Ui.Row(10, Ui.TextButton(L10n.Text("Cancel"), dialog.Close),
                    Ui.TextButton(L10n.Text("Apply"), () => { accepted = true; dialog.Close(); }, accent: true))
            }
        };
        var movingStart = false;
        void Move(SKPoint point)
        {
            current = movingStart ? current with { ProgressiveStart = point } : current with { ProgressiveEnd = point };
            Schedule();
        }
        canvas.FilterPointPressed = point =>
        {
            movingStart = SKPoint.Distance(point, current.ProgressiveStart!.Value) <= SKPoint.Distance(point, current.ProgressiveEnd!.Value);
            Move(point); return true;
        };
        canvas.FilterPointMoved = Move;
        canvas.FilterPointReleased = Schedule;
        canvas.FilterOverlay = view =>
        {
            var start = view.MapPoint(current.ProgressiveStart!.Value);
            var end = view.MapPoint(current.ProgressiveEnd!.Value);
            return drawing =>
            {
                using var line = new SKPaint { Color = SKColors.DeepSkyBlue, StrokeWidth = 2, IsAntialias = true };
                drawing.DrawLine(start, end, line);
                drawing.DrawCircle(start, 7, line);
                drawing.DrawCircle(end, 7, line);
                using var font = new SKFont(SKTypeface.Default, 12);
                drawing.DrawText("1", start.X + 10, start.Y, SKTextAlign.Left, font, line);
                drawing.DrawText("2", end.X + 10, end.Y, SKTextAlign.Left, font, line);
            };
        };
        dialog.Closed += (_, _) =>
        {
            timer.Stop();
            canvas.FilterPointPressed = null; canvas.FilterPointMoved = null; canvas.FilterPointReleased = null; canvas.FilterOverlay = null;
            canvas.InvalidateVisual();
            completion.TrySetResult(accepted ? current : null);
        };
        dialog.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Escape) { e.Handled = true; dialog.Close(); } };
        dialog.Show(owner);
        Schedule();
        return completion.Task;
    }
}
