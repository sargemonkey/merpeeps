using Merpeeps.Core.Model;
using Merpeeps.Core.Parsing;

namespace Merpeeps.Core.Layout;

public sealed record VennLayoutResult(DrawingScene Scene, IReadOnlyList<VennCircle> Circles,
    IReadOnlyList<VennAreaFit> Areas, IReadOnlyList<DataDiagramDiagnostic> Diagnostics);
public static class VennDiagramLayout
{
    public static VennLayoutResult Create(DiagramModel model, DrawingTextMeasure? measure = null, string? themeOverride = null)
    {
        var document = model.Venn ?? throw new ArgumentException("Venn content is missing.");
        var geometry = VennGeometry.Fit(document);
        var width = Number(document.Configuration, "width", 800); var height = Number(document.Configuration, "height", 450);
        var padding = Number(document.Configuration, "padding", 25); var fontSize = Number(document.Configuration, "fontSize", 24);
        if (width <= padding * 2 || height <= padding * 2 + 35 || fontSize <= 0) throw new ArgumentException("Venn canvas is too small for its padding.");
        var background = new ClassColor(255, 255, 255);
        if ((themeOverride ?? model.Theme) == "dark") background = new(31, 32, 32);
        var ink = background.Red < 128 ? new ClassColor(240, 240, 240) : new ClassColor(35, 45, 58);
        var figures = new List<DrawingFigure> { new("background", new("Diagram", ""), DrawingFigureKind.Rectangle, new(0, 0, width, height), [], [], background, null) };
        var labels = new List<DrawingLabel>(); var regions = new List<DrawingRegion>(); var diagnostics = new List<DataDiagramDiagnostic>();
        var titleHeight = document.Title.Length == 0 ? 0 : 36;
        if (document.Title.Length > 0) Label("title", new("VennDiagram", ""), document.Title, new(width / 2, padding / 2 + 12), fontSize, ink);
        if (geometry.Circles.Count == 0) return new(new(width, height, figures, labels, regions, measure is null), [], [], []);
        var minX = geometry.Circles.Min(c => c.X - c.Radius); var maxX = geometry.Circles.Max(c => c.X + c.Radius);
        var minY = geometry.Circles.Min(c => c.Y - c.Radius); var maxY = geometry.Circles.Max(c => c.Y + c.Radius);
        var scale = Math.Min((width - padding * 2) / Math.Max(1e-9, maxX - minX), (height - padding * 2 - titleHeight) / Math.Max(1e-9, maxY - minY));
        if (!double.IsFinite(scale) || scale > 1e8) scale = 1;
        var offsetX = (width - (maxX - minX) * scale) / 2 - minX * scale;
        var offsetY = titleHeight + (height - titleHeight - (maxY - minY) * scale) / 2 - minY * scale;
        var circles = geometry.Circles.Select(c => c with { X = c.X * scale + offsetX, Y = c.Y * scale + offsetY, Radius = c.Radius * scale }).ToArray();
        var byId = circles.ToDictionary(c => c.SetId);
        foreach (var (set, i) in document.Sets.Select((set, i) => (set, i)))
        {
            var circle = byId[set.Id]; var color = Parse(ChartConfiguration.Palette[i % ChartConfiguration.Palette.Length]);
            var fill = Paint(set.Style, "fill", color); fill = fill with { Alpha = Alpha(set.Style, .3) };
            var stroke = Paint(set.Style, "stroke", color); var lineWidth = Number(set.Style, "stroke-width", 2.5);
            if (circle.Radius > 0)
            {
                var bounds = new DiagramRect(circle.X - circle.Radius, circle.Y - circle.Radius, circle.Radius * 2, circle.Radius * 2);
                figures.Add(new(set.Id, new("VennSet", set.Id), DrawingFigureKind.Ellipse, bounds, [], [], fill, stroke, lineWidth));
                regions.Add(new(new("VennSet", set.Id), DrawingHitKind.CircleIntersection, bounds, [], Circles: [new(new(circle.X, circle.Y), circle.Radius)]));
            }
            DrawArea(set.Id, new("VennSet", set.Id), [circle], set.Label ?? set.Name, Paint(set.Style, "color", ink));
        }
        foreach (var overlap in document.Overlaps.OrderBy(o => o.SetIds.Count))
        {
            var included = overlap.SetIds.Select(id => byId[id]).ToArray();
            var arcs = VennGeometry.IntersectionArcs(included);
            if (arcs.Count == 0)
            {
                diagnostics.Add(new("venn-empty-overlap", overlap.Id, "This overlap has no visible circle intersection; its label and text nodes remain in the document."));
                continue;
            }
            var path = Path(arcs);
            var bounds = IntersectionBounds(included);
            var fill = overlap.Style["fill"] is null ? (ClassColor?)null : Paint(overlap.Style, "fill", ink) with { Alpha = Alpha(overlap.Style, 1) };
            figures.Add(new(overlap.Id, new("VennOverlap", overlap.Id), DrawingFigureKind.Path, bounds, [], path, fill,
                overlap.Style["stroke"] is null ? null : Paint(overlap.Style, "stroke", ink), Number(overlap.Style, "stroke-width", 1)));
            regions.Add(new(new("VennOverlap", overlap.Id), DrawingHitKind.CircleIntersection, IntersectionBounds(included), [],
                Circles: included.Select(c => new DrawingCircle(new(c.X, c.Y), c.Radius)).ToArray()));
            DrawArea(overlap.Id, new("VennOverlap", overlap.Id), included, overlap.Label, Paint(overlap.Style, "color", ink));
        }
        foreach (var fit in geometry.Areas.Where(a => !a.Implied && a.RelativeError > .02))
            diagnostics.Add(new("venn-area-fit", fit.AreaId, $"Circle layout is approximate: requested {fit.RequestedArea:G5}, represented {fit.ActualArea:G5}. Some combinations cannot be represented exactly by circles."));
        return new(new(width, height, figures, labels, regions, measure is null), circles, geometry.Areas, diagnostics);

        void DrawArea(string id, DrawingOwner owner, IReadOnlyList<VennCircle> included, string? title, ClassColor color)
        {
            if (included.Any(c => c.Radius <= 0)) { diagnostics.Add(new("venn-zero-set", id, "Zero-sized area has no circle; its text is preserved.")); return; }
            var excluded = circles.Where(c => included.All(x => x.SetId != c.SetId)).ToArray();
            var center = LabelCenter(included, excluded);
            if (Clearance(center, included, []) < -1e-5) { diagnostics.Add(new("venn-text-region", id, "No common region is available for this area's text.")); return; }
            var texts = document.Texts.Where(t => t.AreaId == id).ToArray();
            var y = center.Y - (title is null ? 0 : (texts.Length > 0 ? 20 : 0));
            if (title is not null && title.Length > 0) Label(id + "-label", owner, title, new(center.X, y), fontSize, color);
            y += title is null ? 0 : fontSize + 5;
            foreach (var text in texts)
            {
                var label = text.Label ?? text.Name; var textColor = Paint(text.Style, "color", ink);
                var bounds = Label(text.Id, new("VennText", text.Id, id), label, new(center.X, y), fontSize * .8, textColor);
                if (text.Style["fill"] is not null) figures.Add(new(text.Id + "-fill", new("VennText", text.Id, id), DrawingFigureKind.Rectangle,
                    bounds, [], [], Paint(text.Style, "fill", ink) with { Alpha = Alpha(text.Style, 1) }, text.Style["stroke"] is null ? null : Paint(text.Style, "stroke", ink), Number(text.Style, "stroke-width", 1)));
                regions.Add(new(new("VennText", text.Id, id), DrawingHitKind.Rectangle, bounds, [])); y += bounds.Height + 6;
                if (new[] { new DiagramPoint(bounds.X, bounds.Y), new(bounds.X + bounds.Width, bounds.Y + bounds.Height) }.Any(p => Clearance(p, included, []) < 0))
                    diagnostics.Add(new("venn-text-overflow", text.Id, "This text does not fit inside its overlap; the full text remains editable."));
            }
        }
        DiagramRect Label(string id, DrawingOwner owner, string text, DiagramPoint center, double size, ClassColor color)
        {
            var font = new DrawingFont(document.GlobalConfiguration["fontFamily"]?.ToString() ?? "Segoe UI", size);
            var m = measure?.Invoke(text, font) ?? new(text.EnumerateRunes().Count() * size * .6, size * 1.4);
            if (!double.IsFinite(m.Width) || !double.IsFinite(m.Height) || m.Width < 0 || m.Height < 0) throw new ArgumentException("Invalid Venn text measurements.");
            var r = new DiagramRect(center.X - m.Width / 2, center.Y - m.Height / 2, m.Width, m.Height);
            labels.Add(new(id, owner, text, font, color, r));
            return r;
        }
    }
    public static IReadOnlyList<DrawingPathPart> Path(IReadOnlyList<VennArc> arcs)
    {
        if (arcs.Count == 0) return [];
        var remaining = arcs.ToList(); var ordered = new List<VennArc> { remaining[0] }; remaining.RemoveAt(0);
        while (remaining.Count > 0)
        {
            var end = Point(ordered[^1], ordered[^1].StartAngle + ordered[^1].SweepAngle);
            var next = remaining.MinBy(a => Distance(end, Point(a, a.StartAngle)))!;
            ordered.Add(next); remaining.Remove(next);
        }
        var path = new List<DrawingPathPart> { new(DrawingPathKind.Move, Point(ordered[0], ordered[0].StartAngle)) };
        foreach (var arc in ordered)
        {
            var count = Math.Max(1, (int)Math.Ceiling(arc.SweepAngle / (Math.PI / 2))); var step = arc.SweepAngle / count;
            for (var i = 0; i < count; i++)
            {
                var a = arc.StartAngle + i * step; var z = a + step; var factor = 4d / 3 * Math.Tan(step / 4) * arc.Circle.Radius;
                var p = Point(arc, a); var q = Point(arc, z);
                path.Add(new(DrawingPathKind.Cubic, q, new(p.X - Math.Sin(a) * factor, p.Y + Math.Cos(a) * factor),
                    new(q.X + Math.Sin(z) * factor, q.Y - Math.Cos(z) * factor)));
            }
        }
        path.Add(new(DrawingPathKind.Close, default)); return path;
    }
    private static DiagramPoint LabelCenter(IReadOnlyList<VennCircle> included, IReadOnlyList<VennCircle> excluded)
    {
        var seeds = included.Select(c => new DiagramPoint(c.X, c.Y)).ToList();
        seeds.Add(new(included.Average(c => c.X), included.Average(c => c.Y)));
        foreach (var arc in VennGeometry.IntersectionArcs(included)) seeds.Add(Point(arc, arc.StartAngle + arc.SweepAngle / 2));
        var best = seeds.MaxBy(p => Clearance(p, included, excluded));
        var score = Clearance(best, included, excluded); var step = included.Min(c => c.Radius) / 2;
        for (var iteration = 0; iteration < 100 && step > .02; iteration++)
        {
            var improved = false;
            foreach (var angle in Enumerable.Range(0, 8).Select(i => i * Math.PI / 4))
            {
                var p = new DiagramPoint(best.X + Math.Cos(angle) * step, best.Y + Math.Sin(angle) * step);
                var value = Clearance(p, included, excluded);
                if (value > score) { best = p; score = value; improved = true; }
            }
            if (!improved) step /= 2;
        }
        if (Clearance(best, included, []) < 0 && excluded.Count > 0) return LabelCenter(included, []);
        return best;
    }
    private static double Clearance(DiagramPoint point, IReadOnlyList<VennCircle> included, IReadOnlyList<VennCircle> excluded) =>
        included.Select(c => c.Radius - Distance(point, new(c.X, c.Y))).Concat(excluded.Select(c => Distance(point, new(c.X, c.Y)) - c.Radius)).Min();
    private static DiagramPoint Point(VennArc arc, double angle) => new(arc.Circle.X + arc.Circle.Radius * Math.Cos(angle), arc.Circle.Y + arc.Circle.Radius * Math.Sin(angle));
    private static double Distance(DiagramPoint a, DiagramPoint b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
    private static DiagramRect IntersectionBounds(IReadOnlyList<VennCircle> circles)
    {
        var x = circles.Max(c => c.X - c.Radius); var y = circles.Max(c => c.Y - c.Radius);
        return new(x, y, Math.Max(0, circles.Min(c => c.X + c.Radius) - x), Math.Max(0, circles.Min(c => c.Y + c.Radius) - y));
    }
    private static DiagramRect Bounds(IReadOnlyList<DiagramPoint> points) => new(points.Min(p => p.X), points.Min(p => p.Y), points.Max(p => p.X) - points.Min(p => p.X), points.Max(p => p.Y) - points.Min(p => p.Y));
    private static double Number(System.Text.Json.Nodes.JsonObject style, string key, double fallback) => style[key] is { } value ? ChartConfiguration.ParseNumber(value.ToString()) : fallback;
    private static byte Alpha(System.Text.Json.Nodes.JsonObject style, double fallback) => (byte)Math.Round(Number(style, "fill-opacity", fallback) * 255);
    private static ClassColor Paint(System.Text.Json.Nodes.JsonObject style, string key, ClassColor fallback) => style[key] is { } value ? Parse(value.ToString()) : fallback;
    private static ClassColor Parse(string value) => SequenceColors.TryParse(value, out var color) ? color : throw new ArgumentException("Invalid Venn color.");
}
