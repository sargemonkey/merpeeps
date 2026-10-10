using System.Text.Json.Nodes;

namespace Merpeeps.Core.Model;

public sealed record SwimlaneGroup(string Id, string SourceId, string Label, string? ParentId = null, Direction? Direction = null)
{
    public JsonObject Style { get; init; } = new();
    public IReadOnlyList<string> Classes { get; init; } = [];
    public SwimlaneGroup DeepCopy() => this with { Style = (JsonObject)Style.DeepClone(), Classes = Classes.ToArray() };
}
public sealed record SwimlaneNode(string Id, string SourceId, string Label, NodeShape Shape = NodeShape.Rectangle, string? GroupId = null)
{
    public JsonObject Style { get; init; } = new();
    public IReadOnlyList<string> Classes { get; init; } = [];
    public SwimlaneNode DeepCopy() => this with { Style = (JsonObject)Style.DeepClone(), Classes = Classes.ToArray() };
}
public sealed record SwimlaneLink(string Id, string From, string To, EdgeKind Kind = EdgeKind.Arrow, string Label = "");
public sealed class SwimlaneDiagramDocument
{
    public Direction Direction { get; set; } = Direction.TB;
    public List<SwimlaneGroup> Groups { get; } = new();
    public List<SwimlaneNode> Nodes { get; } = new();
    public List<SwimlaneLink> Links { get; } = new();
    public Dictionary<string, JsonObject> Classes { get; } = new(StringComparer.Ordinal);
    public string AccessibilityTitle { get; set; } = "";
    public string AccessibilityDescription { get; set; } = "";
    public List<string> Comments { get; } = new();
    public JsonObject Configuration { get; set; } = new();
    public JsonObject ThemeVariables { get; set; } = new();
    public JsonObject GlobalConfiguration { get; set; } = new();
    public string? LaneOf(string nodeId)
    {
        var groupId = Nodes.Single(n => n.Id == nodeId).GroupId; var visited = new HashSet<string>();
        while (groupId is not null)
        {
            if (!visited.Add(groupId)) throw new ArgumentException("Swimlane group hierarchy contains a cycle.");
            var group = Groups.Single(g => g.Id == groupId);
            if (group.ParentId is null) return group.Id;
            groupId = group.ParentId;
        }
        return null;
    }
    public SwimlaneDiagramDocument Clone()
    {
        var copy = new SwimlaneDiagramDocument
        {
            Direction = Direction, AccessibilityTitle = AccessibilityTitle, AccessibilityDescription = AccessibilityDescription,
            Configuration = (JsonObject)Configuration.DeepClone(), ThemeVariables = (JsonObject)ThemeVariables.DeepClone(),
            GlobalConfiguration = (JsonObject)GlobalConfiguration.DeepClone(),
        };
        copy.Groups.AddRange(Groups.Select(g => g.DeepCopy())); copy.Nodes.AddRange(Nodes.Select(n => n.DeepCopy())); copy.Links.AddRange(Links);
        foreach (var pair in Classes) copy.Classes[pair.Key] = (JsonObject)pair.Value.DeepClone();
        copy.Comments.AddRange(Comments); return copy;
    }
}
