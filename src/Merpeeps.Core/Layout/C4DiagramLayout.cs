using Merpeeps.Core.Model;

namespace Merpeeps.Core.Layout;

public sealed record C4LayoutResult(DrawingScene Scene, IReadOnlyDictionary<string, DiagramRect> ElementBounds,
    IReadOnlyDictionary<string, IReadOnlyList<DiagramPoint>> Routes, IReadOnlyList<DataDiagramDiagnostic> Diagnostics);
public static class C4DiagramLayout
{
    private sealed record Row(IReadOnlyList<C4Element> Elements, double Width, double Height);
    public static C4LayoutResult Create(DiagramModel model, DrawingTextMeasure? measure = null, string? themeOverride = null)
    {
        var document = model.C4 ?? throw new ArgumentException("C4 content is missing.");
        C4DiagramValidation.Check(document);
        var builder = new DataDiagramLayouts.Builder(measure, document.ThemeVariables, document.GlobalConfiguration, themeOverride ?? model.Theme);
        var wrap = document.Configuration["wrap"]?.GetValue<bool>() ?? document.GlobalConfiguration["wrap"]?.GetValue<bool>() ?? true;
        var targetWidth = C4Appearance.Number(document.Configuration, "width", 216);
        var padding = C4Appearance.Number(document.Configuration, "c4ShapePadding", 20);
        var margin = C4Appearance.Number(document.Configuration, "c4ShapeMargin", 50);
        var shapeColumns = (int)C4Appearance.Number(document.LayoutOptions, "c4ShapeInRow", C4Appearance.Number(document.Configuration, "c4ShapeInRow", 4));
        var boundaryColumns = (int)C4Appearance.Number(document.LayoutOptions, "c4BoundaryInRow", C4Appearance.Number(document.Configuration, "c4BoundaryInRow", 2));
        if (targetWidth <= 0 || padding < 0 || margin < 0 || shapeColumns <= 0 || boundaryColumns <= 0)
            throw new ArgumentException("C4 width, spacing and row counts are invalid.");
        var styles = document.Elements.ToDictionary(e => e.Id, e => C4Appearance.Element(document, e));
        var sizes = new Dictionary<string, DrawingTextSize>();
        var rows = new Dictionary<string, IReadOnlyList<Row>>();
        var bounds = new Dictionary<string, DiagramRect>();
        var routes = new Dictionary<string, IReadOnlyList<DiagramPoint>>();
        string TypeText(C4Element e) => "[" + (e.Level switch
        {
            C4ElementLevel.Person => "Person", C4ElementLevel.System => "Software System", C4ElementLevel.Container => "Container",
            C4ElementLevel.Component => "Component", C4ElementLevel.DeploymentNode => e.Technology.Length == 0 ? "Deployment Node" : e.Technology,
            _ => e.BoundaryType.Length == 0 ? e.BoundaryKind + " Boundary" : e.BoundaryType,
        }) + (e.Level is C4ElementLevel.Container or C4ElementLevel.Component && e.Technology.Length > 0 ? ": " + e.Technology : "") + "]";
        DrawingTextSize TextSize(C4Element e, string text, bool bold = false)
        {
            var style = styles[e.Id];
            return builder.MeasureWrapped(text, style.FontSize, wrap ? Math.Max(1, targetWidth - padding * 2) : double.MaxValue,
                bold, family: style.FontFamily, weight: style.FontWeight);
        }
        IReadOnlyList<Row> MeasureChildren(string? parent)
        {
            var result = new List<Row>(); var current = new List<C4Element>(); var width = 0d; var height = 0d;
            foreach (var element in document.Elements.Where(e => e.ParentId == parent))
            {
                var label = TextSize(element, element.Label, true);
                var type = TextSize(element, TypeText(element));
                var description = element.Description.Length == 0 ? default : TextSize(element, element.Description);
                var contentWidth = Math.Max(label.Width, Math.Max(type.Width, description.Width)) + padding * 2;
                var contentHeight = label.Height + type.Height + description.Height + padding * 2 + (element.Description.Length == 0 ? 8 : 16);
                if (element.IsContainer)
                {
                    var children = MeasureChildren(element.Id); rows[element.Id] = children;
                    contentWidth = Math.Max(contentWidth, children.Select(r => r.Width).DefaultIfEmpty(0).Max() + padding * 2);
                    contentHeight += children.Sum(r => r.Height) + margin * Math.Max(0, children.Count - 1) + padding;
                }
                else if (element.Level == C4ElementLevel.Person) contentHeight += 36;
                else if (element.Shape == C4ElementShape.Database) contentHeight += 20;
                else if (element.Shape == C4ElementShape.Queue) contentWidth += 24;
                var size = new DrawingTextSize(Math.Max(targetWidth, contentWidth), Math.Max(60, contentHeight));
                sizes[element.Id] = size;
                var limit = element.IsContainer ? boundaryColumns : shapeColumns;
                if (current.Count >= limit) Flush();
                current.Add(element); width += (current.Count > 1 ? margin : 0) + size.Width; height = Math.Max(height, size.Height);
            }
            Flush(); return result;
            void Flush()
            {
                if (current.Count == 0) return;
                result.Add(new(current.ToArray(), width, height)); current.Clear(); width = height = 0;
            }
        }
        void DrawText(C4Element element, string suffix, string text, double x, double y, double width, bool bold = false)
        {
            var style = styles[element.Id]; var size = builder.MeasureWrapped(text, style.FontSize, width, bold, family: style.FontFamily, weight: style.FontWeight);
            builder.Text(element.Id + suffix, "C4Element", element.Id, text, style.FontSize, x + (width - size.Width) / 2, y, width,
                bold, style.TextColor, family: style.FontFamily, alignment: "center", weight: style.FontWeight);
        }
        void Place(IReadOnlyList<Row> content, double x, double y)
        {
            foreach (var row in content)
            {
                var left = x;
                foreach (var element in row.Elements)
                {
                    var size = sizes[element.Id]; var r = new DiagramRect(left, y, size.Width, size.Height);
                    bounds[element.Id] = r;
                    var style = styles[element.Id];
                    var label = TextSize(element, element.Label, true); var type = TextSize(element, TypeText(element));
                    var description = element.Description.Length == 0 ? default : TextSize(element, element.Description);
                    var top = y + padding;
                    if (element.IsContainer)
                        builder.Box(element.Id, "C4Element", element.Id, r, style.Fill, style.Stroke, 1, true);
                    else if (element.Level == C4ElementLevel.Person)
                    {
                        builder.Circle(element.Id + "-head", "C4Element", element.Id, new(left + size.Width / 2, y + 18), 18, style.Fill, style.Stroke);
                        builder.Box(element.Id, "C4Element", element.Id, new(left, y + 36, size.Width, size.Height - 36), style.Fill, style.Stroke);
                        top += 36;
                    }
                    else if (element.Shape == C4ElementShape.Database)
                    {
                        builder.Box(element.Id, "C4Element", element.Id, new(left, y + 12, size.Width, size.Height - 24), style.Fill, style.Stroke);
                        Ellipse(element.Id + "-bottom", element.Id, new(left + size.Width / 2, y + size.Height - 12), size.Width / 2, 12, style.Fill, style.Stroke);
                        Ellipse(element.Id + "-top", element.Id, new(left + size.Width / 2, y + 12), size.Width / 2, 12, style.Fill, style.Stroke);
                        top += 12;
                    }
                    else if (element.Shape == C4ElementShape.Queue)
                    {
                        builder.Box(element.Id, "C4Element", element.Id, new(left + 12, y, size.Width - 24, size.Height), style.Fill, style.Stroke);
                        Ellipse(element.Id + "-right", element.Id, new(left + size.Width - 12, y + size.Height / 2), 12, size.Height / 2, style.Fill, style.Stroke);
                        Ellipse(element.Id + "-left", element.Id, new(left + 12, y + size.Height / 2), 12, size.Height / 2, style.Fill, style.Stroke);
                    }
                    else builder.Box(element.Id, "C4Element", element.Id, r, style.Fill, style.Stroke);
                    builder.Hit("C4Element", element.Id, r, element.ParentId);
                    DrawText(element, "-label", element.Label, left + padding, top, size.Width - padding * 2, true);
                    top += label.Height + 4;
                    DrawText(element, "-type", TypeText(element), left + padding, top, size.Width - padding * 2);
                    top += type.Height + 4;
                    if (element.Description.Length > 0)
                    {
                        DrawText(element, "-description", element.Description, left + padding, top, size.Width - padding * 2);
                        top += description.Height + 8;
                    }
                    if (element.IsContainer) Place(rows[element.Id], left + padding, top + padding);
                    left += size.Width + margin;
                }
                y += row.Height + margin;
            }
        }
        var root = MeasureChildren(null);
        var titleHeight = document.Title.Length == 0 ? 0 : 44;
        if (titleHeight > 0) builder.Text("title", "C4Diagram", "", document.Title, 20, 20, 10, Math.Max(targetWidth, root.Select(r => r.Width).DefaultIfEmpty(targetWidth).Max()), true);
        Place(root, 20, 20 + titleHeight);
        for (var i = 0; i < document.Relationships.Count; i++)
        {
            var relationship = document.Relationships[i]; var from = bounds[relationship.From]; var to = bounds[relationship.To];
            var start = Anchor(from, Center(to)); var end = Anchor(to, Center(from));
            if (relationship.Kind == C4RelationshipKind.Back) (start, end) = (end, start);
            var route = relationship.From == relationship.To
                ? new[] { new DiagramPoint(from.X + from.Width, from.Y + from.Height / 2), new(from.X + from.Width + 35, from.Y + from.Height / 2),
                    new(from.X + from.Width + 35, from.Y - 25), new(from.X + from.Width / 2, from.Y - 25), new(from.X + from.Width / 2, from.Y) }
                : new[] { start, end };
            routes[relationship.Id] = route;
            var style = C4Appearance.Relationship(relationship);
            builder.Line(relationship.Id, "C4Relationship", relationship.Id, route, style.Stroke, 1.5);
            Arrow(relationship.Id + "-head", relationship.Id, route[^1], route[^2], style.Stroke);
            if (relationship.Kind == C4RelationshipKind.Bidirectional) Arrow(relationship.Id + "-start", relationship.Id, route[0], route[1], style.Stroke);
            var text = (document.Kind == C4DiagramKind.Dynamic ? (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + ". " : "") + relationship.Label;
            if (relationship.Technology.Length > 0) text += "\n[" + relationship.Technology + "]";
            var measured = builder.MeasureWrapped(text, 12, 220);
            var center = route.Length == 2 ? new DiagramPoint((start.X + end.X) / 2, (start.Y + end.Y) / 2) : route[2];
            builder.Text(relationship.Id + "-label", "C4Relationship", relationship.Id, text, 12, center.X - measured.Width / 2 + style.OffsetX,
                center.Y - measured.Height - 4 + style.OffsetY, 220, color: style.TextColor, alignment: "center");
        }
        var scene = builder.Finish(); var offset = builder.OriginOffset;
        var finalBounds = bounds.ToDictionary(p => p.Key, p => p.Value with { X = p.Value.X + offset.X, Y = p.Value.Y + offset.Y });
        var finalRoutes = routes.ToDictionary(p => p.Key, p => (IReadOnlyList<DiagramPoint>)p.Value.Select(v => new DiagramPoint(v.X + offset.X, v.Y + offset.Y)).ToArray());
        var regions = finalRoutes.Select(p => new DrawingRegion(new("C4Relationship", p.Key), DrawingHitKind.Polyline, Bounds(p.Value), p.Value));
        return new(scene with { Regions = scene.Regions.Concat(regions).ToArray() }, finalBounds, finalRoutes, C4DiagramValidation.Validate(document));

        void Ellipse(string id, string owner, DiagramPoint center, double rx, double ry, ClassColor fill, ClassColor stroke)
        {
            var points = Enumerable.Range(0, 49).Select(i => new DiagramPoint(center.X + rx * Math.Cos(i * Math.PI / 24), center.Y + ry * Math.Sin(i * Math.PI / 24))).ToArray();
            builder.Polygon(id, "C4Element", owner, points, fill, stroke);
        }
        void Arrow(string id, string owner, DiagramPoint tip, DiagramPoint toward, ClassColor color)
        {
            var dx = toward.X - tip.X; var dy = toward.Y - tip.Y; var length = Math.Sqrt(dx * dx + dy * dy);
            if (length <= 0) return;
            builder.Polygon(id, "C4Relationship", owner, [tip, new(tip.X + dx / length * 10 - dy / length * 4, tip.Y + dy / length * 10 + dx / length * 4),
                new(tip.X + dx / length * 10 + dy / length * 4, tip.Y + dy / length * 10 - dx / length * 4)], color, color);
        }
    }
    private static DiagramPoint Center(DiagramRect r) => new(r.X + r.Width / 2, r.Y + r.Height / 2);
    private static DiagramPoint Anchor(DiagramRect r, DiagramPoint toward)
    {
        var center = Center(r); var dx = toward.X - center.X; var dy = toward.Y - center.Y;
        if (dx == 0 && dy == 0) return center;
        var factor = Math.Min(dx == 0 ? double.MaxValue : r.Width / 2 / Math.Abs(dx), dy == 0 ? double.MaxValue : r.Height / 2 / Math.Abs(dy));
        return new(center.X + dx * factor, center.Y + dy * factor);
    }
    private static DiagramRect Bounds(IReadOnlyList<DiagramPoint> points) => new(points.Min(p => p.X), points.Min(p => p.Y), points.Max(p => p.X) - points.Min(p => p.X), points.Max(p => p.Y) - points.Min(p => p.Y));
}
