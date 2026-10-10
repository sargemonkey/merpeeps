using Merpeeps.Core.Model;

namespace Merpeeps.Core.Editing;

public sealed class SetClassNamespaceDefinitionCommand(
    DiagramSubgraph group, string id, string? title, string? parentId) : IEditCommand
{
    private DiagramSubgraph[] _before = [];
    private readonly Dictionary<DiagramNode, string?> _scopes = new();
    private Dictionary<string, DiagramRect> _layouts = new();
    public DiagramSubgraph? Result { get; private set; }
    public string Description => "Edit class namespace";

    public void Do(DiagramModel model)
    {
        ClassEditing.RequireModel(model);
        ClassEditing.ValidId(id);
        if (!model.Subgraphs.Contains(group)) throw new ArgumentException("Namespace no longer exists.");
        if (parentId is not null &&
            (parentId == group.Id || parentId.StartsWith(group.Id + ".", StringComparison.Ordinal) ||
             !model.Subgraphs.Any(g => g.Id == parentId) || !id.StartsWith(parentId + ".", StringComparison.Ordinal)))
            throw new ArgumentException("Choose an existing parent outside this namespace and use a qualified identity.");
        var affected = model.Subgraphs.Where(g => g.Id == group.Id || g.Id.StartsWith(group.Id + ".", StringComparison.Ordinal)).ToArray();
        var mapping = affected.ToDictionary(g => g.Id, g => id + g.Id[group.Id.Length..]);
        if (mapping.Values.Distinct().Count() != mapping.Count ||
            model.Subgraphs.Except(affected).Any(g => mapping.Values.Contains(g.Id)))
            throw new ArgumentException("The new namespace identity conflicts with an existing namespace.");
        _before = model.Subgraphs.ToArray();
        _layouts = new(model.GroupLayouts, StringComparer.Ordinal);
        _scopes.Clear();
        foreach (var node in model.Nodes) _scopes.Add(node, node.SubgraphId);
        var replacements = affected.ToDictionary(g => g.Id, g => new DiagramSubgraph
        {
            Id = mapping[g.Id],
            Title = ReferenceEquals(g, group) ? title : g.Title,
            ParentId = ReferenceEquals(g, group) ? parentId
                : g.ParentId is { } parent ? mapping.GetValueOrDefault(parent, parent) : null,
            IsExplicit = ReferenceEquals(g, group) || g.IsExplicit,
        });
        foreach (var original in affected) replacements[original.Id].NodeIds.AddRange(original.NodeIds);
        model.Subgraphs.Clear();
        model.Subgraphs.AddRange(_before.Select(g => replacements.GetValueOrDefault(g.Id, g)));
        foreach (var node in model.Nodes)
            if (node.SubgraphId is { } scope && mapping.TryGetValue(scope, out var replacement))
                node.SubgraphId = replacement;
        foreach (var oldId in mapping.Keys) model.GroupLayouts.Remove(oldId);
        foreach (var (oldId, newId) in mapping)
            if (_layouts.TryGetValue(oldId, out var frame)) model.GroupLayouts[newId] = frame;
        Result = replacements[group.Id];
    }

    public void Undo(DiagramModel model)
    {
        model.Subgraphs.Clear();
        model.Subgraphs.AddRange(_before);
        foreach (var (node, scope) in _scopes) node.SubgraphId = scope;
        model.GroupLayouts.Clear();
        foreach (var (id, frame) in _layouts) model.GroupLayouts[id] = frame;
    }
}

/// <summary>Remove a namespace subtree but keep all of its classes in an explicit destination scope.</summary>
public sealed class DeleteClassNamespaceCommand(DiagramSubgraph group, string? destinationId = null) : IEditCommand
{
    private DiagramSubgraph[] _before = [];
    private readonly Dictionary<DiagramNode, string?> _scopes = new();
    private readonly Dictionary<DiagramSubgraph, string[]> _members = new();
    private Dictionary<string, DiagramRect> _layouts = new();
    public string Description => "Remove namespace and retain its classes";
    public void Do(DiagramModel model)
    {
        ClassEditing.RequireModel(model);
        if (!model.Subgraphs.Contains(group)) throw new ArgumentException("Namespace no longer exists.");
        var removed = model.Subgraphs.Where(g => g.Id == group.Id || g.Id.StartsWith(group.Id + ".", StringComparison.Ordinal)).ToArray();
        var target = destinationId is null ? null : model.Subgraphs.SingleOrDefault(g => g.Id == destinationId)
            ?? throw new ArgumentException("Destination namespace does not exist.");
        if (target is not null && removed.Contains(target)) throw new ArgumentException("Move classes outside the namespace being removed.");
        _before = model.Subgraphs.ToArray();
        _layouts = new(model.GroupLayouts, StringComparer.Ordinal);
        _scopes.Clear(); _members.Clear();
        foreach (var ns in model.Subgraphs) _members.Add(ns, ns.NodeIds.ToArray());
        var ids = removed.Select(g => g.Id).ToHashSet();
        foreach (var node in model.Nodes.Where(n => n.SubgraphId is { } id && ids.Contains(id)))
        {
            _scopes.Add(node, node.SubgraphId);
            node.SubgraphId = destinationId;
            target?.NodeIds.Add(node.Id);
        }
        model.Subgraphs.RemoveAll(removed.Contains);
        foreach (var id in ids) model.GroupLayouts.Remove(id);
    }
    public void Undo(DiagramModel model)
    {
        model.Subgraphs.Clear(); model.Subgraphs.AddRange(_before);
        foreach (var (node, scope) in _scopes) node.SubgraphId = scope;
        foreach (var (group, members) in _members)
        {
            group.NodeIds.Clear(); group.NodeIds.AddRange(members);
        }
        model.GroupLayouts.Clear();
        foreach (var (id, frame) in _layouts) model.GroupLayouts[id] = frame;
    }
}
