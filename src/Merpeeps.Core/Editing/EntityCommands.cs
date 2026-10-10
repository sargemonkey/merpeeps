using Merpeeps.Core.Model;
using Merpeeps.Core.Parsing;

namespace Merpeeps.Core.Editing;

public static class EntityEditing
{
    public static DiagramNode CreateEntity(string id, string? alias = null)
    {
        _ = EntityDiagramAdapter.IdentifierText(id);
        ValidateLabel(alias);
        return new() { Id = id, Label = alias, Entity = new(), Width = 280, Height = 100 };
    }
    public static IEditCommand AddAttribute(DiagramNode node, EntityAttribute attribute)
    {
        var next = Definition(node).Clone();
        if (next.Attributes.Any(a => a.Id == attribute.Id)) throw new ArgumentException("Attribute identity already exists.");
        next.Attributes.Add(attribute);
        return new SetEntityDefinitionCommand(node, next, "Add attribute to " + node.Display);
    }
    public static IEditCommand EditAttribute(DiagramNode node, EntityAttribute attribute)
    {
        var next = Definition(node).Clone();
        var index = next.Attributes.FindIndex(a => a.Id == attribute.Id);
        if (index < 0) throw new ArgumentException("Attribute no longer exists.");
        next.Attributes[index] = attribute;
        return new SetEntityDefinitionCommand(node, next, "Edit attribute of " + node.Display);
    }
    public static IEditCommand RemoveAttribute(DiagramNode node, string attributeId)
    {
        var next = Definition(node).Clone();
        var index = next.Attributes.FindIndex(a => a.Id == attributeId);
        if (index < 0) throw new ArgumentException("Attribute no longer exists.");
        next.Attributes.RemoveAt(index);
        return new SetEntityDefinitionCommand(node, next, "Remove attribute from " + node.Display);
    }
    public static IEditCommand MoveAttribute(DiagramNode node, string attributeId, int targetIndex)
    {
        var next = Definition(node).Clone();
        var index = next.Attributes.FindIndex(a => a.Id == attributeId);
        if (index < 0 || targetIndex < 0 || targetIndex >= next.Attributes.Count) throw new ArgumentOutOfRangeException(nameof(targetIndex));
        var attribute = next.Attributes[index];
        next.Attributes.RemoveAt(index);
        next.Attributes.Insert(targetIndex, attribute);
        return new SetEntityDefinitionCommand(node, next, "Reorder attributes of " + node.Display);
    }
    internal static EntityNodeDefinition Definition(DiagramNode node) =>
        node.Entity ?? throw new ArgumentException("Select an entity.");
    internal static void Require(DiagramModel model)
    {
        if (model.EntityOptions is null || model.Kind != "erDiagram") throw new InvalidOperationException("This edit requires an ER diagram.");
    }
    internal static void RequireNode(DiagramModel model, DiagramNode node)
    {
        Require(model);
        if (!model.Nodes.Contains(node) || node.Entity is null) throw new ArgumentException("The selected entity is no longer present.");
    }
    internal static void ValidateLabel(string? label)
    {
        if (label?.IndexOfAny(['"', '\r', '\n']) >= 0) throw new ArgumentException("Use a single-line ER label without double quotes.");
    }
}

public sealed class AddEntityCommand(DiagramNode node) : IEditCommand
{
    private readonly AddNodeCommand _add = new(node);
    public string Description => "Add entity " + node.Display;
    public void Do(DiagramModel model)
    {
        EntityEditing.Require(model);
        _ = EntityEditing.Definition(node);
        _ = EntityDiagramAdapter.IdentifierText(node.Id);
        if (model.Node(node.Id) is not null || model.Subgraphs.Any(g => g.Id == node.Id)) throw new ArgumentException("Endpoint identity already exists.");
        if (node.SubgraphId is { } scope && !model.Subgraphs.Any(g => g.Id == scope)) throw new ArgumentException("Subgraph does not exist.");
        _add.Do(model);
        if (node.SubgraphId is { } id) model.Subgraphs.Single(g => g.Id == id).NodeIds.Add(node.Id);
        EntityDiagramAdapter.ResolveStyles(model);
    }
    public void Undo(DiagramModel model)
    {
        foreach (var group in model.Subgraphs) group.NodeIds.Remove(node.Id);
        _add.Undo(model);
    }
}

public sealed class SetEntityDefinitionCommand : IEditCommand
{
    private readonly DiagramNode _node;
    private readonly EntityNodeDefinition _next;
    private EntityNodeDefinition? _before;
    public string Description { get; }
    public SetEntityDefinitionCommand(DiagramNode node, EntityNodeDefinition definition, string? description = null)
    {
        if (definition.Attributes.Select(a => a.Id).Distinct().Count() != definition.Attributes.Count)
            throw new ArgumentException("Attribute identities must be unique within an entity.");
        _node = node; _next = definition.Clone(); Description = description ?? "Edit entity " + node.Display;
    }
    public void Do(DiagramModel model)
    {
        EntityEditing.RequireNode(model, _node);
        _before = _node.Entity!.Clone();
        _node.Entity = _next.Clone();
        EntityDiagramAdapter.ResolveStyles(model);
    }
    public void Undo(DiagramModel model)
    {
        _node.Entity = _before?.Clone() ?? throw new InvalidOperationException("Edit has not been applied.");
        EntityDiagramAdapter.ResolveStyles(model);
    }
}

public sealed class SetEntityAliasCommand(DiagramNode node, string? alias) : IEditCommand
{
    private string? _before;
    public string Description => "Rename entity " + node.Display;
    public void Do(DiagramModel model)
    {
        EntityEditing.RequireNode(model, node);
        EntityEditing.ValidateLabel(alias);
        _before = node.Label;
        node.Label = alias;
    }
    public void Undo(DiagramModel model) => node.Label = _before;
}

public sealed class AddEntityRelationshipCommand(DiagramEdge edge) : IEditCommand
{
    public string Description => $"Connect {edge.From} to {edge.To}";
    public void Do(DiagramModel model)
    {
        Validate(model, edge.From, edge.To, edge.EntityRelationship, edge.Label);
        if (model.Edges.Any(e => e.Key == edge.Key)) throw new ArgumentException("Relationship identity already exists.");
        model.Edges.Add(edge);
    }
    public void Undo(DiagramModel model) => model.Edges.Remove(edge);
    internal static void Validate(DiagramModel model, string from, string to, EntityRelationship? relation, string? label)
    {
        EntityEditing.Require(model);
        EntityEditing.ValidateLabel(label);
        if (relation is null || !Enum.IsDefined(relation.FromCardinality) || !Enum.IsDefined(relation.ToCardinality))
            throw new ArgumentException("Choose a cardinality at both ends.");
        _ = EntityDiagramGeometry.Endpoint(model, from);
        _ = EntityDiagramGeometry.Endpoint(model, to);
    }
}

public sealed class SetEntityRelationshipCommand(DiagramEdge edge, EntityRelationship relation, string? label) : IEditCommand
{
    private EntityRelationship? _before;
    private string? _label;
    private EdgeKind _kind;
    public string Description => "Edit ER relationship";
    public void Do(DiagramModel model)
    {
        if (!model.Edges.Contains(edge)) throw new ArgumentException("The relationship is no longer present.");
        AddEntityRelationshipCommand.Validate(model, edge.From, edge.To, relation, label);
        _before = edge.EntityRelationship; _label = edge.Label; _kind = edge.Kind;
        edge.EntityRelationship = relation; edge.Label = label;
        edge.Kind = relation.Identifying ? EdgeKind.Open : EdgeKind.Dotted;
    }
    public void Undo(DiagramModel model)
    {
        edge.EntityRelationship = _before; edge.Label = _label; edge.Kind = _kind;
    }
}

public sealed class ReconnectEntityRelationshipCommand(DiagramEdge edge, string from, string to) : IEditCommand
{
    private int _index;
    public DiagramEdge? Result { get; private set; }
    public string Description => "Reconnect ER relationship";
    public void Do(DiagramModel model)
    {
        AddEntityRelationshipCommand.Validate(model, from, to, edge.EntityRelationship, edge.Label);
        _index = model.Edges.IndexOf(edge);
        if (_index < 0) throw new ArgumentException("The relationship is no longer present.");
        var ordinal = model.Edges.Where(e => !ReferenceEquals(e, edge) && e.From == from && e.To == to)
            .Select(e => e.Ordinal + 1).DefaultIfEmpty(0).Max();
        Result = new() { From = from, To = to, Label = edge.Label, Kind = edge.Kind, Ordinal = ordinal, EntityRelationship = edge.EntityRelationship };
        model.Edges[_index] = Result;
    }
    public void Undo(DiagramModel model) => model.Edges[_index] = edge;
}

public sealed class RenameEntityIdentityCommand(DiagramNode node, string id) : IEditCommand
{
    private int _index;
    private readonly List<(int Index, DiagramEdge Edge)> _edges = new();
    public DiagramNode? Result { get; private set; }
    public string Description => "Change entity identity";
    public void Do(DiagramModel model)
    {
        EntityEditing.RequireNode(model, node);
        _ = EntityDiagramAdapter.IdentifierText(id);
        if (model.Node(id) is not null || model.Subgraphs.Any(g => g.Id == id)) throw new ArgumentException("Endpoint identity already exists.");
        _index = model.Nodes.IndexOf(node);
        Result = new()
        {
            Id = id,
            Label = node.Label,
            Entity = node.Entity!.Clone(),
            SubgraphId = node.SubgraphId,
            X = node.X,
            Y = node.Y,
            Width = node.Width,
            Height = node.Height,
            Shape = node.Shape,
            Fill = node.Fill,
            Stroke = node.Stroke,
            TextColor = node.TextColor,
            StrokeWidth = node.StrokeWidth,
            Dashed = node.Dashed,
            Icon = node.Icon,
            HasExplicitPosition = node.HasExplicitPosition,
        };
        _edges.Clear();
        for (var i = 0; i < model.Edges.Count; i++)
        {
            var edge = model.Edges[i];
            if (edge.From != node.Id && edge.To != node.Id) continue;
            _edges.Add((i, edge));
            model.Edges[i] = new()
            {
                From = edge.From == node.Id ? id : edge.From,
                To = edge.To == node.Id ? id : edge.To,
                Label = edge.Label,
                Ordinal = edge.Ordinal,
                Kind = edge.Kind,
                EntityRelationship = edge.EntityRelationship,
            };
        }
        foreach (var group in model.Subgraphs)
            for (var i = 0; i < group.NodeIds.Count; i++)
                if (group.NodeIds[i] == node.Id) group.NodeIds[i] = id;
        model.Nodes[_index] = Result;
    }
    public void Undo(DiagramModel model)
    {
        model.Nodes[_index] = node;
        foreach (var (index, edge) in _edges) model.Edges[index] = edge;
        foreach (var group in model.Subgraphs)
            for (var i = 0; i < group.NodeIds.Count; i++)
                if (group.NodeIds[i] == id) group.NodeIds[i] = node.Id;
    }
}

public sealed class AddEntityGroupCommand(DiagramSubgraph group) : IEditCommand
{
    public string Description => "Add ER subgraph " + (group.Title ?? group.Id);
    public void Do(DiagramModel model)
    {
        EntityEditing.Require(model);
        _ = EntityDiagramAdapter.IdentifierText(group.Id);
        EntityEditing.ValidateLabel(group.Title);
        if (model.Node(group.Id) is not null || model.Subgraphs.Any(g => g.Id == group.Id)) throw new ArgumentException("Endpoint identity already exists.");
        if (group.ParentId is { } parent && !model.Subgraphs.Any(g => g.Id == parent)) throw new ArgumentException("Parent subgraph does not exist.");
        model.Subgraphs.Add(group);
    }
    public void Undo(DiagramModel model) => model.Subgraphs.Remove(group);
}

public sealed class SetEntityGroupMembershipCommand(DiagramNode node, string? groupId) : IEditCommand
{
    private string? _before;
    public string Description => "Move entity to subgraph";
    public void Do(DiagramModel model)
    {
        EntityEditing.RequireNode(model, node);
        if (groupId is not null && !model.Subgraphs.Any(g => g.Id == groupId)) throw new ArgumentException("Subgraph does not exist.");
        _before = node.SubgraphId; Assign(model, groupId);
    }
    public void Undo(DiagramModel model) => Assign(model, _before);
    private void Assign(DiagramModel model, string? scope)
    {
        foreach (var group in model.Subgraphs) group.NodeIds.Remove(node.Id);
        node.SubgraphId = scope;
        if (scope is not null) model.Subgraphs.Single(g => g.Id == scope).NodeIds.Add(node.Id);
    }
}
