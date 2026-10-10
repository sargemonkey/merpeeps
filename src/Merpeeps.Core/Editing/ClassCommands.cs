using Merpeeps.Core.Model;
using Merpeeps.Core.Parsing;

namespace Merpeeps.Core.Editing;

public static class ClassEditing
{
    public static DiagramNode CreateClass(string id, string? label = null) => new()
    {
        Id = ValidId(id),
        Label = label,
        Class = new(),
        Width = 240,
        Height = 100,
    };

    public static DiagramNode CreateNote(string id, string text, string? target = null) => new()
    {
        Id = ValidId(id),
        Label = text,
        Class = new() { Kind = ClassNodeKind.Note, NoteFor = target },
        Width = 240,
        Height = 100,
    };

    public static DiagramNode CreateInterface(string id, string name) => new()
    {
        Id = ValidId(id),
        Label = name,
        Class = new() { Kind = ClassNodeKind.Lollipop, ReferenceName = name },
        Width = Math.Max(80, name.Length * 7 + 16),
        Height = 60,
    };

    public static IEditCommand AddMember(DiagramNode node, ClassMember member)
    {
        var next = Definition(node).Clone();
        if (next.Members.Any(m => m.Id == member.Id))
            throw new ArgumentException($"Member identity '{member.Id}' already exists.");
        next.Members.Add(member);
        return new SetClassDefinitionCommand(node, next, $"Add member to {node.Display}");
    }

    public static IEditCommand EditMember(DiagramNode node, ClassMember member)
    {
        var next = Definition(node).Clone();
        var index = next.Members.FindIndex(m => m.Id == member.Id);
        if (index < 0) throw new ArgumentException($"Member '{member.Id}' does not exist.");
        next.Members[index] = member;
        return new SetClassDefinitionCommand(node, next, $"Edit member of {node.Display}");
    }

    public static IEditCommand DeleteMember(DiagramNode node, string memberId)
    {
        var next = Definition(node).Clone();
        var index = next.Members.FindIndex(m => m.Id == memberId);
        if (index < 0) throw new ArgumentException($"Member '{memberId}' does not exist.");
        next.Members.RemoveAt(index);
        return new SetClassDefinitionCommand(node, next, $"Remove member from {node.Display}");
    }

    public static IEditCommand MoveMember(DiagramNode node, string memberId, int targetIndex)
    {
        var next = Definition(node).Clone();
        var index = next.Members.FindIndex(m => m.Id == memberId);
        if (index < 0 || targetIndex < 0 || targetIndex >= next.Members.Count)
            throw new ArgumentOutOfRangeException(nameof(targetIndex), "Member and destination must exist.");
        var member = next.Members[index];
        next.Members.RemoveAt(index);
        next.Members.Insert(targetIndex, member);
        return new SetClassDefinitionCommand(node, next, $"Reorder members of {node.Display}");
    }

    internal static ClassNodeDefinition Definition(DiagramNode node) =>
        node.Class is { Kind: ClassNodeKind.Class } definition ? definition
            : throw new ArgumentException("Choose a class card, not a note or interface endpoint.", nameof(node));

    internal static void RequireModel(DiagramModel model)
    {
        if (model.ClassOptions is null || model.Kind is not ("classDiagram" or "classDiagram-v2"))
            throw new InvalidOperationException("This edit requires a class diagram.");
    }

    internal static void RequireNode(DiagramModel model, DiagramNode node)
    {
        RequireModel(model);
        if (!model.Nodes.Contains(node)) throw new InvalidOperationException("The selected element is no longer in this diagram.");
    }

    internal static string ValidId(string id)
    {
        _ = ClassDiagramAdapter.Id(id);
        return id;
    }
}

public sealed class AddClassElementCommand(DiagramNode node) : IEditCommand
{
    private readonly AddNodeCommand _add = new(node);
    public string Description => $"Add {node.Class?.Kind.ToString().ToLowerInvariant()} {node.Display}";
    public void Do(DiagramModel model)
    {
        ClassEditing.RequireModel(model);
        ClassEditing.ValidId(node.Id);
        if (node.Class is null) throw new ArgumentException("A class element needs typed content.");
        if (node.Class.Kind == ClassNodeKind.Lollipop)
            throw new ArgumentException("Add an interface occurrence with its relationship atomically.");
        if (model.Node(node.Id) is not null) throw new ArgumentException($"Element '{node.Id}' already exists.");
        if (node.Class.NoteFor is { } target && model.Node(target)?.Class?.Kind != ClassNodeKind.Class)
            throw new ArgumentException($"Note target '{target}' is not a class.");
        if (node.SubgraphId is { } ns && !model.Subgraphs.Any(g => g.Id == ns))
            throw new ArgumentException($"Namespace '{ns}' does not exist.");
        _add.Do(model);
        if (node.SubgraphId is { } namespaceId)
            model.Subgraphs.Single(g => g.Id == namespaceId).NodeIds.Add(node.Id);
        ClassDiagramAdapter.ResolveStyles(model);
    }
    public void Undo(DiagramModel model)
    {
        foreach (var group in model.Subgraphs) group.NodeIds.Remove(node.Id);
        _add.Undo(model);
    }
}

public sealed class SetClassDefinitionCommand : IEditCommand
{
    private readonly DiagramNode _node;
    private readonly ClassNodeDefinition _next;
    private ClassNodeDefinition? _before;
    public string Description { get; }

    public SetClassDefinitionCommand(DiagramNode node, ClassNodeDefinition definition, string? description = null)
    {
        if (node.Class is null || node.Class.Kind != definition.Kind)
            throw new ArgumentException("Changing an element's semantic kind is not a class-content edit.");
        if (definition.Members.Select(m => m.Id).Distinct().Count() != definition.Members.Count)
            throw new ArgumentException("Class member identities must be unique within a class.");
        if (definition.Annotations.Any(a => string.IsNullOrWhiteSpace(a) || a.IndexOfAny(['\r', '\n', '<', '>']) >= 0))
            throw new ArgumentException("An annotation must be a name without << >> delimiters or line breaks.");
        if (definition.GenericType?.IndexOfAny(['\r', '\n', '{', '}', ',']) >= 0)
            throw new ArgumentException("Generic types cannot contain blocks, line breaks or comma type arguments.");
        _node = node;
        _next = definition.Clone();
        Description = description ?? $"Edit class {node.Display}";
    }

    public void Do(DiagramModel model)
    {
        ClassEditing.RequireNode(model, _node);
        if (_next.NoteFor is { } target && model.Node(target)?.Class?.Kind != ClassNodeKind.Class)
            throw new ArgumentException($"Note target '{target}' does not exist.");
        _before = _node.Class!.Clone();
        _node.Class = _next.Clone();
        ClassDiagramAdapter.ResolveStyles(model);
    }
    public void Undo(DiagramModel model)
    {
        _node.Class = _before?.Clone() ?? throw new InvalidOperationException("The edit has not been applied.");
        ClassDiagramAdapter.ResolveStyles(model);
    }
}

public sealed class AddClassRelationshipCommand(DiagramEdge edge) : IEditCommand
{
    public string Description => $"Connect {edge.From} to {edge.To}";
    public void Do(DiagramModel model)
    {
        ClassEditing.RequireModel(model);
        Validate(model, edge, edge.ClassRelationship);
        if (model.Edges.Any(e => e.Key == edge.Key)) throw new ArgumentException("That relationship identity already exists.");
        model.Edges.Add(edge);
    }
    public void Undo(DiagramModel model) => model.Edges.Remove(edge);

    internal static void Validate(DiagramModel model, DiagramEdge edge, ClassRelationship? relation)
    {
        if (relation is null) throw new ArgumentException("Choose UML relationship endpoints and a line style.");
        if (edge.Label?.IndexOfAny(['\r', '\n', ';']) >= 0 ||
            relation.FromMultiplicity?.IndexOfAny(['\r', '\n']) >= 0 ||
            relation.ToMultiplicity?.IndexOfAny(['\r', '\n']) >= 0)
            throw new ArgumentException("Relationship labels and multiplicities must be single-line values.");
        foreach (var from in new[] { true, false })
        {
            var id = from ? edge.From : edge.To;
            var node = model.Node(id) ?? throw new ArgumentException($"Endpoint '{id}' does not exist.");
            var marker = from ? relation.FromEnd : relation.ToEnd;
            if (!Enum.IsDefined(marker)) throw new ArgumentException("Unknown UML endpoint decoration.");
            if (node.Class is null || node.Class.Kind == ClassNodeKind.Note)
                throw new ArgumentException("A UML relationship requires classes or interface occurrences.");
            if ((node.Class.Kind == ClassNodeKind.Lollipop) != (marker == ClassRelationEnd.Lollipop))
                throw new ArgumentException("A lollipop marker requires its own interface occurrence.");
            if (node.Class.Kind == ClassNodeKind.Lollipop &&
                model.Edges.Any(e => !ReferenceEquals(e, edge) && (e.From == id || e.To == id)))
                throw new ArgumentException("Each lollipop occurrence belongs to one relationship.");
        }
    }
}

public sealed class AddClassInterfaceCommand : IEditCommand
{
    public DiagramNode Node { get; }
    public DiagramEdge Edge { get; }
    private readonly string _ownerId;
    public string Description => $"Add interface {Node.Display}";

    public AddClassInterfaceCommand(string ownerId, string occurrenceId, string name, bool atSource = false)
    {
        _ownerId = ownerId;
        Node = ClassEditing.CreateInterface(occurrenceId, ClassEditing.ValidId(name));
        Edge = new DiagramEdge
        {
            From = atSource ? Node.Id : ownerId,
            To = atSource ? ownerId : Node.Id,
            Kind = EdgeKind.Open,
            ClassRelationship = new(
                atSource ? ClassRelationEnd.Lollipop : ClassRelationEnd.None,
                atSource ? ClassRelationEnd.None : ClassRelationEnd.Lollipop),
        };
    }
    public void Do(DiagramModel model)
    {
        ClassEditing.RequireModel(model);
        var owner = model.Node(_ownerId);
        if (owner?.Class?.Kind != ClassNodeKind.Class) throw new ArgumentException("An interface requires an existing owner class.");
        if (model.Node(Node.Id) is not null || model.Edges.Any(e => e.Key == Edge.Key))
            throw new ArgumentException("The interface occurrence identity already exists.");
        Node.X = owner.X + owner.Width + 60;
        Node.Y = owner.Y;
        model.Nodes.Add(Node);
        model.Edges.Add(Edge);
    }
    public void Undo(DiagramModel model)
    {
        model.Edges.Remove(Edge);
        model.Nodes.Remove(Node);
    }
}

public sealed class DeleteClassRelationshipCommand(DiagramEdge edge) : IEditCommand
{
    private int _index;
    private readonly List<(DiagramNode Node, int Index)> _occurrences = new();
    public string Description => $"Remove relationship {edge.From} to {edge.To}";
    public void Do(DiagramModel model)
    {
        ClassEditing.RequireModel(model);
        _index = model.Edges.IndexOf(edge);
        if (_index < 0) throw new ArgumentException("The relationship no longer exists.");
        _occurrences.Clear();
        foreach (var id in new[] { edge.From, edge.To })
            if (model.Node(id) is { Class.Kind: ClassNodeKind.Lollipop } node)
                _occurrences.Add((node, model.Nodes.IndexOf(node)));
        model.Edges.RemoveAt(_index);
        foreach (var (node, _) in _occurrences) model.Nodes.Remove(node);
    }
    public void Undo(DiagramModel model)
    {
        foreach (var (node, index) in _occurrences.OrderBy(e => e.Index)) model.Nodes.Insert(index, node);
        model.Edges.Insert(_index, edge);
    }
}

public sealed class ReconnectClassRelationshipCommand : IEditCommand
{
    private readonly DiagramEdge _before;
    private readonly string _from;
    private readonly string _to;
    private int _index;
    public DiagramEdge? Result { get; private set; }
    public string Description => "Reconnect class relationship";
    public ReconnectClassRelationshipCommand(DiagramEdge edge, string from, string to) =>
        (_before, _from, _to) = (edge, from, to);
    public void Do(DiagramModel model)
    {
        ClassEditing.RequireModel(model);
        _index = model.Edges.IndexOf(_before);
        if (_index < 0) throw new ArgumentException("The relationship no longer exists.");
        if (_before.ClassRelationship is { FromEnd: ClassRelationEnd.Lollipop } or { ToEnd: ClassRelationEnd.Lollipop })
            throw new ArgumentException("Recreate a lollipop connection rather than sharing its occurrence.");
        var ordinal = model.Edges.Where(e => !ReferenceEquals(e, _before) && e.From == _from && e.To == _to)
            .Select(e => e.Ordinal + 1).DefaultIfEmpty(0).Max();
        Result = new()
        {
            From = _from,
            To = _to,
            Ordinal = ordinal,
            Label = _before.Label,
            Kind = _before.Kind,
            ClassRelationship = _before.ClassRelationship,
        };
        AddClassRelationshipCommand.Validate(model, Result, Result.ClassRelationship);
        model.Edges[_index] = Result;
    }
    public void Undo(DiagramModel model) => model.Edges[_index] = _before;
}

public sealed class RenameClassIdentityCommand : IEditCommand
{
    private readonly DiagramNode _before;
    private readonly string _id;
    private readonly List<(int Index, DiagramEdge Edge)> _edges = new();
    private readonly List<DiagramNode> _notes = new();
    private int _index;
    public DiagramNode? Result { get; private set; }
    public string Description => $"Change class identity to {_id}";
    public RenameClassIdentityCommand(DiagramNode node, string id) =>
        (_before, _id) = (node, ClassEditing.ValidId(id));
    public void Do(DiagramModel model)
    {
        ClassEditing.RequireNode(model, _before);
        ClassEditing.Definition(_before);
        if (model.Node(_id) is not null) throw new ArgumentException($"Class '{_id}' already exists.");
        _index = model.Nodes.IndexOf(_before);
        Result = new DiagramNode
        {
            Id = _id,
            Label = _before.Label,
            Class = _before.Class!.Clone(),
            SubgraphId = _before.SubgraphId,
            X = _before.X,
            Y = _before.Y,
            Width = _before.Width,
            Height = _before.Height,
            HasExplicitPosition = _before.HasExplicitPosition,
            Shape = _before.Shape,
            Fill = _before.Fill,
            Stroke = _before.Stroke,
            TextColor = _before.TextColor,
            StrokeWidth = _before.StrokeWidth,
            Dashed = _before.Dashed,
            Icon = _before.Icon,
        };
        _edges.Clear();
        for (var i = 0; i < model.Edges.Count; i++)
        {
            var edge = model.Edges[i];
            if (edge.From != _before.Id && edge.To != _before.Id) continue;
            _edges.Add((i, edge));
            model.Edges[i] = new()
            {
                From = edge.From == _before.Id ? _id : edge.From,
                To = edge.To == _before.Id ? _id : edge.To,
                Ordinal = edge.Ordinal,
                Label = edge.Label,
                Kind = edge.Kind,
                ClassRelationship = edge.ClassRelationship,
            };
        }
        _notes.Clear();
        foreach (var note in model.Nodes.Where(n => n.Class?.NoteFor == _before.Id))
        {
            _notes.Add(note);
            note.Class!.NoteFor = _id;
        }
        foreach (var group in model.Subgraphs)
            for (var i = 0; i < group.NodeIds.Count; i++)
                if (group.NodeIds[i] == _before.Id) group.NodeIds[i] = _id;
        model.Nodes[_index] = Result;
    }
    public void Undo(DiagramModel model)
    {
        model.Nodes[_index] = _before;
        foreach (var (index, edge) in _edges) model.Edges[index] = edge;
        foreach (var note in _notes) note.Class!.NoteFor = _before.Id;
        foreach (var group in model.Subgraphs)
            for (var i = 0; i < group.NodeIds.Count; i++)
                if (group.NodeIds[i] == _id) group.NodeIds[i] = _before.Id;
    }
}

public sealed class SetClassRelationshipCommand(
    DiagramEdge edge, ClassRelationship next, string? label) : IEditCommand
{
    private ClassRelationship? _before;
    private string? _label;
    private EdgeKind _kind;
    public string Description => $"Edit relationship {edge.From} to {edge.To}";
    public void Do(DiagramModel model)
    {
        ClassEditing.RequireModel(model);
        if (!model.Edges.Contains(edge)) throw new ArgumentException("The relationship is no longer present.");
        AddClassRelationshipCommand.Validate(model, edge, next);
        _before = edge.ClassRelationship;
        _label = edge.Label;
        _kind = edge.Kind;
        edge.ClassRelationship = next;
        edge.Label = label;
        edge.Kind = next.Dashed ? EdgeKind.Dotted : EdgeKind.Open;
    }
    public void Undo(DiagramModel model)
    {
        edge.ClassRelationship = _before ?? throw new InvalidOperationException("The edit has not been applied.");
        edge.Label = _label;
        edge.Kind = _kind;
    }
}

public sealed class AddClassNamespaceCommand(DiagramSubgraph group) : IEditCommand
{
    public string Description => $"Add namespace {group.Title ?? group.Id}";
    public void Do(DiagramModel model)
    {
        ClassEditing.RequireModel(model);
        ClassEditing.ValidId(group.Id);
        if (model.Subgraphs.Any(g => g.Id == group.Id)) throw new ArgumentException("Namespace identity already exists.");
        if (group.ParentId is { } parent &&
            (!model.Subgraphs.Any(g => g.Id == parent) || !group.Id.StartsWith(parent + ".", StringComparison.Ordinal)))
            throw new ArgumentException("A nested namespace needs its existing parent and a qualified identity.");
        model.Subgraphs.Add(group);
    }
    public void Undo(DiagramModel model) => model.Subgraphs.Remove(group);
}

public sealed class SetClassNamespaceCommand(DiagramNode node, string? namespaceId) : IEditCommand
{
    private string? _before;
    private bool _wasExplicit;
    public string Description => $"Move {node.Display} to namespace";
    public void Do(DiagramModel model)
    {
        ClassEditing.RequireNode(model, node);
        ClassEditing.Definition(node);
        var group = namespaceId is null ? null : model.Subgraphs.SingleOrDefault(g => g.Id == namespaceId)
            ?? throw new ArgumentException($"Namespace '{namespaceId}' does not exist.");
        _before = node.SubgraphId;
        _wasExplicit = group?.IsExplicit ?? false;
        Assign(model, node, namespaceId);
        if (group is not null) group.IsExplicit = true;
    }
    public void Undo(DiagramModel model)
    {
        Assign(model, node, _before);
        if (namespaceId is not null) model.Subgraphs.Single(g => g.Id == namespaceId).IsExplicit = _wasExplicit;
    }
    private static void Assign(DiagramModel model, DiagramNode node, string? id)
    {
        foreach (var group in model.Subgraphs) group.NodeIds.Remove(node.Id);
        node.SubgraphId = id;
        if (id is not null) model.Subgraphs.Single(g => g.Id == id).NodeIds.Add(node.Id);
    }
}

public sealed class SetClassOptionsCommand(bool hideEmptyMembersBox, bool hierarchicalNamespaces) : IEditCommand
{
    private bool _hide;
    private bool _hierarchical;
    public string Description => "Change class diagram display";
    public void Do(DiagramModel model)
    {
        ClassEditing.RequireModel(model);
        _hide = model.ClassOptions!.HideEmptyMembersBox;
        _hierarchical = model.ClassOptions.HierarchicalNamespaces;
        model.ClassOptions.HideEmptyMembersBox = hideEmptyMembersBox;
        model.ClassOptions.HierarchicalNamespaces = hierarchicalNamespaces;
    }
    public void Undo(DiagramModel model)
    {
        ClassEditing.RequireModel(model);
        model.ClassOptions!.HideEmptyMembersBox = _hide;
        model.ClassOptions.HierarchicalNamespaces = _hierarchical;
    }
}
