using Merpeeps.Core.Model;

namespace Merpeeps.Core.Editing;

/// <summary>
/// A reversible edit. Every structural change goes through one of these so
/// undo/redo is uniform and the UI never mutates the model directly.
/// </summary>
public interface IEditCommand
{
    string Description { get; }
    void Do(DiagramModel model);
    void Undo(DiagramModel model);
}

/// <summary>Undo and redo stacks over a single model.</summary>
public sealed class EditHistory
{
    private readonly Stack<IEditCommand> _undo = new();
    private readonly Stack<IEditCommand> _redo = new();
    private readonly DiagramModel _model;

    public EditHistory(DiagramModel model) => _model = model;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string? NextUndo => _undo.Count > 0 ? _undo.Peek().Description : null;
    public string? NextRedo => _redo.Count > 0 ? _redo.Peek().Description : null;

    public event EventHandler? Changed;

    public void Apply(IEditCommand cmd)
    {
        cmd.Do(_model);
        _undo.Push(cmd);
        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Record an already-applied change (a completed drag, say) without
    /// re-running it.
    /// </summary>
    public void Record(IEditCommand cmd)
    {
        _undo.Push(cmd);
        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Undo()
    {
        if (_undo.Count == 0) return;
        var c = _undo.Pop();
        c.Undo(_model);
        _redo.Push(c);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        var c = _redo.Pop();
        c.Do(_model);
        _undo.Push(c);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

// ----------------------------------------------------------------- commands

public sealed class AddNodeCommand(DiagramNode node) : IEditCommand
{
    public string Description => $"Add {node.Id}";

    public void Do(DiagramModel m)
    {
        if (m.Node(node.Id) is not null) return;
        if (m.ClassOptions is not null && node.Class is null)
        {
            node.Class = new ClassNodeDefinition();
            node.Shape = NodeShape.Rectangle;
        }

        // The editor supplies a drop point; an agent supplies none, and every
        // node it adds would otherwise pile up on the origin, on top of
        // whatever is already there. Stack them below the diagram instead.
        //
        // Deliberately does not set HasExplicitPosition: this is a fallback,
        // not a placement a human chose, so a later layout pass is still free
        // to integrate the node properly.
        if (!node.HasExplicitPosition && node.X == 0 && node.Y == 0 && m.Nodes.Count > 0)
        {
            node.X = m.Nodes.Min(n => n.X);
            node.Y = m.Nodes.Max(n => n.Y + n.Height) + 60;
        }

        m.Nodes.Add(node);
    }

    public void Undo(DiagramModel m) => m.Nodes.Remove(node);
}

/// <summary>Deletes nodes together with every edge touching them.</summary>
public sealed class DeleteNodesCommand(IEnumerable<DiagramNode> nodes) : IEditCommand
{
    private readonly List<DiagramNode> _nodes = nodes.ToList();
    private readonly List<(DiagramEdge Edge, int Index)> _edges = new();
    private readonly List<(DiagramNode Node, int Index)> _nodeIndices = new();
    private readonly List<(DiagramSubgraph Sg, string Id, int Index)> _members = new();
    private List<DiagramNode> _removed = new();

    public string Description => _nodes.Count == 1
        ? $"Delete {_nodes[0].Id}" : $"Delete {_nodes.Count} nodes";

    public void Do(DiagramModel m)
    {
        _edges.Clear(); _nodeIndices.Clear(); _members.Clear();
        var ids = _nodes.Select(n => n.Id).ToHashSet();
        _removed = _nodes.ToList();
        if (m.ClassOptions is not null)
        {
            foreach (var dependent in m.Nodes.Where(n =>
                n.Class is { Kind: ClassNodeKind.Note, NoteFor: { } target } && ids.Contains(target) ||
                n.Class?.Kind == ClassNodeKind.Lollipop && m.Edges.Any(e =>
                    e.From == n.Id && ids.Contains(e.To) || e.To == n.Id && ids.Contains(e.From))).ToArray())
            {
                if (ids.Add(dependent.Id)) _removed.Add(dependent);
            }
        }

        for (var i = m.Edges.Count - 1; i >= 0; i--)
        {
            var e = m.Edges[i];
            if (!ids.Contains(e.From) && !ids.Contains(e.To)) continue;
            _edges.Add((e, i));
            m.Edges.RemoveAt(i);
        }
        foreach (var sg in m.Subgraphs)
            for (var i = sg.NodeIds.Count - 1; i >= 0; i--)
                if (ids.Contains(sg.NodeIds[i]))
                {
                    _members.Add((sg, sg.NodeIds[i], i));
                    sg.NodeIds.RemoveAt(i);
                }
        foreach (var n in _removed)
        {
            var idx = m.Nodes.IndexOf(n);
            if (idx < 0) continue;
            _nodeIndices.Add((n, idx));
            m.Nodes.RemoveAt(idx);
        }
    }

    public void Undo(DiagramModel m)
    {
        foreach (var (n, i) in _nodeIndices.OrderBy(t => t.Index))
            m.Nodes.Insert(Math.Min(i, m.Nodes.Count), n);
        foreach (var (sg, id, i) in _members.OrderBy(t => t.Index))
            sg.NodeIds.Insert(Math.Min(i, sg.NodeIds.Count), id);
        foreach (var (e, i) in _edges.OrderBy(t => t.Index))
            m.Edges.Insert(Math.Min(i, m.Edges.Count), e);
    }
}

public sealed class AddEdgeCommand(DiagramEdge edge) : IEditCommand
{
    public string Description => $"Connect {edge.From} to {edge.To}";
    public void Do(DiagramModel m)
    {
        if (m.ClassOptions is not null)
        {
            edge.ClassRelationship ??= new ClassRelationship();
            AddClassRelationshipCommand.Validate(m, edge, edge.ClassRelationship);
        }
        m.Edges.Add(edge);
    }
    public void Undo(DiagramModel m) => m.Edges.Remove(edge);
}

public sealed class DeleteEdgeCommand(DiagramEdge edge) : IEditCommand
{
    private int _index = -1;
    private DeleteClassRelationshipCommand? _classDelete;
    public string Description => $"Delete link {edge.From}->{edge.To}";
    public void Do(DiagramModel m)
    {
        if (m.ClassOptions is not null)
        {
            _classDelete = new(edge);
            _classDelete.Do(m);
            return;
        }
        _index = m.Edges.IndexOf(edge);
        if (_index >= 0) m.Edges.RemoveAt(_index);
    }
    public void Undo(DiagramModel m)
    {
        if (_classDelete is not null) { _classDelete.Undo(m); return; }
        if (_index >= 0) m.Edges.Insert(Math.Min(_index, m.Edges.Count), edge);
    }
}

public sealed class RenameNodeCommand(DiagramNode node, string? label) : IEditCommand
{
    private string? _prev;
    private string? _reference;
    public string Description => $"Rename {node.Id}";
    public void Do(DiagramModel m)
    {
        _prev = node.Label;
        _reference = node.Class?.ReferenceName;
        if (node.Class?.Kind == ClassNodeKind.Lollipop && string.IsNullOrWhiteSpace(label))
            throw new ArgumentException("An interface occurrence needs a name.", nameof(label));
        node.Label = label;
        if (node.Class?.Kind == ClassNodeKind.Lollipop) node.Class.ReferenceName = label;
    }
    public void Undo(DiagramModel m)
    {
        node.Label = _prev;
        if (node.Class?.Kind == ClassNodeKind.Lollipop) node.Class.ReferenceName = _reference;
    }
}

/// <summary>Set or clear the label on a link.</summary>
/// <remarks>
/// Unlike a node's, an edge label is part of the mermaid body: <c>a --&gt;|text| b</c>.
/// So changing it rewrites a line, and the empty string is stored as null so the
/// writer emits <c>a --&gt; b</c> rather than an empty pair of pipes.
/// </remarks>
public sealed class RelabelEdgeCommand(DiagramEdge edge, string? label) : IEditCommand
{
    private string? _prev;

    public string Description => $"Label {edge.From}->{edge.To}";

    public void Do(DiagramModel m)
    {
        _prev = edge.Label;
        edge.Label = string.IsNullOrWhiteSpace(label) ? null : label.Trim();
    }

    public void Undo(DiagramModel m) => edge.Label = _prev;
}

/// <summary>Change a link's arrow style — solid, dotted, thick, open, invisible.</summary>
public sealed class ChangeEdgeKindCommand(DiagramEdge edge, EdgeKind kind) : IEditCommand
{
    private EdgeKind _prev;
    public string Description => $"Style {edge.From}->{edge.To}";
    public void Do(DiagramModel m) { _prev = edge.Kind; edge.Kind = kind; }
    public void Undo(DiagramModel m) => edge.Kind = _prev;
}

public sealed class ChangeShapeCommand(DiagramNode node, NodeShape shape) : IEditCommand
{
    private NodeShape _prev;
    public string Description => $"Shape of {node.Id}";
    public void Do(DiagramModel m) { _prev = node.Shape; node.Shape = shape; }
    public void Undo(DiagramModel m) => node.Shape = _prev;
}

/// <summary>
/// Applies presentation to nodes. Styling lives entirely in Merpeeps, so this never
/// touches the diagram body — which is what lets Merpeeps offer node styling and
/// icons that mermaid syntax itself cannot express.
/// </summary>
public sealed class StyleNodesCommand : IEditCommand
{
    public sealed record Style(
        string? Fill = null, string? Stroke = null, string? TextColor = null,
        double? StrokeWidth = null, bool? Dashed = null, string? Icon = null,
        bool ClearIcon = false);

    private readonly List<DiagramNode> _nodes;
    private readonly Style _next;
    private readonly List<(string? Fill, string? Stroke, string? Text,
                           double? Width, bool Dashed, string? Icon)> _prev = new();

    public StyleNodesCommand(IEnumerable<DiagramNode> nodes, Style style)
    {
        _nodes = nodes.ToList();
        _next = style;
    }

    public string Description => _nodes.Count == 1
        ? $"Style {_nodes[0].Id}" : $"Style {_nodes.Count} nodes";

    public void Do(DiagramModel m)
    {
        _prev.Clear();
        foreach (var n in _nodes)
        {
            _prev.Add((n.Fill, n.Stroke, n.TextColor, n.StrokeWidth, n.Dashed, n.Icon));
            if (_next.Fill is not null) n.Fill = _next.Fill;
            if (_next.Stroke is not null) n.Stroke = _next.Stroke;
            if (_next.TextColor is not null) n.TextColor = _next.TextColor;
            if (_next.StrokeWidth is { } w) n.StrokeWidth = w;
            if (_next.Dashed is { } d) n.Dashed = d;
            if (_next.ClearIcon) n.Icon = null;
            else if (_next.Icon is not null) n.Icon = _next.Icon;
        }
    }

    public void Undo(DiagramModel m)
    {
        for (var i = 0; i < _nodes.Count && i < _prev.Count; i++)
        {
            var (fill, stroke, text, width, dashed, icon) = _prev[i];
            _nodes[i].Fill = fill;
            _nodes[i].Stroke = stroke;
            _nodes[i].TextColor = text;
            _nodes[i].StrokeWidth = width;
            _nodes[i].Dashed = dashed;
            _nodes[i].Icon = icon;
        }
    }
}

/// <summary>
/// Records a completed move. Positions live in Merpeeps rather than the body, but
/// moves still belong on the undo stack so the user's mental model holds.
/// </summary>
public sealed class MoveNodesCommand : IEditCommand
{
    private readonly List<(DiagramNode Node, double FromX, double FromY,
                           double ToX, double ToY)> _moves;

    /// <summary>
    /// Whether each node was explicitly positioned before the move, so undo can
    /// put that back.
    /// </summary>
    /// <remarks>
    /// Restoring the coordinates is not enough. Dragging a node pins it, and for
    /// a node that was already pinned undo is symmetric. But adopting a
    /// reference layout pins nodes that were never positioned at all, and if
    /// undo left them pinned it would leave the document worse than it started:
    /// every node frozen at a stale coordinate with the layout engine switched
    /// off for it. Undo has to be able to un-pin.
    /// </remarks>
    private readonly bool[] _wasExplicit;

    public MoveNodesCommand(
        IEnumerable<(DiagramNode Node, double FromX, double FromY,
                     double ToX, double ToY)> moves)
    {
        _moves = moves.ToList();
        _wasExplicit = _moves.Select(mv => mv.Node.HasExplicitPosition).ToArray();
    }

    public string Description => _moves.Count == 1
        ? $"Move {_moves[0].Node.Id}" : $"Move {_moves.Count} nodes";

    public void Do(DiagramModel m)
    {
        foreach (var mv in _moves)
        {
            mv.Node.X = mv.ToX; mv.Node.Y = mv.ToY;
            mv.Node.HasExplicitPosition = true;
        }
    }

    public void Undo(DiagramModel m)
    {
        for (var i = 0; i < _moves.Count; i++)
        {
            var mv = _moves[i];
            mv.Node.X = mv.FromX; mv.Node.Y = mv.FromY;
            mv.Node.HasExplicitPosition = _wasExplicit[i];
        }
    }
}
