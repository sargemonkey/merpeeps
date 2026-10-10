using Merpeeps.Core.Model;

namespace Merpeeps.Core.Layout;

public sealed record SwimlaneLayoutResult(DrawingScene Scene, IReadOnlyDictionary<string, DiagramRect> Lanes,
    IReadOnlyDictionary<string, DiagramRect> Groups, IReadOnlyDictionary<string, DiagramRect> Nodes,
    IReadOnlyDictionary<string, IReadOnlyList<DiagramPoint>> Routes, IReadOnlyList<DataDiagramDiagnostic> Diagnostics);
public static class SwimlaneDiagramLayout
{
    public static SwimlaneLayoutResult Create(DiagramModel model, DrawingTextMeasure? measure = null, string? themeOverride = null)
    {
        var d = model.Swimlane ?? throw new ArgumentException("Swimlane content is missing."); SwimlaneValidation.Check(d);
        var b = new DataDiagramLayouts.Builder(measure, d.ThemeVariables, d.GlobalConfiguration, themeOverride ?? model.Theme);
        var graph = new DiagramModel { Direction = d.Direction };
        var styles = new Dictionary<string, ClassResolvedStyle>();
        foreach (var node in d.Nodes)
        {
            var style = SwimlaneValidation.Appearance(d, node.Id, node.Classes, node.Style); styles[node.Id] = style;
            var label = b.MeasureWrapped(node.Label, style.FontSize, 200, style.Bold, style.Italic, family: style.FontFamily);
            var width = Math.Max(100, label.Width + 32); var height = Math.Max(50, label.Height + 24);
            if (node.Shape == NodeShape.Rhombus) { width *= 1.8; height *= 1.8; }
            if (node.Shape is NodeShape.Circle or NodeShape.DoubleCircle) width = height = Math.Max(width, height);
            graph.Nodes.Add(new DiagramNode { Id = node.Id, Width = width, Height = height });
        }
        graph.Edges.AddRange(d.Links.Select(l => new DiagramEdge { From = l.From, To = l.To }));
        LayoutEngines.Apply(graph, "layered");
        var horizontal = d.Direction is Direction.LR or Direction.RL;
        var topGroups = d.Groups.Where(g => g.ParentId is null).ToArray();
        var laneIds = topGroups.Select(g => g.Id).ToList();
        if (d.Nodes.Any(n => d.LaneOf(n.Id) is null)) laneIds.Add("");
        var ranks = graph.Nodes.Select(n => horizontal ? n.X : n.Y).Distinct().Order().ToArray();
        var bandSizes = ranks.Select(rank => graph.Nodes.Where(n => (horizontal ? n.X : n.Y) == rank).Max(n => horizontal ? n.Width : n.Height)).ToArray();
        var totalLength = Math.Max(200, bandSizes.Sum() + Math.Max(0, ranks.Length - 1) * 90);
        var nodeBounds = new Dictionary<string, DiagramRect>(); var laneBounds = new Dictionary<string, DiagramRect>();
        var groupBounds = new Dictionary<string, DiagramRect>(); var cursor = 20d; const double header = 42;
        foreach (var lane in laneIds)
        {
            var nodes = d.Nodes.Where(n => (d.LaneOf(n.Id) ?? "") == lane).ToArray();
            var thickness = Math.Max(110, ranks.Select(rank =>
                nodes.Select(n => graph.Node(n.Id)!).Where(n => (horizontal ? n.X : n.Y) == rank).Sum(n => (horizontal ? n.Height : n.Width) + 30)).DefaultIfEmpty(100).Max());
            var laneRect = horizontal ? new DiagramRect(20, cursor, totalLength + 80, thickness + header + 30)
                : new DiagramRect(cursor, 20, thickness + 40, totalLength + header + 60);
            laneBounds[lane] = laneRect;
            var laneGroup = d.Groups.FirstOrDefault(g => g.Id == lane);
            var style = laneGroup is null ? null : SwimlaneValidation.Appearance(d, lane, laneGroup.Classes, laneGroup.Style);
            b.Box("lane-" + lane, "SwimlaneGroup", lane, laneRect, style?.Fill ?? b.Tint(b.Scale(laneIds.IndexOf(lane))) with { Alpha = 90 }, style?.Stroke ?? b.Ink);
            var label = laneGroup?.Label ?? "Unassigned";
            b.Text("lane-title-" + lane, "SwimlaneGroup", lane, label, 16, laneRect.X + 12, laneRect.Y + 8, laneRect.Width - 24, true, style?.TextColor);
            b.Hit("SwimlaneGroup", lane, laneRect);
            for (var rankIndex = 0; rankIndex < ranks.Length; rankIndex++)
            {
                var rank = ranks[rankIndex]; var primary = bandSizes.Take(rankIndex).Sum() + rankIndex * 90;
                var cross = 0d;
                foreach (var node in nodes.Where(n => (horizontal ? graph.Node(n.Id)!.X : graph.Node(n.Id)!.Y) == rank))
                {
                    var geometry = graph.Node(node.Id)!;
                    nodeBounds[node.Id] = horizontal ? new(laneRect.X + 40 + primary, laneRect.Y + header + cross, geometry.Width, geometry.Height)
                        : new(laneRect.X + 20 + cross, laneRect.Y + header + primary, geometry.Width, geometry.Height);
                    cross += (horizontal ? geometry.Height : geometry.Width) + 30;
                }
            }
            cursor += (horizontal ? laneRect.Height : laneRect.Width) + 10;
        }
        foreach (var group in d.Groups.Where(g => g.ParentId is not null).OrderBy(g => Depth(g)))
        {
            var members = d.Nodes.Where(n => Inside(n.GroupId, group.Id)).Select(n => nodeBounds[n.Id]).ToArray();
            var r = members.Length == 0 ? new DiagramRect(30, 30, 160, 80) : members.Aggregate(Union);
            r = new(r.X - 12, r.Y - 20, r.Width + 24, r.Height + 32); groupBounds[group.Id] = r;
            b.Box(group.Id, "SwimlaneGroup", group.Id, r, null, b.Ink, 1, true);
            b.Text(group.Id + "-title", "SwimlaneGroup", group.Id, group.Label, 12, r.X + 8, r.Y, r.Width - 16, true);
            b.Hit("SwimlaneGroup", group.Id, r, group.ParentId);
        }
        var routes = new Dictionary<string, IReadOnlyList<DiagramPoint>>();
        foreach (var link in d.Links.Where(l => l.Kind != EdgeKind.Invisible))
        {
            var a = nodeBounds[link.From]; var z = nodeBounds[link.To];
            var forward = horizontal ? z.X >= a.X : z.Y >= a.Y;
            DiagramPoint start = horizontal ? new(forward ? a.X + a.Width : a.X, a.Y + a.Height / 2) : new(a.X + a.Width / 2, forward ? a.Y + a.Height : a.Y);
            DiagramPoint end = horizontal ? new(forward ? z.X : z.X + z.Width, z.Y + z.Height / 2) : new(z.X + z.Width / 2, forward ? z.Y : z.Y + z.Height);
            DiagramPoint[] route = horizontal ? [start, new((start.X + end.X) / 2, start.Y), new((start.X + end.X) / 2, end.Y), end]
                : [start, new(start.X, (start.Y + end.Y) / 2), new(end.X, (start.Y + end.Y) / 2), end];
            if (link.From == link.To) route = [new(a.X + a.Width, a.Y + a.Height / 2), new(a.X + a.Width + 36, a.Y + a.Height / 2), new(a.X + a.Width + 36, a.Y - 30), new(a.X + a.Width / 2, a.Y - 30), new(a.X + a.Width / 2, a.Y)];
            routes[link.Id] = route;
            b.Line(link.Id, "SwimlaneLink", link.Id, route, b.Ink, link.Kind is EdgeKind.Thick or EdgeKind.ThickArrow ? 3 : 1.5);
            b.HitLine("SwimlaneLink", link.Id, route);
            if (link.Kind is EdgeKind.Arrow or EdgeKind.DottedArrow or EdgeKind.ThickArrow)
            {
                var last = route.Last(); var prior = route.Reverse().Skip(1).First(p => p != last);
                var dx = prior.X - last.X; var dy = prior.Y - last.Y; var length = Math.Sqrt(dx * dx + dy * dy);
                b.Polygon(link.Id + "-head", "SwimlaneLink", link.Id, [last, new(last.X + dx / length * 10 - dy / length * 4, last.Y + dy / length * 10 + dx / length * 4),
                    new(last.X + dx / length * 10 + dy / length * 4, last.Y + dy / length * 10 - dx / length * 4)], b.Ink, b.Ink);
            }
            if (link.Label.Length > 0) b.Text(link.Id + "-label", "SwimlaneLink", link.Id, link.Label, 12, (start.X + end.X) / 2 + 5, (start.Y + end.Y) / 2 - 16, 200);
        }
        foreach (var node in d.Nodes)
        {
            var r = nodeBounds[node.Id]; var style = styles[node.Id];
            BlockDiagramLayout.Shape(b, new(node.Id, BlockItemKind.Shape, []) { Shape = node.Shape }, r, style.Fill ?? b.Tint(b.Scale(0)), style.Stroke ?? b.Ink, style.StrokeWidth);
            var label = b.MeasureWrapped(node.Label, style.FontSize, r.Width - 24, style.Bold, style.Italic, family: style.FontFamily);
            b.Text(node.Id + "-label", "SwimlaneNode", node.Id, node.Label, style.FontSize, r.X + (r.Width - label.Width) / 2, r.Y + (r.Height - label.Height) / 2,
                r.Width - 24, style.Bold, style.TextColor, style.Italic, family: style.FontFamily, alignment: "center");
            b.Hit("SwimlaneNode", node.Id, r, node.GroupId);
        }
        var scene = b.Finish(); var delta = b.OriginOffset;
        var figures = scene.Figures.Select(f => f.Owner.Role == "Block" ? f with { Owner = new("SwimlaneNode", f.Owner.Id), DashPattern = styles[f.Owner.Id].DashPattern }
            : d.Links.Any(l => l.Id == f.Id && l.Kind is EdgeKind.Dotted or EdgeKind.DottedArrow) ? f with { Dashed = true } : f).ToArray();
        var diagnostics = d.Groups.Where(g => g.ParentId is not null && g.Direction is not null).Select(g => new DataDiagramDiagnostic("swimlane-nested-direction", g.Id, "The native lane layout keeps process direction global; this nested direction is retained in source.")).ToArray();
        return new(scene with { Figures = figures }, Shift(laneBounds), Shift(groupBounds), Shift(nodeBounds),
            routes.ToDictionary(p => p.Key, p => (IReadOnlyList<DiagramPoint>)p.Value.Select(v => new DiagramPoint(v.X + delta.X, v.Y + delta.Y)).ToArray()), diagnostics);
        Dictionary<string, DiagramRect> Shift(Dictionary<string, DiagramRect> input) => input.ToDictionary(p => p.Key, p => p.Value with { X = p.Value.X + delta.X, Y = p.Value.Y + delta.Y });
        bool Inside(string? current, string id)
        {
            while (current is not null) { if (current == id) return true; current = d.Groups.Single(g => g.Id == current).ParentId; }
            return false;
        }
        int Depth(SwimlaneGroup g) => g.ParentId is null ? 0 : 1 + Depth(d.Groups.Single(p => p.Id == g.ParentId));
    }
    private static DiagramRect Union(DiagramRect a, DiagramRect b) => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
        Math.Max(a.X + a.Width, b.X + b.Width) - Math.Min(a.X, b.X), Math.Max(a.Y + a.Height, b.Y + b.Height) - Math.Min(a.Y, b.Y));
}
