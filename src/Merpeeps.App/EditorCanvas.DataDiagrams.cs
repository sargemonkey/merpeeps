using Avalonia;
using Avalonia.Media;
using Merpeeps.Core.Editing;
using Merpeeps.Core.Layout;
using Merpeeps.Core.Model;

namespace Merpeeps.App;

public sealed partial class EditorCanvas
{
    private object? _dataCacheModel;
    private string? _dataCacheTheme;
    private DataDiagramLayout? _dataLayout;
    private DataDiagramTarget? _dataSelection;
    private Point? _outlineDragStart;
    private DiagramRect _outlineDragBounds;
    private Vector _outlineDragDelta;
    public DataDiagramTarget? SelectedDataTarget => _dataSelection;
    public event EventHandler? DataEditRequested;
    public DataDiagramLayout DataScene
    {
        get
        {
            var data = (object?)_model?.Outline ?? (object?)_model?.Temporal ?? (object?)_model?.Packet ?? _model?.Kanban ?? throw new InvalidOperationException("Open a structured diagram.");
            if (!ReferenceEquals(data, _dataCacheModel) || _dataCacheTheme != _model!.Theme)
            {
                _dataLayout = _model!.Packet is not null || _model.Kanban is not null
                    ? new(BoardPacketLayout.Create(_model, MeasureDrawingText), [], default, new Dictionary<string, DiagramRect>())
                    : DataDiagramLayouts.Create(_model, MeasureDrawingText);
                _dataCacheModel = data; _dataCacheTheme = _model!.Theme;
            }
            return _dataLayout!;
        }
    }
    public void SelectData(DataDiagramTarget? target)
    {
        if (_dataSelection != target) _dataSelection = target;
        _selection.Clear(); _selectedEdge = null; _selectedSequence = null; _selectedGroup = null; _chartSelection = null;
        SelectionChanged?.Invoke(this, EventArgs.Empty); InvalidateVisual();
    }
    private void ClearDataState()
    {
        _dataCacheModel = null; _dataSelection = null; _outlineDragStart = null;
    }
    private bool DataPointer(Point point)
    {
        var hit = DrawingHitTest.Hit(DataScene.Scene, new(point.X, point.Y), 5 / Zoom);
        var target = hit is { } owner && Enum.TryParse<DataDiagramRole>(owner.Role, out var role) ? new DataDiagramTarget(role, owner.Id, owner.DetailId) : null;
        SelectData(target);
        if (target is { Role: DataDiagramRole.Node } && _model!.Outline is { Family: OutlineFamily.Mindmap } && ChartEditReady?.Invoke() != false)
        {
            _outlineDragStart = point;
            _outlineDragBounds = DataScene.NodeBounds[target.Id];
            _outlineDragDelta = default;
            return true;
        }
        return false;
    }
    private void DrawDataDiagram(DrawingContext context)
    {
        DrawNeutralScene(context, DataScene.Scene, _dataSelection is { } target ? new DrawingOwner(target.Role.ToString(), target.Id, target.ParentId) : null);
        if (_outlineDragStart is not null)
        {
            var r = _outlineDragBounds;
            var offset = DataScene.OriginOffset;
            context.DrawRectangle(null, _theme.NodeStrokeSelected, new Rect(r.X + offset.X + _outlineDragDelta.X, r.Y + offset.Y + _outlineDragDelta.Y, r.Width, r.Height));
        }
    }
    private void EndOutlineDrag(bool cancel)
    {
        if (_outlineDragStart is null) return;
        _outlineDragStart = null;
        if (!cancel && _dataSelection is { } selection && _outlineDragDelta.Length > .1)
            ApplyDiagramEdit(OutlineEditing.SetPosition(selection.Id, new(_outlineDragBounds.X + _outlineDragDelta.X, _outlineDragBounds.Y + _outlineDragDelta.Y)));
        _dataCacheModel = null;
        InvalidateVisual();
    }
    public void DeleteDataSelection()
    {
        if (_dataSelection is not { } target) return;
        IEditCommand? command = _model?.Packet is not null ? PacketEditing.Remove(target.Id)
            : _model?.Kanban is not null ? target.Role == DataDiagramRole.Section ? KanbanEditing.RemoveColumn(target.Id)
                : KanbanEditing.RemoveCard(target.ParentId!, target.Id)
            : target.Role switch
        {
            DataDiagramRole.Node => OutlineEditing.Remove(target.Id),
            DataDiagramRole.Section => TemporalEditing.RemoveSection(target.Id),
            DataDiagramRole.Period => TemporalEditing.RemovePeriod(target.Id),
            DataDiagramRole.Event => TemporalEditing.RemoveEvent(target.ParentId!, target.Id),
            DataDiagramRole.Task => TemporalEditing.RemoveTask(target.Id),
            _ => null,
        };
        if (command is not null) { ApplyDiagramEdit(command); SelectData(null); }
    }
    private bool DataTargetExists(DataDiagramTarget target)
    {
        if (target.Role == DataDiagramRole.Diagram) return true;
        if (_model?.Packet is { } packet) return packet.Fields.Any(f => f.Id == target.Id);
        if (_model?.Kanban is { } board) return board.Columns.Any(c => c.Id == target.Id || c.Cards.Any(t => t.Id == target.Id));
        if (_model?.Outline is { } outline) return outline.Nodes.Any(n => n.Id == target.Id);
        if (_model?.Temporal is not { } temporal) return false;
        return temporal.Sections.Any(s => s.Id == target.Id) || temporal.Periods.Any(p => p.Id == target.Id || p.Events.Any(e => e.Id == target.Id)) || temporal.Tasks.Any(t => t.Id == target.Id);
    }
}
