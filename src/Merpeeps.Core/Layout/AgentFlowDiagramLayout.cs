using Merpeeps.Core.Model;
using Merpeeps.Core.Parsing;

namespace Merpeeps.Core.Layout;

public sealed record AgentFlowLayoutResult(DrawingScene Scene, IReadOnlyDictionary<string, DiagramRect> Elements,
    IReadOnlyDictionary<string, string> VisibleEndpoints, IReadOnlyList<DataDiagramDiagnostic> Diagnostics);
public static class AgentFlowDiagramLayout
{
    public static AgentFlowLayoutResult Create(DiagramModel model, DrawingTextMeasure? measure = null, string? themeOverride = null)
    {
        var d = model.AgentFlow ?? throw new ArgumentException("Agentflow content is missing."); AgentFlowSemantics.Check(d);
        var b = new DataDiagramLayouts.Builder(measure, d.ThemeVariables, d.GlobalConfiguration, themeOverride ?? model.Theme);
        var visible = d.Elements.ToDictionary(e => e.Id, e => AgentFlowSemantics.VisibleEndpoint(d, e.Id));
        var leafs = d.Elements.Where(e => visible[e.Id] == e.Id && (e.Kind != AgentFlowElementKind.Flow || e.Collapsed || !d.Elements.Any(c => c.ParentId == e.Id))).ToArray();
        var graph = new DiagramModel { Direction = d.Direction };
        foreach (var e in leafs)
        {
            var text = b.MeasureWrapped(e.Display, 14, 200, markdown: e.Markdown);
            var shape = e.Kind == AgentFlowElementKind.Node ? AgentFlowSemantics.Shape(e.Shape) : NodeShape.RoundedRect;
            var width = Math.Max(140, text.Width + 36); var height = Math.Max(60, text.Height + 36);
            if (shape == NodeShape.Rhombus) { width *= 1.6; height *= 1.6; }
            graph.Nodes.Add(new DiagramNode { Id = e.Id, Width = width, Height = height });
        }
        string Representative(string id)
        {
            var mapped = visible[id];
            if (graph.Node(mapped) is not null) return mapped;
            return leafs.FirstOrDefault(e => Descends(e.Id, mapped))?.Id ?? throw new ArgumentException("Expanded flow has no visible representative.");
        }
        foreach (var link in d.Links)
        {
            var from = Representative(link.From); var to = Representative(link.To);
            if (from != to) graph.Edges.Add(new DiagramEdge { From = from, To = to });
        }
        var nodeSpacing = d.Configuration["nodeSpacing"] is { } ns ? ChartConfiguration.ParseNumber(ns.ToString()) : 50;
        var rankSpacing = d.Configuration["rankSpacing"] is { } rs ? ChartConfiguration.ParseNumber(rs.ToString()) : 50;
        if (nodeSpacing < 0 || rankSpacing < 0) throw new ArgumentException("Agentflow spacing must be nonnegative.");
        LayeredLayout.Apply(graph, new(NodeSpacing: nodeSpacing, LayerSpacing: rankSpacing));
        var bounds = graph.Nodes.ToDictionary(n => n.Id, n => new DiagramRect(n.X, n.Y, n.Width, n.Height));
        var groups = d.Elements.Where(e => visible[e.Id] == e.Id && e.Kind == AgentFlowElementKind.Flow && !e.Collapsed && d.Elements.Any(c => c.ParentId == e.Id)).OrderByDescending(Depth).ToArray();
        foreach (var flow in groups)
        {
            var children = d.Elements.Where(e => e.ParentId == flow.Id && bounds.ContainsKey(e.Id)).Select(e => bounds[e.Id]).ToArray();
            var r = children.Length == 0 ? new DiagramRect(20, 20, 200, 130) : children.Aggregate(Union);
            bounds[flow.Id] = new(r.X - 24, r.Y - 42, r.Width + 48, r.Height + 66);
        }
        foreach (var flow in groups.Reverse())
        {
            var r = bounds[flow.Id];
            b.Box(flow.Id, "AgentFlowElement", flow.Id, r, b.Tint(b.Scale(0)) with { Alpha = 50 }, Paint("flowContainerStroke", b.Ink));
            b.Text(flow.Id + "-title", "AgentFlowElement", flow.Id, flow.Display, 14, r.X + 12, r.Y + 8, r.Width - 24, true, markdown: flow.Markdown);
            b.Hit("AgentFlowElement", flow.Id, r, flow.ParentId);
        }
        var diagnostics = new List<DataDiagramDiagnostic>();
        foreach (var link in d.Links)
        {
            var from = visible[link.From]; var to = visible[link.To]; if (from == to && link.From != link.To) continue;
            var a = bounds[from]; var z = bounds[to]; var start = Anchor(a, Center(z)); var end = Anchor(z, Center(a));
            var route = from == to ? new[] { start, new DiagramPoint(a.X + a.Width + 40, start.Y), new(a.X + a.Width + 40, a.Y - 30), new(a.X + a.Width / 2, a.Y - 30), new(a.X + a.Width / 2, a.Y) } : new[] { start, end };
            b.Line(link.Id, "AgentFlowLink", link.Id, route, b.Ink, 1.5);
            b.HitLine("AgentFlowLink", link.Id, route);
            var delta = new DiagramPoint(route[^2].X - route[^1].X, route[^2].Y - route[^1].Y); var length = Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y);
            if (length > 0 && link.Kind == AgentFlowLinkKind.Sequence)
                b.Polygon(link.Id + "-arrow", "AgentFlowLink", link.Id, [route[^1], new(route[^1].X + delta.X / length * 10 - delta.Y / length * 4, route[^1].Y + delta.Y / length * 10 + delta.X / length * 4),
                    new(route[^1].X + delta.X / length * 10 + delta.Y / length * 4, route[^1].Y + delta.Y / length * 10 - delta.X / length * 4)], b.Ink, b.Ink);
            if (link.Kind == AgentFlowLinkKind.Failure)
            {
                b.Line(link.Id + "-cross1", "AgentFlowLink", link.Id, [new(end.X - 5, end.Y - 5), new(end.X + 5, end.Y + 5)], b.Ink, 2);
                b.Line(link.Id + "-cross2", "AgentFlowLink", link.Id, [new(end.X - 5, end.Y + 5), new(end.X + 5, end.Y - 5)], b.Ink, 2);
            }
            if (link.Label.Length > 0) b.Text(link.Id + "-label", "AgentFlowLink", link.Id, link.Label, 12, (start.X + end.X) / 2 + 5, (start.Y + end.Y) / 2 - 15, 200);
            if (link.Metadata.Any(p => p.Key is "curve" or "animate" or "animation"))
                diagnostics.Add(new("agentflow-edge-presentation", link.Id, "Edge interpolation/animation metadata is preserved; native output uses a static route."));
        }
        foreach (var element in leafs)
        {
            var r = bounds[element.Id]; var shape = element.Kind == AgentFlowElementKind.Node ? AgentFlowSemantics.Shape(element.Shape) : NodeShape.RoundedRect;
            BlockDiagramLayout.Shape(b, new(element.Id, BlockItemKind.Shape, []) { Shape = shape }, r, b.Tint(b.Scale(element.Kind == AgentFlowElementKind.Connector ? 2 : 0)), b.Ink, 1.5);
            if (element.Shape is "refdoc" or "lin-doc")
                for (var line = 0; line < 3; line++) b.Line(element.Id + "-document-" + line, "AgentFlowElement", element.Id, [new(r.X + 12, r.Y + r.Height - 8 - line * 5), new(r.X + 32, r.Y + r.Height - 8 - line * 5)], b.Ink, 1);
            b.Text(element.Id + "-label", "AgentFlowElement", element.Id, element.Display + (element.Collapsed ? " (collapsed)" : ""), 14, r.X + 15, r.Y + 14, r.Width - 30, element.Kind != AgentFlowElementKind.Node, markdown: element.Markdown);
            b.Hit("AgentFlowElement", element.Id, r, element.ParentId);
        }
        foreach (var element in d.Elements)
        {
            if (element.Metadata["algorithm"] is not null) diagnostics.Add(new("agentflow-algorithm", element.Id, "The requested ELK container algorithm is retained; native layout uses its deterministic layered engine."));
            if (element.Metadata["connectorRef"] is { } reference && !reference.ToString().Contains('.') && !reference.ToString().Contains("://") && d.Elements.All(e => e.SourceId != reference.ToString()))
                diagnostics.Add(new("agentflow-connector", element.Id, "Unresolved connector reference is retained as inert metadata."));
        }
        var scene = b.Finish(); var offset = b.OriginOffset;
        var figures = scene.Figures.Select(f => f.Owner.Role == "Block" ? f with { Owner = new("AgentFlowElement", f.Owner.Id) }
            : d.Links.Any(l => l.Id == f.Id && l.Kind == AgentFlowLinkKind.Reference) ? f with { Dashed = true } : f).ToArray();
        return new(scene with { Figures = figures }, bounds.ToDictionary(p => p.Key, p => p.Value with { X = p.Value.X + offset.X, Y = p.Value.Y + offset.Y }), visible, diagnostics);
        bool Descends(string id, string parent)
        {
            var current = d.Elements.Single(e => e.Id == id).ParentId;
            while (current is not null) { if (current == parent) return true; current = d.Elements.Single(e => e.Id == current).ParentId; }
            return false;
        }
        int Depth(AgentFlowElement e) => e.ParentId is null ? 0 : 1 + Depth(d.Elements.Single(p => p.Id == e.ParentId));
        ClassColor Paint(string key, ClassColor fallback) => d.ThemeVariables[key] is { } value ? SequenceColors.TryParse(value.ToString(), out var color) ? color : throw new ArgumentException("Invalid Agentflow color.") : fallback;
    }
    private static DiagramPoint Center(DiagramRect r) => new(r.X + r.Width / 2, r.Y + r.Height / 2);
    private static DiagramPoint Anchor(DiagramRect r, DiagramPoint toward)
    {
        var c = Center(r); var dx = toward.X - c.X; var dy = toward.Y - c.Y;
        if (dx == 0 && dy == 0) return new(r.X + r.Width, c.Y);
        var factor = Math.Min(dx == 0 ? double.MaxValue : r.Width / 2 / Math.Abs(dx), dy == 0 ? double.MaxValue : r.Height / 2 / Math.Abs(dy));
        return new(c.X + dx * factor, c.Y + dy * factor);
    }
    private static DiagramRect Union(DiagramRect a, DiagramRect b) => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
        Math.Max(a.X + a.Width, b.X + b.Width) - Math.Min(a.X, b.X), Math.Max(a.Y + a.Height, b.Y + b.Height) - Math.Min(a.Y, b.Y));
}
