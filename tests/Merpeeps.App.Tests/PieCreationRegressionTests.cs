using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Merpeeps.Core;
using Merpeeps.Core.Model;
using Xunit;

namespace Merpeeps.App.Tests;

public class PieCreationRegressionTests
{
    [AvaloniaFact]
    public void ClickingAnotherSliceCommitsTheValueAndCreatesTheNextSlice()
    {
        var window = new MainWindow();
        window.Show(); window.LoadSourceForTest("pie\n"); Settle();
        try
        {
            var add = Find<Button>(window.FindControl<Border>("FamilyPaletteHost")!, "Add slice");
            Click(add, window);
            var value = Find<TextBox>(window.FindControl<Border>("ClassInspectorHost")!, "Slice value");
            value.Focus(); value.Text = "30"; Settle();
            Click(add, window);
            var pie = (PieChart)window.CanvasForTest.Model!.Chart!;
            Assert.Equal(2, pie.Slices.Count);
            Assert.Equal(30, pie.Slices[0].Value);
            Assert.Equal(10, pie.Slices[1].Value);
            Assert.Contains(window.CanvasForTest.ChartScene.Scene.Labels, l => l.Text == "75%");
            Assert.Contains(window.CanvasForTest.ChartScene.Scene.Labels, l => l.Text == "25%");
            Assert.Equal(2, ((PieChart)MerpeepsDocument.Load(window.CurrentSource).Chart!).Slices.Count);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void InvalidSliceValueBlocksInsertionWithFeedbackInsteadOfDisablingThePalette()
    {
        var window = new MainWindow(); window.Show(); window.LoadSourceForTest("pie\n"); Settle();
        try
        {
            var add = Find<Button>(window.FindControl<Border>("FamilyPaletteHost")!, "Add slice");
            Click(add, window);
            var panel = window.FindControl<Border>("ClassInspectorHost")!;
            var value = Find<TextBox>(panel, "Slice value");
            value.Focus(); value.Text = "-1"; Settle(); Click(add, window);
            Assert.Single(((PieChart)window.CanvasForTest.Model!.Chart!).Slices);
            Assert.True(add.IsEnabled);
            Assert.False(window.FindControl<Button>("SaveBtn")!.IsEnabled);
            Assert.Contains(panel.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains("nonnegative", StringComparison.OrdinalIgnoreCase) == true);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TimelinePeriodsAdvanceHorizontallyAndEventsStayUnderTheirPeriod()
    {
        var window = new MainWindow(); window.Show(); window.LoadSourceForTest("timeline\n"); Settle();
        try
        {
            var palette = window.FindControl<Border>("FamilyPaletteHost")!;
            var period = Find<Button>(palette, "Add time period");
            Click(period, window);
            Click(Find<Button>(palette, "Add timeline event"), window);
            Click(period, window);
            Click(Find<Button>(palette, "Add timeline event"), window);
            var canvas = window.CanvasForTest;
            var timeline = canvas.Model!.Temporal!;
            Assert.False(timeline.TopDown);
            Assert.Equal(2, timeline.Periods.Count);
            Assert.All(timeline.Periods, p => Assert.Single(p.Events));
            var labels = canvas.DataScene.Scene.Labels;
            var first = labels.Single(l => l.Text == "Period1").Bounds;
            var second = labels.Single(l => l.Text == "Period2").Bounds;
            Assert.True(second.X > first.X + first.Width);
            Assert.Equal(first.Y, second.Y);
            foreach (var detail in labels.Where(l => l.Text == "Event")) Assert.True(detail.Bounds.Y > first.Y);
        }
        finally { window.Close(); }
    }

    private static T Find<T>(Visual root, string name) where T : Control =>
        root.GetVisualDescendants().OfType<T>().Single(c => c.IsEffectivelyVisible && AutomationProperties.GetName(c) == name);
    private static void Click(Control control, MainWindow window)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Settle();
    }
    private static void Settle() => Dispatcher.UIThread.RunJobs();
}
