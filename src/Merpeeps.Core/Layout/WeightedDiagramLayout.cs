using System.Globalization;
using System.Text.RegularExpressions;
using Merpeeps.Core.Model;
using Merpeeps.Core.Parsing;

namespace Merpeeps.Core.Layout;

public sealed record WeightedLayout(DrawingScene Scene, IReadOnlyList<DataDiagramDiagnostic> Diagnostics);
public static class WeightedDiagramLayout
{
    public static WeightedLayout Create(DiagramModel model, DrawingTextMeasure? measure = null, string? themeOverride = null)
    {
        if (model.Sankey is { } flow) return Sankey(flow, measure, themeOverride ?? model.Theme);
        if (model.Treemap is { } tree) return Treemap(tree, measure, themeOverride ?? model.Theme);
        throw new ArgumentException("Choose a weighted flow or treemap.");
    }
    private static WeightedLayout Sankey(SankeyDocument document, DrawingTextMeasure? measure, string? theme)
    {
        SankeyDiagramAdapter.Validate(document);
        var width = Number(document.Configuration, "width", 800);
        var height = Number(document.Configuration, "height", 500);
        var nodeWidth = Number(document.Configuration, "nodeWidth", 10);
        var padding = Number(document.Configuration, "nodePadding", 12);
        var showValues = document.Configuration["showValues"]?.GetValue<bool>() ?? true;
        var align = document.Configuration["nodeAlignment"]?.GetValue<string>() ?? "justify";
        var linkColor = document.Configuration["linkColor"]?.GetValue<string>() ?? "gradient";
        var outlined = document.Configuration["labelStyle"]?.GetValue<string>() == "outlined";
        var b = new DataDiagramLayouts.Builder(measure, document.ThemeVariables, document.GlobalConfiguration, theme);
        var incoming = document.Nodes.ToDictionary(n => n.Id, n => document.Links.Where(l => l.To == n.Id).ToArray());
        var outgoing = document.Nodes.ToDictionary(n => n.Id, n => document.Links.Where(l => l.From == n.Id).ToArray());
        var degree = incoming.ToDictionary(p => p.Key, p => p.Value.Length);
        var ready = new Queue<string>(degree.Where(p => p.Value == 0).Select(p => p.Key));
        var depth = document.Nodes.ToDictionary(n => n.Id, _ => 0);
        var order = new List<string>();
        while (ready.TryDequeue(out var id))
        {
            order.Add(id);
            foreach (var link in outgoing[id]) { depth[link.To] = Math.Max(depth[link.To], depth[id] + 1); if (--degree[link.To] == 0) ready.Enqueue(link.To); }
        }
        var reverse = document.Nodes.ToDictionary(n => n.Id, _ => 0);
        foreach (var id in order.AsEnumerable().Reverse()) foreach (var link in outgoing[id]) reverse[id] = Math.Max(reverse[id], reverse[link.To] + 1);
        var last = Math.Max(1, depth.Values.DefaultIfEmpty(0).Max());
        var layer = document.Nodes.ToDictionary(n => n.Id, n => align switch
        {
            "left" => depth[n.Id], "right" => last - reverse[n.Id],
            "center" => incoming[n.Id].Length > 0 ? depth[n.Id] : outgoing[n.Id].Length > 0 ? Math.Max(0, outgoing[n.Id].Min(l => depth[l.To]) - 1) : 0,
            "justify" => outgoing[n.Id].Length == 0 ? last : depth[n.Id],
            _ => throw new ArgumentException("Unknown Sankey node alignment."),
        });
        var values = document.Nodes.ToDictionary(n => n.Id, n => Math.Max(incoming[n.Id].Sum(l => l.Value), outgoing[n.Id].Sum(l => l.Value)));
        var centralLayer = 0;
        var maximumValue = 0d;
        foreach (var node in document.Nodes)
            if (values[node.Id] > maximumValue)
            {
                maximumValue = values[node.Id];
                centralLayer = layer[node.Id];
            }
        var scale = document.Nodes.GroupBy(n => layer[n.Id]).Select(group =>
        {
            var sum = group.Sum(n => values[n.Id]);
            return sum <= 0 ? 1 : Math.Max(1, height - padding * Math.Max(0, group.Count() - 1)) / sum;
        }).DefaultIfEmpty(1).Min();
        var bounds = new Dictionary<string, DiagramRect>();
        var colors = new Dictionary<string, ClassColor>();
        foreach (var group in document.Nodes.GroupBy(n => layer[n.Id]))
        {
            var y = 20d;
            foreach (var node in group)
            {
                var color = (document.Configuration["nodeColors"] as System.Text.Json.Nodes.JsonObject)?[node.Label]?.ToString();
                colors[node.Id] = color is not null && SequenceColors.TryParse(color, out var parsed) ? parsed : b.Scale(document.Nodes.IndexOf(node));
                bounds[node.Id] = new(20 + group.Key * (width - nodeWidth) / last, y, nodeWidth, values[node.Id] * scale);
                y += values[node.Id] * scale + padding;
            }
        }
        var fromOffsets = document.Nodes.ToDictionary(n => n.Id, _ => 0d);
        var toOffsets = document.Nodes.ToDictionary(n => n.Id, _ => 0d);
        var figures = new List<DrawingFigure>();
        foreach (var link in document.Links)
        {
            var a = bounds[link.From]; var z = bounds[link.To];
            var thickness = link.Value * scale;
            var y1 = a.Y + fromOffsets[link.From]; var y2 = z.Y + toOffsets[link.To];
            fromOffsets[link.From] += thickness; toOffsets[link.To] += thickness;
            if (thickness <= 0) continue;
            var x1 = a.X + a.Width; var x2 = z.X; var middle = (x1 + x2) / 2;
            DrawingPathPart[] path = [
                new(DrawingPathKind.Move, new(x1, y1)),
                new(DrawingPathKind.Cubic, new(x2, y2), new(middle, y1), new(middle, y2)),
                new(DrawingPathKind.Line, new(x2, y2 + thickness)),
                new(DrawingPathKind.Cubic, new(x1, y1 + thickness), new(middle, y2 + thickness), new(middle, y1 + thickness)),
                new(DrawingPathKind.Close, default),
            ];
            var color = linkColor switch { "source" or "gradient" => colors[link.From], "target" => colors[link.To], _ => SequenceColors.TryParse(linkColor, out var custom) ? custom : throw new ArgumentException("Invalid Sankey link color.") };
            var owner = new DrawingOwner("Flow", link.Id);
            figures.Add(new(link.Id, owner, DrawingFigureKind.Path, new(x1, Math.Min(y1, y2), Math.Max(0, x2 - x1), Math.Abs(y2 - y1) + thickness), [], path,
                color with { Alpha = 140 }, null, Gradient: linkColor == "gradient" ? new(colors[link.From] with { Alpha = 140 }, colors[link.To] with { Alpha = 140 }) : null));
        }
        foreach (var node in document.Nodes)
        {
            var r = bounds[node.Id]; b.Box(node.Id, "FlowNode", node.Id, r, colors[node.Id], null);
            var label = node.Label + (showValues ? "\n" + values[node.Id].ToString("G6", CultureInfo.InvariantCulture) : "");
            var left = outlined ? layer[node.Id] < centralLayer : r.X - 20 >= width / 2;
            var labelWidth = b.MeasureWrapped(label, 14, 180).Width;
            var outline = b.Background;
            var outlineValue = document.ThemeVariables["mainBkg"] ?? document.ThemeVariables["background"];
            if (outlined && outlineValue is not null) SequenceColors.TryParse(ChartConfiguration.Raw(outlineValue), out outline);
            b.Text(node.Id + "-label", "FlowNode", node.Id, label, 14, left ? r.X - 6 - labelWidth : r.X + r.Width + 6,
                r.Y + r.Height / 2, 180, alignment: left ? "right" : "left", outline: outlined ? outline : null, outlineWidth: outlined ? 4 : 0);
            b.Hit("FlowNode", node.Id, new(r.X - 4, r.Y, Math.Max(8, r.Width + 8), Math.Max(8, r.Height)));
        }
        var scene = b.Finish();
        var origin = b.OriginOffset;
        var shifted = figures.Select(f => f with
        {
            Bounds = f.Bounds with { X = f.Bounds.X + origin.X, Y = f.Bounds.Y + origin.Y },
            Path = f.Path.Select(p => p with { End = new(p.End.X + origin.X, p.End.Y + origin.Y), Control1 = new(p.Control1.X + origin.X, p.Control1.Y + origin.Y), Control2 = new(p.Control2.X + origin.X, p.Control2.Y + origin.Y) }).ToArray(),
        }).ToArray();
        var regions = shifted.Select(f => new DrawingRegion(f.Owner, DrawingHitKind.Polygon, f.Bounds, Flatten(f.Path))).Concat(scene.Regions).ToArray();
        var allFigures = scene.Figures.Take(1).Concat(shifted).Concat(scene.Figures.Skip(1)).ToArray();
        return new(scene with { Figures = allFigures, Regions = regions }, []);
    }
    private static WeightedLayout Treemap(TreemapDocument document, DrawingTextMeasure? measure, string? theme)
    {
        TreemapDiagramAdapter.Validate(document);
        var b = new DataDiagramLayouts.Builder(measure, document.ThemeVariables, document.GlobalConfiguration, theme);
        var padding = Number(document.Configuration, "padding", 10); var outside = Number(document.Configuration, "diagramPadding", 8);
        var labelSize = Number(document.Configuration, "labelFontSize", 14); var valueSize = Number(document.Configuration, "valueFontSize", 12);
        var showValues = document.Configuration["showValues"]?.GetValue<bool>() ?? true;
        var width = Math.Max(600, Math.Sqrt(Math.Max(1, document.Nodes.Count())) * Number(document.Configuration, "nodeWidth", 100) * 2);
        var height = Math.Max(400, Math.Sqrt(Math.Max(1, document.Nodes.Count())) * Number(document.Configuration, "nodeHeight", 40) * 3);
        var issues = new List<DataDiagramDiagnostic>();
        Tile(document.Roots, new(outside, outside, width, height), 0);
        return new(b.Finish(), issues);
        void Tile(IReadOnlyList<TreemapNode> nodes, DiagramRect rect, int depth)
        {
            var total = nodes.Sum(n => n.Total);
            if (total <= 0) return;
            var horizontal = rect.Width >= rect.Height;
            var cursor = horizontal ? rect.X : rect.Y;
            for (var index = 0; index < nodes.Count; index++)
            {
                var node = nodes[index]; var fraction = node.Total / total;
                var area = horizontal ? new DiagramRect(cursor, rect.Y, rect.Width * fraction, rect.Height)
                    : new DiagramRect(rect.X, cursor, rect.Width, rect.Height * fraction);
                cursor += horizontal ? area.Width : area.Height;
                var style = new Dictionary<string, string>();
                foreach (var key in node.Classes)
                    foreach (var value in document.Classes[key]) if (value.Value is not null) style[value.Key] = ChartConfiguration.Raw(value.Value);
                var resolved = ClassDiagramStyle.ResolveValues(new DiagramNode { Id = node.Id }, style, false);
                if (resolved.Diagnostics.Count > 0) throw new NotSupportedException(string.Join("\n", resolved.Diagnostics));
                var color = resolved.Fill ?? b.Scale(depth + index);
                b.Box(node.Id, "WeightedNode", node.Id, area, color, resolved.Stroke ?? b.Background, resolved.StrokeWidth);
                b.Hit("WeightedNode", node.Id, area);
                var availableWidth = Math.Max(0, area.Width - padding * 2);
                var availableHeight = Math.Max(0, area.Height - padding * 2);
                var text = b.MeasureWrapped(node.Label, labelSize, Math.Max(1, availableWidth), true);
                var labelFits = text.Width <= availableWidth && text.Height <= availableHeight;
                if (labelFits)
                    b.Text(node.Id + "-label", "WeightedNode", node.Id, node.Label, labelSize, area.X + padding, area.Y + padding, Math.Max(1, availableWidth), true, resolved.TextColor ?? b.ScaleLabel(depth + index, color));
                else issues.Add(new("tile-label", node.Id, "This tile is too small for its full label. Select it to inspect the retained text."));
                if (node.Children.Count > 0)
                    Tile(node.Children, new(area.X + padding, area.Y + padding + text.Height + 4, Math.Max(0, area.Width - padding * 2), Math.Max(0, area.Height - padding * 2 - text.Height - 4)), depth + 1);
                else if (showValues && labelFits)
                {
                    var value = Format(node.Value ?? 0, document.Configuration["valueFormat"]?.ToString() ?? ",");
                    var measured = b.MeasureWrapped(value, valueSize, Math.Max(1, availableWidth));
                    if (measured.Width <= availableWidth && text.Height + measured.Height + 4 <= availableHeight)
                        b.Text(node.Id + "-value", "WeightedNode", node.Id, value, valueSize,
                            area.X + padding, area.Y + padding + text.Height + 4, Math.Max(1, availableWidth), false, resolved.TextColor ?? b.ScaleLabel(depth + index, color));
                    else issues.Add(new("tile-value", node.Id, "This tile is too small for its formatted value. Select it to inspect the retained value."));
                }
            }
        }
    }
    private static IReadOnlyList<DiagramPoint> Flatten(IReadOnlyList<DrawingPathPart> path)
            {
                var points = new List<DiagramPoint>(); var current = new DiagramPoint();
                foreach (var part in path)
                {
                    if (part.Kind is DrawingPathKind.Move or DrawingPathKind.Line) { current = part.End; points.Add(current); }
                    else if (part.Kind == DrawingPathKind.Cubic)
                    {
                        var start = current;
                        for (var i = 1; i <= 24; i++)
                        {
                            var t = i / 24d; var u = 1 - t;
                            points.Add(new(u * u * u * start.X + 3 * u * u * t * part.Control1.X + 3 * u * t * t * part.Control2.X + t * t * t * part.End.X,
                                u * u * u * start.Y + 3 * u * u * t * part.Control1.Y + 3 * u * t * t * part.Control2.Y + t * t * t * part.End.Y));
                        }
                        current = part.End;
                    }
                }
                return points;
            }
    public static string Format(double value, string format)
    {
        if (format == "$0,0") return "$" + value.ToString("N0", CultureInfo.InvariantCulture);
        var match = Regex.Match(format, @"^(?<currency>\$)?(?<comma>,)?(?:\.(?<precision>\d+)(?<kind>[f%]))?$");
        if (!match.Success) throw new NotSupportedException("This D3 value format is preserved but not yet interpreted natively: " + format);
        var precision = match.Groups["precision"].Success ? int.Parse(match.Groups["precision"].Value, CultureInfo.InvariantCulture) : 0;
        if (precision > 15) throw new NotSupportedException("Native numeric formatting supports up to 15 displayed decimal places.");
        var percent = match.Groups["kind"].Value == "%";
        var number = percent ? value * 100 : value;
        return (match.Groups["currency"].Success ? "$" : "") + number.ToString((match.Groups["comma"].Success ? "N" : "F") + precision, CultureInfo.InvariantCulture) + (percent ? "%" : "");
    }
    private static double Number(System.Text.Json.Nodes.JsonObject config, string key, double fallback) =>
        config[key] is { } value ? ChartConfiguration.ParseNumber(ChartConfiguration.Raw(value)) : fallback;
}
