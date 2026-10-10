namespace Merpeeps.Core.Model;

public readonly record struct DiagramPoint(double X, double Y);
public readonly record struct DiagramRect(double X, double Y, double Width, double Height);
public sealed record ClassCardRegions(DiagramRect Header, DiagramRect Attributes, DiagramRect Operations, double RequiredHeight);
public sealed record ClassMarkerGeometry(IReadOnlyList<DiagramPoint> Points, bool Closed, bool Filled, double Length);

/// <summary>Shared semantic geometry. Hosts measure text, but do not reinterpret ownership or UML markers.</summary>
public static class ClassDiagramGeometry
{
    public static ClassCardRegions CardRegions(DiagramNode node, ClassDiagramOptions options, double lineHeight)
    {
        if (!double.IsFinite(lineHeight) || lineHeight <= 0) throw new ArgumentOutOfRangeException(nameof(lineHeight));
        var content = ClassDiagramText.Content(node, options);
        if (!content.ShowMemberCompartments)
            return new(new(node.X, node.Y, node.Width, node.Height), default, default, content.Header.Count * lineHeight + 16);
        var headerHeight = content.Header.Count * lineHeight + 16;
        var attributeHeight = Math.Max(1, content.Attributes.Count) * lineHeight + 8;
        var operationHeight = Math.Max(1, content.Operations.Count) * lineHeight + 8;
        return new(
            new(node.X, node.Y, node.Width, headerHeight),
            new(node.X, node.Y + headerHeight, node.Width, attributeHeight),
            new(node.X, node.Y + headerHeight + attributeHeight, node.Width, Math.Max(operationHeight, node.Height - headerHeight - attributeHeight)),
            headerHeight + attributeHeight + operationHeight);
    }

    public static DiagramPoint Center(DiagramNode node) => node.Class?.Kind == ClassNodeKind.Lollipop
        ? new(node.X + node.Width / 2, node.Y + 12)
        : new(node.X + node.Width / 2, node.Y + node.Height / 2);

    public static DiagramPoint Anchor(DiagramNode node, DiagramPoint toward)
    {
        var center = Center(node);
        var dx = toward.X - center.X;
        var dy = toward.Y - center.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 1e-9) return center;
        var factor = node.Class?.Kind == ClassNodeKind.Lollipop ? 8 / length
            : Math.Min(dx == 0 ? double.MaxValue : node.Width / 2 / Math.Abs(dx),
                dy == 0 ? double.MaxValue : node.Height / 2 / Math.Abs(dy));
        return new(center.X + dx * factor, center.Y + dy * factor);
    }

    public static ClassMarkerGeometry Marker(ClassRelationEnd end, DiagramPoint tip, DiagramPoint toward, double size = 12)
    {
        if (end is ClassRelationEnd.None or ClassRelationEnd.Lollipop) return new(Array.Empty<DiagramPoint>(), false, false, 0);
        var dx = toward.X - tip.X;
        var dy = toward.Y - tip.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 1e-9) return new(Array.Empty<DiagramPoint>(), false, false, 0);
        dx /= length; dy /= length;
        DiagramPoint Point(double along, double side) => new(tip.X + dx * along - dy * side, tip.Y + dy * along + dx * side);
        return end switch
        {
            ClassRelationEnd.Arrow => new(new[] { Point(size, size * .4), tip, Point(size, -size * .4) }, false, false, size),
            ClassRelationEnd.Triangle => new(new[] { tip, Point(size, size * .55), Point(size, -size * .55) }, true, false, size),
            ClassRelationEnd.Composition or ClassRelationEnd.Aggregation => new(
                new[] { tip, Point(size * .6, size * .4), Point(size * 1.2, 0), Point(size * .6, -size * .4) },
                true, end == ClassRelationEnd.Composition, size * 1.2),
            _ => throw new ArgumentOutOfRangeException(nameof(end)),
        };
    }

    public static string? NamespaceFor(DiagramModel model, DiagramNode node)
    {
        var owner = node;
        if (node.Class?.Kind == ClassNodeKind.Note && node.Class.NoteFor is { } target)
            owner = model.Node(target) ?? node;
        else if (node.Class?.Kind == ClassNodeKind.Lollipop)
        {
            var edge = model.Edges.SingleOrDefault(e => e.From == node.Id || e.To == node.Id);
            if (edge is not null) owner = model.Node(edge.From == node.Id ? edge.To : edge.From) ?? node;
        }
        var id = owner.SubgraphId;
        if (model.ClassOptions?.HierarchicalNamespaces != false) return id;
        while (id is not null)
        {
            var group = model.Subgraphs.Single(g => g.Id == id);
            if (group.IsExplicit) return id;
            id = group.ParentId;
        }
        return null;
    }

    public static IReadOnlyList<DiagramNode> NamespaceMembers(DiagramModel model, DiagramSubgraph group)
    {
        var prefix = group.Id + ".";
        return model.Nodes.Where(n => NamespaceFor(model, n) is { } scope &&
            (scope == group.Id || model.ClassOptions?.HierarchicalNamespaces != false && scope.StartsWith(prefix, StringComparison.Ordinal))).ToArray();
    }
}
