using Merpeeps.Core.Model;

namespace Merpeeps.Core.Editing;

public sealed class MoveGroupCommand : IEditCommand
{
    private readonly MoveNodesCommand _nodes;
    private readonly Dictionary<string, DiagramRect> _frames = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DiagramRect?> _before = new(StringComparer.Ordinal);
    public string Description { get; }

    public MoveGroupCommand(DiagramModel model, DiagramSubgraph group, double deltaX, double deltaY)
    {
        if (!double.IsFinite(deltaX) || !double.IsFinite(deltaY)) throw new ArgumentException("Group movement must be finite.");
        Description = "Move " + (group.Title ?? group.Id);
        _nodes = new(GraphGroupGeometry.Members(model, group).Select(n => (n, n.X, n.Y, n.X + deltaX, n.Y + deltaY)));
        foreach (var empty in GraphGroupGeometry.Descendants(model, group).Where(g => GraphGroupGeometry.IsEmpty(model, g)))
        {
            _before[empty.Id] = model.GroupLayouts.TryGetValue(empty.Id, out var frame) ? frame : null;
            var bounds = GraphGroupGeometry.Bounds(model, empty);
            _frames[empty.Id] = bounds with { X = bounds.X + deltaX, Y = bounds.Y + deltaY };
        }
    }

    public void Do(DiagramModel model)
    {
        _nodes.Do(model);
        foreach (var (id, frame) in _frames) model.GroupLayouts[id] = frame;
    }

    public void Undo(DiagramModel model)
    {
        _nodes.Undo(model);
        foreach (var (id, frame) in _before)
            if (frame is { } previous) model.GroupLayouts[id] = previous;
            else model.GroupLayouts.Remove(id);
    }
}
