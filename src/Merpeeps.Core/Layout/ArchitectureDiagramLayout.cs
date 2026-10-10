using Merpeeps.Core.Model;
using Merpeeps.Core.Parsing;

namespace Merpeeps.Core.Layout;

public sealed record ArchitectureNodePlacement(DiagramRect IconBounds, DiagramRect Bounds);
public sealed record ArchitectureLayoutResult(DrawingScene Scene,
    IReadOnlyDictionary<string, ArchitectureNodePlacement> Nodes,
    IReadOnlyDictionary<string, DiagramRect> Groups,
    IReadOnlyDictionary<string, IReadOnlyList<DiagramPoint>> Routes,
    IReadOnlyList<DataDiagramDiagnostic> Diagnostics);

public static class ArchitectureDiagramLayout
{
    public static ArchitectureLayoutResult Create(DiagramModel model, DrawingTextMeasure? measure = null, string? themeOverride = null)
    {
        var document = model.Architecture ?? throw new ArgumentException("Architecture content is missing.");
        ArchitectureDiagramAdapter.Check(document);
        var b = new DataDiagramLayouts.Builder(measure, document.ThemeVariables, document.GlobalConfiguration, themeOverride ?? model.Theme);
        var iconSize = Number(document, "iconSize", 80);
        var separation = Number(document, "nodeSeparation", 75);
        var multiplier = Number(document, "idealEdgeLengthMultiplier", 1.5);
        if (iconSize <= 0 || separation < 0 || multiplier <= 0) throw new ArgumentException("Architecture sizes and spacing must be positive.");
        var issues = new List<DataDiagramDiagnostic>();
        foreach (var option in new[] { "randomize", "seed", "numIter", "edgeElasticity" })
            if (document.Configuration[option] is not null)
                issues.Add(new("architecture-layout-engine", "", option + " is an upstream fcose setting; the native constraint layout does not apply it."));
        var sizes = document.Nodes.ToDictionary(n => n.Id, n =>
        {
            if (n.Kind == ArchitectureNodeKind.Junction) return new DrawingTextSize(12, 12);
            var label = b.MeasureWrapped(n.Display, 14, iconSize + 24);
            return new DrawingTextSize(Math.Max(iconSize, label.Width), iconSize + 8 + label.Height);
        });
        var x = new Axis(document.Nodes.Select(n => n.Id)); var y = new Axis(document.Nodes.Select(n => n.Id));
        foreach (var alignment in document.Alignments)
        {
            var shared = alignment.Axis == ArchitectureAlignmentAxis.Row ? y : x;
            foreach (var member in alignment.Members.Skip(1)) shared.Union(alignment.Members[0], member);
        }
        foreach (var alignment in document.Alignments)
        {
            var ordered = alignment.Axis == ArchitectureAlignmentAxis.Row ? x : y;
            for (var i = 1; i < alignment.Members.Count; i++) ordered.Before(alignment.Members[i - 1], alignment.Members[i]);
        }
        foreach (var edge in document.Connections)
        {
            if (edge.From.NodeId == edge.To.NodeId) continue;
            if (edge.From.Port == Opposite(edge.To.Port))
                Direction(edge.From.NodeId, edge.To.NodeId, edge.From.Port);
            else if (Horizontal(edge.From.Port) != Horizontal(edge.To.Port))
            {
                Direction(edge.From.NodeId, edge.To.NodeId, edge.From.Port);
                Direction(edge.To.NodeId, edge.From.NodeId, edge.To.Port);
            }
        }
        var positionsX = x.Solve(); var positionsY = y.Solve();
        for (var iteration = 0; iteration < document.Nodes.Count * document.Nodes.Count; iteration++)
        {
            var collision = false;
            for (var i = 0; i < document.Nodes.Count && !collision; i++)
                for (var j = i + 1; j < document.Nodes.Count; j++)
                {
                    var a = document.Nodes[i].Id; var z = document.Nodes[j].Id;
                    if (positionsX[a] != positionsX[z] || positionsY[a] != positionsY[z]) continue;
                    if (y.Same(a, z)) x.Before(a, z); else y.Before(a, z);
                    positionsX = x.Solve(); positionsY = y.Solve(); collision = true; break;
                }
            if (!collision) break;
        }
        var dx = sizes.Values.Select(s => s.Width).DefaultIfEmpty(iconSize).Max() + Math.Max(separation, iconSize * multiplier);
        var dy = sizes.Values.Select(s => s.Height).DefaultIfEmpty(iconSize).Max() + Math.Max(separation, iconSize * multiplier);
        var nodes = document.Nodes.ToDictionary(n => n.Id, n =>
        {
            var center = new DiagramPoint(60 + positionsX[n.Id] * dx, 60 + positionsY[n.Id] * dy);
            var size = n.Kind == ArchitectureNodeKind.Junction ? 12 : iconSize;
            return new ArchitectureNodePlacement(new(center.X - size / 2, center.Y - size / 2, size, size),
                new(center.X - sizes[n.Id].Width / 2, center.Y - size / 2, sizes[n.Id].Width, sizes[n.Id].Height));
        });
        var groups = new Dictionary<string, DiagramRect>();
        foreach (var group in document.Groups) GroupBounds(group);
        foreach (var group in document.Groups.OrderBy(Depth))
        {
            var bounds = groups[group.Id];
            b.Box(group.Id, "ArchitectureGroup", group.Id, bounds, null, b.Ink, 1);
            b.Text(group.Id + "-title", "ArchitectureGroup", group.Id, group.Display, 14, bounds.X + 34, bounds.Y + 8, Math.Max(1, bounds.Width - 44), true);
            DrawIcon(group.Id + "-icon", "ArchitectureGroup", group.Id, group.Icon, new(bounds.X + 8, bounds.Y + 6, 20, 20));
            b.Hit("ArchitectureGroup", group.Id, bounds, group.ParentId);
        }
        var routes = new Dictionary<string, IReadOnlyList<DiagramPoint>>();
        foreach (var connection in document.Connections)
        {
            var from = Endpoint(connection.From); var to = Endpoint(connection.To);
            var route = Route(from, to, connection.From.Port, connection.To.Port, connection.From.NodeId == connection.To.NodeId);
            routes[connection.Id] = route;
            b.Line(connection.Id, "ArchitectureConnection", connection.Id, route, b.Ink, 1.5);
            if (connection.FromArrow) Arrow(connection.Id + "-from", connection.Id, route[0], route[1]);
            if (connection.ToArrow) Arrow(connection.Id + "-to", connection.Id, route[^1], route[^2]);
        }
        foreach (var node in document.Nodes)
        {
            var placement = nodes[node.Id]; var r = placement.IconBounds;
            if (node.Kind == ArchitectureNodeKind.Junction)
                b.Circle(node.Id, "ArchitectureNode", node.Id, new(r.X + r.Width / 2, r.Y + r.Height / 2), 5, b.Ink, null);
            else
            {
                DrawIcon(node.Id, "ArchitectureNode", node.Id, node.Icon, r);
                var text = b.MeasureWrapped(node.Display, 14, iconSize + 24);
                b.Text(node.Id + "-label", "ArchitectureNode", node.Id, node.Display, 14, r.X + (r.Width - text.Width) / 2, r.Y + r.Height + 8, iconSize + 24, alignment: "center");
            }
            b.Hit("ArchitectureNode", node.Id, placement.Bounds, node.GroupId);
        }
        var scene = b.Finish(); var offset = b.OriginOffset;
        var finalNodes = nodes.ToDictionary(p => p.Key, p => new ArchitectureNodePlacement(Shift(p.Value.IconBounds), Shift(p.Value.Bounds)));
        var finalGroups = groups.ToDictionary(p => p.Key, p => Shift(p.Value));
        var finalRoutes = routes.ToDictionary(p => p.Key, p => (IReadOnlyList<DiagramPoint>)p.Value.Select(point => new DiagramPoint(point.X + offset.X, point.Y + offset.Y)).ToArray());
        var edgeRegions = finalRoutes.Select(pair => new DrawingRegion(new("ArchitectureConnection", pair.Key), DrawingHitKind.Polyline,
            Bounds(pair.Value), pair.Value));
        return new(scene with { Regions = scene.Regions.Concat(edgeRegions).ToArray() }, finalNodes, finalGroups, finalRoutes, issues);

        DiagramRect Shift(DiagramRect r) => r with { X = r.X + offset.X, Y = r.Y + offset.Y };
        void Direction(string from, string to, ArchitecturePort port)
        {
            switch (port)
            {
                case ArchitecturePort.Left: x.Before(to, from); break;
                case ArchitecturePort.Right: x.Before(from, to); break;
                case ArchitecturePort.Top: y.Before(to, from); break;
                case ArchitecturePort.Bottom: y.Before(from, to); break;
            }
        }
        int Depth(ArchitectureGroup group) => group.ParentId is null ? 0 : 1 + Depth(document.Groups.Single(g => g.Id == group.ParentId));
        DiagramRect GroupBounds(ArchitectureGroup group)
        {
            if (groups.TryGetValue(group.Id, out var existing)) return existing;
            var children = document.Nodes.Where(n => n.GroupId == group.Id).Select(n => nodes[n.Id].Bounds)
                .Concat(document.Groups.Where(g => g.ParentId == group.Id).Select(GroupBounds)).ToArray();
            var label = b.MeasureWrapped(group.Display, 14, double.MaxValue, true);
            var raw = children.Length == 0 ? new DiagramRect(20, 20 + document.Groups.IndexOf(group) * 140, Math.Max(120, label.Width + 60), 80) : children.Aggregate(Union);
            return groups[group.Id] = new(raw.X - 20, raw.Y - 42, Math.Max(raw.Width + 40, label.Width + 60), raw.Height + 62);
        }
        DiagramPoint Endpoint(ArchitectureEndpoint endpoint)
        {
            var node = document.Nodes.Single(n => n.Id == endpoint.NodeId);
            var icon = nodes[node.Id].IconBounds;
            var bounds = endpoint.GroupBoundary ? groups[node.GroupId!] : icon;
            var marginX = Math.Min(8, bounds.Width / 2); var marginY = Math.Min(8, bounds.Height / 2);
            var cx = Math.Clamp(icon.X + icon.Width / 2, bounds.X + marginX, bounds.X + bounds.Width - marginX);
            var cy = Math.Clamp(icon.Y + icon.Height / 2, bounds.Y + marginY, bounds.Y + bounds.Height - marginY);
            return endpoint.Port switch
            {
                ArchitecturePort.Left => new(bounds.X, cy), ArchitecturePort.Right => new(bounds.X + bounds.Width, cy),
                ArchitecturePort.Top => new(cx, bounds.Y), ArchitecturePort.Bottom => new(cx, bounds.Y + bounds.Height),
                _ => throw new ArgumentOutOfRangeException(),
            };
        }
        void Arrow(string id, string owner, DiagramPoint tip, DiagramPoint toward)
        {
            var ax = toward.X - tip.X; var ay = toward.Y - tip.Y; var length = Math.Sqrt(ax * ax + ay * ay);
            if (length <= 0) return;
            b.Polygon(id, "ArchitectureConnection", owner, [tip, new(tip.X + ax / length * 10 - ay / length * 4, tip.Y + ay / length * 10 + ax / length * 4),
                new(tip.X + ax / length * 10 + ay / length * 4, tip.Y + ay / length * 10 - ax / length * 4)], b.Ink, b.Ink);
        }
        void DrawIcon(string prefix, string role, string owner, string? icon, DiagramRect r)
        {
            if (icon is null) { b.Box(prefix, role, owner, r, b.Background, b.Ink); return; }
            IReadOnlyList<IconShape> shapes = icon switch
            {
                "disk" => [new IconShape.Box(.12, .28, .76, .48, .08), new IconShape.Line(.12, .57, .88, .57), new IconShape.Ellipse(.74, .66, .035, .035, true)],
                "internet" => [new IconShape.Ellipse(.5, .5, .38, .38, false), new IconShape.Ellipse(.5, .5, .18, .38, false), new IconShape.Line(.12, .5, .88, .5), new IconShape.Line(.19, .29, .81, .29), new IconShape.Line(.19, .71, .81, .71)],
                "server" or "cloud" or "database" => IconGeometry.For(icon),
                _ => [],
            };
            if (shapes.Count == 0)
                throw new DataDiagramNotEditableException([new("architecture-icon-pack", owner, "The icon pack for '" + icon + "' is not available in the native renderer.", true)]);
            var index = 0;
            DiagramPoint P(double xx, double yy) => new(r.X + xx * r.Width, r.Y + yy * r.Height);
            foreach (var shape in shapes)
            {
                var id = prefix + "-" + index++;
                switch (shape)
                {
                    case IconShape.Box box:
                        b.Box(id, role, owner, new(r.X + box.X * r.Width, r.Y + box.Y * r.Height, box.W * r.Width, box.H * r.Height), null, b.Ink, 1.5); break;
                    case IconShape.Line line: b.Line(id, role, owner, [P(line.X1, line.Y1), P(line.X2, line.Y2)], b.Ink, 1.5); break;
                    case IconShape.Ellipse ellipse:
                        var points = Enumerable.Range(0, 49).Select(i => P(ellipse.CX + ellipse.RX * Math.Cos(i * Math.PI / 24), ellipse.CY + ellipse.RY * Math.Sin(i * Math.PI / 24))).ToArray();
                        if (ellipse.Filled) b.Polygon(id, role, owner, points, b.Ink, b.Ink); else b.Line(id, role, owner, points, b.Ink, 1.5);
                        break;
                    case IconShape.Polygon polygon: b.Polygon(id, role, owner, polygon.Points.Select(p => P(p.X, p.Y)).ToArray(), b.Background, b.Ink); break;
                    case IconShape.Polyline line: b.Line(id, role, owner, line.Points.Select(p => P(p.X, p.Y)).ToArray(), b.Ink, 1.5); break;
                }
            }
        }
    }
    private static bool Horizontal(ArchitecturePort port) => port is ArchitecturePort.Left or ArchitecturePort.Right;
    private static ArchitecturePort Opposite(ArchitecturePort port) => port switch
    {
        ArchitecturePort.Left => ArchitecturePort.Right, ArchitecturePort.Right => ArchitecturePort.Left,
        ArchitecturePort.Top => ArchitecturePort.Bottom, _ => ArchitecturePort.Top,
    };
    private static DiagramPoint Normal(ArchitecturePort port) => port switch
    {
        ArchitecturePort.Left => new(-1, 0), ArchitecturePort.Right => new(1, 0),
        ArchitecturePort.Top => new(0, -1), _ => new(0, 1),
    };
    private static IReadOnlyList<DiagramPoint> Route(DiagramPoint start, DiagramPoint end, ArchitecturePort from, ArchitecturePort to, bool self)
    {
        var a = Normal(from); var z = Normal(to);
        var first = new DiagramPoint(start.X + a.X * 24, start.Y + a.Y * 24);
        var last = new DiagramPoint(end.X + z.X * 24, end.Y + z.Y * 24);
        var points = new List<DiagramPoint> { start, first };
        if (self && start == end)
            points.AddRange([new(first.X + 40, first.Y), new(first.X + 40, first.Y - 40), new(first.X, first.Y - 40)]);
        else if (Horizontal(from) && Horizontal(to))
        {
            var middle = (first.X + last.X) / 2;
            points.Add(new(middle, first.Y)); points.Add(new(middle, last.Y));
        }
        else if (!Horizontal(from) && !Horizontal(to))
        {
            var middle = (first.Y + last.Y) / 2;
            points.Add(new(first.X, middle)); points.Add(new(last.X, middle));
        }
        else points.Add(Horizontal(from) ? new(last.X, first.Y) : new(first.X, last.Y));
        points.Add(last); points.Add(end);
        return points.Where((p, index) => index == 0 || p != points[index - 1]).ToArray();
    }
    private static double Number(ArchitectureDiagramDocument document, string key, double fallback) =>
        document.Configuration[key] is { } value ? ChartConfiguration.ParseNumber(ChartConfiguration.Raw(value)) : fallback;
    private static DiagramRect Bounds(IReadOnlyList<DiagramPoint> points) => new(points.Min(p => p.X), points.Min(p => p.Y), points.Max(p => p.X) - points.Min(p => p.X), points.Max(p => p.Y) - points.Min(p => p.Y));
    private static DiagramRect Union(DiagramRect a, DiagramRect b) => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
        Math.Max(a.X + a.Width, b.X + b.Width) - Math.Min(a.X, b.X), Math.Max(a.Y + a.Height, b.Y + b.Height) - Math.Min(a.Y, b.Y));

    private sealed class Axis
    {
        private readonly Dictionary<string, string> _parent;
        private readonly List<(string Before, string After)> _order = new();
        public Axis(IEnumerable<string> ids) => _parent = ids.ToDictionary(id => id, id => id);
        private string Root(string id) => _parent[id] == id ? id : _parent[id] = Root(_parent[id]);
        public void Union(string a, string b) => _parent[Root(b)] = Root(a);
        public bool Same(string a, string b) => Root(a) == Root(b);
        public void Before(string a, string b) => _order.Add((a, b));
        public Dictionary<string, int> Solve()
        {
            var ids = _parent.Keys.ToArray();
            var roots = ids.Select(Root).Distinct().ToArray();
            var values = roots.ToDictionary(id => id, _ => 0);
            var edges = _order.Select(e => (A: Root(e.Before), B: Root(e.After))).Distinct().ToArray();
            for (var iteration = 0; iteration <= roots.Length; iteration++)
            {
                var changed = false;
                foreach (var (a, b) in edges)
                    if (values[b] <= values[a]) { values[b] = values[a] + 1; changed = true; }
                if (!changed) return ids.ToDictionary(id => id, id => values[Root(id)]);
            }
            throw new DataDiagramNotEditableException([new("architecture-layout-conflict", "", "Port directions and alignment ordering contradict each other.", true)]);
        }
    }
}
