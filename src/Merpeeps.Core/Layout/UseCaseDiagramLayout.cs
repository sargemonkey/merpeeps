using Merpeeps.Core.Model;
using Merpeeps.Core.Parsing;

namespace Merpeeps.Core.Layout;

public sealed record UseCaseLayoutResult(DrawingScene Scene, IReadOnlyDictionary<string, DiagramRect> Nodes,
    IReadOnlyDictionary<string, DiagramRect> Boundaries, IReadOnlyDictionary<string, IReadOnlyList<DiagramPoint>> Routes,
    IReadOnlyList<DataDiagramDiagnostic> Diagnostics);
public static class UseCaseDiagramLayout
{
    public static UseCaseLayoutResult Create(DiagramModel model, DrawingTextMeasure? measure = null, string? themeOverride = null)
    {
        var d = model.UseCase ?? throw new ArgumentException("Use-case content is missing."); UseCaseValidation.Check(d);
        var b = new DataDiagramLayouts.Builder(measure, d.ThemeVariables, d.GlobalConfiguration, themeOverride ?? model.Theme);
        var graph = new DiagramModel { Direction = d.Direction };
        var styles = new Dictionary<string, ClassResolvedStyle>(); var rows = new Dictionary<string, IReadOnlyList<UseCaseJsonRow>>();
        var diagnostics = new List<DataDiagramDiagnostic>();
        foreach (var node in d.Nodes)
        {
            var style = UseCaseValidation.Appearance(d, node.Id, node.Classes, node.Style); styles[node.Id] = style;
            var text = node.Stereotype is null ? node.Label.Text : "<<" + node.Stereotype + ">>\n" + node.Label.Text;
            var size = b.MeasureWrapped(text, style.FontSize, 240, style.Bold, style.Italic, node.Label.Markdown, style.FontFamily);
            var width = Math.Max(100, size.Width + 40); var height = Math.Max(55, size.Height + 30);
            if (node.Kind == UseCaseNodeKind.Actor) { width = Math.Max(70, size.Width + 20); height = 85 + size.Height; }
            if (node.Kind == UseCaseNodeKind.UseCase && node.Shape == UseCaseShape.Ellipse) { width *= 1.35; height *= 1.35; }
            if (node.Kind == UseCaseNodeKind.JsonTable)
            {
                var data = UseCaseJson.Rows(node.Data!); rows[node.Id] = data;
                var keyWidth = data.Select(r => b.MeasureWrapped(r.Path, style.FontSize, double.MaxValue, family: style.FontFamily).Width).DefaultIfEmpty(0).Max();
                var valueWidth = data.Select(r => b.MeasureWrapped(r.Value, style.FontSize, double.MaxValue, family: style.FontFamily).Width).DefaultIfEmpty(0).Max();
                width = Math.Max(width, keyWidth + valueWidth + 44); height = (data.Count + 1) * (style.FontSize * 1.4 + 12);
            }
            graph.Nodes.Add(new DiagramNode { Id = node.Id, Width = width, Height = height });
        }
        foreach (var edge in d.Relationships)
        {
            var from = edge.From;
            for (var i = 1; i < edge.MinimumLength; i++)
            {
                var id = "$layout-" + edge.Id + "-" + i;
                graph.Nodes.Add(new DiagramNode { Id = id, Width = 1, Height = 1 });
                graph.Edges.Add(new DiagramEdge { From = from, To = id }); from = id;
            }
            graph.Edges.Add(new DiagramEdge { From = from, To = edge.To });
        }
        graph.Edges.AddRange(d.Nodes.Where(n => n.Kind == UseCaseNodeKind.Note).Select(n => new DiagramEdge { From = n.NoteTargetId!, To = n.Id }));
        var rankSpacing = d.Configuration["rankSpacing"] is { } rank ? ChartConfiguration.ParseNumber(rank.ToString()) : 50;
        var nodeSpacing = d.Configuration["nodeSpacing"] is { } spacing ? ChartConfiguration.ParseNumber(spacing.ToString()) : 50;
        LayeredLayout.Apply(graph, new(NodeSpacing: nodeSpacing, LayerSpacing: rankSpacing));
        var bounds = d.Nodes.ToDictionary(n => n.Id, n =>
        {
            var node = graph.Node(n.Id)!;
            return new DiagramRect(node.X, node.Y, node.Width, node.Height);
        });
        var boundaryBounds = new Dictionary<string, DiagramRect>();
        foreach (var boundary in d.Boundaries)
        {
            var members = d.Nodes.Where(n => n.BoundaryId == boundary.Id).Select(n => bounds[n.Id]).ToArray();
            var r = members.Length == 0 ? new DiagramRect(20 + boundaryBounds.Count * 250, 20, 220, 160) : members.Aggregate(Union);
            r = new(r.X - 24, r.Y - 50, r.Width + 48, r.Height + 74); boundaryBounds[boundary.Id] = r;
            var style = UseCaseValidation.Appearance(d, boundary.Id, boundary.Classes, boundary.Style);
            var fill = style.Fill ?? b.Tint(b.Scale(d.Boundaries.IndexOf(boundary)));
            b.Box(boundary.Id, "UseCaseBoundary", boundary.Id, r, fill with { Alpha = style.Fill?.Alpha ?? 70 }, style.Stroke ?? b.Ink, style.StrokeWidth);
            if (boundary.Package)
            {
                var title = b.MeasureWrapped(boundary.Title.Text, style.FontSize, r.Width - 20, true, markdown: boundary.Title.Markdown, family: style.FontFamily);
                b.Box(boundary.Id + "-tab", "UseCaseBoundary", boundary.Id, new(r.X, r.Y - title.Height - 12, Math.Min(r.Width, title.Width + 24), title.Height + 12), fill, style.Stroke ?? b.Ink);
                b.Text(boundary.Id + "-title", "UseCaseBoundary", boundary.Id, boundary.Title.Text, style.FontSize, r.X + 10, r.Y - title.Height - 6, r.Width - 20, true, style.TextColor, markdown: boundary.Title.Markdown, family: style.FontFamily);
            }
            else b.Text(boundary.Id + "-title", "UseCaseBoundary", boundary.Id, boundary.Title.Text, style.FontSize, r.X + 12, r.Y + 8, r.Width - 24, true, style.TextColor, markdown: boundary.Title.Markdown, family: style.FontFamily);
            b.Hit("UseCaseBoundary", boundary.Id, r);
        }
        var routes = new Dictionary<string, IReadOnlyList<DiagramPoint>>();
        foreach (var edge in d.Relationships)
        {
            var from = d.Nodes.Single(n => n.Id == edge.From); var to = d.Nodes.Single(n => n.Id == edge.To);
            var start = Anchor(from, bounds[from.Id], Center(bounds[to.Id])); var end = Anchor(to, bounds[to.Id], Center(bounds[from.Id]));
            var points = from.Id == to.Id ? new[] { start, new DiagramPoint(start.X + 40, start.Y), new(start.X + 40, bounds[from.Id].Y - 35),
                new(bounds[from.Id].X + bounds[from.Id].Width / 2, bounds[from.Id].Y - 35), new(bounds[from.Id].X + bounds[from.Id].Width / 2, bounds[from.Id].Y) } : new[] { start, end };
            routes[edge.Id] = points;
            var style = UseCaseValidation.Appearance(d, edge.Id, edge.Classes, edge.Style);
            var stroke = style.Stroke ?? (edge.Kind == UseCaseRelationshipKind.Include ? Theme("usecaseIncludeLine", b.Scale(0)) :
                edge.Kind == UseCaseRelationshipKind.Extend ? Theme("usecaseExtendLine", b.Scale(2)) : b.Ink);
            b.Line(edge.Id, "UseCaseRelationship", edge.Id, points, stroke, style.StrokeWidth);
            b.HitLine("UseCaseRelationship", edge.Id, points);
            Marker(edge.Id + "-from", edge.Id, edge.FromEnd, points[0], points[1], stroke);
            Marker(edge.Id + "-to", edge.Id, edge.Kind == UseCaseRelationshipKind.Generalization ? UseCaseEnd.Triangle : edge.ToEnd, points[^1], points[^2], stroke);
            var label = edge.Kind is UseCaseRelationshipKind.Include or UseCaseRelationshipKind.Extend ? "<<" + edge.Kind.ToString().ToLowerInvariant() + ">>" : edge.Label.Text;
            if (label.Length > 0)
            {
                var measuredLabel = b.MeasureWrapped(label, style.FontSize, 240, style.Bold, style.Italic, edge.Label.Markdown, style.FontFamily);
                var horizontal = Math.Abs(start.X - end.X) >= Math.Abs(start.Y - end.Y);
                var labelX = horizontal ? (start.X + end.X - measuredLabel.Width) / 2 : Math.Max(bounds[from.Id].X + bounds[from.Id].Width, bounds[to.Id].X + bounds[to.Id].Width) + 10;
                var labelY = horizontal ? Math.Min(bounds[from.Id].Y, bounds[to.Id].Y) - measuredLabel.Height - 8 : (start.Y + end.Y - measuredLabel.Height) / 2;
                b.Text(edge.Id + "-label", "UseCaseRelationship", edge.Id, label, style.FontSize, labelX, labelY, 240, style.Bold, style.TextColor, style.Italic, edge.Label.Markdown, style.FontFamily);
            }
            if (edge.Animated) diagnostics.Add(new("usecase-animation", edge.Id, "Animation is preserved in source; native canvas and editable export show its static relationship."));
        }
        foreach (var note in d.Nodes.Where(n => n.Kind == UseCaseNodeKind.Note))
        {
            var a = Center(bounds[note.NoteTargetId!]); var z = Center(bounds[note.Id]);
            b.Line(note.Id + "-attachment", "UseCaseNode", note.Id, [a, z], b.Ink, 1);
        }
        foreach (var node in d.Nodes)
        {
            var r = bounds[node.Id]; var style = styles[node.Id];
            var rotated = d.Configuration["colorScheme"]?.ToString() == "rotate";
            var index = node.Kind == UseCaseNodeKind.Actor ? d.Nodes.Where(n => n.Kind == UseCaseNodeKind.Actor).ToList().IndexOf(node)
                : d.Nodes.Count(n => n.Kind == UseCaseNodeKind.Actor) + d.Nodes.Where(n => n.Kind == UseCaseNodeKind.UseCase).ToList().IndexOf(node);
            var fill = style.Fill ?? (node.Kind == UseCaseNodeKind.Note ? Theme("noteBkgColor", new(255, 247, 205))
                : rotated && node.Kind is UseCaseNodeKind.Actor or UseCaseNodeKind.UseCase ? b.Tint(b.Scale(index))
                : node.Kind == UseCaseNodeKind.Actor ? Theme("usecaseActorBkg", b.Tint(b.Scale(0))) : Theme("usecaseBkg", b.Tint(b.Scale(1))));
            var stroke = style.Stroke ?? Theme(node.Kind == UseCaseNodeKind.Actor ? "usecaseActorBorder" : "usecaseBorder", b.Ink);
            if (node.Kind == UseCaseNodeKind.Actor)
            {
                var x = r.X + r.Width / 2; var y = r.Y;
                if (node.ActorKind == UseCaseActorKind.Icon)
                {
                    // No implicit icon-pack installation or network fetch is performed.
                    b.Box(node.Id, "UseCaseNode", node.Id, new(x - 24, y, 48, 60), fill, stroke);
                    b.Text(node.Id + "-missing-icon", "UseCaseNode", node.Id, "?", 32, x - 9, y + 8, 40, true, stroke);
                    diagnostics.Add(new("usecase-icon", node.Id, "Icon '" + node.Icon + "' is not registered in this native renderer; the visible unknown-icon fallback is shown."));
                }
                else
                {
                    b.Circle(node.Id + "-head", "UseCaseNode", node.Id, new(x, y + 12), 12, node.ActorKind == UseCaseActorKind.Normal ? fill : b.Background, stroke);
                    if (node.ActorKind == UseCaseActorKind.Awesome)
                        b.Polygon(node.Id, "UseCaseNode", node.Id, [new(x - 24, y + 65), new(x - 18, y + 30), new(x + 18, y + 30), new(x + 24, y + 65)], fill, stroke);
                    else
                    {
                        b.Line(node.Id, "UseCaseNode", node.Id, [new(x, y + 24), new(x, y + 50), new(x - 19, y + 72)], stroke, node.ActorKind == UseCaseActorKind.Hollow ? 4 : 2);
                        b.Line(node.Id + "-leg", "UseCaseNode", node.Id, [new(x, y + 50), new(x + 19, y + 72)], stroke, 2);
                        b.Line(node.Id + "-arms", "UseCaseNode", node.Id, [new(x - 25, y + 34), new(x + 25, y + 34)], stroke, 2);
                    }
                    if (node.Business) b.Line(node.Id + "-business", "UseCaseNode", node.Id, [new(x - 20, y + 52), new(x + 20, y + 26)], stroke, 2);
                }
                Text(node, r.X, y + 80, r.Width, style);
            }
            else if (node.Kind == UseCaseNodeKind.UseCase && node.Shape == UseCaseShape.Ellipse)
            {
                var ellipse = Enumerable.Range(0, 73).Select(i => new DiagramPoint(r.X + r.Width / 2 + r.Width / 2 * Math.Cos(i * Math.PI / 36), r.Y + r.Height / 2 + r.Height / 2 * Math.Sin(i * Math.PI / 36))).ToArray();
                b.Polygon(node.Id, "UseCaseNode", node.Id, ellipse, fill, stroke);
                if (node.Business) b.Line(node.Id + "-business", "UseCaseNode", node.Id, [new(r.X + r.Width * .75, r.Y + r.Height * .78), new(r.X + r.Width * .92, r.Y + r.Height * .28)], stroke, 2);
                Text(node, r.X + 20, r.Y + 15, r.Width - 40, style);
            }
            else
            {
                b.Box(node.Id, "UseCaseNode", node.Id, r, fill, stroke, style.StrokeWidth);
                if (node.Kind == UseCaseNodeKind.JsonTable)
                {
                    var data = rows[node.Id]; var rowHeight = style.FontSize * 1.4 + 12;
                    var keyWidth = data.Select(row => b.MeasureWrapped(row.Path, style.FontSize, double.MaxValue, family: style.FontFamily).Width).DefaultIfEmpty(0).Max() + 20;
                    b.Text(node.Id + "-title", "UseCaseNode", node.Id, node.SourceId, style.FontSize, r.X + 10, r.Y + 6, r.Width - 20, true, style.TextColor, family: style.FontFamily);
                    for (var i = 0; i < data.Count; i++)
                    {
                        var y = r.Y + (i + 1) * rowHeight;
                        b.Line(node.Id + "-line-" + i, "UseCaseNode", node.Id, [new(r.X, y), new(r.X + r.Width, y)], stroke, 1);
                        b.Text(node.Id + "-key-" + i, "UseCaseNode", node.Id, data[i].Path, style.FontSize, r.X + 8, y + 6, keyWidth, color: style.TextColor, family: style.FontFamily);
                        b.Text(node.Id + "-value-" + i, "UseCaseNode", node.Id, data[i].Value, style.FontSize, r.X + keyWidth + 8, y + 6, r.Width - keyWidth - 12, color: style.TextColor, family: style.FontFamily);
                    }
                    b.Line(node.Id + "-divider", "UseCaseNode", node.Id, [new(r.X + keyWidth, r.Y + rowHeight), new(r.X + keyWidth, r.Y + r.Height)], stroke, 1);
                }
                else Text(node, r.X + 15, r.Y + 12, r.Width - 30, style);
            }
            b.Hit("UseCaseNode", node.Id, r, node.BoundaryId);
        }
        var scene = b.Finish(); var offset = b.OriginOffset;
        var styled = scene.Figures.Select(f =>
        {
            var node = d.Nodes.FirstOrDefault(n => n.Id == f.Owner.Id);
            var edge = d.Relationships.FirstOrDefault(e => e.Id == f.Owner.Id);
            if (node is not null) return f with { StrokeWidth = styles[node.Id].StrokeWidth, DashPattern = styles[node.Id].DashPattern.Count > 0 ? styles[node.Id].DashPattern : null,
                Dashed = f.Id.EndsWith("-attachment", StringComparison.Ordinal) };
            if (edge is null) return f;
            var style = UseCaseValidation.Appearance(d, edge.Id, edge.Classes, edge.Style);
            return f with { DashPattern = style.DashPattern.Count > 0 ? style.DashPattern : null,
                Dashed = f.Id == edge.Id && edge.Kind is UseCaseRelationshipKind.Include or UseCaseRelationshipKind.Extend };
        }).ToArray();
        return new(scene with { Figures = styled }, Shift(bounds), Shift(boundaryBounds),
            routes.ToDictionary(p => p.Key, p => (IReadOnlyList<DiagramPoint>)p.Value.Select(v => new DiagramPoint(v.X + offset.X, v.Y + offset.Y)).ToArray()), diagnostics);
        Dictionary<string, DiagramRect> Shift(Dictionary<string, DiagramRect> values) => values.ToDictionary(p => p.Key, p => p.Value with { X = p.Value.X + offset.X, Y = p.Value.Y + offset.Y });
        ClassColor Theme(string key, ClassColor fallback) => d.ThemeVariables[key] is { } value ? SequenceColors.TryParse(value.ToString(), out var color) ? color : throw new ArgumentException("Invalid use-case theme color.") : fallback;
        void Text(UseCaseNode node, double x, double y, double width, ClassResolvedStyle style)
        {
            var text = node.Stereotype is null ? node.Label.Text : "<<" + node.Stereotype + ">>\n" + node.Label.Text;
            b.Text(node.Id + "-label", "UseCaseNode", node.Id, text, style.FontSize, x, y, width, style.Bold, style.TextColor, style.Italic, node.Label.Markdown, style.FontFamily, "center");
        }
        void Marker(string id, string owner, UseCaseEnd marker, DiagramPoint tip, DiagramPoint toward, ClassColor color)
        {
            var dx = toward.X - tip.X; var dy = toward.Y - tip.Y; var length = Math.Sqrt(dx * dx + dy * dy);
            if (length == 0 || marker == UseCaseEnd.None) return;
            DiagramPoint At(double along, double side) => new(tip.X + dx / length * along - dy / length * side, tip.Y + dy / length * along + dx / length * side);
            if (marker is UseCaseEnd.Arrow or UseCaseEnd.Triangle) b.Polygon(id, "UseCaseRelationship", owner, [tip, At(12, 5), At(12, -5)], marker == UseCaseEnd.Triangle ? b.Background : color, color);
            else if (marker == UseCaseEnd.Circle) b.Circle(id, "UseCaseRelationship", owner, tip, 5, b.Background, color);
            else { b.Line(id + "-a", "UseCaseRelationship", owner, [At(-5, -5), At(5, 5)], color, 2); b.Line(id + "-b", "UseCaseRelationship", owner, [At(-5, 5), At(5, -5)], color, 2); }
        }
    }
    private static DiagramPoint Center(DiagramRect r) => new(r.X + r.Width / 2, r.Y + r.Height / 2);
    private static DiagramPoint Anchor(UseCaseNode node, DiagramRect r, DiagramPoint toward)
    {
        var center = Center(r); var dx = toward.X - center.X; var dy = toward.Y - center.Y;
        if (dx == 0 && dy == 0) return new(r.X + r.Width, center.Y);
        var factor = node.Kind == UseCaseNodeKind.UseCase && node.Shape == UseCaseShape.Ellipse
            ? 1 / Math.Sqrt(dx * dx / Math.Pow(r.Width / 2, 2) + dy * dy / Math.Pow(r.Height / 2, 2))
            : Math.Min(dx == 0 ? double.MaxValue : r.Width / 2 / Math.Abs(dx), dy == 0 ? double.MaxValue : r.Height / 2 / Math.Abs(dy));
        return new(center.X + dx * factor, center.Y + dy * factor);
    }
    private static DiagramRect Union(DiagramRect a, DiagramRect b) => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
        Math.Max(a.X + a.Width, b.X + b.Width) - Math.Min(a.X, b.X), Math.Max(a.Y + a.Height, b.Y + b.Height) - Math.Min(a.Y, b.Y));
}
