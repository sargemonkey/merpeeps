using Avalonia;
using Avalonia.Input;
using Merpeeps.Core.Editing;
using Merpeeps.Core.Model;

namespace Merpeeps.App;

public sealed partial class EditorCanvas
{
    private readonly HashSet<DiagramSubgraph> _groupSelection = new();
    private DiagramSubgraph? _selectedGroup
    {
        get => _selection.Count == 0 && _groupSelection.Count == 1 ? _groupSelection.First() : null;
        set { _groupSelection.Clear(); if (value is not null) _groupSelection.Add(value); }
    }
    private DiagramSubgraph? _draggingGroup;
    private Point _dragAnchor;
    private IPointer? _dragPointer;
    private IEditCommand? _dragPreview;
    private GraphObject[] _dragObjects = [];
    public DiagramSubgraph? SelectedGroup => _selectedGroup;
    public IReadOnlyCollection<DiagramSubgraph> SelectedGroups => _groupSelection;
    public int SelectedObjectCount => _selection.Count + _groupSelection.Count;
    public bool SupportsArrangement => _model is not null && GraphObjectGeometry.SupportsArrangement(_model);
    internal void CancelGraphDrag() => EndGraphDrag(cancel: true);

    public void SelectGroup(DiagramSubgraph? group)
    {
        if (group is not null && (_model is null || !_model.Subgraphs.Contains(group)))
            throw new ArgumentException("Choose a group in the current diagram.", nameof(group));
        EndGraphDrag(cancel: true);
        CancelEntityConnection();
        ResetClassConnection();
        _selection.Clear(); _selectedEdge = null; _selectedGroup = group;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    public void SelectGraphObjects(IEnumerable<GraphObject> objects)
    {
        if (_model is null) throw new InvalidOperationException("Open a diagram first.");
        var normalized = GraphObjectGeometry.Normalize(_model, objects);
        EndGraphDrag(cancel: true);
        CancelEntityConnection(); ResetClassConnection();
        _selection.Clear(); _groupSelection.Clear(); _selectedEdge = null;
        foreach (var item in normalized)
            if (item.Kind == GraphObjectKind.Group) _groupSelection.Add(_model.Subgraphs.Single(g => g.Id == item.Id));
            else _selection.Add(_model.Node(item.Id)!);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    private IEnumerable<GraphObject> GraphSelection =>
        _selection.Select(n => new GraphObject(GraphObjectKind.Node, n.Id))
            .Concat(_groupSelection.Select(g => new GraphObject(GraphObjectKind.Group, g.Id)));

    private void NormalizeGraphSelection()
    {
        if (_model is null) return;
        var normalized = GraphObjectGeometry.Normalize(_model, GraphSelection);
        _selection.RemoveWhere(n => !normalized.Contains(new(GraphObjectKind.Node, n.Id)));
        _groupSelection.RemoveWhere(g => !normalized.Contains(new(GraphObjectKind.Group, g.Id)));
    }

    public bool ArrangeSelection(GraphArrangement arrangement)
    {
        if (_model is null || _history is null) throw new InvalidOperationException("Open a diagram first.");
        EndGraphDrag(cancel: true);
        var command = new ArrangeGraphObjectsCommand(_model, GraphSelection, arrangement);
        if (!command.HasChanges) return false;
        _history.Apply(command);
        ModelChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
        return true;
    }

    private DiagramSubgraph? HitGroup(Point point)
    {
        if (_model is null) return null;
        return _model.Subgraphs.Where(g => GraphGroupGeometry.IsVisible(_model, g))
            .Select(g => (Group: g, Bounds: GraphGroupGeometry.Bounds(_model, g)))
            .Where(g =>
            {
                var rect = new Rect(g.Bounds.X, g.Bounds.Y, g.Bounds.Width, g.Bounds.Height);
                return rect.Contains(point) && (point.Y < rect.Y + 32 || !rect.Deflate(10).Contains(point) ||
                    GraphGroupGeometry.IsEmpty(_model, g.Group));
            }).OrderBy(g => g.Bounds.Width * g.Bounds.Height).Select(g => g.Group).FirstOrDefault();
    }

    private void BeginGraphDrag(Point world, IPointer pointer, DiagramNode? node = null, DiagramSubgraph? group = null)
    {
        if (_model is null || node is null && group is null || SelectedObjectCount == 0) return;
        if (node is not null && !_selection.Contains(node) || group is not null && !_groupSelection.Contains(group)) return;
        _dragging = node; _draggingGroup = group;
        var bounds = group is null ? new DiagramRect(node!.X, node.Y, node.Width, node.Height) : GraphGroupGeometry.Bounds(_model, group);
        _dragAnchor = new(bounds.X, bounds.Y);
        _dragOffset = world - _dragAnchor;
        _dragObjects = GraphSelection.ToArray();
        _dragPointer = pointer;
        pointer.Capture(this);
    }

    private void MoveGraphDrag(Point world, KeyModifiers modifiers)
    {
        if (_model is null) return;
        _dragPreview?.Undo(_model);
        _dragPreview = null;
        var target = world - _dragOffset;
        if (!modifiers.HasFlag(KeyModifiers.Alt))
            target = new(Math.Round(target.X / 10) * 10, Math.Round(target.Y / 10) * 10);
        var delta = target - _dragAnchor;
        if (Math.Abs(delta.X) <= .01 && Math.Abs(delta.Y) <= .01) { InvalidateVisual(); return; }
        _dragPreview = new MoveGraphObjectsCommand(_model, _dragObjects, delta.X, delta.Y);
        _dragPreview.Do(_model);
        InvalidateVisual();
    }

    private void EndGraphDrag(bool cancel)
    {
        var preview = _dragPreview;
        var pointer = _dragPointer;
        _dragPreview = null; _dragPointer = null;
        _dragging = null; _draggingGroup = null; _dragObjects = [];
        if (preview is not null && _model is not null)
        {
            if (cancel) preview.Undo(_model);
            else _history?.Record(preview);
        }
        pointer?.Capture(null);
        if (preview is not null && !cancel) ModelChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (ReferenceEquals(e.Pointer, _dragPointer)) EndGraphDrag(cancel: true);
    }
}
