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
using Merpeeps.Core.Layout;
using Merpeeps.Core.Model;
using Xunit;

namespace Merpeeps.App.Tests;

public class ChartWorkspaceTests
{
    [AvaloniaTheory]
    [InlineData("Pie chart", ChartFamily.Pie)]
    [InlineData("Quadrant chart", ChartFamily.Quadrant)]
    [InlineData("XY chart", ChartFamily.Xy)]
    [InlineData("Radar chart", ChartFamily.Radar)]
    public void AllFourGalleryRoutesCreateTypedEmptyCharts(string name, ChartFamily family)
    {
        using var fixture = new Fixture();
        var button = fixture.Window.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Create " + name);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); fixture.Settle();
        Assert.Equal(family, fixture.Canvas.Model!.Chart!.Family);
        Assert.Empty(fixture.Canvas.Model.Nodes); Assert.Empty(fixture.Canvas.Model.Edges);
        Assert.Null(fixture.Canvas.Model.Sequence);
        Assert.False(fixture.Window.FindControl<ComboBox>("EngineBox")!.IsVisible);
    }

    [AvaloniaFact]
    public void PieSliceCreationEditingPendingCancelAndUndoRoundtrip()
    {
        using var fixture = new Fixture("pie\n");
        WorkspaceControls.OpenCreationForm(fixture.Window);
        fixture.Field<TextBox>("Slice label").Text = "Dogs";
        fixture.Field<TextBox>("Slice value").Text = "42.96";
        fixture.Click("Apply slice");
        var slice = Assert.Single(((PieChart)fixture.Canvas.Model!.Chart!).Slices);
        fixture.Field<TextBox>("Slice value").Text = "-1";
        fixture.Click("Apply slice");
        Assert.Equal(42.96, ((PieChart)fixture.Canvas.Model.Chart!).Slices[0].Value);
        Assert.False(fixture.Window.FindControl<Button>("SaveBtn")!.IsEnabled);
        fixture.Click("Cancel edit");
        var saved = fixture.Window.CurrentSource;
        Assert.Equal(slice.Id, ((PieChart)MerpeepsDocument.Load(saved).Chart!).Slices.Single().Id);
        fixture.Canvas.Undo(); fixture.Settle();
        Assert.Empty(((PieChart)fixture.Canvas.Model.Chart!).Slices);
        Assert.NotNull(fixture.Field<Button>("Add slice"));
    }

    [AvaloniaFact]
    public void XySeriesAndPointEditorsRetainSignedValuesAndLabels()
    {
        using var fixture = new Fixture("xychart\n");
        WorkspaceControls.OpenCreationForm(fixture.Window);
        fixture.Field<TextBox>("Series name (optional)").Text = "Revenue";
        fixture.Click("Apply series");
        fixture.Click("Add data value");
        fixture.Field<TextBox>("Numeric value").Text = "-2.5";
        fixture.Field<TextBox>("Point label (line only)").Text = "Launch";
        fixture.Click("Apply data value");
        fixture.Click("Series data…");
        fixture.Click("Add data value");
        fixture.Field<TextBox>("Numeric value").Text = "9.25";
        fixture.Click("Apply data value");
        var chart = (XyChart)MerpeepsDocument.Load(fixture.Window.CurrentSource).Chart!;
        Assert.Equal(new[] { -2.5, 9.25 }, chart.Series[0].Values.Select(v => v.Value));
        Assert.Equal("Launch", chart.Series[0].Values[0].Label);
        Assert.Contains(fixture.Canvas.ChartScene.Scene.Labels, l => l.Text == "Launch");
    }

    [AvaloniaFact]
    public void QuadrantDraggingUpdatesNormalizedDataOnceAndEscapeCancels()
    {
        using var fixture = new Fixture("quadrantChart\nP: [0.25,0.25]\n");
        var chart = (QuadrantChart)fixture.Canvas.Model!.Chart!;
        var point = chart.Points.Single();
        fixture.Canvas.FitToView();
        var map = fixture.Canvas.ChartScene.Coordinates!;
        fixture.Drag(map.ToPoint(.25, .25), map.ToPoint(.75, .6));
        var moved = ((QuadrantChart)fixture.Canvas.Model.Chart!).Points.Single();
        Assert.Equal(.75, moved.X, 5); Assert.Equal(.6, moved.Y, 5);
        Assert.Equal("Move quadrant data point", fixture.Canvas.History!.NextUndo);
        fixture.Canvas.Undo(); fixture.Settle();
        Assert.Equal(.25, ((QuadrantChart)fixture.Canvas.Model.Chart!).Points.Single().X);
        fixture.Drag(map.ToPoint(.25, .25), map.ToPoint(.8, .8), cancel: true);
        Assert.Equal(.25, ((QuadrantChart)fixture.Canvas.Model.Chart!).Points.Single().X);
        Assert.Empty(fixture.Canvas.Model.Nodes);
        Assert.Equal(point.Id, ((QuadrantChart)MerpeepsDocument.Load(fixture.Window.CurrentSource).Chart!).Points.Single().Id);
    }

    [AvaloniaFact]
    public void RadarAxisCurveIdentityCollisionAndValueMappingStayTyped()
    {
        using var fixture = new Fixture("radar-beta\naxis A,B,C\n");
        fixture.Window.FindControl<MenuItem>("MenuClassNamespaces")!.RaiseClick(); fixture.Settle();
        fixture.Click("Add curve");
        fixture.Field<TextBox>("Curve identity").Text = "A";
        fixture.Field<TextBox>("Value for A").Text = "1";
        fixture.Field<TextBox>("Value for B").Text = "2";
        fixture.Field<TextBox>("Value for C").Text = "3";
        fixture.Click("Apply radar curve");
        fixture.Canvas.SelectChart(new(ChartElementRole.Axis, "A")); fixture.Settle();
        fixture.Click("Move axis later");
        var chart = (RadarChart)MerpeepsDocument.Load(fixture.Window.CurrentSource).Chart!;
        Assert.Equal(1, chart.Curves.Single(c => c.Id == "A").Values.Single(v => v.AxisId == "A").Value);
        Assert.Equal("A", chart.Axes[1].Id);
        fixture.Canvas.SelectChart(new(ChartElementRole.CurveValue, "A", "C")); fixture.Settle();
        fixture.Field<TextBox>("Value").Text = "4";
        fixture.Click("Apply radar value");
        Assert.Equal(4, ((RadarChart)fixture.Canvas.Model!.Chart!).Curves[0].Values.Single(v => v.AxisId == "C").Value);
    }

    private sealed class Fixture : IDisposable
    {
        public MainWindow Window { get; } = new();
        public EditorCanvas Canvas => Window.CanvasForTest;
        public Border Panel => Window.FindControl<Border>("ClassInspectorHost")!;
        public Fixture(string? source = null)
        {
            Window.Show();
            if (source is not null) Window.LoadSourceForTest(source);
            Settle(); if (Canvas.Model?.Chart is not null) Canvas.FitToView();
        }
        public T Field<T>(string name) where T : Control => WorkspaceControls.Find<T>(Window, name);
        public void Click(string name) => WorkspaceControls.Click(Window, name);
        public void Drag(DiagramPoint from, DiagramPoint to, bool cancel = false)
        {
            Point At(DiagramPoint point) => Canvas.TranslatePoint(new(point.X * Canvas.Zoom + Canvas.OffsetX, point.Y * Canvas.Zoom + Canvas.OffsetY), Window)!.Value;
            var start = At(from); var end = At(to);
            Window.MouseDown(start, MouseButton.Left);
            Window.MouseMove(end);
            if (cancel)
            {
                Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
                Window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            }
            Window.MouseUp(end, MouseButton.Left);
            Settle();
        }
        public void Settle() => Dispatcher.UIThread.RunJobs();
        public void Dispose()
        {
            foreach (var dialog in Window.OwnedWindows.ToArray()) dialog.Close();
            Window.Close();
        }
    }
}
