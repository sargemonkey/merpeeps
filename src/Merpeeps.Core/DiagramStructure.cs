using Merpeeps.Core.Model;

namespace Merpeeps.Core;

/// <summary>A compact, renderer-independent description of non-graph diagram data.</summary>
public static class DiagramStructure
{
    public static object? DescribeData(DiagramModel model)
    {
        Dictionary<string, object?> Item(string role, string id, string? text = null, object? value = null, string? parent = null) =>
            new() { ["role"] = role, ["id"] = id, ["label"] = text, ["value"] = value, ["parent"] = parent };
        var items = new List<object?>();
        string? title = null;
        switch (model.Chart)
        {
            case PieChart pie:
                title = pie.Title; items.AddRange(pie.Slices.Select(s => Item("slice", s.Id, s.Label, s.Value))); break;
            case QuadrantChart quadrant:
                title = quadrant.Title; items.AddRange(quadrant.Points.Select(p => Item("point", p.Id, p.Label, new[] { p.X, p.Y }))); break;
            case XyChart xy:
                title = xy.Title;
                foreach (var series in xy.Series)
                {
                    items.Add(Item(series.Kind.ToString(), series.Id, series.Name));
                    items.AddRange(series.Values.Select(v => Item("value", v.Id, v.Label, v.Value, series.Id)));
                }
                break;
            case RadarChart radar:
                title = radar.Title;
                items.AddRange(radar.Axes.Select(a => Item("axis", a.Id, a.Display)));
                foreach (var curve in radar.Curves)
                {
                    items.Add(Item("curve", curve.Id, curve.Display));
                    items.AddRange(curve.Values.Select(v => Item("axis-value", v.AxisId, value: v.Value, parent: curve.Id)));
                }
                break;
        }
        if (model.Outline is { } outline)
            Outline(outline.Roots, null);
        if (model.Temporal is { } temporal)
        {
            title = temporal.Title;
            items.AddRange(temporal.Sections.Select(s => Item("section", s.Id, s.Label)));
            foreach (var period in temporal.Periods)
            {
                items.Add(Item("period", period.Id, period.Label, parent: period.SectionId));
                items.AddRange(period.Events.Select(e => Item("event", e.Id, e.Text, parent: period.Id)));
            }
            items.AddRange(temporal.Tasks.Select(t => Item("task", t.Id, t.Label,
                new Dictionary<string, object?> { ["score"] = t.Score, ["actors"] = t.Actors.ToArray() }, t.SectionId)));
        }
        if (model.Packet is { } packet)
        {
            title = packet.Title;
            items.AddRange(packet.Resolve().Select(f => Item("field", f.Field.Id, f.Field.Label,
                new Dictionary<string, object?> { ["start"] = f.Start, ["end"] = f.End, ["bits"] = f.Field.Bits })));
        }
        if (model.Kanban is { } board)
            foreach (var column in board.Columns)
            {
                items.Add(Item("column", column.Id, column.Label));
                items.AddRange(column.Cards.Select(c => Item("card", c.Id, c.Display, parent: column.Id)));
            }
        if (model.Requirements is { } requirements)
        {
            items.AddRange(requirements.Items.Select(r => Item(r.Kind.ToString(), r.Id, r.Name,
                new Dictionary<string, object?> { ["id"] = r.DisplayId, ["text"] = r.Text, ["risk"] = r.Risk.ToString(), ["verify"] = r.Verification.ToString() })));
            items.AddRange(requirements.Relations.Select(r => Item("relationship", r.Id, r.Kind.ToString(),
                new Dictionary<string, object?> { ["from"] = r.From, ["to"] = r.To })));
        }
        if (model.GitDiagram is { } git)
        {
            var state = git.Replay();
            items.AddRange(state.Branches.Select(b => Item("branch", b.Name, b.Name, b.HeadId)));
            items.AddRange(state.Commits.Select(c => Item("commit", c.Id, c.DisplayId,
                new Dictionary<string, object?> { ["branch"] = c.Branch, ["parents"] = c.Parents.ToArray(), ["type"] = c.Kind.ToString(), ["tags"] = c.Tags.ToArray() })));
        }
        if (model.Sankey is { } sankey)
        {
            items.AddRange(sankey.Nodes.Select(n => Item("flow-node", n.Id, n.Label)));
            items.AddRange(sankey.Links.Select(l => Item("weighted-flow", l.Id, value:
                new Dictionary<string, object?> { ["from"] = l.From, ["to"] = l.To, ["value"] = l.Value })));
        }
        if (model.Treemap is { } treemap) WeightedNodes(treemap.Roots, null);
        if (model.Blocks is { } blocks)
        {
            Blocks(blocks.Items, null);
            items.AddRange(blocks.Connections.Select(e => Item("block-connection", e.Id, e.Label,
                new Dictionary<string, object?> { ["from"] = e.From, ["to"] = e.To, ["fromEnd"] = e.FromEnd.ToString(), ["toEnd"] = e.ToEnd.ToString(), ["stroke"] = e.Stroke.ToString() })));
        }
        if (model.Architecture is { } architecture)
        {
            items.AddRange(architecture.Groups.Select(g => Item("group", g.Id, g.Display, g.SourceId, g.ParentId)));
            items.AddRange(architecture.Nodes.Select(n => Item(n.Kind.ToString(), n.Id, n.Display, n.SourceId, n.GroupId)));
            items.AddRange(architecture.Connections.Select(e => Item("port-connection", e.Id, value:
                new Dictionary<string, object?> { ["from"] = e.From.NodeId, ["fromPort"] = e.From.Port.ToString(), ["fromGroup"] = e.From.GroupBoundary,
                    ["to"] = e.To.NodeId, ["toPort"] = e.To.Port.ToString(), ["toGroup"] = e.To.GroupBoundary, ["fromArrow"] = e.FromArrow, ["toArrow"] = e.ToArrow })));
        }
        if (model.C4 is { } c4)
        {
            title = c4.Title;
            items.AddRange(c4.Elements.Select(e => Item(e.Level.ToString(), e.Id, e.Label,
                new Dictionary<string, object?> { ["alias"] = e.Alias, ["external"] = e.External, ["technology"] = e.Technology, ["description"] = e.Description }, e.ParentId)));
            items.AddRange(c4.Relationships.Select(r => Item("c4-relationship", r.Id, r.Label,
                new Dictionary<string, object?> { ["from"] = r.From, ["to"] = r.To, ["kind"] = r.Kind.ToString() })));
        }
        if (model.Gantt is { } gantt)
        {
            title = gantt.Title;
            items.AddRange(gantt.Sections.Select(s => Item("section", s.Id, s.Label)));
            items.AddRange(gantt.Tasks.Select(t => Item("gantt-task", t.Id, t.Label,
                new Dictionary<string, object?> { ["sourceId"] = t.SourceId, ["flags"] = t.Flags.ToString(),
                    ["start"] = t.Start switch
                    {
                        GanttStartSpec.Date date => (object)date.Text, GanttStartSpec.After after => after.TaskIds.ToArray(), _ => "previous",
                    },
                    ["end"] = t.End switch
                    {
                        GanttEndSpec.Date date => (object)date.Text, GanttEndSpec.Until until => until.TaskIds.ToArray(),
                        GanttEndSpec.Duration duration => new Dictionary<string, object?> { ["amount"] = duration.Amount, ["unit"] = duration.Unit.ToString() },
                        _ => null,
                    } }, t.SectionId)));
        }
        if (model.Wardley is { } wardley)
        {
            title = wardley.Title;
            items.AddRange(wardley.Components.Select(c => Item(c.Anchor ? "anchor" : "component", c.Id, c.Name,
                new Dictionary<string, object?> { ["visibility"] = wardley.VisibilityOf(c.Id), ["evolution"] = c.Evolution, ["strategy"] = c.Strategy.ToString(), ["inertia"] = c.Inertia, ["evolveTo"] = c.EvolveTo }, c.PipelineParentId)));
            items.AddRange(wardley.Links.Select(l => Item("map-link", l.Id, l.Label, new Dictionary<string, object?> { ["from"] = l.From, ["to"] = l.To, ["kind"] = l.Kind.ToString() })));
            items.AddRange(wardley.Annotations.Select(a => Item(a.Kind.ToString(), a.Id, a.Text, new Dictionary<string, object?> { ["visibility"] = a.Visibility, ["evolution"] = a.Evolution, ["number"] = a.Number })));
        }
        if (model.Ishikawa is { } ishikawa)
        {
            title = ishikawa.Problem; Causes(ishikawa.Causes, null);
        }
        if (model.Cynefin is { } cynefin)
        {
            title = cynefin.Title;
            items.AddRange(cynefin.Items.Select(i => Item("domain-item", i.Id, i.Label, parent: i.Domain.ToString())));
            items.AddRange(cynefin.Transitions.Select(t => Item("domain-transition", t.Id, t.Label, new Dictionary<string, object?> { ["from"] = t.From.ToString(), ["to"] = t.To.ToString() })));
        }
        if (model.Venn is { } venn)
        {
            title = venn.Title;
            items.AddRange(venn.Sets.Select(s => Item("set", s.Id, s.Label ?? s.Name, new Dictionary<string, object?> { ["name"] = s.Name, ["size"] = s.EffectiveSize })));
            items.AddRange(venn.Overlaps.Select(o => Item("overlap", o.Id, o.Label ?? "", new Dictionary<string, object?> { ["sets"] = o.SetIds.ToArray(), ["size"] = o.EffectiveSize })));
            items.AddRange(venn.Texts.Select(t => Item("text", t.Id, t.Label ?? t.Name, parent: t.AreaId)));
        }
        if (model.Railroad is { } grammar)
        {
            title = grammar.Title;
            foreach (var rule in grammar.Rules)
            {
                items.Add(Item("rule", rule.Id, rule.Name));
                foreach (var e in RailroadDiagramDocument.Walk(rule.Expression)) items.Add(Item(e.Kind.ToString(), e.Id, e.Text,
                    new Dictionary<string, object?> { ["minimum"] = e.Minimum, ["maximum"] = e.Maximum }, rule.Id));
            }
        }
        if (model.Swimlane is { } lanes)
        {
            items.AddRange(lanes.Groups.Select(g => Item("lane-group", g.Id, g.Label, parent: g.ParentId)));
            items.AddRange(lanes.Nodes.Select(n => Item("lane-step", n.Id, n.Label, new Dictionary<string, object?> { ["sourceId"] = n.SourceId, ["lane"] = lanes.LaneOf(n.Id) }, n.GroupId)));
            items.AddRange(lanes.Links.Select(l => Item("handoff", l.Id, l.Label, new Dictionary<string, object?> { ["from"] = l.From, ["to"] = l.To })));
        }
        if (model.UseCase is { } usecase)
        {
            items.AddRange(usecase.Boundaries.Select(b => Item("system-boundary", b.Id, b.Title.Text, b.Package)));
            items.AddRange(usecase.Nodes.Select(n => Item(n.Kind.ToString(), n.Id, n.Label.Text,
                new Dictionary<string, object?> { ["sourceId"] = n.SourceId, ["actorKind"] = n.ActorKind.ToString(), ["business"] = n.Business, ["stereotype"] = n.Stereotype }, n.BoundaryId)));
            items.AddRange(usecase.Relationships.Select(r => Item(r.Kind.ToString(), r.Id, r.Label.Text, new Dictionary<string, object?> { ["from"] = r.From, ["to"] = r.To })));
        }
        if (model.AgentFlow is { } agent)
        {
            items.AddRange(agent.Elements.Select(e => Item(e.Kind.ToString(), e.Id, e.Display, new Dictionary<string, object?> { ["sourceId"] = e.SourceId, ["shape"] = e.Shape, ["global"] = e.Global, ["collapsed"] = e.Collapsed }, e.ParentId)));
            items.AddRange(agent.Links.Select(l => Item(l.Kind.ToString(), l.Id, l.Label, new Dictionary<string, object?> { ["from"] = l.From, ["to"] = l.To })));
        }
        if (model.EventModeling is { } events)
            items.AddRange(events.Frames.Select(f => Item("timeframe", f.Id, f.EntityName, new Dictionary<string, object?> { ["number"] = f.Number, ["kind"] = f.Kind.ToString(), ["reset"] = f.Reset, ["references"] = f.References.ToArray() })));
        if (model.Zen is { } zen)
        {
            title = zen.Title;
            items.AddRange(zen.Participants.Select(p => Item("participant", p.Id, p.Alias ?? p.SourceId, p.Kind.ToString())));
            items.AddRange(ZenSemantics.Walk(zen.Statements).Select(s => Item(s.Kind.ToString(), s.Id, s.Text, new Dictionary<string, object?> { ["from"] = s.From, ["to"] = s.To })));
        }
        return model.Venn is null && model.Railroad is null && model.Swimlane is null && model.UseCase is null && model.AgentFlow is null && model.EventModeling is null && model.Zen is null && model.Chart is null && model.Outline is null && model.Temporal is null && model.Packet is null && model.Kanban is null && model.Requirements is null && model.GitDiagram is null && model.Sankey is null && model.Treemap is null && model.Blocks is null && model.Architecture is null && model.C4 is null && model.Gantt is null && model.Wardley is null && model.Ishikawa is null && model.Cynefin is null
            ? null : new Dictionary<string, object?> { ["ok"] = true, ["kind"] = model.Kind, ["title"] = title, ["itemCount"] = items.Count, ["items"] = items };
        void Causes(IEnumerable<IshikawaCause> causes, string? parent)
        {
            foreach (var cause in causes) { items.Add(Item("cause", cause.Id, cause.Label, parent: parent)); Causes(cause.Children, cause.Id); }
        }
        void WeightedNodes(IEnumerable<TreemapNode> nodes, string? parent)
        {
            foreach (var node in nodes)
            {
                items.Add(Item("weighted-node", node.Id, node.Label, node.Value, parent));
                WeightedNodes(node.Children, node.Id);
            }
        }
        void Blocks(IEnumerable<BlockItem> nodes, string? parent)
        {
            foreach (var node in nodes)
            {
                items.Add(Item(node.Kind.ToString(), node.Id, node.Display,
                    new Dictionary<string, object?> { ["sourceId"] = node.SourceId, ["span"] = node.Span, ["columns"] = node.Columns }, parent));
                Blocks(node.Children, node.Id);
            }
        }
        void Outline(IEnumerable<OutlineNode> nodes, string? parent)
        {
            foreach (var node in nodes) { items.Add(Item("node", node.Id, node.Label, parent: parent)); Outline(node.Children, node.Id); }
        }
    }
}
