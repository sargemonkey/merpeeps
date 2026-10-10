using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Merpeeps.Core.Model;

namespace Merpeeps.App;

internal sealed partial class DiagramInspector
{
    public WrapPanel ItemPalette { get; } = new();
    private DiagramModel? _paletteModel;
    private readonly List<(Button Button, Func<bool> Enabled)> _paletteRules = [];

    private void RefreshItemPalette()
    {
        var model = Model();
        if (!ReferenceEquals(_paletteModel, model))
        {
            _paletteModel = model;
            ItemPalette.Children.Clear();
            _paletteRules.Clear();
            BuildItemPalette(model);
        }
        foreach (var (button, enabled) in _paletteRules) button.IsEnabled = enabled();
    }

    private void BuildItemPalette(DiagramModel model)
    {
        switch (model)
        {
            case { ClassOptions: not null }:
                PaletteItem("Class", "Add class", TaskKind.CreateClass);
                PaletteItem("Note", "Add diagram note", TaskKind.CreateNote);
                PaletteItem("Namespace", "Add namespace", TaskKind.Namespaces);
                break;
            case { EntityOptions: not null }:
                PaletteItem("Entity", "Add entity", TaskKind.CreateEntity);
                PaletteItem("Subgraph", "Add subgraph", TaskKind.EntityGroups);
                break;
            case { Sequence: not null }:
                PaletteItem("Participant", "Add participant", TaskKind.SequenceParticipant);
                PaletteItem("Actor", "Add actor", TaskKind.SequenceParticipant, SequenceParticipantKind.Actor);
                PaletteItem("Message", "Add message...", TaskKind.SequenceMessage,
                    enabled: () => Model().Sequence!.Participants.Count > 0, hint: "Add a participant first.");
                PaletteItem("Note", "Add sequence note", TaskKind.SequenceNote,
                    enabled: () => Model().Sequence!.Participants.Count > 0, hint: "Add a participant first.");
                PaletteItem("Fragment", "Add fragment...", TaskKind.SequenceBlock);
                PaletteItem("Participant box", "Add participant box", TaskKind.SequenceBox);
                break;
            case { Chart: PieChart }:
                PaletteItem("Slice", "Add slice", TaskKind.PieSlice);
                break;
            case { Chart: QuadrantChart }:
                PaletteItem("Point", "Add point", TaskKind.QuadrantPoint);
                break;
            case { Chart: XyChart }:
                PaletteItem("Line series", "Add series", TaskKind.XySeries);
                PaletteItem("Bar series", "Add bar series", TaskKind.XySeries, ChartSeriesKind.Bar);
                break;
            case { Chart: RadarChart }:
                PaletteItem("Axis", "Add axis", TaskKind.RadarAxis);
                PaletteItem("Curve", "Add curve", TaskKind.RadarCurve,
                    enabled: () => ((RadarChart)Chart).Axes.Count > 0, hint: "Add an axis first.");
                break;
            case { Outline: not null }:
                PaletteItem(model.Outline.Family == OutlineFamily.Mindmap ? "Topic" : "Entry", "Add root", TaskKind.OutlineNode);
                break;
            case { Temporal: not null }:
                PaletteItem("Section", "Add section", TaskKind.TemporalSection);
                PaletteItem(model.Temporal.Family == TemporalFamily.Timeline ? "Time period" : "Task",
                    model.Temporal.Family == TemporalFamily.Timeline ? "Add time period" : "Add task",
                    model.Temporal.Family == TemporalFamily.Timeline ? TaskKind.TimelinePeriod : TaskKind.JourneyTask,
                    hint: model.Temporal.Family == TemporalFamily.Timeline ? "Add the next period across the timeline. Events belong beneath a period." : null);
                if (model.Temporal.Family == TemporalFamily.Timeline)
                    PaletteItem("Event", "Add timeline event", TaskKind.TimelineEvent,
                        enabled: () => SelectedPeriod() is not null, hint: "Add an event beneath the selected period. Use Time period to move along the timeline.");
                break;
            case { Packet: not null }:
                PaletteItem("Bit field", "Add field", TaskKind.PacketField);
                break;
            case { Kanban: not null }:
                PaletteItem("Column", "Add column", TaskKind.KanbanColumn);
                PaletteItem("Card", "Add board card", TaskKind.KanbanCard,
                    enabled: () => SelectedColumn() is not null, hint: "Select a column, or add one first.");
                break;
            case { Requirements: not null }:
                PaletteItem("Requirement", "Add requirement or element", TaskKind.CreateRequirement);
                PaletteItem("Element", "Add requirement element", TaskKind.CreateRequirement, RequirementKind.Element);
                break;
            case { GitDiagram: not null }:
                PaletteItem("Commit", "Add operation", TaskKind.GitOperation);
                break;
            case { Sankey: not null }:
                PaletteItem("Weighted flow", "Add weighted flow", TaskKind.SankeyFlow);
                break;
            case { Treemap: not null }:
                PaletteItem("Root", "Add root", TaskKind.TreemapNode);
                break;
            case { Blocks: not null }:
                PaletteItem("Block", "Add block", TaskKind.BlockItem);
                PaletteItem("Composite", "Add composite block", TaskKind.BlockItem, BlockItemKind.Composite);
                PaletteItem("Spacer", "Add spacer", TaskKind.BlockItem, BlockItemKind.Space);
                PaletteItem("Arrow", "Add arrow block", TaskKind.BlockItem, BlockItemKind.Arrow);
                break;
            case { Architecture: not null }:
                PaletteItem("Service", "Add service", TaskKind.ArchitectureNode);
                PaletteItem("Junction", "Add junction", TaskKind.ArchitectureNode, ArchitectureNodeKind.Junction);
                PaletteItem("Group", "Add group", TaskKind.ArchitectureGroup);
                break;
            case { C4: not null }:
                foreach (var level in Enum.GetValues<C4ElementLevel>())
                {
                    var label = level == C4ElementLevel.DeploymentNode ? "Deployment node" : level.ToString();
                    PaletteItem(label, "Add C4 " + label.ToLowerInvariant(), TaskKind.C4Element, level);
                }
                break;
            case { Gantt: not null }:
                PaletteItem("Task", "Add task", TaskKind.GanttTask);
                PaletteItem("Section", "Add section", TaskKind.GanttSection);
                break;
            case { Wardley: not null }:
                PaletteItem("Component", "Add component", TaskKind.WardleyComponent);
                PaletteItem("Annotation", "Add map annotation", TaskKind.WardleyAnnotation);
                break;
            case { Ishikawa: not null }:
                PaletteItem("Problem", "Set problem", TaskKind.IshikawaProblem,
                    enabled: () => Model().Ishikawa!.Problem.Length == 0, hint: "Select the existing problem to edit it.");
                PaletteItem("Cause", "Add cause", TaskKind.IshikawaCause,
                    enabled: () => Model().Ishikawa!.Problem.Length > 0, hint: "Set the problem first.");
                break;
            case { Cynefin: not null }:
                PaletteItem("Domain item", "Add domain item", TaskKind.CynefinItem);
                PaletteItem("Domain transition", "Add domain transition", TaskKind.CynefinTransition);
                break;
            case { Venn: not null }:
                PaletteItem("Set", "Add set", TaskKind.VennSet);
                PaletteItem("Overlap", "Create overlap", TaskKind.VennOverlap,
                    enabled: () => UnusedVennPair() is not null, hint: "Add two sets without an existing overlap first.");
                break;
            case { Railroad: not null }:
                PaletteItem("Grammar rule", "Add grammar rule", TaskKind.RailroadRule);
                break;
            case { Swimlane: not null }:
                PaletteItem("Lane", "Add lane", TaskKind.LaneGroup);
                PaletteItem("Step", "Add step", TaskKind.LaneNode);
                break;
            case { UseCase: not null }:
                PaletteItem("Use case", "Add use-case element", TaskKind.UseCaseNode);
                PaletteItem("Actor", "Add actor", TaskKind.UseCaseNode, UseCaseNodeKind.Actor);
                PaletteItem("Note", "Add use-case note", TaskKind.UseCaseNode, UseCaseNodeKind.Note,
                    enabled: () => Model().UseCase!.Nodes.Any(n => n.Kind is UseCaseNodeKind.Actor or UseCaseNodeKind.UseCase),
                    hint: "Add an actor or use case first. The note attaches to the selected element.");
                PaletteItem("System boundary", "Add system boundary", TaskKind.UseCaseBoundary);
                break;
            case { AgentFlow: not null }:
                PaletteItem("Task", "Add agent-flow element", TaskKind.AgentElement);
                PaletteItem("Flow container", "Add flow container", TaskKind.AgentElement, AgentFlowElementKind.Flow);
                PaletteItem("Connector", "Add workflow connector", TaskKind.AgentElement, AgentFlowElementKind.Connector);
                break;
            case { EventModeling: not null }:
                PaletteItem("Timeframe", "Add timeframe", TaskKind.EventFrame);
                PaletteItem("Data example", "Add data example", TaskKind.EventData);
                break;
            case { Zen: not null }:
                PaletteItem("Participant", "Add participant", TaskKind.ZenParticipant);
                PaletteItem("Actor", "Add actor", TaskKind.ZenParticipant, ZenParticipantKind.Actor);
                PaletteItem("Database", "Add database", TaskKind.ZenParticipant, ZenParticipantKind.Database);
                PaletteItem("Call", "Add statement", TaskKind.ZenStatement,
                    enabled: () => Model().Zen!.Participants.Count > 0, hint: "Add a participant first.");
                break;
            default:
                throw new InvalidOperationException("No item palette is registered for " + model.Kind + ".");
        }
    }

    private void PaletteItem(string label, string name, TaskKind task, object? value = null,
        Func<bool>? enabled = null, string? hint = null) =>
        AddStencil(label, name, task, value, () =>
        {
            if (_active is { Inline: true, Dirty: true } draft && !TryCommitInline(draft, restoreFocus: false)) return;
            if (HasPendingEdits) throw new InvalidOperationException("Apply or cancel the pending property edits before adding an element.");
            CreateStencil(task, value);
        }, enabled, hint);

    private void AddStencil(string label, string name, TaskKind task, object? preset, Action action, Func<bool>? enabled, string? hint)
    {
        var button = new Button
        {
            Content = new DiagramStencilPreview(StencilPicture(task, preset)) { Width = 46, Height = 34 },
            Width = 60, Height = 54, Margin = new Thickness(0, 0, 0, 8),
            HorizontalContentAlignment = HorizontalAlignment.Center, Padding = new Thickness(6),
        };
        button.Classes.Add("ghost");
        UiIcon.Describe(button, name);
        ToolTip.SetTip(button, label + "\n" + (hint ?? "Click to add with defaults."));
        AutomationProperties.SetHelpText(button, hint ?? "Creates an element immediately. Undo removes it.");
        button.Click += (_, _) => Perform(action);
        ItemPalette.Children.Add(button);
        _paletteRules.Add((button, enabled ?? (() => true)));
    }
}
