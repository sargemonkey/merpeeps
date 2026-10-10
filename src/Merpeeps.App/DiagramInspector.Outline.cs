using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Merpeeps.Core.Model;

namespace Merpeeps.App;

internal sealed partial class DiagramInspector
{
    public void FillOutline(StackPanel panel)
    {
        var model = Model();
        void Add(string label, Action select)
        {
            var button = new Button
            {
                Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap },
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
                Classes = { "ghost" },
            };
            AutomationProperties.SetName(button, "Select " + label);
            button.Click += (_, _) => Perform(select);
            panel.Children.Add(button);
        }
        void Structural(string label, string role, string id) => Add(label, () => _canvas.SelectStructural(new(role, id)));
        void Data(string label, DataDiagramRole role, string id, string? parent = null) => Add(label, () => _canvas.SelectData(new(role, id, parent)));
        void ChartItem(string label, ChartElementRole role, string id) => Add(label, () => _canvas.SelectChart(new(role, id)));
        if (model.Sequence is { } sequence)
        {
            foreach (var p in sequence.Participants) Add(p.Display, () => _canvas.SelectSequenceId(p.Id));
            foreach (var s in SequenceDiagramModel.Walk(sequence.Statements).Where(IsTimelineItem)) Add(StatementLabel(s), () => _canvas.SelectSequenceId(s.Id));
        }
        switch (model.Chart)
        {
            case PieChart pie: foreach (var s in pie.Slices) ChartItem(s.Label, ChartElementRole.Slice, s.Id); break;
            case QuadrantChart q: foreach (var p in q.Points) ChartItem(p.Label, ChartElementRole.Point, p.Id); break;
            case XyChart xy: foreach (var s in xy.Series) ChartItem(s.Name ?? s.Kind.ToString(), ChartElementRole.Series, s.Id); break;
            case RadarChart radar:
                foreach (var a in radar.Axes) ChartItem("Axis: " + a.Display, ChartElementRole.Axis, a.Id);
                foreach (var c in radar.Curves) ChartItem("Curve: " + c.Display, ChartElementRole.Curve, c.Id);
                break;
        }
        if (model.Outline is { } outline) foreach (var n in outline.Nodes) Data(n.Label, DataDiagramRole.Node, n.Id);
        if (model.Temporal is { } temporal)
        {
            foreach (var s in temporal.Sections) Data(s.Label, DataDiagramRole.Section, s.Id);
            foreach (var p in temporal.Periods)
            {
                Data(p.Label, DataDiagramRole.Period, p.Id);
                foreach (var e in p.Events) Data("  " + e.Text, DataDiagramRole.Event, e.Id, p.Id);
            }
            foreach (var t in temporal.Tasks) Data(t.Label, DataDiagramRole.Task, t.Id);
        }
        if (model.Packet is { } packet) foreach (var f in packet.Fields) Data(f.Label, DataDiagramRole.Node, f.Id);
        if (model.Kanban is { } board) foreach (var col in board.Columns)
        {
            Data(col.Label, DataDiagramRole.Section, col.Id);
            foreach (var card in col.Cards) Data("  " + card.Display, DataDiagramRole.Node, card.Id, col.Id);
        }
        if (model.Requirements is { } requirements) foreach (var i in requirements.Items) Structural(i.Name, "Requirement", i.Id);
        if (model.GitDiagram is { } git) foreach (var i in git.Operations) Structural(i.Kind + " " + (i.CommitId ?? i.Name), "GitCommit", i.Id);
        if (model.Sankey is { } sankey) foreach (var n in sankey.Nodes) Structural(n.Label, "FlowNode", n.Id);
        if (model.Treemap is { } tree) foreach (var n in tree.Nodes) Structural(n.Label, "WeightedNode", n.Id);
        if (model.Blocks is { } blocks) foreach (var n in blocks.AllItems) Structural(n.Display.Length > 0 ? n.Display : n.Kind.ToString(), "Block", n.Id);
        if (model.Architecture is { } architecture)
        {
            foreach (var g in architecture.Groups) Structural(g.Display, "ArchitectureGroup", g.Id);
            foreach (var n in architecture.Nodes) Structural(n.Display, "ArchitectureNode", n.Id);
        }
        if (model.C4 is { } c4) foreach (var n in c4.Elements) Structural(n.Label, "C4Element", n.Id);
        if (model.Gantt is { } gantt)
        {
            foreach (var s in gantt.Sections) Structural(s.Label, "GanttSection", s.Id);
            foreach (var t in gantt.Tasks) Structural(t.Label, "GanttTask", t.Id);
        }
        if (model.Wardley is { } wardley) foreach (var n in wardley.Components) Structural(n.Name, "WardleyComponent", n.Id);
        if (model.Ishikawa is { } fish)
        {
            if (fish.Problem.Length > 0) Structural(fish.Problem, "IshikawaProblem", "problem");
            foreach (var c in fish.AllCauses) Structural(c.Label, "IshikawaCause", c.Id);
        }
        if (model.Cynefin is { } cynefin) foreach (var i in cynefin.Items) Structural(i.Domain + ": " + i.Label, "CynefinItem", i.Id);
        if (model.Venn is { } venn)
        {
            foreach (var s in venn.Sets) Structural(s.Label ?? s.Name, "VennSet", s.Id);
            foreach (var o in venn.Overlaps) Structural(o.Label ?? "Overlap", "VennOverlap", o.Id);
        }
        if (model.Railroad is { } railroad) foreach (var r in railroad.Rules) Structural(r.Name, "RailroadRule", r.Id);
        if (model.Swimlane is { } lanes)
        {
            foreach (var g in lanes.Groups) Structural(g.Label, "SwimlaneGroup", g.Id);
            foreach (var n in lanes.Nodes) Structural(n.Label, "SwimlaneNode", n.Id);
        }
        if (model.UseCase is { } cases)
        {
            foreach (var b in cases.Boundaries) Structural(b.Title.Text, "UseCaseBoundary", b.Id);
            foreach (var n in cases.Nodes) Structural(n.Label.Text, "UseCaseNode", n.Id);
        }
        if (model.AgentFlow is { } agents) foreach (var e in agents.Elements) Structural(e.Display, "AgentFlowElement", e.Id);
        if (model.EventModeling is { } events)
        {
            foreach (var f in events.Frames) Structural(f.Number + ": " + f.EntityName, "EventFrame", f.Id);
            foreach (var d in events.DataBlocks) Structural(d.Name, "EventData", d.Id);
        }
        if (model.Zen is { } zen)
        {
            foreach (var p in zen.Participants) Structural(p.Alias ?? p.SourceId, "ZenParticipant", p.Id);
            foreach (var s in ZenSemantics.Walk(zen.Statements)) Structural(s.Kind + ": " + s.Text, "ZenStatement", s.Id);
        }
        foreach (var g in model.Subgraphs.Where(g => GraphGroupGeometry.IsVisible(model, g)))
            Add(g.Title ?? g.Id, () => _canvas.SelectGroup(g));
    }
}
