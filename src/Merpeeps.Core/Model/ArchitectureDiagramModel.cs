using System.Text.Json.Nodes;

namespace Merpeeps.Core.Model;

public enum ArchitectureNodeKind { Service, Junction }
public enum ArchitecturePort { Left, Right, Top, Bottom }
public enum ArchitectureAlignmentAxis { Row, Column }
public sealed record ArchitectureNode(string Id, string SourceId, ArchitectureNodeKind Kind)
{
    public string? Label { get; init; }
    public string? Icon { get; init; }
    public string? GroupId { get; init; }
    public string Display => Label ?? SourceId;
}
public sealed record ArchitectureGroup(string Id, string SourceId)
{
    public string? Label { get; init; }
    public string? Icon { get; init; }
    public string? ParentId { get; init; }
    public string Display => Label ?? SourceId;
}
public sealed record ArchitectureEndpoint(string NodeId, ArchitecturePort Port, bool GroupBoundary = false);
public sealed record ArchitectureConnection(string Id, ArchitectureEndpoint From, ArchitectureEndpoint To)
{
    public bool FromArrow { get; init; }
    public bool ToArrow { get; init; }
}
public sealed record ArchitectureAlignment(string Id, ArchitectureAlignmentAxis Axis, IReadOnlyList<string> Members);
public sealed class ArchitectureDiagramDocument
{
    public List<ArchitectureNode> Nodes { get; } = new();
    public List<ArchitectureGroup> Groups { get; } = new();
    public List<ArchitectureConnection> Connections { get; } = new();
    public List<ArchitectureAlignment> Alignments { get; } = new();
    public JsonObject Configuration { get; set; } = new();
    public JsonObject ThemeVariables { get; set; } = new();
    public JsonObject GlobalConfiguration { get; set; } = new();
    public List<string> Comments { get; } = new();
    public ArchitectureDiagramDocument Clone()
    {
        var copy = new ArchitectureDiagramDocument
        {
            Configuration = (JsonObject)Configuration.DeepClone(), ThemeVariables = (JsonObject)ThemeVariables.DeepClone(),
            GlobalConfiguration = (JsonObject)GlobalConfiguration.DeepClone(),
        };
        copy.Nodes.AddRange(Nodes); copy.Groups.AddRange(Groups); copy.Connections.AddRange(Connections);
        copy.Alignments.AddRange(Alignments.Select(a => a with { Members = a.Members.ToArray() }));
        copy.Comments.AddRange(Comments); return copy;
    }
}
public static class ArchitectureDiagramValidation
{
    public static IReadOnlyList<DataDiagramDiagnostic> Validate(ArchitectureDiagramDocument document)
    {
        var issues = new List<DataDiagramDiagnostic>();
        var allIds = new HashSet<string>(StringComparer.Ordinal);
        var sourceIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (id, sourceId) in document.Nodes.Select(n => (n.Id, n.SourceId)).Concat(document.Groups.Select(g => (g.Id, g.SourceId))))
        {
            if (id.Length == 0 || !allIds.Add(id)) issues.Add(new("architecture-identity", id, "Architecture identities must be unique.", true));
            if (!Identifier(sourceId) || !sourceIds.Add(sourceId)) issues.Add(new("architecture-source-id", id, "Architecture source identifiers must be unique identifiers.", true));
        }
        var groups = document.Groups.Select(g => g.Id).ToHashSet();
        var nodes = document.Nodes.Select(n => n.Id).ToHashSet();
        foreach (var group in document.Groups)
        {
            if (group.ParentId is { } parent && !groups.Contains(parent)) issues.Add(new("architecture-parent", group.Id, "Parent group does not exist.", true));
            var visited = new HashSet<string> { group.Id }; var current = group.ParentId;
            while (current is not null && groups.Contains(current))
            {
                if (!visited.Add(current)) { issues.Add(new("architecture-group-cycle", group.Id, "Groups cannot contain themselves.", true)); break; }
                current = document.Groups.First(g => g.Id == current).ParentId;
            }
        }
        foreach (var node in document.Nodes)
        {
            if (!Enum.IsDefined(node.Kind)) issues.Add(new("architecture-kind", node.Id, "Unknown service/junction kind.", true));
            if (node.GroupId is { } group && !groups.Contains(group)) issues.Add(new("architecture-parent", node.Id, "Service group does not exist.", true));
            if (node.Kind == ArchitectureNodeKind.Junction && (node.Icon is not null || node.Label is not null))
                issues.Add(new("architecture-junction", node.Id, "A junction has no source icon or label.", true));
        }
        foreach (var link in document.Connections)
        {
            Identity(link.Id);
            Endpoint(link.Id, link.From); Endpoint(link.Id, link.To);
        }
        foreach (var alignment in document.Alignments)
        {
            Identity(alignment.Id);
            if (!Enum.IsDefined(alignment.Axis) || alignment.Members.Count < 2 || alignment.Members.Distinct().Count() != alignment.Members.Count || alignment.Members.Any(m => !nodes.Contains(m)))
                issues.Add(new("architecture-alignment", alignment.Id, "An alignment needs two or more distinct services/junctions.", true));
        }
        return issues;
        void Identity(string id)
        {
            if (id.Length == 0 || !allIds.Add(id)) issues.Add(new("architecture-identity", id, "Connection/alignment identities must be unique.", true));
        }
        void Endpoint(string id, ArchitectureEndpoint endpoint)
        {
            var node = document.Nodes.FirstOrDefault(n => n.Id == endpoint.NodeId);
            if (node is null || !Enum.IsDefined(endpoint.Port))
                issues.Add(new("architecture-endpoint", id, "Edges must target services or junctions with an explicit port.", true));
            else if (endpoint.GroupBoundary && (node.Kind != ArchitectureNodeKind.Service || node.GroupId is null))
                issues.Add(new("architecture-group-endpoint", id, "A group-boundary endpoint requires a service inside a group.", true));
        }
    }
    internal static bool Identifier(string value) => System.Text.RegularExpressions.Regex.IsMatch(value, @"^[\p{L}_][\p{L}\p{N}_-]*$");
}
