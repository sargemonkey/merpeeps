using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Merpeeps.Core;
using Merpeeps.Core.Layout;
using Merpeeps.Core.Model;
using Xunit;

namespace Merpeeps.App.Tests;

public class PieSelectionRegressionTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClickingEachWedgeSelectsItsOwnEditableSlice(bool pendingEdit)
    {
        var window = new MainWindow(); window.Show();
        window.LoadSourceForTest("pie\n\"Alpha\": 30\n\"Beta\": 20\n\"Gamma\": 50\n");
        Settle(); var canvas = window.CanvasForTest; canvas.FitToView(); Settle();
        try
        {
            var ids = ((PieChart)canvas.Model!.Chart!).Slices.Select(s => s.Id).ToArray();
            foreach (var id in ids)
            {
                var sector = canvas.ChartScene.Scene.Regions.Single(r => r.Owner.Role == "Slice" && r.Owner.Id == id && r.Kind == DrawingHitKind.Sector);
                var angle = sector.StartAngle + sector.SweepAngle / 2;
                var radius = sector.Bounds.Width * .4;
                var point = new Point(sector.Bounds.X + sector.Bounds.Width / 2 + radius * Math.Cos(angle),
                    sector.Bounds.Y + sector.Bounds.Height / 2 + radius * Math.Sin(angle));
                var screen = canvas.TranslatePoint(new Point(point.X * canvas.Zoom + canvas.OffsetX, point.Y * canvas.Zoom + canvas.OffsetY), window)!.Value;
                window.MouseMove(screen); window.MouseDown(screen, MouseButton.Left); window.MouseUp(screen, MouseButton.Left); Settle();
                Assert.Equal(new ChartTarget(ChartElementRole.Slice, id), canvas.SelectedChartTarget);
                var slice = ((PieChart)canvas.Model.Chart!).Slices.Single(s => s.Id == id);
                var panel = window.FindControl<Border>("ClassInspectorHost")!;
                Assert.Equal(slice.Label, Field(panel, "Slice label").Text);
                var value = Field(panel, "Slice value"); value.Focus();
                value.Text = "40";
                if (!pendingEdit)
                {
                    window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                    window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                    Settle();
                    Assert.Equal(40, ((PieChart)canvas.Model.Chart!).Slices.Single(s => s.Id == id).Value);
                }
            }
            canvas.Focus(); Settle();
            var reopened = (PieChart)MerpeepsDocument.Load(window.CurrentSource).Chart!;
            Assert.All(reopened.Slices, s => Assert.Equal(40, s.Value));
        }
        finally { window.Close(); }
    }
    private static TextBox Field(Visual root, string name) => root.GetVisualDescendants().OfType<TextBox>()
        .Single(c => c.IsEffectivelyVisible && AutomationProperties.GetName(c) == name);
    private static void Settle() => Dispatcher.UIThread.RunJobs();
}
