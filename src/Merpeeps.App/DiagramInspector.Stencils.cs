using System.Globalization;
using System.Text.Json.Nodes;
using Merpeeps.Core.Editing;
using Merpeeps.Core.Layout;
using Merpeeps.Core.Model;
using Merpeeps.Core.Parsing;

namespace Merpeeps.App;

internal sealed partial class DiagramInspector
{
    public void EditDiagramTitle() => Open(new(Model().Chart is not null ? TaskKind.ChartTitle : TaskKind.TemporalTitle, Model()));
    public void EditQuadrantLabels() => Open(new(TaskKind.QuadrantLabels, Model()));
    public void EditArchitectureAlignment() => Open(new(TaskKind.ArchitectureAlignment, Model()));

    private static string NextName(string prefix, IEnumerable<string?> existing)
    {
        var used = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var index = 1; ; index++)
        {
            var name = prefix + index.ToString(CultureInfo.InvariantCulture);
            if (!used.Contains(name)) return name;
        }
    }
    private static string StencilId() => Guid.NewGuid().ToString("N");
    private string? SelectedPeriod() =>
        _canvas.SelectedDataTarget is { Role: DataDiagramRole.Period } period ? period.Id
        : _canvas.SelectedDataTarget is { Role: DataDiagramRole.Event, ParentId: { } parent } ? parent
        : Model().Temporal?.Periods is { Count: 1 } periods ? periods[0].Id : null;
    private string? SelectedColumn() =>
        _canvas.SelectedDataTarget is { Role: DataDiagramRole.Section } column ? column.Id
        : _canvas.SelectedDataTarget is { Role: DataDiagramRole.Node, ParentId: { } parent } ? parent
        : Model().Kanban?.Columns is { Count: 1 } columns ? columns[0].Id : null;
    private string[]? UnusedVennPair()
    {
        var document = Model().Venn!;
        for (var i = 0; i < document.Sets.Count; i++)
            for (var j = i + 1; j < document.Sets.Count; j++)
            {
                var pair = new[] { document.Sets[i].Id, document.Sets[j].Id };
                if (!document.Overlaps.Any(o => o.SetIds.Count == 2 && pair.All(o.SetIds.Contains))) return pair;
            }
        return null;
    }

    private void CreateStencil(TaskKind task, object? preset)
    {
        var model = Model();
        var id = StencilId();
        switch (task)
        {
            case TaskKind.CreateClass:
            case TaskKind.CreateNote:
            case TaskKind.CreateEntity:
                var name = NextName(task == TaskKind.CreateClass ? "Class" : task == TaskKind.CreateEntity ? "Entity" : "Note",
                    model.Nodes.Select(n => n.Id).Concat(model.Subgraphs.Select(g => g.Id)));
                var node = task == TaskKind.CreateClass ? ClassEditing.CreateClass(name)
                    : task == TaskKind.CreateEntity ? EntityEditing.CreateEntity(name) : ClassEditing.CreateNote(name, "Note");
                Place(node);
                Apply(task == TaskKind.CreateEntity ? new AddEntityCommand(node) : new AddClassElementCommand(node), model, node.Id);
                break;
            case TaskKind.Namespaces:
            case TaskKind.EntityGroups:
                var groupName = NextName(task == TaskKind.Namespaces ? "Namespace" : "Group",
                    model.Subgraphs.Select(g => g.Id).Concat(model.Nodes.Select(n => n.Id)));
                var group = new DiagramSubgraph { Id = groupName, Title = groupName };
                Apply(task == TaskKind.Namespaces ? new AddClassNamespaceCommand(group) : new AddEntityGroupCommand(group), model,
                    after: task == TaskKind.EntityGroups ? () => _canvas.SelectEntityGroup(group) : null);
                if (task == TaskKind.Namespaces) Open(new(TaskKind.Namespaces, group), false);
                break;
            case TaskKind.SequenceParticipant:
                var participantKind = preset is SequenceParticipantKind pk ? pk : SequenceParticipantKind.Participant;
                ApplySequence(SequenceEditing.AddParticipant(new(NextName(participantKind.ToString(), Sequence.Participants.Select(p => p.Id)),
                    participantKind)), model);
                break;
            case TaskKind.SequenceMessage:
                var from = Sequence.Participants.FirstOrDefault(p => p.Id == _canvas.SelectedSequenceElement?.Id) ?? Sequence.Participants[0];
                var to = Sequence.Participants.FirstOrDefault(p => p.Id != from.Id) ?? from;
                ApplySequence(SequenceEditing.Insert(new SequenceMessage(id, from.Id, to.Id, "Message")), model);
                break;
            case TaskKind.SequenceNote:
                var participant = Sequence.Participants.FirstOrDefault(p => p.Id == _canvas.SelectedSequenceElement?.Id) ?? Sequence.Participants[0];
                ApplySequence(SequenceEditing.Insert(new SequenceNote(id, SequenceNotePlacement.Over, participant.Id, null, "Note")), model);
                break;
            case TaskKind.SequenceBlock:
                ApplySequence(SequenceEditing.Insert(new SequenceBlock(id, SequenceBlockKind.Loop, "Repeat", [new(StencilId(), "", [])])), model);
                break;
            case TaskKind.SequenceBox:
                ApplySequence(SequenceEditing.AddBox(new(id, NextName("Group", Sequence.Boxes.Select(b => b.Title)), "transparent")), model);
                break;
            case TaskKind.PieSlice:
                ApplyChart(ChartEditing.AddSlice(new(id, NextName("Slice", ((PieChart)Chart).Slices.Select(s => s.Label)), 10)), model);
                break;
            case TaskKind.QuadrantPoint:
                var quadrant = (QuadrantChart)Chart;
                ApplyChart(ChartEditing.AddQuadrantPoint(new(id, NextName("Point", quadrant.Points.Select(p => p.Label)),
                    .2 + quadrant.Points.Count % 4 * .2, .2 + quadrant.Points.Count / 4 % 4 * .2)), model);
                break;
            case TaskKind.XySeries:
                var xy = (XyChart)Chart;
                var count = xy.XAxis.Kind == ChartAxisKind.Categories ? xy.XAxis.Categories.Count : 3;
                var kind = preset is ChartSeriesKind sk ? sk : ChartSeriesKind.Line;
                var low = xy.YAxis.Minimum ?? 0; var high = xy.YAxis.Maximum ?? 10;
                var values = Enumerable.Range(0, count).Select(i => new ChartValue(StencilId(), low + (high - low) * (i % 3 + 1) / 4)).ToArray();
                ApplyChart(ChartEditing.AddSeries(new(id, kind, NextName("Series", xy.Series.Select(s => s.Name)), values)), model);
                break;
            case TaskKind.RadarAxis:
                var radar = (RadarChart)Chart;
                ApplyChart(ChartEditing.AddRadarAxis(new(NextName("Axis", radar.Axes.Select(a => a.Id))),
                    radar.Curves.ToDictionary(c => c.Id, _ => RadarDefault(radar))), model);
                break;
            case TaskKind.RadarCurve:
                var chart = (RadarChart)Chart;
                ApplyChart(ChartEditing.AddRadarCurve(new(NextName("Curve", chart.Curves.Select(c => c.Id)), null,
                    chart.Axes.Select(a => new RadarValue(a.Id, RadarDefault(chart))).ToArray())), model);
                break;
            case TaskKind.OutlineNode:
                var parent = _canvas.SelectedDataTarget is { Role: DataDiagramRole.Node } selected ? selected.Id
                    : Outline.Family == OutlineFamily.Mindmap ? Outline.Roots.FirstOrDefault()?.Id : null;
                ApplyData(OutlineEditing.Add(new(id, NextName(Outline.Family == OutlineFamily.Mindmap ? "Topic" : "Entry",
                    Outline.Nodes.Select(n => n.Label)), []), parent), model);
                break;
            case TaskKind.TemporalSection:
                ApplyData(TemporalEditing.AddSection(new(id, NextName("Section", Temporal.Sections.Select(s => s.Label)))), model);
                break;
            case TaskKind.TimelinePeriod:
                ApplyData(TemporalEditing.AddPeriod(new(id, NextName("Period", Temporal.Periods.Select(p => p.Label)),
                    _canvas.SelectedDataTarget is { Role: DataDiagramRole.Section } section ? section.Id : null, [])), model);
                break;
            case TaskKind.TimelineEvent:
                ApplyData(TemporalEditing.AddEvent(SelectedPeriod() ?? throw new InvalidOperationException("Select a time period."),
                    new(id, "Event")), model);
                break;
            case TaskKind.JourneyTask:
                ApplyData(TemporalEditing.AddTask(new(id, NextName("Task", Temporal.Tasks.Select(t => t.Label)),
                    _canvas.SelectedDataTarget is { Role: DataDiagramRole.Section } sectionTarget ? sectionTarget.Id : null, 3, [])), model);
                break;
            case TaskKind.PacketField:
                ApplyBoardPacket(PacketEditing.Add(new(id, NextName("Field", model.Packet!.Fields.Select(f => f.Label)), 8)), model);
                break;
            case TaskKind.KanbanColumn:
                ApplyBoardPacket(KanbanEditing.AddColumn(new(id, NextName("Column", model.Kanban!.Columns.Select(c => c.Label)), [])), model);
                break;
            case TaskKind.KanbanCard:
                ApplyBoardPacket(KanbanEditing.AddCard(SelectedColumn() ?? throw new InvalidOperationException("Select a column."), new(id, "Task")), model);
                break;
            case TaskKind.CreateRequirement:
                var requirementKind = preset is RequirementKind rk ? rk : RequirementKind.Requirement;
                ApplyRequirement(RequirementEditing.Add(new(id, NextName(requirementKind.ToString(), model.Requirements!.Items.Select(i => i.Name)), requirementKind)), model);
                break;
            case TaskKind.GitOperation:
                ApplyGit(GitDiagramEditing.Insert(new(id, GitDiagramOperationKind.Commit)), model);
                break;
            case TaskKind.SankeyFlow:
                var names = model.Sankey!.Nodes.Select(n => n.Label).ToArray();
                ApplyWeighted(SankeyEditing.AddFlow(NextName("Source", names), NextName("Target", names), 10), model);
                break;
            case TaskKind.TreemapNode:
                ApplyWeighted(TreemapEditing.Add(new(id, NextName("Item", model.Treemap!.Nodes.Select(n => n.Label)), 10, [])), model);
                break;
            case TaskKind.BlockItem:
                var blockKind = preset is BlockItemKind bk ? bk : BlockItemKind.Shape;
                var blockName = NextName("Block", model.Blocks!.AllItems.Select(i => i.SourceId));
                ApplyBlock(BlockDiagramEditing.Add(new(id, blockKind, [])
                {
                    SourceId = blockKind == BlockItemKind.Space ? null : blockName,
                    Label = blockKind == BlockItemKind.Space ? null : blockName,
                    Arrows = blockKind == BlockItemKind.Arrow ? BlockArrowDirections.Right : BlockArrowDirections.None,
                }), model);
                break;
            case TaskKind.ArchitectureNode:
                var nodeKind = preset is ArchitectureNodeKind ak ? ak : ArchitectureNodeKind.Service;
                var architecture = model.Architecture!;
                var nodeName = NextName(nodeKind.ToString(), architecture.Nodes.Select(n => n.SourceId).Concat(architecture.Groups.Select(g => g.SourceId)));
                ApplyArchitecture(ArchitectureDiagramEditing.AddNode(new(id, nodeName, nodeKind)
                {
                    Label = nodeKind == ArchitectureNodeKind.Service ? nodeName : null,
                    Icon = nodeKind == ArchitectureNodeKind.Service ? "server" : null,
                    GroupId = _canvas.SelectedStructuralTarget is { Role: "ArchitectureGroup" } ag ? ag.Id : null,
                }), model);
                break;
            case TaskKind.ArchitectureGroup:
                var architectureName = NextName("Group", model.Architecture!.Groups.Select(g => g.SourceId).Concat(model.Architecture.Nodes.Select(n => n.SourceId)));
                ApplyArchitecture(ArchitectureDiagramEditing.AddGroup(new(id, architectureName) { Label = architectureName }), model);
                break;
            case TaskKind.C4Element:
                var level = preset is C4ElementLevel cl ? cl : C4ElementLevel.System;
                var c4Name = NextName(level.ToString(), model.C4!.Elements.Select(e => e.Alias));
                ApplyC4(C4DiagramEditing.AddElement(new(id, c4Name, c4Name, level)), model);
                break;
            case TaskKind.GanttTask:
                var gantt = model.Gantt!;
                var previous = gantt.Tasks.LastOrDefault();
                GanttStartSpec start = previous?.SourceId is { Length: > 0 } ? new GanttStartSpec.After([previous.Id])
                    : new GanttStartSpec.Date(GanttDateCodec.Format(previous is null ? _canvas.GanttContext.ReferenceInstant
                        : GanttScheduleResolver.Resolve(gantt, _canvas.GanttContext).Tasks.Last(t => t.Id == previous.Id).End,
                        gantt.DateFormat, _canvas.GanttContext, start: true));
                var taskName = NextName("Task", gantt.Tasks.Select(t => t.SourceId));
                ApplyGantt(GanttDiagramEditing.AddTask(new(id, taskName, start, new GanttEndSpec.Duration(1, GanttDurationUnit.Day))
                { SourceId = taskName, SectionId = _canvas.SelectedStructuralTarget is { Role: "GanttSection" } gs ? gs.Id : null }, _canvas.GanttContext), model);
                break;
            case TaskKind.GanttSection:
                ApplyGantt(GanttDiagramEditing.AddSection(new(id, NextName("Section", model.Gantt!.Sections.Select(s => s.Label))), _canvas.GanttContext), model);
                break;
            case TaskKind.WardleyComponent:
                var map = model.Wardley!;
                ApplyStrategy(WardleyEditing.AddComponent(new(id, NextName("Component", map.Components.Select(c => c.Name)),
                    .2 + map.Components.Count % 4 * .2) { Visibility = .8 - map.Components.Count / 4 % 4 * .2 }), model);
                break;
            case TaskKind.WardleyAnnotation:
                ApplyStrategy(WardleyEditing.SetAnnotation(new(id, WardleyAnnotationKind.Note, "Note", .5, .5)), model);
                break;
            case TaskKind.IshikawaProblem:
                Apply(IshikawaEditing.SetProblem("Problem"), model, after: () => _canvas.SelectStructural(new("IshikawaProblem", "problem")));
                break;
            case TaskKind.IshikawaCause:
                ApplyStrategy(IshikawaEditing.AddCause(new(id, NextName("Cause", model.Ishikawa!.AllCauses.Select(c => c.Label)), [])), model);
                break;
            case TaskKind.CynefinItem:
                var domain = _canvas.SelectedStructuralTarget is { Role: "CynefinDomain" } cd && Enum.TryParse<CynefinDomain>(cd.Id, out var parsed) ? parsed : CynefinDomain.Complex;
                ApplyStrategy(CynefinEditing.AddItem(new(id, domain, NextName("Item", model.Cynefin!.Items.Select(i => i.Label)))), model);
                break;
            case TaskKind.CynefinTransition:
                ApplyStrategy(CynefinEditing.SetTransition(new(id, CynefinDomain.Complex, CynefinDomain.Complicated, "Transition")), model);
                break;
            case TaskKind.VennSet:
                ApplyVenn(VennEditing.AddSet(new(id, NextName("Set", model.Venn!.Sets.Select(s => s.Name)), Size: 10)), model);
                break;
            case TaskKind.VennOverlap:
                var pair = UnusedVennPair() ?? throw new InvalidOperationException("Add two sets without an existing overlap.");
                ApplyVenn(VennEditing.AddOverlap(new(id, pair, "Overlap", pair.Min(set => model.Venn!.Sets.Single(s => s.Id == set).Size ?? 10) / 4)), model);
                break;
            case TaskKind.RailroadRule:
                ApplyRailroad(RailroadEditing.AddRule(new(id, NextName("rule", model.Railroad!.Rules.Select(r => r.Name)),
                    new(StencilId(), RailroadExpressionKind.Terminal, "text", []))), model);
                break;
            case TaskKind.LaneGroup:
                var laneName = NextName("Lane", model.Swimlane!.Groups.Select(g => g.SourceId).Concat(model.Swimlane.Nodes.Select(n => n.SourceId)));
                ApplyLane(SwimlaneEditing.AddGroup(new(id, laneName, laneName)), model);
                break;
            case TaskKind.LaneNode:
                var stepName = NextName("Step", model.Swimlane!.Nodes.Select(n => n.SourceId).Concat(model.Swimlane.Groups.Select(g => g.SourceId)));
                ApplyLane(SwimlaneEditing.AddNode(new(id, stepName, stepName, GroupId:
                    _canvas.SelectedStructuralTarget is { Role: "SwimlaneGroup" } sg ? sg.Id : model.Swimlane.Groups.FirstOrDefault()?.Id)), model);
                break;
            case TaskKind.UseCaseNode:
                var caseKind = preset is UseCaseNodeKind uk ? uk : UseCaseNodeKind.UseCase;
                var caseName = NextName(caseKind.ToString(), model.UseCase!.Nodes.SelectMany(n => new[] { n.SourceId, n.Label.Text }).Concat(model.UseCase.Boundaries.Select(b => b.SourceId)));
                var noteTarget = caseKind == UseCaseNodeKind.Note ? model.UseCase.Nodes
                    .Where(n => n.Kind is UseCaseNodeKind.Actor or UseCaseNodeKind.UseCase)
                    .OrderByDescending(n => n.Id == _canvas.SelectedStructuralTarget?.Id).First().Id : null;
                ApplyUseCase(UseCaseEditing.AddNode(new(id, caseKind == UseCaseNodeKind.Note ? "" : caseName, caseKind, new(caseName))
                {
                    BoundaryId = caseKind != UseCaseNodeKind.Note && _canvas.SelectedStructuralTarget is { Role: "UseCaseBoundary" } ub ? ub.Id : null,
                    NoteTargetId = noteTarget,
                }), model);
                break;
            case TaskKind.UseCaseBoundary:
                var boundaryName = NextName("System", model.UseCase!.Nodes.Select(n => n.SourceId).Concat(model.UseCase.Boundaries.Select(b => b.SourceId)));
                ApplyUseCase(UseCaseEditing.AddBoundary(new(id, boundaryName, new(boundaryName))), model);
                break;
            case TaskKind.AgentElement:
                var agentKind = preset is AgentFlowElementKind af ? af : AgentFlowElementKind.Node;
                var agentName = NextName(agentKind == AgentFlowElementKind.Node ? "Task" : agentKind.ToString(), model.AgentFlow!.Elements.Select(e => e.SourceId));
                ApplyAgent(AgentFlowEditing.AddElement(new(id, agentName, agentKind, agentName)
                { Metadata = agentKind == AgentFlowElementKind.Node ? new JsonObject { ["shape"] = "task" } : new() }), model);
                break;
            case TaskKind.EventFrame:
                var number = 1;
                while (model.EventModeling!.Frames.Any(f => f.Number == number.ToString(CultureInfo.InvariantCulture))) number++;
                ApplyEvent(EventModelingEditing.AddFrame(new(id, number.ToString(CultureInfo.InvariantCulture), EventEntityKind.UserInterface,
                    NextName("Screen", model.EventModeling.Frames.Select(f => f.EntityName)))), model);
                break;
            case TaskKind.EventData:
                ApplyEvent(EventModelingEditing.SetDataBlock(new(id, NextName("Example", model.EventModeling!.DataBlocks.Select(b => b.Name)),
                    new(EventDataKind.Json, "{}", true))), model);
                break;
            case TaskKind.ZenParticipant:
                var zenKind = preset is ZenParticipantKind zk ? zk : ZenParticipantKind.Participant;
                var zenName = NextName(zenKind.ToString(), model.Zen!.Participants.Select(p => p.SourceId));
                ApplyZen(ZenEditing.AddParticipant(new(id, zenName, Kind: zenKind) { Declared = true }), model);
                break;
            case TaskKind.ZenStatement:
                var receiver = model.Zen!.Participants.FirstOrDefault(p => p.Id == _canvas.SelectedStructuralTarget?.Id) ?? model.Zen.Participants[0];
                ApplyZen(ZenEditing.AddStatement(new(id, ZenStatementKind.Call) { To = receiver.Id, Text = "message", Arguments = "" }), model);
                break;
            default:
                throw new InvalidOperationException("No default element is defined for " + task + ".");
        }
    }
    private static double RadarDefault(RadarChart chart) => chart.Maximum is { } maximum
        ? chart.Minimum / 2 + maximum / 2 : Math.Max(1, chart.Minimum);
}
