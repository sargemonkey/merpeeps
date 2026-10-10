using Merpeeps.Core.Model;
using Merpeeps.Core.Parsing;

namespace Merpeeps.Core.Editing;

public sealed class SetEntityGroupCommand(DiagramSubgraph group, string id, string? title, string? parentId, Direction? direction) : IEditCommand
{
    private DiagramSubgraph[] _groups = [];
    private DiagramEdge[] _edges = [];
    private readonly Dictionary<DiagramNode, string?> _scopes = new();
    private Dictionary<string, DiagramRect> _layouts = new();
    public DiagramSubgraph? Result { get; private set; }
    public string Description => "Edit ER subgraph";
    public void Do(DiagramModel model)
    {
        EntityEditing.Require(model);
        _ = EntityDiagramAdapter.IdentifierText(id);
        EntityEditing.ValidateLabel(title);
        if (!model.Subgraphs.Contains(group)) throw new ArgumentException("Subgraph no longer exists.");
        if (id != group.Id && (model.Node(id) is not null || model.Subgraphs.Any(g => g.Id == id)))
            throw new ArgumentException("Endpoint identity already exists.");
        if (direction is { } value && !Enum.IsDefined(value)) throw new ArgumentException("Unknown direction.");
        var ancestor = parentId;
        var visited = new HashSet<string>();
        while (ancestor is not null)
        {
            if (ancestor == group.Id || !visited.Add(ancestor)) throw new ArgumentException("A subgraph cannot contain itself.");
            ancestor = model.Subgraphs.SingleOrDefault(g => g.Id == ancestor)?.ParentId
                ?? (model.Subgraphs.Any(g => g.Id == ancestor) ? null : throw new ArgumentException("Parent subgraph does not exist."));
        }
        _groups = model.Subgraphs.ToArray(); _edges = model.Edges.ToArray();
        _layouts = new(model.GroupLayouts, StringComparer.Ordinal);
        _scopes.Clear();
        foreach (var node in model.Nodes) _scopes.Add(node, node.SubgraphId);
        Result = new() { Id = id, Title = title, ParentId = parentId, Direction = direction };
        Result.NodeIds.AddRange(group.NodeIds);
        if (model.GroupLayouts.Remove(group.Id, out var frame)) model.GroupLayouts[id] = frame;
        model.Subgraphs.Clear();
        foreach (var previous in _groups)
        {
            if (ReferenceEquals(previous, group)) model.Subgraphs.Add(Result);
            else if (previous.ParentId == group.Id)
            {
                var replacement = new DiagramSubgraph { Id = previous.Id, Title = previous.Title, ParentId = id, Direction = previous.Direction };
                replacement.NodeIds.AddRange(previous.NodeIds);
                model.Subgraphs.Add(replacement);
            }
            else model.Subgraphs.Add(previous);
        }
        foreach (var node in model.Nodes)
            if (node.SubgraphId == group.Id) node.SubgraphId = id;
        for (var i = 0; i < model.Edges.Count; i++)
        {
            var edge = model.Edges[i];
            if (edge.From != group.Id && edge.To != group.Id) continue;
            model.Edges[i] = new()
            {
                From = edge.From == group.Id ? id : edge.From,
                To = edge.To == group.Id ? id : edge.To,
                Label = edge.Label,
                Kind = edge.Kind,
                Ordinal = edge.Ordinal,
                EntityRelationship = edge.EntityRelationship,
            };
        }
    }
    public void Undo(DiagramModel model)
    {
        model.Subgraphs.Clear(); model.Subgraphs.AddRange(_groups);
        model.Edges.Clear(); model.Edges.AddRange(_edges);
        foreach (var (node, scope) in _scopes) node.SubgraphId = scope;
        model.GroupLayouts.Clear();
        foreach (var (id, frame) in _layouts) model.GroupLayouts[id] = frame;
    }
}

/// <summary>Remove one grouping boundary and its own relationships, retaining entities and nested groups.</summary>
public sealed class DeleteEntityGroupCommand(DiagramSubgraph group) : IEditCommand
{
    private DiagramSubgraph[] _groups = [];
    private DiagramEdge[] _edges = [];
    private readonly Dictionary<DiagramNode, string?> _scopes = new();
    private readonly Dictionary<DiagramSubgraph, string[]> _members = new();
    private readonly Dictionary<DiagramSubgraph, string?> _parents = new();
    private Dictionary<string, DiagramRect> _layouts = new();
    public string Description => "Remove ER subgraph, keep entities";
    public void Do(DiagramModel model)
    {
        EntityEditing.Require(model);
        if (!model.Subgraphs.Contains(group)) throw new ArgumentException("Subgraph no longer exists.");
        _groups = model.Subgraphs.ToArray(); _edges = model.Edges.ToArray();
        _layouts = new(model.GroupLayouts, StringComparer.Ordinal);
        _scopes.Clear(); _members.Clear(); _parents.Clear();
        foreach (var node in model.Nodes) _scopes.Add(node, node.SubgraphId);
        foreach (var prior in model.Subgraphs)
        {
            _members.Add(prior, prior.NodeIds.ToArray());
            _parents.Add(prior, prior.ParentId);
        }
        var parent = group.ParentId is null ? null : model.Subgraphs.Single(g => g.Id == group.ParentId);
        foreach (var node in model.Nodes.Where(n => n.SubgraphId == group.Id))
        {
            node.SubgraphId = group.ParentId;
            parent?.NodeIds.Add(node.Id);
        }
        foreach (var child in model.Subgraphs.Where(g => g.ParentId == group.Id)) child.ParentId = group.ParentId;
        model.Edges.RemoveAll(e => e.From == group.Id || e.To == group.Id);
        model.Subgraphs.Remove(group);
        model.GroupLayouts.Remove(group.Id);
    }
    public void Undo(DiagramModel model)
    {
        model.Subgraphs.Clear(); model.Subgraphs.AddRange(_groups);
        model.Edges.Clear(); model.Edges.AddRange(_edges);
        foreach (var (node, scope) in _scopes) node.SubgraphId = scope;
        foreach (var (prior, members) in _members)
        {
            prior.NodeIds.Clear(); prior.NodeIds.AddRange(members);
            prior.ParentId = _parents[prior];
        }
        model.GroupLayouts.Clear();
        foreach (var (id, frame) in _layouts) model.GroupLayouts[id] = frame;
    }
}
