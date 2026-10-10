using Avalonia;
using Merpeeps.Core.Editing;
using Merpeeps.Core.Layout;
using Merpeeps.Core.Model;

namespace Merpeeps.App;

public sealed partial class EditorCanvas
{
    private WardleyLayoutResult? _wardleyLayout;
    private CauseDomainLayoutResult? _causeDomainLayout;
    private WardleyDiagramDocument? _wardleyDragDocument;
    private WardleyCoordinates? _wardleyDragCoordinates;
    private string? _wardleyDragId;
    private Point _wardleyDragStart;
    private DiagramPoint _wardleyDragValue;
    private DrawingScene? _wardleyPreview;
    public bool IsStrategyDiagram => _model?.Wardley is not null || _model?.Ishikawa is not null || _model?.Cynefin is not null;
    public Func<bool>? StrategyEditReady { get; set; }
    public WardleyLayoutResult WardleyLayout { get { _ = StructuralScene; return _wardleyLayout ?? throw new InvalidOperationException("Open a Wardley map."); } }
    public IReadOnlyList<DataDiagramDiagnostic> StrategyDiagnostics
    {
        get { _ = StructuralScene; return _wardleyLayout?.Diagnostics ?? _causeDomainLayout?.Diagnostics ?? []; }
    }
    private bool BeginWardleyDrag(Point world)
    {
        if (_model?.Wardley is not { } d || _structuralSelection is not { Role: "WardleyComponent" } target || StrategyEditReady?.Invoke() == false) return false;
        _wardleyDragDocument = d; _wardleyDragCoordinates = WardleyLayout.Coordinates; _wardleyDragId = target.Id;
        _wardleyDragStart = world; var component = d.Components.Single(c => c.Id == target.Id);
        _wardleyDragValue = new(component.Evolution, d.VisibilityOf(component.Id));
        return true;
    }
    private void MoveWardleyDrag(Point world)
    {
        if (_wardleyDragDocument is not { } original || _wardleyDragId is not { } id || _wardleyDragCoordinates is null) return;
        if (!ReferenceEquals(_model?.Wardley, original)) { CancelWardleyDrag(); return; }
        var delta = world - _wardleyDragStart;
        if (Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y) * Zoom < 3) return;
        var copy = original.Clone();
        var index = copy.Components.FindIndex(c => c.Id == id);
        var component = copy.Components[index];
        _wardleyDragValue = _wardleyDragCoordinates.FromPoint(new(world.X, world.Y));
        copy.Components[index] = component with { Evolution = _wardleyDragValue.X, Visibility = component.PipelineParentId is null ? _wardleyDragValue.Y : null };
        _wardleyPreview = StrategyDiagramLayouts.Wardley(new DiagramModel { Kind = "wardley-beta", Wardley = copy, Theme = _model!.Theme }, MeasureDrawingText).Scene;
        InvalidateVisual();
    }
    private void EndWardleyDrag()
    {
        var original = _wardleyDragDocument; var id = _wardleyDragId; var value = _wardleyDragValue; var changed = _wardleyPreview is not null;
        CancelWardleyDrag();
        if (!changed || original is null || id is null || !ReferenceEquals(_model?.Wardley, original) || StrategyEditReady?.Invoke() == false) return;
        try
        {
            ApplyDiagramEdit(WardleyEditing.MoveComponent(id, value.Y, value.X));
            SelectStructural(new("WardleyComponent", id));
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException)
        { StructuralError = error.Message; SelectionChanged?.Invoke(this, EventArgs.Empty); }
    }
    private void CancelWardleyDrag()
    {
        _wardleyDragDocument = null; _wardleyDragId = null; _wardleyDragCoordinates = null; _wardleyPreview = null;
        InvalidateVisual();
    }
}
