using Merpeeps.Core;
using Merpeeps.Core.Editing;
using Merpeeps.Core.Model;
using Xunit;

namespace Merpeeps.Core.Tests;

public class ChartEditingTests
{
    [Theory]
    [InlineData("pie")]
    [InlineData("quadrant")]
    [InlineData("xy")]
    [InlineData("radar")]
    public void EmptyChartFactoryNeverInventsGraphOrSequenceContent(string family)
    {
        var draft = MermaidDiagramCatalog.CreateEmpty(family);
        Assert.NotNull(draft.Model.Chart);
        Assert.Empty(draft.Model.Nodes);
        Assert.Empty(draft.Model.Edges);
        Assert.Null(draft.Model.Sequence);
        Assert.False(draft.Capabilities.CanLayout);
    }

    [Fact]
    public void PiePointAndXyValueIdentitiesSurviveRealSaveReopen()
    {
        var pie = MermaidDiagramCatalog.CreateEmpty("pie");
        new EditHistory(pie.Model).Apply(ChartEditing.AddSlice(new("slice-one", "One", 1.25)));
        var reopenedPie = MerpeepsDocument.Load(MerpeepsDocument.Save(pie.Source, pie.Model));
        Assert.Equal("slice-one", Assert.IsType<PieChart>(reopenedPie.Chart).Slices[0].Id);
        var xy = MermaidDiagramCatalog.CreateEmpty("xy");
        var history = new EditHistory(xy.Model);
        history.Apply(ChartEditing.AddSeries(new("series-one", ChartSeriesKind.Line, "Named", [])));
        history.Apply(ChartEditing.AddValue("series-one", new("value-one", -2.5, "Low")));
        var saved = MerpeepsDocument.Save(xy.Source, xy.Model);
        var reopened = Assert.IsType<XyChart>(MerpeepsDocument.Load(saved).Chart);
        Assert.Equal("series-one", reopened.Series[0].Id);
        Assert.Equal("value-one", reopened.Series[0].Values[0].Id);
        history.Undo();
        Assert.Empty(((XyChart)xy.Model.Chart!).Series[0].Values);
    }

    [Fact]
    public void ClearingAnOptionDoesNotLeaveTheOldValueInFrontmatter()
    {
        const string source = "---\nconfig:\n  pie:\n    donutHole: 0.4\n  extra: keep\n---\npie\n\"A\": 2\n";
        var model = MerpeepsDocument.Load(source);
        new EditHistory(model).Apply(ChartEditing.SetOption("donutHole", null));
        var saved = MerpeepsDocument.Save(source, model);
        Assert.DoesNotContain("donutHole", saved);
        Assert.Contains("extra: keep", saved);
        Assert.Equal(0, ChartConfiguration.Number(MerpeepsDocument.Load(saved).Chart!, "donutHole"));
    }

    [Fact]
    public void RadarReorderAndRenamePreserveAllCurveValueToAxisMeaning()
    {
        const string source = "radar-beta\naxis A,B,C\ncurve A{1,2,3}\ncurve B{C:6,A:4,B:5}\n";
        var model = MerpeepsDocument.Load(source);
        var history = new EditHistory(model);
        history.Apply(ChartEditing.UpdateRadarAxis("A", new("Renamed", "First")));
        history.Apply(ChartEditing.MoveRadarAxis("Renamed", 2));
        var saved = MerpeepsDocument.Save(source, model);
        var reopened = Assert.IsType<RadarChart>(MerpeepsDocument.Load(saved).Chart);
        Assert.Equal(1, reopened.Curves.Single(c => c.Id == "A").Values.Single(v => v.AxisId == "Renamed").Value);
        Assert.Equal(4, reopened.Curves.Single(c => c.Id == "B").Values.Single(v => v.AxisId == "Renamed").Value);
        Assert.True(reopened.Curves.Single(c => c.Id == "B").NamedValues);
        Assert.False(reopened.Curves.Single(c => c.Id == "A").NamedValues);
        Assert.Equal("Renamed", reopened.Axes[2].Id);
    }

    [Fact]
    public void AddingRadarAxisCannotFabricateValuesForExistingCurves()
    {
        var model = MerpeepsDocument.Load("radar-beta\naxis A,B,C\ncurve A{1,2,3}\n");
        var before = model.Chart;
        var history = new EditHistory(model);
        Assert.Throws<ArgumentException>(() => history.Apply(ChartEditing.AddRadarAxis(new("D"))));
        Assert.Same(before, model.Chart);
        history.Apply(ChartEditing.AddRadarAxis(new("D"), new Dictionary<string, double> { ["A"] = 7 }));
        Assert.Equal(7, ((RadarChart)model.Chart!).Curves[0].Values.Single(v => v.AxisId == "D").Value);
        history.Undo();
        Assert.Equal(3, ((RadarChart)model.Chart!).Axes.Count);
    }

    [Fact]
    public void BodyAndConfigurationOrientationRemainDistinct()
    {
        const string source = "---\nconfig:\n  xyChart:\n    chartOrientation: horizontal\n---\nxychart\nline [1,2]\n";
        var model = MerpeepsDocument.Load(source);
        Assert.False(((XyChart)model.Chart!).OrientationInBody);
        new EditHistory(model).Apply(ChartEditing.SetXyOrientation(false));
        var saved = MerpeepsDocument.Save(source, model);
        Assert.Contains("xychart vertical", saved);
        var reopened = Assert.IsType<XyChart>(MerpeepsDocument.Load(saved).Chart);
        Assert.True(reopened.OrientationInBody);
        Assert.False(reopened.Horizontal);
    }
}
