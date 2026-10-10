using Merpeeps.Core.Model;
using Merpeeps.Core.Parsing;
using System.Text.Json.Nodes;

namespace Merpeeps.Core.Editing;

public sealed class ArchitectureEditCommand : IEditCommand
{
    private readonly Action<ArchitectureDiagramDocument> _edit;
    private ArchitectureDiagramDocument? _before;
    public string Description { get; }
    public string? ResultId { get; }
    internal ArchitectureEditCommand(string description, string? resultId, Action<ArchitectureDiagramDocument> edit) =>
        (Description, ResultId, _edit) = (description, resultId, edit);
    public void Do(DiagramModel model)
    {
        var before = model.Architecture ?? throw new ArgumentException("Choose an Architecture diagram.");
        var candidate = before.Clone(); _edit(candidate); ArchitectureDiagramAdapter.Check(candidate);
        _ = ArchitectureDiagramAdapter.Write(candidate);
        _ = Layout.ArchitectureDiagramLayout.Create(new DiagramModel { Kind = "architecture-beta", Architecture = candidate });
        _before = before; model.Architecture = candidate;
    }
    public void Undo(DiagramModel model) => model.Architecture = _before ?? throw new InvalidOperationException("Architecture edit has not been applied.");
}
public static class ArchitectureDiagramEditing
{
    public static ArchitectureEditCommand AddNode(ArchitectureNode node) => new("Add architecture node", node.Id, d => d.Nodes.Add(node));
    public static ArchitectureEditCommand UpdateNode(ArchitectureNode node) => new("Edit architecture node", node.Id, d => d.Nodes[Index(d.Nodes, n => n.Id == node.Id)] = node);
    public static ArchitectureEditCommand RemoveNode(string id) => new("Delete architecture node and connections", null, d =>
    {
        d.Nodes.RemoveAt(Index(d.Nodes, n => n.Id == id));
        d.Connections.RemoveAll(e => e.From.NodeId == id || e.To.NodeId == id);
        for (var i = d.Alignments.Count - 1; i >= 0; i--)
        {
            var members = d.Alignments[i].Members.Where(m => m != id).ToArray();
            if (members.Length < 2) d.Alignments.RemoveAt(i); else d.Alignments[i] = d.Alignments[i] with { Members = members };
        }
    });
    public static ArchitectureEditCommand AddGroup(ArchitectureGroup group) => new("Add architecture group", group.Id, d => d.Groups.Add(group));
    public static ArchitectureEditCommand UpdateGroup(ArchitectureGroup group) => new("Edit architecture group", group.Id, d => d.Groups[Index(d.Groups, g => g.Id == group.Id)] = group);
    /// <summary>Delete the group, reparent direct members, and move group-boundary endpoints to the parent boundary or the service.</summary>
    public static ArchitectureEditCommand RemoveGroup(string id) => new("Ungroup architecture members", null, d =>
    {
        var index = Index(d.Groups, g => g.Id == id); var parent = d.Groups[index].ParentId;
        var members = d.Nodes.Where(n => n.GroupId == id).Select(n => n.Id).ToHashSet();
        d.Groups.RemoveAt(index);
        for (var i = 0; i < d.Groups.Count; i++) if (d.Groups[i].ParentId == id) d.Groups[i] = d.Groups[i] with { ParentId = parent };
        for (var i = 0; i < d.Nodes.Count; i++) if (d.Nodes[i].GroupId == id) d.Nodes[i] = d.Nodes[i] with { GroupId = parent };
        if (parent is null)
            for (var i = 0; i < d.Connections.Count; i++)
            {
                var edge = d.Connections[i];
                d.Connections[i] = edge with
                {
                    From = members.Contains(edge.From.NodeId) ? edge.From with { GroupBoundary = false } : edge.From,
                    To = members.Contains(edge.To.NodeId) ? edge.To with { GroupBoundary = false } : edge.To,
                };
            }
    });
    public static ArchitectureEditCommand Connect(ArchitectureConnection connection) => new("Connect architecture ports", connection.Id, d => d.Connections.Add(connection));
    public static ArchitectureEditCommand UpdateConnection(ArchitectureConnection connection) => new("Edit architecture connection", connection.Id, d => d.Connections[Index(d.Connections, e => e.Id == connection.Id)] = connection);
    public static ArchitectureEditCommand RemoveConnection(string id) => new("Delete architecture connection", null, d => d.Connections.RemoveAt(Index(d.Connections, e => e.Id == id)));
    public static ArchitectureEditCommand SetAlignment(ArchitectureAlignment alignment)
    {
        var copy = alignment with { Members = alignment.Members.ToArray() };
        return new("Align architecture nodes", alignment.Id, d =>
        {
            var index = d.Alignments.FindIndex(a => a.Id == alignment.Id);
            if (index < 0) d.Alignments.Add(copy with { Members = copy.Members.ToArray() });
            else d.Alignments[index] = copy with { Members = copy.Members.ToArray() };
        });
    }
    public static ArchitectureEditCommand RemoveAlignment(string id) => new("Remove architecture alignment", null, d => d.Alignments.RemoveAt(Index(d.Alignments, a => a.Id == id)));
    public static ArchitectureEditCommand SetOption(string key, JsonNode? value)
    {
        var snapshot = value?.DeepClone();
        return new("Edit architecture spacing", null, d =>
        {
            if (snapshot is not null && key is not ("iconSize" or "nodeSeparation" or "idealEdgeLengthMultiplier"))
                throw new NotSupportedException("The native architecture layout does not implement option '" + key + "'. Existing source options are preserved.");
            if (snapshot is null) d.Configuration.Remove(key); else d.Configuration[key] = snapshot.DeepClone();
        });
    }
    private static int Index<T>(IReadOnlyList<T> items, Func<T, bool> predicate)
    {
        for (var index = 0; index < items.Count; index++) if (predicate(items[index])) return index;
        throw new ArgumentException("Architecture element no longer exists.");
    }
}
