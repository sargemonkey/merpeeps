using System.Text.Json;

namespace Merpeeps.Core.Model;

public sealed record RenderedTextLine(string Text, DiagramRect Bounds);
public sealed record RenderedText(string FontFamily, double FontSize, int FontWeight, IReadOnlyList<RenderedTextLine> Lines, string? Background = null);
public sealed record RenderedNode(DiagramRect Bounds, string Text, RenderedText? Label);
public sealed record RenderedGroup(DiagramRect Bounds, string Text, IReadOnlyList<string> Members,
    string? Fill, string? Stroke, double StrokeWidth, RenderedText? Label);
public sealed record RenderedEdge(IReadOnlyList<DiagramPoint> Points, string Text, RenderedText? Label);

public sealed class RenderedGraphGeometry
{
    public Dictionary<string, RenderedNode> Nodes { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, RenderedGroup> Groups { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, RenderedEdge> Edges { get; } = new(StringComparer.Ordinal);
    public RenderedText? Caption { get; private set; }

    public static RenderedGraphGeometry Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("spec").GetInt32() != 1) throw new FormatException("Unsupported rendered geometry version.");
        var result = new RenderedGraphGeometry();
        if (root.TryGetProperty("caption", out var caption) && caption.ValueKind != JsonValueKind.Null)
            result.Caption = Label(caption);
        foreach (var item in root.GetProperty("nodes").EnumerateObject())
            result.Nodes.Add(item.Name, new(Rect(item.Value), Text(item.Value), Label(item.Value)));
        if (root.TryGetProperty("groups", out var groups))
            foreach (var item in groups.EnumerateObject())
                result.Groups.Add(item.Name, new(Rect(item.Value), Text(item.Value),
                    item.Value.GetProperty("members").EnumerateArray().Select(v => v.GetString() ?? throw new FormatException("Group member identity is missing.")).ToArray(),
                    Optional(item.Value, "fill"), Optional(item.Value, "stroke"),
                    item.Value.TryGetProperty("strokeWidth", out _) ? Number(item.Value, "strokeWidth") : 1, Label(item.Value)));
        if (root.TryGetProperty("edges", out var edges))
            foreach (var item in edges.EnumerateObject())
            {
                var points = item.Value.GetProperty("points").EnumerateArray().Select(p =>
                {
                    if (p.GetArrayLength() != 2) throw new FormatException("A route point needs X and Y.");
                    var x = p[0].GetDouble(); var y = p[1].GetDouble();
                    if (!double.IsFinite(x) || !double.IsFinite(y)) throw new FormatException("Route coordinates must be finite.");
                    return new DiagramPoint(x, y);
                }).ToArray();
                if (points.Length < 2) throw new FormatException("A rendered route needs at least two points.");
                result.Edges.Add(item.Name, new(points, Text(item.Value), Label(item.Value)));
            }
        return result;
    }

    private static string Text(JsonElement item) => Optional(item, "text") ?? "";
    private static string? Optional(JsonElement item, string key) =>
        item.TryGetProperty(key, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetString() : null;
    private static double Number(JsonElement item, string key)
    {
        var value = item.GetProperty(key).GetDouble();
        return double.IsFinite(value) ? value : throw new FormatException($"Geometry '{key}' must be finite.");
    }
    private static DiagramRect Rect(JsonElement item)
    {
        var rect = new DiagramRect(Number(item, "x"), Number(item, "y"), Number(item, "w"), Number(item, "h"));
        if (rect.Width <= 0 || rect.Height <= 0) throw new FormatException("Geometry dimensions must be positive.");
        return rect;
    }
    private static RenderedText? Label(JsonElement item)
    {
        if (!item.TryGetProperty("label", out var label) || label.ValueKind == JsonValueKind.Null) return null;
        var fontSize = Number(label, "fontSize");
        var weight = label.GetProperty("fontWeight").GetInt32();
        if (fontSize <= 0 || weight is < 1 or > 1000) throw new FormatException("Invalid rendered label font.");
        return new(Optional(label, "fontFamily") ?? "Segoe UI", fontSize, weight,
            label.GetProperty("lines").EnumerateArray().Select(line => new RenderedTextLine(Text(line), Rect(line))).ToArray(), Optional(label, "background"));
    }

    public Dictionary<string, object?> ToMetadata()
    {
        Dictionary<string, object?> RectMap(DiagramRect r) => new() { ["x"] = r.X, ["y"] = r.Y, ["w"] = r.Width, ["h"] = r.Height };
        object? LabelMap(RenderedText? label) => label is null ? null : new Dictionary<string, object?>
        {
            ["fontFamily"] = label.FontFamily, ["fontSize"] = label.FontSize, ["fontWeight"] = label.FontWeight,
            ["background"] = label.Background,
            ["lines"] = label.Lines.Select(line =>
            {
                var map = RectMap(line.Bounds); map["text"] = line.Text; return (object?)map;
            }).ToList(),
        };
        return new()
        {
            ["spec"] = 1,
            ["caption"] = Caption is null ? null : new Dictionary<string, object?> { ["label"] = LabelMap(Caption) },
            ["nodes"] = Nodes.ToDictionary(p => p.Key, p =>
            {
                var map = RectMap(p.Value.Bounds); map["text"] = p.Value.Text; map["label"] = LabelMap(p.Value.Label); return (object?)map;
            }),
            ["groups"] = Groups.ToDictionary(p => p.Key, p =>
            {
                var g = p.Value; var map = RectMap(g.Bounds); map["text"] = g.Text;
                map["members"] = g.Members.Cast<object?>().ToList(); map["fill"] = g.Fill; map["stroke"] = g.Stroke;
                map["strokeWidth"] = g.StrokeWidth; map["label"] = LabelMap(g.Label); return (object?)map;
            }),
            ["edges"] = Edges.ToDictionary(p => p.Key, p => (object?)new Dictionary<string, object?>
            {
                ["points"] = p.Value.Points.Select(point => (object?)new List<object?> { point.X, point.Y }).ToList(),
                ["text"] = p.Value.Text, ["label"] = LabelMap(p.Value.Label),
            }),
        };
    }

    public DiagramPoint? NodeDelta(DiagramModel model, string id)
    {
        if (!Nodes.TryGetValue(id, out var original) || model.Node(id) is not { } node ||
            Math.Abs(node.Width - original.Bounds.Width) > .02 || Math.Abs(node.Height - original.Bounds.Height) > .02) return null;
        return new(node.X - original.Bounds.X, node.Y - original.Bounds.Y);
    }
    public DiagramPoint? GroupDelta(DiagramModel model, DiagramSubgraph group)
    {
        if (!Groups.TryGetValue(group.Id, out var original)) return null;
        var members = GraphGroupGeometry.Members(model, group).Select(n => n.Id).Order(StringComparer.Ordinal).ToArray();
        if (!members.SequenceEqual(original.Members.Order(StringComparer.Ordinal))) return null;
        var deltas = members.Select(id => NodeDelta(model, id)).ToArray();
        if (deltas.Any(d => d is null)) return null;
        var delta = deltas.FirstOrDefault() ?? new DiagramPoint(0, 0);
        return deltas.All(d => Math.Abs(d!.Value.X - delta.X) < .02 && Math.Abs(d.Value.Y - delta.Y) < .02) ? delta : null;
    }
    public RenderedText? NodeLabel(DiagramModel model, DiagramNode node) =>
        Nodes.TryGetValue(node.Id, out var original) && node.Display == original.Text && NodeDelta(model, node.Id) is { } delta
            ? Shift(original.Label, delta) : null;
    public RenderedText? GroupLabel(DiagramModel model, DiagramSubgraph group) =>
        Groups.TryGetValue(group.Id, out var original) && (group.Title ?? group.Id) == original.Text && GroupDelta(model, group) is { } delta
            ? Shift(original.Label, delta) : null;
    public DiagramPoint? EdgeDelta(DiagramModel model, DiagramEdge edge)
    {
        if (NodeDelta(model, edge.From) is not { } from || NodeDelta(model, edge.To) is not { } to ||
            Math.Abs(from.X - to.X) >= .02 || Math.Abs(from.Y - to.Y) >= .02) return null;
        return from;
    }
    public RenderedText? EdgeLabel(DiagramModel model, DiagramEdge edge) =>
        Edges.TryGetValue(edge.Key, out var original) && (edge.Label ?? "") == original.Text && EdgeDelta(model, edge) is { } delta
            ? Shift(original.Label, delta) : null;
    private static RenderedText? Shift(RenderedText? label, DiagramPoint delta) => label is null ? null
        : label with { Lines = label.Lines.Select(line => line with
        { Bounds = line.Bounds with { X = line.Bounds.X + delta.X, Y = line.Bounds.Y + delta.Y } }).ToArray() };
}
