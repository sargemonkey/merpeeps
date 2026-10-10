using Merpeeps.Core.Editing;
using Merpeeps.Core.Layout;
using Merpeeps.Core.Model;
using Merpeeps.Core.Parsing;
using Xunit;

namespace Merpeeps.Core.Tests;

public class ChartLayoutTests
{
    private static DiagramModel Parse(ChartFamily family, string source) => new ChartDiagramAdapter(family).Parse(source);
    private static DrawingTextSize Measure(string text, DrawingFont font) => new(text.Length * font.Size * .65, font.Size * 1.4);

    [Fact]
    public void PieUsesAnnularPathsAndProportionalClockwiseHitRegions()
    {
        var model = Parse(ChartFamily.Pie, "pie showData\n\"A\": 1\n\"B\": 3\n");
        var chart = (PieChart)model.Chart!;
        ChartConfiguration.Set(chart, "donutHole", "0.4");
        var layout = ChartDiagramLayout.Create(model, Measure);
        Assert.False(layout.Scene.UsesApproximateTextMetrics);
        var regions = layout.Scene.Regions.Where(r => r.Kind == DrawingHitKind.Sector).ToArray();
        Assert.Equal(2, regions.Length);
        Assert.Equal(Math.PI / 2, regions[0].SweepAngle, 8);
        Assert.Equal(3 * Math.PI / 2, regions[1].SweepAngle, 8);
        var center = new DiagramPoint(regions[0].Bounds.X + regions[0].Bounds.Width / 2, regions[0].Bounds.Y + regions[0].Bounds.Height / 2);
        Assert.Null(ChartDiagramLayout.Hit(layout, center));
        Assert.Contains(layout.Scene.Figures, f => f.Kind == DrawingFigureKind.Path && f.Path.Any(p => p.Kind == DrawingPathKind.Cubic));
        Assert.DoesNotContain(layout.Scene.Figures, f => f.Kind == DrawingFigureKind.Ellipse && f.Fill is not null);
        var at = new DiagramPoint(center.X + 120 * Math.Cos(-Math.PI / 4), center.Y + 120 * Math.Sin(-Math.PI / 4));
        Assert.Equal(chart.Slices[0].Id, ChartDiagramLayout.Hit(layout, at)!.Id);
        var source = new ChartDiagramAdapter(ChartFamily.Pie).Write(model);
        _ = ChartDiagramLayout.Create(model, Measure, hovered: new(ChartElementRole.Slice, chart.Slices[1].Id));
        Assert.Equal(source, new ChartDiagramAdapter(ChartFamily.Pie).Write(model));
    }

    [Fact]
    public void QuadrantTransformAndStylePrecedenceUseActualDataCoordinates()
    {
        var model = Parse(ChartFamily.Quadrant, "quadrantChart\nP:::red: [0.75,0.25] color: #00ff00\nclassDef red color: #ff0000, radius: 10\n");
        var chart = (QuadrantChart)model.Chart!;
        var layout = ChartDiagramLayout.Create(model, Measure);
        var point = chart.Points.Single();
        var where = layout.Coordinates!.ToPoint(point.X, point.Y);
        Assert.Equal(point.Id, ChartDiagramLayout.Hit(layout, where)!.Id);
        var inverse = layout.Coordinates.FromPoint(where);
        Assert.Equal(.75, inverse.X, 8); Assert.Equal(.25, inverse.Y, 8);
        var style = ChartDiagramLayout.PointAppearance(chart, point);
        Assert.Equal(new ClassColor(0, 255, 0), style.Fill);
        Assert.Equal(10, style.Radius);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void XyMapsNumericValuesAndRetainsMixedSeriesIdentity(bool horizontal)
    {
        var model = Parse(ChartFamily.Xy, "xychart" + (horizontal ? " horizontal" : "") + "\nx-axis 10 --> 20\ny-axis -5 --> 10\nbar \"Bars\" [-5, 10]\nline \"Line\" [-1 \"Label\", 6]\n");
        var chart = (XyChart)model.Chart!;
        var layout = ChartDiagramLayout.Create(model, Measure);
        Assert.Equal(horizontal, layout.Coordinates!.Horizontal);
        var point = layout.Coordinates.ToPoint(20, 6);
        Assert.Equal(chart.Series[1].Values[1].Id, ChartDiagramLayout.Hit(layout, point)!.Id);
        Assert.Equal(ChartElementRole.Point, ChartDiagramLayout.Hit(layout, point)!.Role);
        var value = layout.Coordinates.FromPoint(point);
        Assert.Equal(20, value.X, 8); Assert.Equal(6, value.Y, 8);
        Assert.Contains(layout.Scene.Labels, l => l.Text == "Label");
        Assert.Contains(layout.Scene.Figures, f => f.Owner.Id == chart.Series[0].Values[0].Id && f.Kind == DrawingFigureKind.Rectangle);
    }

    [Fact]
    public void RadarUsesRealAxisMappingAndTensionNotASequencePolygon()
    {
        var model = Parse(ChartFamily.Radar, "radar-beta\naxis A,B,C\ncurve A{C: 30,A: 10,B:20}\nmax 30\n");
        var layout = ChartDiagramLayout.Create(model, Measure);
        var path = layout.Scene.Figures.Single(f => f.Id == "curve-A");
        Assert.Equal(DrawingFigureKind.Path, path.Kind);
        Assert.Equal(3, path.Path.Count(p => p.Kind == DrawingPathKind.Cubic));
        Assert.NotEqual(path.Path[0].End, path.Path[1].Control1);
        var transform = layout.RadarCoordinates!;
        Assert.Equal(30, transform.ValueAt("A", new(transform.Center.X, transform.Center.Y - transform.Radius)), 8);
        Assert.Contains(layout.Scene.Regions, r => r.Owner.Role == "Axis" && r.Owner.Id == "A");
        Assert.Contains(layout.Scene.Regions, r => r.Owner.Role == "Curve" && r.Owner.Id == "A");
    }

    [Fact]
    public void AllFourChartEditsAreAtomicAndDoNotCreateGraphNodes()
    {
        var pie = Parse(ChartFamily.Pie, "pie\n\"A\": 1\n");
        var history = new EditHistory(pie);
        history.Apply(ChartEditing.AddSlice(new("B", "B", 2)));
        Assert.Equal(2, ((PieChart)pie.Chart!).Slices.Count);
        var before = pie.Chart;
        Assert.Throws<ChartNotEditableException>(() => history.Apply(ChartEditing.UpdateSlice(new("B", "B", -2))));
        Assert.Same(before, pie.Chart);
        history.Undo();
        Assert.Single(((PieChart)pie.Chart!).Slices);
        Assert.Empty(pie.Nodes);
        var radar = Parse(ChartFamily.Radar, "radar-beta\naxis A,B,C\ncurve C{1,2,3}\n");
        var radarHistory = new EditHistory(radar);
        radarHistory.Apply(ChartEditing.MoveRadarAxis("A", 2));
        var chart = (RadarChart)radar.Chart!;
        Assert.Equal(1, chart.Curves[0].Values.Single(v => v.AxisId == "A").Value);
        Assert.Contains("curve C{2, 3, 1}", ChartDiagramAdapter.Write(chart));
        radarHistory.Undo();
        Assert.Equal("A", ((RadarChart)radar.Chart!).Axes[0].Id);
    }
}
