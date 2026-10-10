using Merpeeps.Core.Model;

namespace Merpeeps.Core.Editing;

public enum GraphArrangement
{
    Left, HorizontalCenter, Right, Top, VerticalCenter, Bottom, DistributeHorizontally, DistributeVertically,
}

public sealed class MoveGraphObjectsCommand : IEditCommand
{
    private readonly IEditCommand[] _commands;
    public string Description { get; }

    public MoveGraphObjectsCommand(DiagramModel model, IEnumerable<GraphObject> objects, double deltaX, double deltaY)
    {
        if (!double.IsFinite(deltaX) || !double.IsFinite(deltaY)) throw new ArgumentException("Movement must be finite.");
        var items = GraphObjectGeometry.Normalize(model, objects);
        Description = items.Count == 1 ? "Move " + items[0].Id : $"Move {items.Count} objects";
        _commands = items.Select(item => Translate(model, item, deltaX, deltaY)).ToArray();
    }

    internal static IEditCommand Translate(DiagramModel model, GraphObject item, double deltaX, double deltaY)
    {
        if (item.Kind == GraphObjectKind.Group)
            return new MoveGroupCommand(model, model.Subgraphs.Single(g => g.Id == item.Id), deltaX, deltaY);
        var node = model.Node(item.Id) ?? throw new ArgumentException($"Node '{item.Id}' no longer exists.");
        return new MoveNodesCommand([(node, node.X, node.Y, node.X + deltaX, node.Y + deltaY)]);
    }

    public void Do(DiagramModel model) { foreach (var command in _commands) command.Do(model); }
    public void Undo(DiagramModel model) { foreach (var command in _commands.Reverse()) command.Undo(model); }
}

public sealed class ArrangeGraphObjectsCommand : IEditCommand
{
    private readonly IEditCommand[] _commands;
    public bool HasChanges => _commands.Length > 0;
    public string Description { get; }

    public ArrangeGraphObjectsCommand(DiagramModel model, IEnumerable<GraphObject> objects, GraphArrangement arrangement)
    {
        if (!Enum.IsDefined(arrangement)) throw new ArgumentException("Unknown arrangement.", nameof(arrangement));
        var items = GraphObjectGeometry.Normalize(model, objects);
        var distribute = arrangement is GraphArrangement.DistributeHorizontally or GraphArrangement.DistributeVertically;
        if (items.Count < (distribute ? 3 : 2)) throw new ArgumentException(distribute
            ? "Select at least three independent objects to distribute." : "Select at least two independent objects to align.");
        Description = arrangement switch
        {
            GraphArrangement.Left => "Align left",
            GraphArrangement.HorizontalCenter => "Align horizontal centers",
            GraphArrangement.Right => "Align right",
            GraphArrangement.Top => "Align top",
            GraphArrangement.VerticalCenter => "Align vertical centers",
            GraphArrangement.Bottom => "Align bottom",
            GraphArrangement.DistributeHorizontally => "Distribute horizontally",
            _ => "Distribute vertically",
        };
        var bounds = items.ToDictionary(item => item, item => GraphObjectGeometry.Bounds(model, item));
        if (bounds.Values.Any(r => !double.IsFinite(r.X) || !double.IsFinite(r.Y) ||
            !double.IsFinite(r.Width) || !double.IsFinite(r.Height) || r.Width <= 0 || r.Height <= 0))
            throw new ArgumentException("Objects need finite coordinates and positive dimensions.");
        var left = bounds.Values.Min(r => r.X); var right = bounds.Values.Max(r => r.X + r.Width);
        var top = bounds.Values.Min(r => r.Y); var bottom = bounds.Values.Max(r => r.Y + r.Height);
        var moves = new List<IEditCommand>();
        if (distribute)
        {
            var horizontal = arrangement == GraphArrangement.DistributeHorizontally;
            var ordered = items.OrderBy(i => horizontal ? bounds[i].X : bounds[i].Y).ThenBy(i => i.Id, StringComparer.Ordinal).ToArray();
            var first = bounds[ordered[0]];
            var last = bounds[ordered[^1]];
            var start = horizontal ? first.X : first.Y;
            var end = horizontal ? last.X + last.Width : last.Y + last.Height;
            var gap = (end - start - ordered.Sum(i => horizontal ? bounds[i].Width : bounds[i].Height)) / (ordered.Length - 1);
            var cursor = start + (horizontal ? first.Width : first.Height) + gap;
            foreach (var item in ordered.Skip(1).SkipLast(1))
            {
                var rect = bounds[item];
                Add(item, horizontal ? cursor - rect.X : 0, horizontal ? 0 : cursor - rect.Y);
                cursor += (horizontal ? rect.Width : rect.Height) + gap;
            }
        }
        else
            foreach (var item in items)
            {
                var r = bounds[item];
                var dx = arrangement switch
                {
                    GraphArrangement.Left => left - r.X,
                    GraphArrangement.HorizontalCenter => (left + right - r.Width) / 2 - r.X,
                    GraphArrangement.Right => right - r.Width - r.X,
                    _ => 0,
                };
                var dy = arrangement switch
                {
                    GraphArrangement.Top => top - r.Y,
                    GraphArrangement.VerticalCenter => (top + bottom - r.Height) / 2 - r.Y,
                    GraphArrangement.Bottom => bottom - r.Height - r.Y,
                    _ => 0,
                };
                Add(item, dx, dy);
            }
        _commands = moves.ToArray();
        void Add(GraphObject item, double dx, double dy)
        {
            if (Math.Abs(dx) <= 1e-9 && Math.Abs(dy) <= 1e-9) return;
            moves.Add(MoveGraphObjectsCommand.Translate(model, item, dx, dy));
        }
    }

    public void Do(DiagramModel model) { foreach (var command in _commands) command.Do(model); }
    public void Undo(DiagramModel model) { foreach (var command in _commands.Reverse()) command.Undo(model); }
}
