namespace Merpeeps.Core.Model;

public enum EntityEndpointKind { Entity, Subgraph }
public sealed record EntityEndpoint(string Id, string Name, EntityEndpointKind Kind, DiagramRect Bounds);
public sealed record EntityMarkerLine(DiagramPoint Start, DiagramPoint End);
public sealed record EntityMarkerCircle(DiagramPoint Center, double Radius);
public sealed record EntityMarkerGeometry(IReadOnlyList<EntityMarkerLine> Lines, IReadOnlyList<EntityMarkerCircle> Circles, double Length);

public static class EntityDiagramStyle
{
    /// <summary>Uses the same renderer-independent paint values as class diagrams, not class semantics.</summary>
    public static ClassResolvedStyle Resolve(DiagramNode node, bool showMerpeepsStyling = true)
    {
        var style = ClassDiagramStyle.ResolveValues(node, node.Entity?.ResolvedStyle
            ?? throw new ArgumentException("Select a typed entity.", nameof(node)), showMerpeepsStyling);
        if (showMerpeepsStyling && !string.IsNullOrWhiteSpace(node.Icon))
            return style with { Diagnostics = style.Diagnostics.Append("ER icons are preserved but have no native table rendering.").ToArray() };
        return style;
    }
}

public static class EntityDiagramGeometry
{
    public static IReadOnlyList<double> Columns(DiagramNode node, double fontSize) =>
        Columns(node, fontSize, text => text.Length * fontSize * .55);

    public static IReadOnlyList<double> Columns(DiagramNode node, double fontSize, Func<string, double> measure)
    {
        ArgumentNullException.ThrowIfNull(measure);
        if (!double.IsFinite(fontSize) || fontSize <= 0) throw new ArgumentOutOfRangeException(nameof(fontSize));
        var content = EntityDiagramText.Content(node);
        double Required(Func<EntityAttributeRow, string> text) =>
            content.Attributes.Select(a => Width(text(a)) + 14).DefaultIfEmpty(30).Max();
        var widths = new[] { Required(a => a.Type), Required(a => a.Name), Required(a => a.Keys), Required(a => a.Comment ?? "") };
        var total = widths.Sum();
        if (total < node.Width) widths[1] += node.Width - total;
        return widths;

        double Width(string value)
        {
            var width = measure(value);
            return double.IsFinite(width) && width >= 0 ? width
                : throw new ArgumentException("Text measurement must return a finite nonnegative width.", nameof(measure));
        }
    }

    public static EntityEndpoint Endpoint(DiagramModel model, string id)
    {
        var node = model.Node(id);
        var group = model.Subgraphs.SingleOrDefault(g => g.Id == id);
        if (node is not null && group is not null) throw new ArgumentException("An endpoint cannot be both an entity and a subgraph.");
        if (node?.Entity is not null)
            return new(id, EntityDiagramText.Content(node).Name, EntityEndpointKind.Entity, new(node.X, node.Y, node.Width, node.Height));
        if (group is not null)
            return new(id, group.Title ?? group.Id, EntityEndpointKind.Subgraph, GroupBounds(model, group));
        throw new ArgumentException($"ER endpoint '{id}' does not exist.");
    }

    public static DiagramRect GroupBounds(DiagramModel model, DiagramSubgraph group) => Bounds(model, group, new(StringComparer.Ordinal));

    private static DiagramRect Bounds(DiagramModel model, DiagramSubgraph group, HashSet<string> path)
    {
        if (!path.Add(group.Id)) throw new ArgumentException("ER subgraph nesting contains a cycle.");
        var rectangles = model.Nodes.Where(n => n.SubgraphId == group.Id)
            .Select(n => new DiagramRect(n.X, n.Y, n.Width, n.Height)).ToList();
        foreach (var child in model.Subgraphs.Where(g => g.ParentId == group.Id))
            rectangles.Add(Bounds(model, child, path));
        path.Remove(group.Id);
        if (rectangles.Count == 0)
        {
            if (model.GroupLayouts.TryGetValue(group.Id, out var explicitFrame)) return explicitFrame;
            if (model.EntityOptions?.EmptyGroupLayouts.TryGetValue(group.Id, out var frame) == true) return frame;
            var index = model.Subgraphs.IndexOf(group);
            return new(24 + index * 280, 24, 240, 100);
        }
        var x = rectangles.Min(r => r.X) - 20;
        var y = rectangles.Min(r => r.Y) - 36;
        return new(x, y, rectangles.Max(r => r.X + r.Width) - x + 20, rectangles.Max(r => r.Y + r.Height) - y + 20);
    }

    public static DiagramPoint Center(EntityEndpoint endpoint) =>
        new(endpoint.Bounds.X + endpoint.Bounds.Width / 2, endpoint.Bounds.Y + endpoint.Bounds.Height / 2);

    public static DiagramPoint Anchor(EntityEndpoint endpoint, DiagramPoint toward)
    {
        var center = Center(endpoint);
        var dx = toward.X - center.X;
        var dy = toward.Y - center.Y;
        if (Math.Abs(dx) + Math.Abs(dy) < 1e-9) return center;
        var factor = Math.Min(dx == 0 ? double.MaxValue : endpoint.Bounds.Width / 2 / Math.Abs(dx),
            dy == 0 ? double.MaxValue : endpoint.Bounds.Height / 2 / Math.Abs(dy));
        return new(center.X + dx * factor, center.Y + dy * factor);
    }

    public static IReadOnlyList<DiagramPoint> Route(DiagramModel model, DiagramEdge edge)
    {
        var from = Endpoint(model, edge.From);
        var to = Endpoint(model, edge.To);
        if (from.Id == to.Id)
        {
            var r = from.Bounds;
            var distance = 44 + edge.Ordinal * 20;
            return new[]
            {
                new DiagramPoint(r.X + r.Width, r.Y + r.Height * .3),
                new DiagramPoint(r.X + r.Width + distance, r.Y + r.Height * .3),
                new DiagramPoint(r.X + r.Width + distance, r.Y + r.Height + distance),
                new DiagramPoint(r.X + r.Width * .7, r.Y + r.Height + distance),
                new DiagramPoint(r.X + r.Width * .7, r.Y + r.Height),
            };
        }
        var start = Anchor(from, Center(to));
        var end = Anchor(to, Center(from));
        if (edge.Ordinal == 0) return new[] { start, end };
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 1e-9) throw new ArgumentException("Relationship endpoints overlap; move them apart before routing.");
        var offset = 28 * edge.Ordinal;
        return new[] { start, new DiagramPoint((start.X + end.X) / 2 - dy * offset / length, (start.Y + end.Y) / 2 + dx * offset / length), end };
    }

    public static EntityMarkerGeometry Marker(EntityCardinality cardinality, DiagramPoint tip, DiagramPoint toward)
    {
        if (!Enum.IsDefined(cardinality)) throw new ArgumentException("Unknown cardinality.");
        var dx = toward.X - tip.X;
        var dy = toward.Y - tip.Y;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance < 1e-9) return new(Array.Empty<EntityMarkerLine>(), Array.Empty<EntityMarkerCircle>(), 0);
        dx /= distance; dy /= distance;
        DiagramPoint Point(double along, double sideways) => new(tip.X + dx * along - dy * sideways, tip.Y + dy * along + dx * sideways);
        var lines = new List<EntityMarkerLine>();
        var circles = new List<EntityMarkerCircle>();
        var many = cardinality is EntityCardinality.ZeroOrMore or EntityCardinality.OneOrMore;
        if (many)
        {
            lines.Add(new(Point(1, -6), Point(11, 0)));
            lines.Add(new(Point(1, 6), Point(11, 0)));
            lines.Add(new(Point(1, 0), Point(11, 0)));
        }
        else lines.Add(new(Point(4, -6), Point(4, 6)));
        if (cardinality is EntityCardinality.ZeroOrOne or EntityCardinality.ZeroOrMore)
            circles.Add(new(Point(18, 0), 4));
        else lines.Add(new(Point(17, -6), Point(17, 6)));
        return new(lines.AsReadOnly(), circles.AsReadOnly(), 24);
    }
}
