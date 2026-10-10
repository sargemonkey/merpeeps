using Merpeeps.Core.Model;

namespace Merpeeps.App;

internal sealed partial class DiagramInspector
{
    private static StencilGlyph StencilPicture(TaskKind task, object? preset) => task switch
    {
        TaskKind.CreateClass => StencilGlyph.Class,
        TaskKind.CreateEntity => StencilGlyph.Entity,
        TaskKind.CreateNote or TaskKind.SequenceNote => StencilGlyph.Note,
        TaskKind.Namespaces or TaskKind.EntityGroups or TaskKind.ArchitectureGroup or TaskKind.UseCaseBoundary => StencilGlyph.Group,
        TaskKind.SequenceParticipant => preset is SequenceParticipantKind.Actor ? StencilGlyph.Actor : StencilGlyph.Participant,
        TaskKind.SequenceMessage => StencilGlyph.Message,
        TaskKind.SequenceBlock => StencilGlyph.Fragment,
        TaskKind.SequenceBox => StencilGlyph.Group,
        TaskKind.PieSlice => StencilGlyph.Slice,
        TaskKind.QuadrantPoint => StencilGlyph.Point,
        TaskKind.XySeries => preset is ChartSeriesKind.Bar ? StencilGlyph.Bar : StencilGlyph.Line,
        TaskKind.RadarAxis => StencilGlyph.Axis,
        TaskKind.RadarCurve => StencilGlyph.Curve,
        TaskKind.OutlineNode => StencilGlyph.Tree,
        TaskKind.TemporalSection or TaskKind.GanttSection => StencilGlyph.Section,
        TaskKind.TimelinePeriod => StencilGlyph.Period,
        TaskKind.TimelineEvent => StencilGlyph.Event,
        TaskKind.JourneyTask or TaskKind.GanttTask => StencilGlyph.Task,
        TaskKind.PacketField => StencilGlyph.Packet,
        TaskKind.KanbanColumn => StencilGlyph.Column,
        TaskKind.KanbanCard => StencilGlyph.Card,
        TaskKind.CreateRequirement => preset is RequirementKind.Element ? StencilGlyph.Block : StencilGlyph.Requirement,
        TaskKind.GitOperation => StencilGlyph.Commit,
        TaskKind.SankeyFlow => StencilGlyph.Flow,
        TaskKind.TreemapNode => StencilGlyph.Treemap,
        TaskKind.BlockItem => preset switch
        {
            BlockItemKind.Space => StencilGlyph.Spacer, BlockItemKind.Composite => StencilGlyph.Group,
            BlockItemKind.Arrow => StencilGlyph.Arrow, _ => StencilGlyph.Block,
        },
        TaskKind.ArchitectureNode => preset is ArchitectureNodeKind.Junction ? StencilGlyph.Junction : StencilGlyph.Server,
        TaskKind.C4Element => preset switch
        {
            C4ElementLevel.Person => StencilGlyph.Person, C4ElementLevel.Container => StencilGlyph.Container,
            C4ElementLevel.Component => StencilGlyph.Component, C4ElementLevel.Boundary => StencilGlyph.Group,
            C4ElementLevel.DeploymentNode => StencilGlyph.Deployment, _ => StencilGlyph.System,
        },
        TaskKind.WardleyComponent => StencilGlyph.MapComponent,
        TaskKind.WardleyAnnotation => StencilGlyph.Annotation,
        TaskKind.IshikawaProblem => StencilGlyph.Problem,
        TaskKind.IshikawaCause => StencilGlyph.Cause,
        TaskKind.CynefinItem => StencilGlyph.DomainItem,
        TaskKind.CynefinTransition => StencilGlyph.Transition,
        TaskKind.VennSet => StencilGlyph.Set,
        TaskKind.VennOverlap => StencilGlyph.Overlap,
        TaskKind.RailroadRule => StencilGlyph.Rule,
        TaskKind.LaneGroup => StencilGlyph.Lane,
        TaskKind.LaneNode => StencilGlyph.Step,
        TaskKind.UseCaseNode => preset switch
        {
            UseCaseNodeKind.Actor => StencilGlyph.Actor, UseCaseNodeKind.Note => StencilGlyph.Note, _ => StencilGlyph.UseCase,
        },
        TaskKind.AgentElement => preset switch
        {
            AgentFlowElementKind.Flow => StencilGlyph.FlowContainer, AgentFlowElementKind.Connector => StencilGlyph.Connector, _ => StencilGlyph.Task,
        },
        TaskKind.EventFrame => StencilGlyph.Timeframe,
        TaskKind.EventData => StencilGlyph.Data,
        TaskKind.ZenParticipant => preset switch
        {
            ZenParticipantKind.Actor => StencilGlyph.Actor, ZenParticipantKind.Database => StencilGlyph.Database, _ => StencilGlyph.Participant,
        },
        TaskKind.ZenStatement => StencilGlyph.Call,
        _ => throw new InvalidOperationException("No picture is defined for " + task + "."),
    };
}
