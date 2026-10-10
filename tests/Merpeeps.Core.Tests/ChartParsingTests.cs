using Merpeeps.Core.Model;
using Merpeeps.Core.Parsing;
using Xunit;

namespace Merpeeps.Core.Tests;

public class ChartParsingTests
{
    [Fact]
    public void PieKeepsOrderValuesShowDataAndDonutConfiguration()
    {
        const string source = """
            ---
            config:
              pie:
                donutHole: 0.2
                highlightSlice: Cats
            ---
            pie showData title Pets
            "Dogs": 42.96
            "Cats": 0
            """;
        var adapter = new ChartDiagramAdapter(ChartFamily.Pie);
        var chart = Assert.IsType<PieChart>(adapter.Parse(source).Chart);
        Assert.Equal(new[] { "Dogs", "Cats" }, chart.Slices.Select(s => s.Label));
        Assert.Equal(42.96, chart.Slices[0].Value);
        Assert.True(chart.ShowData);
        Assert.Equal(.2, chart.Configuration["donutHole"]!.GetValue<double>());
        Assert.Equal("Pets", chart.Title);
        Assert.Throws<ChartNotEditableException>(() => adapter.Parse("pie\n\"A\": -1\n"));
        var duplicate = adapter.ReadSource("pie\n\"A\": 1\n\"A\": 2\n");
        Assert.False(duplicate.CanEdit);
        Assert.Equal(2, ((PieChart)duplicate.Chart).Slices.Count);
        Assert.Contains(duplicate.Diagnostics, d => d.Code == "duplicate-label" && d.Kind == ChartDiagnosticKind.NativeUnsupported);
    }

    [Fact]
    public void QuadrantDataStylesAndRegionsAreNotGraphNodes()
    {
        const string source = """
            quadrantChart
            x-axis Low --> High
            y-axis Less --> More
            quadrant-1 Expand
            quadrant-2 Promote
            quadrant-3 Stop
            quadrant-4 Improve
            A:::accent: [0.75, 0.25] radius: 12, color: #ffaa00
            classDef accent color: #000000, stroke-width: 2px
            """;
        var adapter = new ChartDiagramAdapter(ChartFamily.Quadrant);
        var model = adapter.Parse(source);
        var chart = Assert.IsType<QuadrantChart>(model.Chart);
        Assert.Empty(model.Nodes); Assert.Empty(model.Edges);
        Assert.Equal("Expand", chart.Quadrants[0]);
        Assert.Equal(.75, chart.Points[0].X);
        Assert.Equal("accent", chart.Points[0].StyleClass);
        Assert.Equal("#ffaa00", chart.Points[0].Style["color"]!.GetValue<string>());
        Assert.Equal(ChartDiagramAdapter.Write(chart), adapter.Write(adapter.Parse(ChartDiagramAdapter.Write(chart))));
        Assert.Throws<ChartNotEditableException>(() => adapter.Parse("quadrantChart\nA: [2,0]\n"));
    }

    [Theory]
    [InlineData("xychart")]
    [InlineData("xychart-beta")]
    public void XyRetainsAxesOrientationMixedSeriesAndPointLabels(string directive)
    {
        var source = directive + """
             horizontal
            title "Revenue"
            x-axis "Quarter" [Q1, "Second quarter", Q3]
            y-axis "Amount" -10 --> 20
            bar "Actual" [5, -3, 12]
            line "Trend" [+1.3 "Launch", .6, -.34 "Note"]
            """;
        var adapter = new ChartDiagramAdapter(ChartFamily.Xy);
        var chart = Assert.IsType<XyChart>(adapter.Parse(source).Chart);
        Assert.True(chart.Horizontal);
        Assert.Equal(directive, chart.Directive);
        Assert.Equal(ChartAxisKind.Categories, chart.XAxis.Kind);
        Assert.Equal("Second quarter", chart.XAxis.Categories[1]);
        Assert.Equal(-10, chart.YAxis.Minimum);
        Assert.Equal(new[] { ChartSeriesKind.Bar, ChartSeriesKind.Line }, chart.Series.Select(s => s.Kind));
        Assert.Equal("Launch", chart.Series[1].Values[0].Label);
        Assert.Equal(-.34, chart.Series[1].Values[2].Value);
        Assert.Equal(ChartDiagramAdapter.Write(chart), adapter.Write(adapter.Parse(ChartDiagramAdapter.Write(chart))));
    }

    [Fact]
    public void RadarSeparatesAxisAndCurveIdentitiesAndBindsNamedValues()
    {
        const string source = """
            ---
            title: Grades
            ---
            radar-beta
            axis a["Art"], b["Biology"], c["Computing"]
            curve a["Alice"]{c: 30, a: 20, b: 10}, b["Bob"]{5, 6, 7}
            min 0
            max 100
            graticule polygon
            ticks 5
            showLegend false
            """;
        var adapter = new ChartDiagramAdapter(ChartFamily.Radar);
        var chart = Assert.IsType<RadarChart>(adapter.Parse(source).Chart);
        Assert.True(chart.TitleInFrontmatter);
        Assert.Equal("Grades", chart.Title);
        Assert.Equal("a", chart.Axes[0].Id);
        Assert.Equal("a", chart.Curves[0].Id);
        Assert.True(chart.Curves[0].NamedValues);
        Assert.Equal(20, chart.Curves[0].Values.Single(v => v.AxisId == "a").Value);
        Assert.Equal(5, chart.Curves[1].Values.Single(v => v.AxisId == "a").Value);
        Assert.False(chart.ShowLegend);
        var rewritten = ChartDiagramAdapter.Write(chart);
        Assert.Contains("c: 30, a: 20, b: 10", rewritten);
        Assert.Throws<ChartNotEditableException>(() => adapter.Parse("radar-beta\naxis A,B,C\ncurve x{1,2}\n"));
    }

    [Fact]
    public void UnchangedChartSourceIsPreservedExactly()
    {
        const string source = "%% comment\r\nxychart-beta horizontal\r\n  line [1, 2, 3]\r\n";
        var adapter = new ChartDiagramAdapter(ChartFamily.Xy);
        var model = adapter.Parse(source);
        Assert.Equal(source, adapter.Apply(source, model));
    }
}
