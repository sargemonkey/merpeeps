using Merpeeps.Core.Model;
using Merpeeps.Core.Parsing;

namespace Merpeeps.Core.Layout;

public sealed record BlockPlacement(string Id, string? ParentId, int Row, int Column, int Span, DiagramRect Bounds);
public sealed record BlockDiagramLayoutResult(DrawingScene Scene, IReadOnlyDictionary<string, BlockPlacement> Placements);
public static class BlockDiagramLayout
{
    private const double Gap = 24;
    private const double Padding = 16;
    private sealed record Grid(int Columns, IReadOnlyList<(BlockItem Item, int Row, int Column)> Cells, double[] Widths, double[] Heights);
    public static BlockDiagramLayoutResult Create(DiagramModel model, DrawingTextMeasure? measure = null, string? themeOverride = null)
    {
        var document = model.Blocks ?? throw new ArgumentException("Block content is missing.");
        BlockDiagramAdapter.Check(document);
        var builder = new DataDiagramLayouts.Builder(measure, document.ThemeVariables, document.GlobalConfiguration, themeOverride ?? model.Theme);
        var grids = new Dictionary<string, Grid>();
        var sizes = new Dictionary<string, DrawingTextSize>();
        var appearances = document.AllItems.ToDictionary(n => n.Id, n => BlockDiagramValidation.ResolveStyle(document, n));
        var placements = new Dictionary<string, BlockPlacement>();
        Grid MeasureGrid(IReadOnlyList<BlockItem> items, int? configured)
        {
            var columns = configured ?? Math.Max(1, items.Sum(n => n.Span));
            var cells = new List<(BlockItem, int, int)>();
            var row = 0; var column = 0;
            foreach (var item in items)
            {
                if (item.Span > columns) throw new DataDiagramNotEditableException([new("block-span", item.Id, "Block span exceeds this grid's column count.", true)]);
                if (column + item.Span > columns) { row++; column = 0; }
                cells.Add((item, row, column)); column += item.Span;
                if (column == columns) { row++; column = 0; }
                var style = appearances[item.Id];
                var text = builder.MeasureWrapped(item.Display, style.FontSize, double.MaxValue, style.Bold, style.Italic, item.Markdown, style.FontFamily);
                var width = Math.Max(80, text.Width + Padding * 2);
                var height = Math.Max(48, text.Height + Padding * 2);
                if (item.Kind == BlockItemKind.Space) { width = 50 * item.Span; height = 48; }
                else if (item.Kind == BlockItemKind.Composite)
                {
                    var child = MeasureGrid(item.Children, item.Columns); grids[item.Id] = child;
                    width = Math.Max(width, child.Widths.Sum() + Gap * Math.Max(0, child.Columns - 1) + Padding * 2);
                    height = child.Heights.Sum() + Gap * Math.Max(0, child.Heights.Length - 1) + Padding * 2 + (item.Label is null ? 0 : text.Height + 8);
                }
                else if (item.Kind == BlockItemKind.Arrow) { width = Math.Max(width * 2, 100); height = Math.Max(height * 2, 80); }
                else if (item.Shape is NodeShape.Circle or NodeShape.DoubleCircle) width = height = Math.Max(width, height);
                else if (item.Shape == NodeShape.Rhombus) { width *= 2; height *= 2; }
                else if (item.Shape is NodeShape.Hexagon or NodeShape.Parallelogram or NodeShape.ParallelogramAlt or NodeShape.Trapezoid or NodeShape.TrapezoidAlt or NodeShape.Asymmetric) width += height * .7;
                sizes[item.Id] = new(width, height);
            }
            var widths = Enumerable.Repeat(50d, columns).ToArray();
            var heights = new double[cells.Count == 0 ? 0 : cells.Max(c => c.Item2) + 1];
            foreach (var (item, r, c) in cells)
            {
                var size = sizes[item.Id]; heights[r] = Math.Max(heights[r], size.Height);
                var existing = widths.Skip(c).Take(item.Span).Sum() + Gap * (item.Span - 1);
                if (size.Width > existing)
                    for (var i = c; i < c + item.Span; i++) widths[i] += (size.Width - existing) / item.Span;
            }
            return new(columns, cells, widths, heights);
        }
        var root = MeasureGrid(document.Items, document.Columns);
        void Place(Grid grid, double x, double y, string? parent, double? availableWidth = null)
        {
            var extra = availableWidth is { } available ? Math.Max(0, available - grid.Widths.Sum() - Gap * Math.Max(0, grid.Columns - 1)) / grid.Columns : 0;
            foreach (var (item, row, column) in grid.Cells)
            {
                var style = appearances[item.Id];
                var left = x + grid.Widths.Take(column).Sum() + (Gap + extra) * column;
                var top = y + grid.Heights.Take(row).Sum() + Gap * row;
                var width = grid.Widths.Skip(column).Take(item.Span).Sum() + extra * item.Span + Gap * (item.Span - 1);
                var height = grid.Heights[row];
                var bounds = new DiagramRect(left, top, width, height);
                placements[item.Id] = new(item.Id, parent, row, column, item.Span, bounds);
                builder.Hit("Block", item.Id, bounds, parent);
                if (item.Kind == BlockItemKind.Space) continue;
                var fill = style.Fill ?? builder.Tint(builder.Scale(0)); var stroke = style.Stroke ?? builder.Ink;
                if (item.Kind == BlockItemKind.Composite)
                {
                    builder.Box(item.Id, "Block", item.Id, bounds, builder.Background, stroke, style.StrokeWidth, style.DashPattern.Count > 0);
                    var header = item.Label is null ? 0 : builder.MeasureWrapped(item.Label, style.FontSize, width - Padding * 2, style.Bold, style.Italic, item.Markdown, style.FontFamily).Height + 8;
                    if (header > 0) builder.Text(item.Id + "-label", "Block", item.Id, item.Display, style.FontSize, left + Padding, top + Padding, width - Padding * 2,
                        style.Bold, style.TextColor, style.Italic, item.Markdown, style.FontFamily);
                    Place(grids[item.Id], left + Padding, top + Padding + header, item.Id, width - Padding * 2);
                    continue;
                }
                Shape(builder, item, bounds, fill, stroke, style.StrokeWidth);
                var labelWidth = item.Kind == BlockItemKind.Arrow || item.Shape == NodeShape.Rhombus ? width * .45 : width - Padding * 2;
                var label = builder.MeasureWrapped(item.Display, style.FontSize, labelWidth, style.Bold, style.Italic, item.Markdown, style.FontFamily);
                builder.Text(item.Id + "-label", "Block", item.Id, item.Display, style.FontSize, left + (width - label.Width) / 2, top + (height - label.Height) / 2,
                    labelWidth, style.Bold, style.TextColor, style.Italic, item.Markdown, style.FontFamily, "center");
            }
        }
        Place(root, 20, 20, null);
        foreach (var edge in document.Connections)
        {
            if (edge.Stroke == BlockLinkStroke.Invisible) continue;
            var a = placements[edge.From].Bounds; var z = placements[edge.To].Bounds;
            var start = Anchor(document.AllItems.Single(n => n.Id == edge.From), a, new(z.X + z.Width / 2, z.Y + z.Height / 2));
            var end = Anchor(document.AllItems.Single(n => n.Id == edge.To), z, new(a.X + a.Width / 2, a.Y + a.Height / 2));
            var peers = document.Connections.Where(e => e.From == edge.From && e.To == edge.To).ToArray();
            var offset = (Array.IndexOf(peers, edge) - (peers.Length - 1) / 2d) * 14;
            DiagramPoint[] route;
            if (edge.From == edge.To)
                route = [new(a.X + a.Width, a.Y + a.Height / 3), new(a.X + a.Width + 40 + Math.Abs(offset), a.Y + a.Height / 3),
                    new(a.X + a.Width + 40 + Math.Abs(offset), a.Y - 32), new(a.X + a.Width / 2, a.Y - 32), new(a.X + a.Width / 2, a.Y)];
            else if (offset == 0) route = [start, end];
            else
            {
                var dx = end.X - start.X; var dy = end.Y - start.Y; var length = Math.Sqrt(dx * dx + dy * dy);
                route = length == 0 ? [start, end] : [start, new((start.X + end.X) / 2 - dy / length * offset, (start.Y + end.Y) / 2 + dx / length * offset), end];
            }
            var thickness = edge.Stroke == BlockLinkStroke.Thick ? 3 : 1.5;
            builder.Line(edge.Id, "BlockConnection", edge.Id, route, builder.Ink, thickness);
            Marker(builder, edge.Id + "-start", edge.Id, edge.FromEnd, route[0], route[1], builder.Ink);
            Marker(builder, edge.Id + "-end", edge.Id, edge.ToEnd, route[^1], route[^2], builder.Ink);
            if (edge.Label.Length > 0)
            {
                var middle = route[route.Length / 2];
                if (route.Length == 2) middle = new((route[0].X + route[1].X) / 2, (route[0].Y + route[1].Y) / 2);
                var measured = builder.MeasureWrapped(edge.Label, 12, 200);
                builder.Text(edge.Id + "-label", "BlockConnection", edge.Id, edge.Label, 12, middle.X - measured.Width / 2, middle.Y - measured.Height - 4, 200);
            }
        }
        var scene = builder.Finish();
        var delta = builder.OriginOffset;
        var final = placements.ToDictionary(p => p.Key, p => p.Value with { Bounds = p.Value.Bounds with { X = p.Value.Bounds.X + delta.X, Y = p.Value.Bounds.Y + delta.Y } });
        var edgeOwners = document.Connections.ToDictionary(e => e.Id);
        var figures = scene.Figures.Select(f => appearances.TryGetValue(f.Owner.Id, out var style)
                ? f with { StrokeWidth = style.StrokeWidth, DashPattern = style.DashPattern.Count > 0 ? style.DashPattern.ToArray() : null }
                : edgeOwners.TryGetValue(f.Owner.Id, out var edge) && edge.Stroke == BlockLinkStroke.Dotted && f.Id == edge.Id
                    ? f with { Dashed = true } : f).ToArray();
        var edgeRegions = figures.Where(f => f.Kind == DrawingFigureKind.Polyline && edgeOwners.ContainsKey(f.Id))
            .Select(f => new DrawingRegion(f.Owner, DrawingHitKind.Polyline, f.Bounds, f.Points)).ToArray();
        return new(scene with { Figures = figures, Regions = scene.Regions.Concat(edgeRegions).ToArray() }, final);
    }
    private static DiagramPoint Anchor(BlockItem item, DiagramRect rect, DiagramPoint toward)
    {
        var center = new DiagramPoint(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
        var dx = toward.X - center.X; var dy = toward.Y - center.Y;
        if (dx == 0 && dy == 0) return center;
        var scale = item.Kind == BlockItemKind.Shape && item.Shape is NodeShape.Circle or NodeShape.DoubleCircle
            ? Math.Min(rect.Width, rect.Height) / 2 / Math.Sqrt(dx * dx + dy * dy)
            : item.Kind == BlockItemKind.Shape && item.Shape == NodeShape.Rhombus
                ? 1 / (Math.Abs(dx) / (rect.Width / 2) + Math.Abs(dy) / (rect.Height / 2))
                : Math.Min(dx == 0 ? double.MaxValue : rect.Width / 2 / Math.Abs(dx), dy == 0 ? double.MaxValue : rect.Height / 2 / Math.Abs(dy));
        return new(center.X + dx * scale, center.Y + dy * scale);
    }
    private static void Marker(DataDiagramLayouts.Builder b, string id, string owner, BlockLinkEnd end, DiagramPoint tip, DiagramPoint toward, ClassColor color)
    {
        var dx = toward.X - tip.X; var dy = toward.Y - tip.Y; var length = Math.Sqrt(dx * dx + dy * dy);
        if (length == 0 || end == BlockLinkEnd.None) return;
        dx /= length; dy /= length;
        DiagramPoint At(double along, double side) => new(tip.X + dx * along - dy * side, tip.Y + dy * along + dx * side);
        if (end == BlockLinkEnd.Arrow) b.Polygon(id, "BlockConnection", owner, [tip, At(10, 4), At(10, -4)], color, color);
        else if (end == BlockLinkEnd.Circle) b.Circle(id, "BlockConnection", owner, tip, 4, b.Background, color);
        else
        {
            b.Line(id + "-a", "BlockConnection", owner, [At(-4, -4), At(4, 4)], color, 1.5);
            b.Line(id + "-b", "BlockConnection", owner, [At(-4, 4), At(4, -4)], color, 1.5);
        }
    }
    internal static void Shape(DataDiagramLayouts.Builder b, BlockItem item, DiagramRect r, ClassColor fill, ClassColor stroke, double width)
    {
        var x = r.X; var y = r.Y; var w = r.Width; var h = r.Height;
        if (item.Kind == BlockItemKind.Arrow)
        {
            var points = Arrow(r, item.Arrows); b.Polygon(item.Id, "Block", item.Id, points, fill, stroke); return;
        }
        switch (item.Shape)
        {
            case NodeShape.Circle:
            case NodeShape.DoubleCircle:
                b.Circle(item.Id, "Block", item.Id, new(x + w / 2, y + h / 2), Math.Min(w, h) / 2, fill, stroke);
                if (item.Shape == NodeShape.DoubleCircle) b.Circle(item.Id + "-inner", "Block", item.Id, new(x + w / 2, y + h / 2), Math.Max(1, Math.Min(w, h) / 2 - 6), null, stroke);
                return;
            case NodeShape.RoundedRect:
            case NodeShape.Stadium:
                b.Path(item.Id, "Block", item.Id, Rounded(r, item.Shape == NodeShape.Stadium ? h / 2 : 8), fill, stroke); return;
            case NodeShape.Cylinder:
                var ry = Math.Min(12, h / 5); const double k = .5522847498;
                b.Path(item.Id, "Block", item.Id, [
                    new(DrawingPathKind.Move, new(x, y + ry)),
                    new(DrawingPathKind.Cubic, new(x + w, y + ry), new(x, y - ry * .33), new(x + w, y - ry * .33)),
                    new(DrawingPathKind.Line, new(x + w, y + h - ry)),
                    new(DrawingPathKind.Cubic, new(x, y + h - ry), new(x + w, y + h + ry * .33), new(x, y + h + ry * .33)),
                    new(DrawingPathKind.Close, default)], fill, stroke);
                b.Path(item.Id + "-top", "Block", item.Id, [
                    new(DrawingPathKind.Move, new(x, y + ry)),
                    new(DrawingPathKind.Cubic, new(x + w / 2, y + ry * 2), new(x, y + ry + ry * k), new(x + w / 2 - w / 2 * k, y + ry * 2)),
                    new(DrawingPathKind.Cubic, new(x + w, y + ry), new(x + w / 2 + w / 2 * k, y + ry * 2), new(x + w, y + ry + ry * k))], null, stroke);
                return;
            case NodeShape.Subroutine:
                b.Box(item.Id, "Block", item.Id, r, fill, stroke, width);
                b.Line(item.Id + "-left", "Block", item.Id, [new(x + 8, y), new(x + 8, y + h)], stroke, width);
                b.Line(item.Id + "-right", "Block", item.Id, [new(x + w - 8, y), new(x + w - 8, y + h)], stroke, width); return;
        }
        DiagramPoint[]? polygon = item.Shape switch
        {
            NodeShape.Rhombus => [new(x + w / 2, y), new(x + w, y + h / 2), new(x + w / 2, y + h), new(x, y + h / 2)],
            NodeShape.Hexagon => [new(x + h / 4, y), new(x + w - h / 4, y), new(x + w, y + h / 2), new(x + w - h / 4, y + h), new(x + h / 4, y + h), new(x, y + h / 2)],
            NodeShape.Parallelogram => [new(x + h / 4, y), new(x + w, y), new(x + w - h / 4, y + h), new(x, y + h)],
            NodeShape.ParallelogramAlt => [new(x, y), new(x + w - h / 4, y), new(x + w, y + h), new(x + h / 4, y + h)],
            NodeShape.Trapezoid => [new(x + h / 4, y), new(x + w - h / 4, y), new(x + w, y + h), new(x, y + h)],
            NodeShape.TrapezoidAlt => [new(x, y), new(x + w, y), new(x + w - h / 4, y + h), new(x + h / 4, y + h)],
            NodeShape.Asymmetric => [new(x, y), new(x + w - h / 3, y), new(x + w, y + h / 2), new(x + w - h / 3, y + h), new(x, y + h)],
            _ => null,
        };
        if (polygon is null) b.Box(item.Id, "Block", item.Id, r, fill, stroke, width);
        else b.Polygon(item.Id, "Block", item.Id, polygon, fill, stroke);
    }
    private static IReadOnlyList<DrawingPathPart> Rounded(DiagramRect r, double radius)
    {
        var x = r.X; var y = r.Y; var right = x + r.Width; var bottom = y + r.Height;
        var a = Math.Min(radius, Math.Min(r.Width, r.Height) / 2); const double k = .5522847498;
        return [
            new(DrawingPathKind.Move, new(x + a, y)), new(DrawingPathKind.Line, new(right - a, y)),
            new(DrawingPathKind.Cubic, new(right, y + a), new(right - a + k * a, y), new(right, y + a - k * a)),
            new(DrawingPathKind.Line, new(right, bottom - a)),
            new(DrawingPathKind.Cubic, new(right - a, bottom), new(right, bottom - a + k * a), new(right - a + k * a, bottom)),
            new(DrawingPathKind.Line, new(x + a, bottom)),
            new(DrawingPathKind.Cubic, new(x, bottom - a), new(x + a - k * a, bottom), new(x, bottom - a + k * a)),
            new(DrawingPathKind.Line, new(x, y + a)),
            new(DrawingPathKind.Cubic, new(x + a, y), new(x, y + a - k * a), new(x + a - k * a, y)),
            new(DrawingPathKind.Close, default)];
    }
    private static IReadOnlyList<DiagramPoint> Arrow(DiagramRect r, BlockArrowDirections directions)
    {
        var p = new List<DiagramPoint>();
        void Point(double x, double y) => p.Add(new(r.X + x * r.Width, r.Y + y * r.Height));
        Point(.3, .3);
        if (directions.HasFlag(BlockArrowDirections.Up)) { Point(.3, .2); Point(.18, .2); Point(.5, 0); Point(.82, .2); Point(.7, .2); }
        Point(.7, .3);
        if (directions.HasFlag(BlockArrowDirections.Right)) { Point(.8, .3); Point(.8, .18); Point(1, .5); Point(.8, .82); Point(.8, .7); }
        Point(.7, .7);
        if (directions.HasFlag(BlockArrowDirections.Down)) { Point(.7, .8); Point(.82, .8); Point(.5, 1); Point(.18, .8); Point(.3, .8); }
        Point(.3, .7);
        if (directions.HasFlag(BlockArrowDirections.Left)) { Point(.2, .7); Point(.2, .82); Point(0, .5); Point(.2, .18); Point(.2, .3); }
        return p;
    }
}
