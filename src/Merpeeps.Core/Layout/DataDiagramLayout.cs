using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Merpeeps.Core.Model;
using Merpeeps.Core.Parsing;

namespace Merpeeps.Core.Layout;

public sealed record DataDiagramLayout(DrawingScene Scene, IReadOnlyList<DataDiagramDiagnostic> Diagnostics,
    DiagramPoint OriginOffset, IReadOnlyDictionary<string, DiagramRect> NodeBounds);

public static class DataDiagramLayouts
{
    public static DataDiagramLayout Create(DiagramModel model, DrawingTextMeasure? measure = null, string? themeOverride = null)
    {
        var theme = themeOverride ?? model.Theme;
        if (model.Temporal is { } temporal)
        {
            var builder = new Builder(measure, temporal.ThemeVariables, temporal.GlobalConfiguration, theme);
            var issues = TemporalDiagramAdapter.Validate(temporal);
            if (issues.Any(i => i.Blocking)) throw new DataDiagramNotEditableException(issues);
            if (temporal.Family == TemporalFamily.Timeline) Timeline(builder, temporal); else Journey(builder, temporal);
            return new(builder.Finish(), issues, builder.OriginOffset, builder.NodeBounds);
        }
        if (model.Outline is { } outline)
        {
            var builder = new Builder(measure, outline.ThemeVariables, outline.GlobalConfiguration, theme);
            var issues = OutlineDiagramAdapter.Validate(outline).ToList();
            if (issues.Any(i => i.Blocking)) throw new DataDiagramNotEditableException(issues);
            if (outline.Family == OutlineFamily.TreeView) Tree(builder, outline, issues); else Mindmap(builder, outline);
            return new(builder.Finish(), issues, builder.OriginOffset, builder.NodeBounds);
        }
        throw new ArgumentException("Expected a temporal or outline document.");
    }

    private static void Timeline(Builder b, TemporalDocument document)
    {
        var top = 20d;
        if (document.Title.Length > 0) { b.Text("title", "Diagram", "", document.Title, 22, 20, top, 800, true); top += 48; }
        var disableMulticolor = document.Configuration["disableMulticolor"]?.GetValue<bool>() ?? false;
        var position = 20d;
        var sectionBoxes = new Dictionary<string, List<DiagramRect>>();
        var headingHeight = document.Periods.Select(p => b.MeasureWrapped(p.Label, 16, 180, true).Height + 24).DefaultIfEmpty(46).Max();
        var axisY = top + 36 + headingHeight + 24;
        if (!document.TopDown && document.Periods.Count > 0)
        {
            var end = 20 + document.Periods.Count * 236 - 32;
            b.Line("timeline-axis", "Diagram", "", [new(8, axisY), new(end + 12, axisY)], b.Ink, 1.5);
            b.Line("timeline-arrow", "Diagram", "", [new(end + 5, axisY - 5), new(end + 12, axisY), new(end + 5, axisY + 5)], b.Ink, 1.5);
        }
        for (var index = 0; index < document.Periods.Count; index++)
        {
            var period = document.Periods[index];
            var palette = disableMulticolor ? 0 : period.SectionId is { } section ? document.Sections.FindIndex(s => s.Id == section) : index;
            var fill = b.Scale(palette);
            var foreground = b.ScaleLabel(palette, fill);
            var label = b.MeasureWrapped(period.Label, 16, 180, true);
            var eventSizes = period.Events.Select(e => b.MeasureWrapped(e.Text, 14, 180)).ToArray();
            var main = new DiagramRect(document.TopDown ? 20 : position, document.TopDown ? position + top : top + 36, 204,
                document.TopDown ? label.Height + 24 : headingHeight);
            b.Box("period-" + period.Id, "Period", period.Id, main, fill, b.Ink);
            b.Text("period-text-" + period.Id, "Period", period.Id, period.Label, 16, main.X + 12, main.Y + 12, 180, true, foreground);
            b.Hit("Period", period.Id, main);
            var extent = main;
            var cursor = document.TopDown ? main.X + main.Width + 32 : axisY + 32;
            var previous = document.TopDown ? new DiagramPoint(main.X + main.Width, main.Y + main.Height / 2)
                : new DiagramPoint(main.X + main.Width / 2, axisY);
            if (!document.TopDown)
            {
                b.Line("period-axis-" + period.Id, "Period", period.Id,
                    [new(main.X + main.Width / 2, main.Y + main.Height), previous], b.Ink, 1);
                b.Circle("period-marker-" + period.Id, "Period", period.Id, previous, 3, fill, b.Ink);
            }
            foreach (var (item, eventIndex) in period.Events.Select((item, i) => (item, i)))
            {
                var size = eventSizes[eventIndex];
                var rect = new DiagramRect(document.TopDown ? cursor : main.X, document.TopDown ? main.Y : cursor, 204, size.Height + 20);
                b.Box("event-" + item.Id, "Event", item.Id, rect, b.Tint(fill), b.Ink);
                b.Text("event-text-" + item.Id, "Event", item.Id, item.Text, 14, rect.X + 10, rect.Y + 10, 184, false, b.Ink);
                b.Hit("Event", item.Id, rect, period.Id);
                b.Line("period-event-" + item.Id, "Event", item.Id,
                    [previous, document.TopDown ? new(rect.X, rect.Y + rect.Height / 2) : new(rect.X + rect.Width / 2, rect.Y)], b.Ink, 1);
                previous = document.TopDown ? new(rect.X + rect.Width, rect.Y + rect.Height / 2) : new(rect.X + rect.Width / 2, rect.Y + rect.Height);
                extent = Union(extent, rect);
                cursor += (document.TopDown ? rect.Width : rect.Height) + 18;
            }
            if (period.SectionId is { } id)
            {
                if (!sectionBoxes.TryGetValue(id, out var boxes)) sectionBoxes[id] = boxes = new();
                boxes.Add(extent);
            }
            position += (document.TopDown ? extent.Height : extent.Width) + 32;
        }
        foreach (var section in document.Sections)
        {
            if (!sectionBoxes.TryGetValue(section.Id, out var boxes) || boxes.Count == 0) continue;
            var bounds = boxes.Aggregate(Union);
            var title = new DiagramRect(bounds.X, bounds.Y - 34, bounds.Width, 28);
            b.Box("section-" + section.Id, "Section", section.Id, title, b.Scale(document.Sections.IndexOf(section)), null);
            b.Text("section-text-" + section.Id, "Section", section.Id, section.Label, 14, title.X + 6, title.Y + 5, title.Width - 12, true);
            b.Hit("Section", section.Id, title);
        }
    }

    private static void Journey(Builder b, TemporalDocument document)
    {
        var top = 20d;
        if (document.Title.Length > 0) { b.Text("title", "Diagram", "", document.Title, 22, 20, top, 900, true); top += 48; }
        var actors = document.Tasks.SelectMany(t => t.Actors).Distinct().ToArray();
        for (var i = 0; i < actors.Length; i++)
        {
            b.Circle("actor-dot-" + i, "Diagram", "", new(28, top + i * 23 + 8), 6, b.Scale(i), null);
            b.Text("actor-label-" + i, "Diagram", "", actors[i], 13, 42, top + i * 23, 200);
        }
        top += actors.Length * 23 + 20;
        var maximumHeader = document.Tasks.Select(t => b.MeasureWrapped(t.Label, 14, 150, true).Height + 36).DefaultIfEmpty(50).Max();
        var x = 20d;
        string? previousSection = null;
        for (var index = 0; index < document.Tasks.Count; index++)
        {
            var task = document.Tasks[index];
            var sectionIndex = task.SectionId is { } id ? document.Sections.FindIndex(s => s.Id == id) : index;
            var fill = b.Scale(sectionIndex);
            if (task.SectionId != previousSection && task.SectionId is { } section)
            {
                b.Text("section-" + section, "Section", section, document.Sections.Single(s => s.Id == section).Label, 16, x, top, 220, true);
                previousSection = section;
            }
            var wrapped = b.MeasureWrapped(task.Label, 14, 150, true);
            var rect = new DiagramRect(x, top + 34, 174, wrapped.Height + 36);
            b.Box(task.Id, "Task", task.Id, rect, fill, b.Ink);
            b.Text(task.Id + "-text", "Task", task.Id, task.Label, 14, x + 12, rect.Y + 10, 150, true, b.ScaleLabel(sectionIndex, fill));
            for (var actor = 0; actor < task.Actors.Count; actor++)
                b.Circle(task.Id + "-actor-" + actor, "Task", task.Id, new(x + 15 + actor * 15, rect.Y + rect.Height - 10), 5, b.Scale(Array.IndexOf(actors, task.Actors[actor])), b.Ink);
            var scoreY = top + 34 + maximumHeader + 35 + (5 - task.Score) * 36;
            b.Line(task.Id + "-score-line", "Task", task.Id, [new(x + 87, rect.Y + rect.Height), new(x + 87, scoreY)], b.Ink, 1);
            b.Circle(task.Id + "-face", "Task", task.Id, new(x + 87, scoreY), 17, b.Background, b.Ink);
            b.Circle(task.Id + "-eye-a", "Task", task.Id, new(x + 81, scoreY - 5), 1.5, b.Ink, null);
            b.Circle(task.Id + "-eye-b", "Task", task.Id, new(x + 93, scoreY - 5), 1.5, b.Ink, null);
            var bend = task.Score > 3 ? 8 : task.Score < 3 ? -3 : 2;
            b.Path(task.Id + "-mouth", "Task", task.Id, [
                new(DrawingPathKind.Move, new(x + 78, scoreY + 4)),
                new(DrawingPathKind.Cubic, new(x + 96, scoreY + 4), new(x + 83, scoreY + bend), new(x + 91, scoreY + bend)),
            ], null, b.Ink);
            b.Text(task.Id + "-score", "Task", task.Id, task.Score.ToString(), 12, x + 79, scoreY + 23, 30);
            b.Hit("Task", task.Id, Union(rect, new(x + 67, scoreY - 20, 40, 65)));
            if (index > 0) b.Line(task.Id + "-order", "Task", task.Id, [new(x - 22, rect.Y + 12), new(x, rect.Y + 12)], b.Ink, 1);
            x += 198;
        }
    }

    private static void Tree(Builder b, OutlineDocument document, List<DataDiagramDiagnostic> issues)
    {
        var rowIndent = Number(document.Configuration, "rowIndent", 10);
        var paddingX = Number(document.Configuration, "paddingX", 5);
        var paddingY = Number(document.Configuration, "paddingY", 5);
        var lineWidth = Number(document.Configuration, "lineThickness", 1);
        var theme = document.ThemeVariables["treeView"] as JsonObject ?? new();
        var fontSize = Number(theme, "labelFontSize", 16);
        var y = 15d;
        Walk(document.Roots, 0, null);
        void Walk(IReadOnlyList<OutlineNode> nodes, int depth, DiagramPoint? parent)
        {
            foreach (var node in nodes)
            {
                var x = 20 + depth * Math.Max(12, rowIndent + paddingX * 2);
                var label = node.Label + (node.Directory ? "/" : "");
                var size = b.MeasureWrapped(label, fontSize, double.MaxValue, node.Directory);
                var icon = ResolveTreeIcon(document, node);
                var iconWidth = icon is null ? 0 : fontSize + 8;
                var description = node.Description is { } text ? b.MeasureWrapped(text, fontSize, double.MaxValue, italic: true) : new DrawingTextSize();
                var rect = new DiagramRect(x, y, paddingX * 2 + iconWidth + size.Width + (node.Description is null ? 0 : 12 + description.Width), size.Height + paddingY * 2);
                if (node.Classes.Contains("highlight"))
                    b.Box(node.Id + "-highlight", "Node", node.Id, rect, Color(theme, "highlightBg", new(255, 193, 7, 38)), Color(theme, "highlightStroke", new(255, 193, 7)));
                if (parent is { } origin)
                    b.Line(node.Id + "-branch", "Node", node.Id, [origin, new(origin.X, y + rect.Height / 2), new(x, y + rect.Height / 2)], Color(theme, "lineColor", b.Ink), lineWidth);
                if (icon is not null) DrawIcon(b, node.Id, icon, x + paddingX, y + paddingY, fontSize, Color(theme, "iconColor", b.Ink));
                b.Text(node.Id + "-label", "Node", node.Id, label, fontSize, x + paddingX + iconWidth, y + paddingY, double.MaxValue, node.Directory, Color(theme, "labelColor", b.Ink));
                if (node.Description is not null)
                    b.Text(node.Id + "-description", "Node", node.Id, node.Description, fontSize, x + paddingX + iconWidth + size.Width + 12, y + paddingY, double.MaxValue, false, Color(theme, "descriptionColor", new(70, 120, 70)), true);
                b.Hit("Node", node.Id, rect);
                var start = new DiagramPoint(x + Math.Max(5, rowIndent / 2), y + rect.Height);
                y += rect.Height + 4;
                Walk(node.Children, depth + 1, start);
                if (icon is not null && icon is not ("file" or "folder"))
                    issues.Add(new("unresolved-icon", node.Id, $"Icon '{icon}' is shown as an unresolved-icon indicator."));
            }
        }
    }

    private static void Mindmap(Builder b, OutlineDocument document)
    {
        if (document.Roots.Count == 0) return;
        var sizes = new Dictionary<string, DrawingTextSize>();
        var height = new Dictionary<string, double>();
        var rectangles = new Dictionary<string, DiagramRect>();
        var styles = new Dictionary<string, int>();
        foreach (var node in document.Nodes)
        {
            var measured = b.MeasureWrapped(node.Label, 16, 180, markdown: node.Markdown);
            var width = measured.Width + 32;
            var h = measured.Height + 26 + (node.Icon is null ? 0 : 26);
            if (node.Shape is OutlineShape.Circle or OutlineShape.Bang)
                width = h = Math.Sqrt(width * width + h * h) * (node.Shape == OutlineShape.Bang ? 1.4 : 1);
            if (node.Shape == OutlineShape.Hexagon) width *= 1.3;
            if (node.Shape == OutlineShape.Cloud) { width += 22; h += 22; }
            sizes[node.Id] = new(Math.Max(60, width), Math.Max(44, h));
        }
        double Height(OutlineNode node)
        {
            var children = node.Children.Sum(Height) + Math.Max(0, node.Children.Count - 1) * 20;
            return height[node.Id] = Math.Max(sizes[node.Id].Height, children);
        }
        var root = document.Roots[0]; Height(root);
        var tidy = document.GlobalConfiguration["layout"]?.GetValue<string>() == "tidy-tree";
        rectangles[root.Id] = new(0, 0, sizes[root.Id].Width, sizes[root.Id].Height);
        styles[root.Id] = 0;
        if (tidy) PlaceChildren(root.Children, 1, 0, -root.Children.Sum(n => height[n.Id] + 20) / 2, sizes[root.Id].Width, 0);
        else
        {
            var right = root.Children.Where((_, i) => i % 2 == 0).ToArray();
            var left = root.Children.Where((_, i) => i % 2 != 0).ToArray();
            PlaceChildren(right, 1, 0, -right.Sum(n => height[n.Id] + 20) / 2, sizes[root.Id].Width, 0);
            PlaceChildren(left, -1, 0, -left.Sum(n => height[n.Id] + 20) / 2, 0, 1);
        }
        foreach (var node in document.Nodes)
            if (document.Positions.TryGetValue(node.Id, out var position))
                rectangles[node.Id] = rectangles[node.Id] with { X = position.X, Y = position.Y };
        foreach (var node in document.Nodes)
        {
            var r = rectangles[node.Id];
            foreach (var child in node.Children)
            {
                var c = rectangles[child.Id];
                b.Line(child.Id + "-branch", "Node", child.Id, [new(r.X + r.Width / 2, r.Y + r.Height / 2), new(c.X + c.Width / 2, c.Y + c.Height / 2)], b.Scale(styles.GetValueOrDefault(child.Id)), 2);
            }
        }
        foreach (var node in document.Nodes)
        {
            var r = rectangles[node.Id];
            var color = b.Scale(styles.GetValueOrDefault(node.Id));
            switch (node.Shape)
            {
                case OutlineShape.Circle:
                    b.Circle(node.Id, "Node", node.Id, new(r.X + r.Width / 2, r.Y + r.Height / 2), r.Width / 2, color, b.Ink); break;
                case OutlineShape.Hexagon:
                    b.Polygon(node.Id, "Node", node.Id, [new(r.X + r.Width * .2, r.Y), new(r.X + r.Width * .8, r.Y), new(r.X + r.Width, r.Y + r.Height / 2), new(r.X + r.Width * .8, r.Y + r.Height), new(r.X + r.Width * .2, r.Y + r.Height), new(r.X, r.Y + r.Height / 2)], color, b.Ink); break;
                case OutlineShape.Bang:
                    var points = Enumerable.Range(0, 24).Select(i =>
                    {
                        var radius = r.Width / 2 * (i % 2 == 0 ? 1 : .72);
                        return new DiagramPoint(r.X + r.Width / 2 + radius * Math.Cos(i * Math.PI / 12), r.Y + r.Height / 2 + radius * Math.Sin(i * Math.PI / 12));
                    }).ToArray();
                    b.Polygon(node.Id, "Node", node.Id, points, color, b.Ink); break;
                case OutlineShape.Cloud:
                    b.Path(node.Id, "Node", node.Id, Cloud(r), color, b.Ink); break;
                case OutlineShape.Rounded:
                case OutlineShape.Default:
                    b.Path(node.Id, "Node", node.Id, Rounded(r, 10), color, b.Ink); break;
                default: b.Box(node.Id, "Node", node.Id, r, color, b.Ink); break;
            }
            var label = b.MeasureWrapped(node.Label, 16, 180, markdown: node.Markdown);
            var iconSpace = node.Icon is null ? 0 : 26;
            if (node.Icon is not null) DrawIcon(b, node.Id, node.Icon, r.X + (r.Width - 20) / 2, r.Y + (r.Height - label.Height - iconSpace) / 2, 20, b.Ink);
            b.Text(node.Id + "-label", "Node", node.Id, node.Label, 16, r.X + (r.Width - label.Width) / 2, r.Y + (r.Height - label.Height + iconSpace) / 2, 180, false, b.ScaleLabel(styles.GetValueOrDefault(node.Id), color), markdown: node.Markdown);
            b.Hit("Node", node.Id, r);
        }
        void PlaceChildren(IReadOnlyList<OutlineNode> nodes, int direction, int depth, double top, double edge, int palette)
        {
            for (var index = 0; index < nodes.Count; index++)
            {
                var node = nodes[index];
                var size = sizes[node.Id];
                var x = direction > 0 ? edge + 70 : edge - 70 - size.Width;
                var y = top + (height[node.Id] - size.Height) / 2;
                rectangles[node.Id] = new(x, y, size.Width, size.Height); styles[node.Id] = depth == 0 ? palette + index * 2 : palette;
                PlaceChildren(node.Children, direction, depth + 1, top, direction > 0 ? x + size.Width : x, styles[node.Id]);
                top += height[node.Id] + 20;
            }
        }
    }

    public static string? ResolveTreeIcon(OutlineDocument document, OutlineNode node)
    {
        if (node.Icon is "" or "none") return null;
        var icon = node.Icon;
        if (icon is null && document.Configuration["showIcons"]?.GetValue<bool>() != true) return null;
        if (icon is null)
        {
            if (node.Directory) return "folder";
            icon = (document.Configuration["filenameIcons"] as JsonObject)?[node.Label]?.GetValue<string>();
            if (icon is null)
            {
                var extension = Path.GetExtension(node.Label);
                icon = (document.Configuration["extensionIcons"] as JsonObject)?[extension]?.GetValue<string>();
            }
            icon ??= "file";
        }
        if (icon == "none") return null;
        if (icon is not ("file" or "folder") && !icon.Contains(':') && document.Configuration["defaultIconPack"]?.GetValue<string>() is { Length: > 0 } pack)
            icon = pack + ":" + icon;
        return icon;
    }
    private static void DrawIcon(Builder b, string owner, string icon, double x, double y, double size, ClassColor color)
    {
        if (icon == "folder") b.Polygon(owner + "-icon", "Node", owner,
            [new(x, y + size * .2), new(x + size * .4, y + size * .2), new(x + size * .5, y + size * .35), new(x + size, y + size * .35), new(x + size, y + size), new(x, y + size)], color, color);
        else if (icon == "file")
        {
            b.Box(owner + "-icon", "Node", owner, new(x + 2, y, size * .75, size), null, color);
            b.Line(owner + "-icon-lines", "Node", owner, [new(x + 5, y + size * .4), new(x + size * .6, y + size * .4)], color, 1);
        }
        else b.Text(owner + "-icon", "Node", owner, "?", size, x, y, size + 6, true, color);
    }
    private static IReadOnlyList<DrawingPathPart> Rounded(DiagramRect r, double radius) => [
        new(DrawingPathKind.Move, new(r.X + radius, r.Y)), new(DrawingPathKind.Line, new(r.X + r.Width - radius, r.Y)),
        new(DrawingPathKind.Cubic, new(r.X + r.Width, r.Y + radius), new(r.X + r.Width, r.Y), new(r.X + r.Width, r.Y)),
        new(DrawingPathKind.Line, new(r.X + r.Width, r.Y + r.Height - radius)),
        new(DrawingPathKind.Cubic, new(r.X + r.Width - radius, r.Y + r.Height), new(r.X + r.Width, r.Y + r.Height), new(r.X + r.Width, r.Y + r.Height)),
        new(DrawingPathKind.Line, new(r.X + radius, r.Y + r.Height)),
        new(DrawingPathKind.Cubic, new(r.X, r.Y + r.Height - radius), new(r.X, r.Y + r.Height), new(r.X, r.Y + r.Height)),
        new(DrawingPathKind.Line, new(r.X, r.Y + radius)),
        new(DrawingPathKind.Cubic, new(r.X + radius, r.Y), new(r.X, r.Y), new(r.X, r.Y)), new(DrawingPathKind.Close, default),
    ];
    private static IReadOnlyList<DrawingPathPart> Cloud(DiagramRect r)
    {
        var points = Enumerable.Range(0, 16).Select(i => new DiagramPoint(
            r.X + r.Width / 2 + r.Width / 2 * (i % 2 == 0 ? 1 : .9) * Math.Cos(i * Math.PI / 8),
            r.Y + r.Height / 2 + r.Height / 2 * (i % 2 == 0 ? 1 : .9) * Math.Sin(i * Math.PI / 8))).ToArray();
        return ChartDiagramLayout.Rounded(points, .3);
    }
    private static DiagramRect Union(DiagramRect a, DiagramRect b)
    {
        var x = Math.Min(a.X, b.X); var y = Math.Min(a.Y, b.Y);
        return new(x, y, Math.Max(a.X + a.Width, b.X + b.Width) - x, Math.Max(a.Y + a.Height, b.Y + b.Height) - y);
    }
    private static double Number(JsonObject value, string key, double fallback) => value[key] is { } node ? ChartConfiguration.ParseNumber(ChartConfiguration.Raw(node)) : fallback;
    private static ClassColor Color(JsonObject value, string key, ClassColor fallback) =>
        value[key] is { } node && SequenceColors.TryParse(ChartConfiguration.Raw(node), out var color) ? color : fallback;

    internal sealed class Builder(DrawingTextMeasure? measure, JsonObject theme, JsonObject global, string? style)
    {
        private readonly List<DrawingFigure> _figures = new();
        private readonly List<DrawingLabel> _labels = new();
        private readonly List<DrawingRegion> _regions = new();
        public DiagramPoint OriginOffset { get; private set; }
        public IReadOnlyDictionary<string, DiagramRect> NodeBounds => _regions.Where(r => r.Owner.Role == "Node")
            .ToDictionary(r => r.Owner.Id, r => r.Bounds, StringComparer.Ordinal);
        public ClassColor Background => style?.Contains("dark", StringComparison.OrdinalIgnoreCase) == true ? new(31, 32, 32) : new(255, 255, 255);
        public ClassColor Ink => style?.Contains("dark", StringComparison.OrdinalIgnoreCase) == true ? new(239, 242, 248) : new(32, 45, 62);
        private DrawingFont Font(double size, bool bold, bool italic, string? family = null, int? weight = null) => new(family ?? global["fontFamily"]?.GetValue<string>() ?? theme["fontFamily"]?.GetValue<string>() ?? "Segoe UI", size, bold ? 700 : weight ?? 400, italic);
        public ClassColor Scale(int index) => Color(theme, "cScale" + ((index % 12 + 12) % 12), DefaultScale(index));
        public ClassColor ScaleLabel(int index, ClassColor fill) => Color(theme, "cScaleLabel" + ((index % 12 + 12) % 12),
            fill.Red * .2126 + fill.Green * .7152 + fill.Blue * .0722 > 145 ? new(20, 28, 40) : new(255, 255, 255));
        public ClassColor Tint(ClassColor color) => new((byte)((color.Red + Background.Red * 3) / 4), (byte)((color.Green + Background.Green * 3) / 4), (byte)((color.Blue + Background.Blue * 3) / 4));
        private ClassColor DefaultScale(int index) { SequenceColors.TryParse(ChartConfiguration.Palette[(index % ChartConfiguration.Palette.Length + ChartConfiguration.Palette.Length) % ChartConfiguration.Palette.Length], out var color); return color; }
        private DrawingTextSize Measure(string text, DrawingFont font)
        {
            var value = measure?.Invoke(text, font) ?? new(text.EnumerateRunes().Count() * font.Size * .6, font.Size * 1.4);
            return double.IsFinite(value.Width) && double.IsFinite(value.Height) && value.Width >= 0 && value.Height >= 0 ? value
                : throw new ArgumentException("Text measurement must be finite and nonnegative.");
        }
        private List<List<(string Text, DrawingFont Font, DrawingTextSize Size)>> Wrap(string text, double size, double maxWidth, bool bold, bool italic, bool markdown, string? family = null, int? weight = null)
        {
            var runs = markdown ? EntityDiagramText.NameRuns(text) : new[] { new EntityTextRun(text, bold, italic) };
            var lines = new List<List<(string, DrawingFont, DrawingTextSize)>> { new() };
            var width = 0d;
            foreach (var run in runs)
            {
                var font = Font(size, bold || run.Bold, italic || run.Italic, family, weight);
                foreach (Match token in Regex.Matches(run.Text, @"\r?\n|[^\S\r\n]+|\S+"))
                {
                    if (token.Value is "\n" or "\r\n") { lines.Add(new()); width = 0; continue; }
                    var measured = Measure(token.Value, font);
                    if (width > 0 && width + measured.Width > maxWidth && !string.IsNullOrWhiteSpace(token.Value)) { lines.Add(new()); width = 0; }
                    lines[^1].Add((token.Value, font, measured)); width += measured.Width;
                }
            }
            return lines;
        }
        public DrawingTextSize MeasureWrapped(string text, double size, double width, bool bold = false, bool italic = false, bool markdown = false, string? family = null, int? weight = null)
        {
            var lines = Wrap(text, size, width, bold, italic, markdown, family, weight);
            return new(lines.Max(l => l.Sum(t => t.Item3.Width)), lines.Sum(l => l.Select(t => t.Item3.Height).DefaultIfEmpty(size * 1.4).Max()));
        }
        public void Text(string id, string role, string owner, string text, double size, double x, double y, double width, bool bold = false, ClassColor? color = null, bool italic = false, bool markdown = false, string? family = null, string alignment = "left", ClassColor? outline = null, double outlineWidth = 0, int? weight = null)
        {
            var row = 0;
            var lines = Wrap(text, size, width, bold, italic, markdown, family, weight);
            var blockWidth = lines.Max(line => line.Sum(token => token.Size.Width));
            foreach (var line in lines)
            {
                var lineWidth = line.Sum(token => token.Size.Width);
                var left = x + (alignment == "right" ? blockWidth - lineWidth : alignment == "center" ? (blockWidth - lineWidth) / 2 : 0);
                foreach (var token in line)
                {
                    _labels.Add(new(id + "-" + row++, new(role, owner), token.Text, token.Font, color ?? Ink, new(left, y, token.Size.Width, token.Size.Height),
                        Outline: outline, OutlineWidth: outlineWidth));
                    left += token.Size.Width;
                }
                y += line.Select(t => t.Size.Height).DefaultIfEmpty(size * 1.4).Max();
            }
        }
        public void RotatedText(string id, string role, string owner, string text, double size, double x, double y, double rotation, ClassColor? color = null)
        {
            var font = Font(size, false, false);
            var measured = Measure(text, font);
            _labels.Add(new(id, new(role, owner), text, font, color ?? Ink, new(x, y, measured.Width, measured.Height), Rotation: rotation));
        }
        public void Box(string id, string role, string owner, DiagramRect bounds, ClassColor? fill, ClassColor? stroke, double strokeWidth = 1, bool dashed = false) =>
            _figures.Add(new(id, new(role, owner), DrawingFigureKind.Rectangle, bounds, [], [], fill, stroke, strokeWidth, dashed));
        public void Circle(string id, string role, string owner, DiagramPoint center, double radius, ClassColor? fill, ClassColor? stroke) =>
            _figures.Add(new(id, new(role, owner), DrawingFigureKind.Ellipse, new(center.X - radius, center.Y - radius, radius * 2, radius * 2), [], [], fill, stroke));
        public void Line(string id, string role, string owner, IReadOnlyList<DiagramPoint> points, ClassColor color, double width) =>
            _figures.Add(new(id, new(role, owner), DrawingFigureKind.Polyline, Bounds(points), points, [], null, color, width));
        public void Polygon(string id, string role, string owner, IReadOnlyList<DiagramPoint> points, ClassColor fill, ClassColor stroke) =>
            _figures.Add(new(id, new(role, owner), DrawingFigureKind.Polygon, Bounds(points), points, [], fill, stroke));
        public void Path(string id, string role, string owner, IReadOnlyList<DrawingPathPart> path, ClassColor? fill, ClassColor stroke)
        {
            var points = path.Where(p => p.Kind != DrawingPathKind.Close).SelectMany(p => p.Kind == DrawingPathKind.Cubic ? new[] { p.End, p.Control1, p.Control2 } : new[] { p.End }).ToArray();
            _figures.Add(new(id, new(role, owner), DrawingFigureKind.Path, Bounds(points), [], path, fill, stroke));
        }
        public void Hit(string role, string owner, DiagramRect bounds, string? parent = null) => _regions.Add(new(new(role, owner, parent), DrawingHitKind.Rectangle, bounds, []));
        public void HitLine(string role, string owner, IReadOnlyList<DiagramPoint> points) =>
            _regions.Add(new(new(role, owner), DrawingHitKind.Polyline, Bounds(points), points));
        private static DiagramRect Bounds(IReadOnlyList<DiagramPoint> points) => new(points.Min(p => p.X), points.Min(p => p.Y), points.Max(p => p.X) - points.Min(p => p.X), points.Max(p => p.Y) - points.Min(p => p.Y));
        public DrawingScene Finish()
        {
            var bounds = _figures.Select(f => f.Bounds).Concat(_labels.Select(l =>
            {
                var outline = l.Outline is null ? 0 : l.OutlineWidth / 2;
                var bounds = new DiagramRect(l.Bounds.X - outline, l.Bounds.Y - outline, l.Bounds.Width + outline * 2, l.Bounds.Height + outline * 2);
                if (l.Rotation == 0) return bounds;
                var angle = l.Rotation * Math.PI / 180;
                var width = Math.Abs(bounds.Width * Math.Cos(angle)) + Math.Abs(bounds.Height * Math.Sin(angle));
                var height = Math.Abs(bounds.Width * Math.Sin(angle)) + Math.Abs(bounds.Height * Math.Cos(angle));
                return new DiagramRect(l.Bounds.X + (l.Bounds.Width - width) / 2, l.Bounds.Y + (l.Bounds.Height - height) / 2, width, height);
            })).DefaultIfEmpty(new(0, 0, 200, 120)).Aggregate(Union);
            var x = 20 - bounds.X; var y = 20 - bounds.Y;
            OriginOffset = new(x, y);
            DiagramRect Shift(DiagramRect r) => r with { X = r.X + x, Y = r.Y + y };
            var figures = _figures.Select(f => f with
            {
                Bounds = Shift(f.Bounds), Points = f.Points.Select(p => new DiagramPoint(p.X + x, p.Y + y)).ToArray(),
                Path = f.Path.Select(p => p with { End = new(p.End.X + x, p.End.Y + y), Control1 = new(p.Control1.X + x, p.Control1.Y + y), Control2 = new(p.Control2.X + x, p.Control2.Y + y) }).ToArray(),
            }).ToList();
            figures.Insert(0, new("background", new("Diagram", ""), DrawingFigureKind.Rectangle, new(0, 0, bounds.Width + 40, bounds.Height + 40), [], [], Background, null));
            return new(bounds.Width + 40, bounds.Height + 40, figures, _labels.Select(l => l with { Bounds = Shift(l.Bounds) }).ToArray(),
                _regions.Select(r => r with { Bounds = Shift(r.Bounds), Points = r.Points.Select(p => new DiagramPoint(p.X + x, p.Y + y)).ToArray() }).ToArray(), measure is null);
        }
    }
}
