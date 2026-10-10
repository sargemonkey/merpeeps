using System.Text.Json.Nodes;

namespace Merpeeps.Core.Model;

public enum ChartFamily { Pie, Quadrant, Xy, Radar }
public enum ChartElementRole { Chart, Slice, Point, Series, Axis, Curve, CurveValue }
public enum ChartSeriesKind { Bar, Line }
public enum ChartAxisKind { Auto, Categories, Range }
public enum RadarGridKind { Circle, Polygon }
public sealed record ChartTarget(ChartElementRole Role, string Id, string? DetailId = null);

public abstract class ChartDocument
{
    public abstract ChartFamily Family { get; }
    public string Title { get; set; } = "";
    public bool TitleInFrontmatter { get; set; }
    public JsonObject Configuration { get; set; } = new();
    public JsonObject ThemeVariables { get; set; } = new();
    public JsonObject GlobalConfiguration { get; set; } = new();
    public List<string> Comments { get; } = new();
    public abstract ChartDocument Clone();
    protected T CopyBase<T>(T target) where T : ChartDocument
    {
        target.Title = Title; target.TitleInFrontmatter = TitleInFrontmatter;
        target.Configuration = (JsonObject)Configuration.DeepClone();
        target.ThemeVariables = (JsonObject)ThemeVariables.DeepClone();
        target.GlobalConfiguration = (JsonObject)GlobalConfiguration.DeepClone();
        target.Comments.AddRange(Comments);
        return target;
    }
}

public sealed record PieSlice(string Id, string Label, double Value);
public sealed class PieChart : ChartDocument
{
    public override ChartFamily Family => ChartFamily.Pie;
    public bool ShowData { get; set; }
    public List<PieSlice> Slices { get; } = new();
    public override ChartDocument Clone()
    {
        var copy = CopyBase(new PieChart { ShowData = ShowData });
        copy.Slices.AddRange(Slices);
        return copy;
    }
}

public sealed record QuadrantPoint(string Id, string Label, double X, double Y, string? StyleClass = null)
{
    public JsonObject Style { get; init; } = new();
    public QuadrantPoint DeepCopy() => this with { Style = (JsonObject)Style.DeepClone() };
}
public sealed class QuadrantChart : ChartDocument
{
    public override ChartFamily Family => ChartFamily.Quadrant;
    public string XLow { get; set; } = "";
    public string XHigh { get; set; } = "";
    public string YLow { get; set; } = "";
    public string YHigh { get; set; } = "";
    /// <summary>Top-right, top-left, bottom-left, bottom-right, in Mermaid's quadrant order.</summary>
    public string[] Quadrants { get; set; } = ["", "", "", ""];
    public List<QuadrantPoint> Points { get; } = new();
    public Dictionary<string, JsonObject> StyleClasses { get; } = new(StringComparer.Ordinal);
    public override ChartDocument Clone()
    {
        var copy = CopyBase(new QuadrantChart { XLow = XLow, XHigh = XHigh, YLow = YLow, YHigh = YHigh, Quadrants = Quadrants.ToArray() });
        copy.Points.AddRange(Points.Select(p => p.DeepCopy()));
        foreach (var pair in StyleClasses) copy.StyleClasses.Add(pair.Key, (JsonObject)pair.Value.DeepClone());
        return copy;
    }
}

public sealed record ChartAxis(ChartAxisKind Kind = ChartAxisKind.Auto, string Title = "", double? Minimum = null, double? Maximum = null)
{
    public IReadOnlyList<string> Categories { get; init; } = Array.Empty<string>();
    public ChartAxis DeepCopy() => this with { Categories = Categories.ToArray() };
}
public sealed record ChartValue(string Id, double Value, string? Label = null);
public sealed record XySeries(string Id, ChartSeriesKind Kind, string? Name, IReadOnlyList<ChartValue> Values);
public sealed class XyChart : ChartDocument
{
    public override ChartFamily Family => ChartFamily.Xy;
    public string Directive { get; set; } = "xychart";
    public bool Horizontal { get; set; }
    public bool OrientationInBody { get; set; }
    public ChartAxis XAxis { get; set; } = new();
    public ChartAxis YAxis { get; set; } = new();
    public List<XySeries> Series { get; } = new();
    public override ChartDocument Clone()
    {
        var copy = CopyBase(new XyChart { Directive = Directive, Horizontal = Horizontal, OrientationInBody = OrientationInBody, XAxis = XAxis.DeepCopy(), YAxis = YAxis.DeepCopy() });
        copy.Series.AddRange(Series.Select(s => s with { Values = s.Values.ToArray() }));
        return copy;
    }
}

public sealed record RadarAxis(string Id, string? Label = null)
{
    public string Display => Label ?? Id;
}
public sealed record RadarValue(string AxisId, double Value);
public sealed record RadarCurve(string Id, string? Label, IReadOnlyList<RadarValue> Values, bool NamedValues = false)
{
    public string Display => Label ?? Id;
}
public sealed class RadarChart : ChartDocument
{
    public override ChartFamily Family => ChartFamily.Radar;
    public List<RadarAxis> Axes { get; } = new();
    public List<RadarCurve> Curves { get; } = new();
    public double Minimum { get; set; }
    public double? Maximum { get; set; }
    public bool ShowLegend { get; set; } = true;
    public RadarGridKind Grid { get; set; } = RadarGridKind.Circle;
    public int Ticks { get; set; } = 5;
    public override ChartDocument Clone()
    {
        var copy = CopyBase(new RadarChart { Minimum = Minimum, Maximum = Maximum, ShowLegend = ShowLegend, Grid = Grid, Ticks = Ticks });
        copy.Axes.AddRange(Axes);
        copy.Curves.AddRange(Curves.Select(c => c with { Values = c.Values.ToArray() }));
        return copy;
    }
}

public enum ChartDiagnosticKind { InvalidData, NativeUnsupported, Warning }
public sealed record ChartDiagnostic(string Code, ChartTarget Target, string Message, ChartDiagnosticKind Kind = ChartDiagnosticKind.InvalidData);
public sealed record ChartReadResult(ChartDocument Chart, IReadOnlyList<ChartDiagnostic> Diagnostics)
{
    public bool CanEdit => Diagnostics.All(d => d.Kind == ChartDiagnosticKind.Warning);
}
public sealed class ChartNotEditableException(ChartReadResult result)
    : NotSupportedException(string.Join("\n", result.Diagnostics.Select(d => d.Message)))
{
    public ChartReadResult Result { get; } = result;
}
