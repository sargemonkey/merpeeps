using Avalonia;
using Avalonia.Media;
using Merpeeps.Core.Editing;
using Merpeeps.Core.Layout;

namespace Merpeeps.App;

public sealed record StructuralSelection(string Role, string Id);
public sealed partial class EditorCanvas
{
    private object? _structuralCacheModel;
    private DrawingScene? _structuralScene;
    private string? _structuralTheme;
    private StructuralSelection? _structuralSelection;
    private string? _requirementSource;
    private string? _gridConnectionSource;
    private BlockDiagramLayoutResult? _blockLayout;
    private ArchitectureLayoutResult? _architectureLayout;
    private C4LayoutResult? _c4Layout;
    private GanttLayoutResult? _ganttLayout;
    private VennLayoutResult? _vennLayout;
    private RailroadLayoutResult? _railroadLayout;
    private SwimlaneLayoutResult? _swimlaneLayout;
    private UseCaseLayoutResult? _useCaseLayout;
    private AgentFlowLayoutResult? _agentFlowLayout;
    private EventModelingLayoutResult? _eventLayout;
    private ZenLayoutResult? _zenLayout;
    private Core.Model.GanttEvaluationContext? _ganttContext;
    public StructuralSelection? SelectedStructuralTarget => _structuralSelection;
    public string? RequirementConnectionSource => _requirementSource;
    public string? GridConnectionSource => _gridConnectionSource;
    public BlockDiagramLayoutResult BlockLayout { get { _ = StructuralScene; return _blockLayout ?? throw new InvalidOperationException("Open a Block diagram."); } }
    public ArchitectureLayoutResult ArchitectureLayout { get { _ = StructuralScene; return _architectureLayout ?? throw new InvalidOperationException("Open an Architecture diagram."); } }
    public C4LayoutResult C4Layout { get { _ = StructuralScene; return _c4Layout ?? throw new InvalidOperationException("Open a C4 diagram."); } }
    public GanttLayoutResult GanttLayout { get { _ = StructuralScene; return _ganttLayout ?? throw new InvalidOperationException("Open a Gantt diagram."); } }
    public VennLayoutResult VennLayout { get { _ = StructuralScene; return _vennLayout ?? throw new InvalidOperationException("Open a Venn diagram."); } }
    public RailroadLayoutResult RailroadLayout { get { _ = StructuralScene; return _railroadLayout ?? throw new InvalidOperationException("Open a Railroad diagram."); } }
    public bool IsSetGrammarDiagram => _model?.Venn is not null || _model?.Railroad is not null;
    public bool IsOwnershipDiagram => _model?.Swimlane is not null || _model?.UseCase is not null;
    public bool IsWorkflowDiagram => _model?.AgentFlow is not null || _model?.EventModeling is not null || _model?.Zen is not null;
    public SwimlaneLayoutResult SwimlaneLayout { get { _ = StructuralScene; return _swimlaneLayout ?? throw new InvalidOperationException("Open a Swimlane diagram."); } }
    public UseCaseLayoutResult UseCaseLayout { get { _ = StructuralScene; return _useCaseLayout ?? throw new InvalidOperationException("Open a Use Case diagram."); } }
    public Core.Model.GanttEvaluationContext GanttContext => _ganttContext ?? throw new InvalidOperationException("A Gantt evaluation context is not captured.");
    public static Core.Model.GanttEvaluationContext CaptureGanttContext() => new(DateTimeOffset.Now, TimeZoneInfo.Local,
        System.Globalization.CultureInfo.ReadOnly((System.Globalization.CultureInfo)System.Globalization.CultureInfo.CurrentCulture.Clone()));
    public void SetGanttContext(Core.Model.GanttEvaluationContext context)
    {
        if (_model?.Gantt is { } gantt) _ = GanttScheduleResolver.Resolve(gantt, context);
        _ganttContext = context; _structuralCacheModel = null; InvalidateVisual(); SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
    public event EventHandler? StructuralEditRequested;
    public string? StructuralError { get; private set; }
    public DrawingScene StructuralScene
    {
        get
        {
            var document = (object?)_model?.Requirements ?? (object?)_model?.GitDiagram ?? (object?)_model?.Sankey ?? (object?)_model?.Treemap ??
                (object?)_model?.Blocks ?? (object?)_model?.Architecture ?? (object?)_model?.C4 ?? (object?)_model?.Gantt ??
                (object?)_model?.Wardley ?? (object?)_model?.Ishikawa ?? (object?)_model?.Cynefin ??
                (object?)_model?.Venn ?? (object?)_model?.Railroad ?? (object?)_model?.Swimlane ?? (object?)_model?.UseCase ??
                (object?)_model?.AgentFlow ?? (object?)_model?.EventModeling ?? _model?.Zen ?? throw new InvalidOperationException("Open a structural diagram.");
            if (!ReferenceEquals(document, _structuralCacheModel) || _structuralTheme != _model!.Theme)
            {
                _blockLayout = _model!.Blocks is null ? null : BlockDiagramLayout.Create(_model, MeasureDrawingText);
                _architectureLayout = _model.Architecture is null ? null : ArchitectureDiagramLayout.Create(_model, MeasureDrawingText);
                _c4Layout = _model.C4 is null ? null : C4DiagramLayout.Create(_model, MeasureDrawingText);
                _ganttLayout = _model.Gantt is null ? null : GanttDiagramLayout.Create(_model, GanttContext, MeasureDrawingText);
                _wardleyLayout = _model.Wardley is null ? null : StrategyDiagramLayouts.Wardley(_model, MeasureDrawingText);
                _causeDomainLayout = _model.Ishikawa is not null ? StrategyDiagramLayouts.Ishikawa(_model, MeasureDrawingText) :
                    _model.Cynefin is not null ? StrategyDiagramLayouts.Cynefin(_model, MeasureDrawingText) : null;
                _vennLayout = _model.Venn is null ? null : VennDiagramLayout.Create(_model, MeasureDrawingText);
                _railroadLayout = _model.Railroad is null ? null : RailroadDiagramLayout.Create(_model, MeasureDrawingText);
                _swimlaneLayout = _model.Swimlane is null ? null : SwimlaneDiagramLayout.Create(_model, MeasureDrawingText);
                _useCaseLayout = _model.UseCase is null ? null : UseCaseDiagramLayout.Create(_model, MeasureDrawingText);
                _agentFlowLayout = _model.AgentFlow is null ? null : AgentFlowDiagramLayout.Create(_model, MeasureDrawingText);
                _eventLayout = _model.EventModeling is null ? null : EventModelingLayout.Create(_model, MeasureDrawingText);
                _zenLayout = _model.Zen is null ? null : ZenDiagramLayout.Create(_model, MeasureDrawingText);
                _structuralScene = _blockLayout?.Scene ?? _architectureLayout?.Scene ?? _c4Layout?.Scene ?? _ganttLayout?.Scene ?? _wardleyLayout?.Scene ?? _causeDomainLayout?.Scene ?? _vennLayout?.Scene ?? _railroadLayout?.Scene ?? _swimlaneLayout?.Scene ?? _useCaseLayout?.Scene ?? _agentFlowLayout?.Scene ?? _eventLayout?.Scene ?? _zenLayout?.Scene ?? (_model.Requirements is not null ? RequirementDiagramLayout.Create(_model, MeasureDrawingText)
                    : _model.GitDiagram is not null ? GitDiagramLayout.Create(_model, MeasureDrawingText)
                    : WeightedDiagramLayout.Create(_model, MeasureDrawingText).Scene);
                _structuralCacheModel = document;
                _structuralTheme = _model!.Theme;
            }
            return _structuralScene!;
        }
    }
    public void SelectStructural(StructuralSelection? target)
    {
        StructuralError = null;
        if (_structuralSelection != target) _structuralSelection = target;
        _selection.Clear(); _selectedEdge = null; _selectedGroup = null; _selectedSequence = null; _chartSelection = null; _dataSelection = null;
        if ((IsStrategyDiagram || IsSetGrammarDiagram || IsOwnershipDiagram || IsWorkflowDiagram) && target is not null &&
            StructuralScene.Regions.FirstOrDefault(r => r.Owner.Role == target.Role && r.Owner.Id == target.Id) is { } region)
        {
            var r = region.Bounds;
            var center = new Point((r.X + r.Width / 2) * Zoom + OffsetX, (r.Y + r.Height / 2) * Zoom + OffsetY);
            if (center.X < 0 || center.X > Bounds.Width || center.Y < 0 || center.Y > Bounds.Height)
            {
                OffsetX += Bounds.Width / 2 - center.X;
                OffsetY += Bounds.Height / 2 - center.Y;
            }
        }
        SelectionChanged?.Invoke(this, EventArgs.Empty); InvalidateVisual();
    }
    public void BeginRequirementConnection(string from)
    {
        _requirementSource = from; SelectionChanged?.Invoke(this, EventArgs.Empty); Focus();
    }
    public void CancelRequirementConnection()
    {
        _requirementSource = null; _gridConnectionSource = null; SelectionChanged?.Invoke(this, EventArgs.Empty); InvalidateVisual();
    }
    public void BeginGridConnection(string source)
    {
        _gridConnectionSource = source; SelectionChanged?.Invoke(this, EventArgs.Empty); Focus();
    }
    public void DeleteStructuralSelection()
    {
        if (_structuralSelection is not { } target) return;
        try
        {
            if (_model?.Gantt is not null && target.Role == "GanttSection") { StructuralEditRequested?.Invoke(this, EventArgs.Empty); return; }
            IEditCommand? command = _model?.Wardley is not null ? target.Role switch
                {
                    "WardleyComponent" => WardleyEditing.RemoveComponent(target.Id), "WardleyLink" => WardleyEditing.RemoveLink(target.Id),
                    "WardleyAnnotation" => WardleyEditing.RemoveAnnotation(target.Id), _ => null,
                } : _model?.Ishikawa is not null ? target.Role == "IshikawaCause" ? IshikawaEditing.RemoveCause(target.Id) : null
                : _model?.Cynefin is not null ? target.Role == "CynefinItem" ? CynefinEditing.RemoveItem(target.Id)
                    : target.Role == "CynefinTransition" ? CynefinEditing.RemoveTransition(target.Id) : null
                : _model?.C4 is not null ? target.Role == "C4Relationship" ? C4DiagramEditing.RemoveRelationship(target.Id) : C4DiagramEditing.RemoveElement(target.Id)
                : _model?.Gantt is not null ? GanttDiagramEditing.RemoveTask(target.Id, GanttContext)
                : _model?.Blocks is not null
                ? target.Role == "BlockConnection" ? BlockDiagramEditing.RemoveConnection(target.Id) : BlockDiagramEditing.Remove(target.Id)
                : _model?.Architecture is not null ? target.Role switch
                {
                    "ArchitectureConnection" => ArchitectureDiagramEditing.RemoveConnection(target.Id),
                    "ArchitectureGroup" => ArchitectureDiagramEditing.RemoveGroup(target.Id),
                    _ => ArchitectureDiagramEditing.RemoveNode(target.Id),
                } : _model?.Requirements is not null
                ? target.Role == "Requirement" ? RequirementEditing.Remove(target.Id) : RequirementEditing.RemoveRelation(target.Id)
                : _model?.GitDiagram is not null && _model.GitDiagram.Operations.Any(o => o.Id == target.Id)
                    ? GitDiagramEditing.Remove(target.Id) : null;
            if (command is not null) { ApplyDiagramEdit(command); SelectStructural(null); }
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException)
        {
            StructuralError = error.Message; SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    private void StructuralPointer(Point point)
    {
        var hit = DrawingHitTest.Hit(StructuralScene, new(point.X, point.Y), 5 / Zoom);
        if (_gridConnectionSource is { } source)
        {
            try
            {
                var id = "edge" + Guid.NewGuid().ToString("N");
                if (_model?.Blocks is not null && hit is { Role: "Block" } block)
                {
                    ApplyDiagramEdit(BlockDiagramEditing.Connect(new(id, source, block.Id)));
                    _gridConnectionSource = null; SelectStructural(new("BlockConnection", id));
                }
                else if (_model?.Architecture is not null && hit is { Role: "ArchitectureNode" } node)
                {
                    ApplyDiagramEdit(ArchitectureDiagramEditing.Connect(new(id, new(source, Core.Model.ArchitecturePort.Right), new(node.Id, Core.Model.ArchitecturePort.Left)) { ToArrow = true }));
                    _gridConnectionSource = null; SelectStructural(new("ArchitectureConnection", id));
                }
                else if (_model?.C4 is not null && hit is { Role: "C4Element" } element)
                {
                    ApplyDiagramEdit(C4DiagramEditing.AddRelationship(new(id, source, element.Id, "Uses")));
                    _gridConnectionSource = null; SelectStructural(new("C4Relationship", id));
                }
                else if (_model?.Wardley is not null && hit is { Role: "WardleyComponent" } component)
                {
                    ApplyDiagramEdit(WardleyEditing.AddLink(new(id, source, component.Id, Core.Model.WardleyLinkKind.Dependency)));
                    _gridConnectionSource = null; SelectStructural(new("WardleyLink", id));
                }
                else if (_model?.Swimlane is not null && hit is { Role: "SwimlaneNode" } step)
                {
                    ApplyDiagramEdit(SwimlaneEditing.AddLink(new(id, source, step.Id)));
                    _gridConnectionSource = null; SelectStructural(new("SwimlaneLink", id));
                }
                else if (_model?.UseCase is not null && hit is { Role: "UseCaseNode" } actorOrCase)
                {
                    ApplyDiagramEdit(UseCaseEditing.AddRelationship(new(id, source, actorOrCase.Id)));
                    _gridConnectionSource = null; SelectStructural(new("UseCaseRelationship", id));
                }
                else if (_model?.AgentFlow is not null && hit is { Role: "AgentFlowElement" } agentElement)
                {
                    ApplyDiagramEdit(AgentFlowEditing.AddLink(new(id, source, agentElement.Id)));
                    _gridConnectionSource = null; SelectStructural(new("AgentFlowLink", id));
                }
                else if (hit is null) CancelRequirementConnection();
            }
            catch (Exception error) when (error is ArgumentException or NotSupportedException)
            {
                StructuralError = error.Message; SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
            return;
        }
        if (_requirementSource is { } from)
        {
            if (hit is { Role: "Requirement" } target)
            {
                try
                {
                    var id = "r" + Guid.NewGuid().ToString("N");
                    ApplyDiagramEdit(RequirementEditing.Connect(new(id, from, target.Id, Core.Model.RequirementRelationKind.Traces)));
                    _requirementSource = null;
                    SelectStructural(new("Relationship", id));
                }
                catch (Exception error) when (error is ArgumentException or NotSupportedException)
                {
                    StructuralError = error.Message; SelectionChanged?.Invoke(this, EventArgs.Empty);
                }
            }
            else if (hit is null) CancelRequirementConnection();
            return;
        }
        if (hit is { Role: "GitBranch" } branch)
        {
            var operation = _model?.GitDiagram?.Operations.FirstOrDefault(o => o.Kind == Core.Model.GitDiagramOperationKind.Branch && o.Name == branch.Id);
            SelectStructural(operation is null ? null : new("GitCommit", operation.Id));
        }
        else SelectStructural(hit is { } owner ? new(owner.Role, owner.Id) : null);
    }
    private void DrawStructural(DrawingContext context) => DrawNeutralScene(context, _wardleyPreview ?? StructuralScene,
        _structuralSelection is { } target ? new DrawingOwner(target.Role, target.Id) : null);
    private void ClearStructuralState()
    {
        _structuralCacheModel = null; _structuralSelection = null; _requirementSource = null; _gridConnectionSource = null;
        _blockLayout = null; _architectureLayout = null; _c4Layout = null; _ganttLayout = null; _ganttContext = null;
        _wardleyLayout = null; _causeDomainLayout = null; CancelWardleyDrag();
        _vennLayout = null; _railroadLayout = null;
        _swimlaneLayout = null; _useCaseLayout = null;
        _agentFlowLayout = null; _eventLayout = null; _zenLayout = null;
    }
}
