using System.Text.Json.Nodes;

namespace Merpeeps.Core.Model;

public enum OutlineFamily { Mindmap, TreeView }
public enum DataDiagramRole { Diagram, Node, Section, Period, Event, Task }
public sealed record DataDiagramTarget(DataDiagramRole Role, string Id, string? ParentId = null);
public enum OutlineShape { Default, Square, Rounded, Circle, Bang, Cloud, Hexagon }
public sealed record OutlineNode(string Id, string Label, IReadOnlyList<OutlineNode> Children)
{
    public string? SourceId { get; init; }
    public OutlineShape Shape { get; init; }
    public bool Markdown { get; init; }
    public bool Directory { get; init; }
    public string? Description { get; init; }
    /// <summary>Null means unspecified; empty or "none" explicitly hides an icon.</summary>
    public string? Icon { get; init; }
    public IReadOnlyList<string> Classes { get; init; } = Array.Empty<string>();
    public OutlineNode DeepCopy() => this with { Children = Children.Select(n => n.DeepCopy()).ToArray(), Classes = Classes.ToArray() };
}
public sealed class OutlineDocument
{
    public OutlineFamily Family { get; init; }
    public List<OutlineNode> Roots { get; } = new();
    public JsonObject Configuration { get; set; } = new();
    public JsonObject ThemeVariables { get; set; } = new();
    public JsonObject GlobalConfiguration { get; set; } = new();
    public List<string> Comments { get; } = new();
    public bool BoxDrawingSource { get; set; }
    public Dictionary<string, DiagramPoint> Positions { get; } = new(StringComparer.Ordinal);
    public IEnumerable<OutlineNode> Nodes => Walk(Roots);
    public OutlineNode Node(string id) => Nodes.SingleOrDefault(n => n.Id == id) ?? throw new ArgumentException("Outline node no longer exists.");
    public OutlineDocument Clone()
    {
        var clone = new OutlineDocument
        {
            Family = Family, BoxDrawingSource = BoxDrawingSource, Configuration = (JsonObject)Configuration.DeepClone(),
            ThemeVariables = (JsonObject)ThemeVariables.DeepClone(), GlobalConfiguration = (JsonObject)GlobalConfiguration.DeepClone(),
        };
        clone.Roots.AddRange(Roots.Select(n => n.DeepCopy()));
        clone.Comments.AddRange(Comments);
        foreach (var pair in Positions) clone.Positions.Add(pair.Key, pair.Value);
        return clone;
    }
    public static IEnumerable<OutlineNode> Walk(IEnumerable<OutlineNode> nodes)
    {
        foreach (var node in nodes) { yield return node; foreach (var child in Walk(node.Children)) yield return child; }
    }
}

public enum TemporalFamily { Timeline, Journey }
public sealed record TemporalSection(string Id, string Label);
public sealed record TimelineEvent(string Id, string Text);
public sealed record TimelinePeriod(string Id, string Label, string? SectionId, IReadOnlyList<TimelineEvent> Events);
public sealed record JourneyTask(string Id, string Label, string? SectionId, int Score, IReadOnlyList<string> Actors);
public sealed class TemporalDocument
{
    public TemporalFamily Family { get; init; }
    public string Title { get; set; } = "";
    public bool TitleInFrontmatter { get; set; }
    public bool TopDown { get; set; }
    public List<TemporalSection> Sections { get; } = new();
    public List<TimelinePeriod> Periods { get; } = new();
    public List<JourneyTask> Tasks { get; } = new();
    public JsonObject Configuration { get; set; } = new();
    public JsonObject ThemeVariables { get; set; } = new();
    public JsonObject GlobalConfiguration { get; set; } = new();
    public List<string> Comments { get; } = new();
    public TemporalDocument Clone()
    {
        var copy = new TemporalDocument
        {
            Family = Family, Title = Title, TitleInFrontmatter = TitleInFrontmatter, TopDown = TopDown,
            Configuration = (JsonObject)Configuration.DeepClone(), ThemeVariables = (JsonObject)ThemeVariables.DeepClone(),
            GlobalConfiguration = (JsonObject)GlobalConfiguration.DeepClone(),
        };
        copy.Sections.AddRange(Sections); copy.Periods.AddRange(Periods.Select(p => p with { Events = p.Events.ToArray() }));
        copy.Tasks.AddRange(Tasks.Select(t => t with { Actors = t.Actors.ToArray() })); copy.Comments.AddRange(Comments);
        return copy;
    }
}

public sealed record DataDiagramDiagnostic(string Code, string Id, string Message, bool Blocking = false);
public sealed class DataDiagramNotEditableException(IReadOnlyList<DataDiagramDiagnostic> diagnostics)
    : NotSupportedException(string.Join("\n", diagnostics.Select(d => d.Message)))
{
    public IReadOnlyList<DataDiagramDiagnostic> Diagnostics { get; } = diagnostics;
}
