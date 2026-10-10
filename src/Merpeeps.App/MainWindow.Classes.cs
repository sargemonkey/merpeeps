using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Merpeeps.Core;
using Merpeeps.Core.Model;

namespace Merpeeps.App;

public partial class MainWindow
{
    private DiagramInspector? _classInspector;
    private NewDiagramGallery? _newGallery;
    private IInputElement? _returnFromGallery;

    internal void SyncClassWorkspace()
    {
        var entity = _canvas.Model?.EntityOptions is not null;
        var sequence = _canvas.Model?.Sequence is not null;
        var chart = _canvas.Model?.Chart;
        var structural = _canvas.IsWorkflowDiagram || _canvas.IsOwnershipDiagram || _canvas.IsStrategyDiagram || _canvas.IsSetGrammarDiagram || _canvas.Model?.Requirements is not null || _canvas.Model?.GitDiagram is not null || _canvas.Model?.Sankey is not null || _canvas.Model?.Treemap is not null || _canvas.Model?.Blocks is not null || _canvas.Model?.Architecture is not null || _canvas.Model?.C4 is not null || _canvas.Model?.Gantt is not null;
        var data = _canvas.Model?.Outline is not null || _canvas.Model?.Temporal is not null || _canvas.Model?.Packet is not null || _canvas.Model?.Kanban is not null || structural;
        var enabled = _canvas.Model?.ClassOptions is not null || entity || sequence || chart is not null || data;
        _classInspector?.Refresh();
        Need<Border>("ClassInspectorHost").IsVisible = enabled;
        Need<Border>("MetadataExplanation").IsVisible = false;
        Need<MenuItem>("MenuClassDiagram").IsVisible = enabled;
        Need<MenuItem>("MenuClassAdd").Header = data ? (_canvas.Model?.AgentFlow is not null ? "Add agent-flow _element…" : _canvas.Model?.EventModeling is not null ? "Add _timeframe…" : _canvas.Model?.Zen is not null ? "Add _participant…" : _canvas.Model?.Swimlane is not null ? "Add responsibility _lane…" : _canvas.Model?.UseCase is not null ? "Add actor / use _case…" : _canvas.Model?.Venn is not null ? "Add _set…" : _canvas.Model?.Railroad is not null ? "Add grammar _rule…" : _canvas.Model?.Wardley is not null ? "Add _component…" : _canvas.Model?.Ishikawa is not null ? "Add _cause…" : _canvas.Model?.Cynefin is not null ? "Add _domain item…" : _canvas.Model?.C4 is not null ? "Add C4 _element…" : _canvas.Model?.Gantt is not null ? "Add _task…" : _canvas.Model?.Blocks is not null ? "Add _block…" : _canvas.Model?.Architecture is not null ? "Add _service / junction…" : _canvas.Model?.Sankey is not null ? "Add _flow…" : _canvas.Model?.Treemap is not null ? "Add _root…" : _canvas.Model?.Requirements is not null ? "Add _requirement / element…" : _canvas.Model?.GitDiagram is not null ? "Add _operation…" : _canvas.Model?.Packet is not null ? "Add _field…" : _canvas.Model?.Kanban is not null ? "Add _column…" : _canvas.Model?.Outline is not null ? "Add _root…" : _canvas.Model?.Temporal?.Family == TemporalFamily.Timeline ? "Add _period…" : "Add _task…") : chart is not null ? chart.Family switch
        {
            ChartFamily.Pie => "Add _slice…", ChartFamily.Quadrant => "Add _point…", ChartFamily.Xy => "Add _series…", _ => "Add _axis…",
        } : sequence ? "Add _participant…" : entity ? "Add _entity…" : "Add _class…";
        Need<MenuItem>("MenuClassNote").IsVisible = !entity && chart is null && !data;
        Need<MenuItem>("MenuClassNamespaces").Header = data ? "_Contents…" : chart is not null ? "_Chart data…" : sequence ? "Participant _boxes…" : entity ? "_Subgraphs…" : "_Namespaces…";
        Need<MenuItem>("MenuClassNavigate").Header = data ? "_Contents…" : chart is not null ? "_Chart data…" : sequence ? "_Timeline…" : "_Select element…";
        Need<MenuItem>("MenuDiagramTitle").IsVisible = chart is not null || _canvas.Model?.Temporal is not null;
        Need<MenuItem>("MenuQuadrantLabels").IsVisible = chart is QuadrantChart;
        Need<MenuItem>("MenuArchitectureAlignment").IsVisible = _canvas.Model?.Architecture is not null;
        Need<MenuItem>("MenuGanttReference").IsVisible = _canvas.Model?.Gantt is not null;
        Need<MenuItem>("MenuIshikawaProblem").IsVisible = _canvas.Model?.Ishikawa is not null;
        Need<Separator>("DiagramSettingsSeparator").IsVisible = chart is not null || _canvas.Model?.Temporal is not null
            || _canvas.Model?.Architecture is not null || _canvas.Model?.Gantt is not null || _canvas.Model?.Ishikawa is not null;
        Need<Button>("RelayoutBtn").IsVisible = !sequence && chart is null && !data;
        Need<ComboBox>("EngineBox").IsVisible = !sequence && chart is null && !data;
        var outline = Need<Avalonia.Controls.Primitives.ToggleButton>("OutlineToggle").IsChecked == true;
        Need<ScrollViewer>("FamilyPaletteScroller").IsVisible = enabled && !outline;
        Need<ListBox>("PaletteList").IsVisible = !enabled && !outline;
        Need<TextBlock>("PaletteHint").IsVisible = false;
        UpdateWorkspacePanes();
        if (enabled)
        {
            Need<StackPanel>("NodeProperties").IsVisible = false;
            Need<StackPanel>("EdgeProperties").IsVisible = false;
            Need<TextBlock>("HintText").Text = _canvas.IsWorkflowDiagram || _canvas.IsStrategyDiagram || _canvas.IsSetGrammarDiagram || _canvas.IsOwnershipDiagram
                ? "Pan to inspect this diagram at readable actual size, or use Fit for an overview. Select an item to edit it."
                : _canvas.Model?.Gantt is not null
                ? "Gantt opens at readable actual size. Pan to inspect the timeline, or choose Fit to show the whole schedule. Select a task to edit its dates."
                : data
                ? "Select an item to edit its content, add children/events, or change its order. Diagram menu has contents and display options."
                : chart is not null
                ? "Select chart data to edit it. Drag supported points to change values; Escape cancels a drag. Diagram menu has data and chart options."
                : sequence
                ? "Select a participant to send a message. Select messages or fragments to edit their timeline; no free graph positioning. Diagram menu has timeline and display settings."
                : entity
                ? "Select an entity, subgraph or relationship. Add relationship, then click its target. Escape cancels; Diagram menu has creation and direction settings."
                : "Select a class or relationship to edit it. Add relationship, then click a target. Escape cancels; Diagram menu has creation and display settings.";
            _classInspector?.Refresh();
        }
        else Need<TextBlock>("HintText").Text = CanvasHint;
        foreach (var child in Need<StackPanel>("PropertyStack").Children)
            if (child is TextBlock) child.IsVisible = !enabled;
        SyncArrangement();
    }

    private Task NewDiagram()
    {
        ShowNewGallery();
        return Task.CompletedTask;
    }

    private void ShowNewGallery()
    {
        var host = Need<Border>("NewDiagramHost");
        if (host.IsVisible) return;
        _returnFromGallery = FocusManager?.GetFocusedElement();
        _newGallery ??= new NewDiagramGallery(
            source =>
            {
                LoadSource(source, null);
                _canvas.IgnoreGalleryDoubleClick();
            },
            async () => await PickDiagramAsync(),
            ReturnFromGallery,
            () => _dirty || _classInspector?.HasPendingEdits == true);
        host.Child = _newGallery;
        Need<DockPanel>("EditorWorkspace").IsVisible = false;
        host.IsVisible = true;
        _newGallery.Show(_capabilities is not null);
    }

    private void ReturnFromGallery()
    {
        if (_capabilities is null) return;
        Need<Border>("NewDiagramHost").IsVisible = false;
        Need<DockPanel>("EditorWorkspace").IsVisible = true;
        if (_returnFromGallery is Control { IsEffectivelyVisible: true } control) control.Focus();
        else _canvas.Focus();
        _returnFromGallery = null;
    }

    private void CloseGalleryForDocument()
    {
        Need<Border>("NewDiagramHost").IsVisible = false;
        Need<DockPanel>("EditorWorkspace").IsVisible = true;
        _returnFromGallery = null;
    }

    private bool CanLeaveInspectorDraft()
    {
        if (_classInspector?.HasPendingEdits != true) return true;
        ShowInfo("Unapplied inspector edits",
            "Apply or cancel the inspector edit before opening, saving, exporting or closing a document. Selecting another element retains its draft; clear the selection to resume it.");
        return false;
    }

    private bool HasUnconnectedStateMarker =>
        _canvas.Model is { Kind: "stateDiagram" or "stateDiagram-v2" } model &&
        model.Nodes.Any(n => Merpeeps.Core.Parsing.StateAdapter.IsPseudo(n.Id) &&
            !model.Edges.Any(e => e.From == n.Id || e.To == n.Id));

    private bool CanSaveCurrentDiagram()
    {
        if (!CanLeaveInspectorDraft()) return false;
        if (!HasUnconnectedStateMarker) return true;
        ShowInfo("Connect the state marker",
            "Mermaid represents a start or end marker through a transition. Connect it to a state before saving or exporting; the current draft is unchanged.");
        return false;
    }
}
