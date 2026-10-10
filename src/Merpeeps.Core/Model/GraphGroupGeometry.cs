namespace Merpeeps.Core.Model;

public static class GraphGroupGeometry
{
    public static bool IsVisible(DiagramModel model, DiagramSubgraph group) =>
        model.ClassOptions?.HierarchicalNamespaces != false || group.IsExplicit;

    public static IReadOnlyList<DiagramSubgraph> Descendants(DiagramModel model, DiagramSubgraph group)
    {
        if (!model.Subgraphs.Contains(group)) throw new ArgumentException("Group no longer exists.", nameof(group));
        var result = new List<DiagramSubgraph> { group };
        var seen = new HashSet<string>(StringComparer.Ordinal) { group.Id };
        for (var i = 0; i < result.Count; i++)
            foreach (var child in model.Subgraphs.Where(g => g.ParentId == result[i].Id))
            {
                if (!seen.Add(child.Id)) throw new ArgumentException("Group nesting contains a cycle.");
                result.Add(child);
            }
        return result;
    }

    public static IReadOnlyList<DiagramNode> Members(DiagramModel model, DiagramSubgraph group)
    {
        var ids = Descendants(model, group).Select(g => g.Id).ToHashSet(StringComparer.Ordinal);
        return model.Nodes.Where(n => (model.ClassOptions is null ? n.SubgraphId : ClassDiagramGeometry.NamespaceFor(model, n))
            is { } scope && ids.Contains(scope)).ToArray();
    }

    public static bool IsEmpty(DiagramModel model, DiagramSubgraph group) =>
        Members(model, group).Count == 0 && !model.Subgraphs.Any(g => g.ParentId == group.Id);

    public static DiagramRect Bounds(DiagramModel model, DiagramSubgraph group)
    {
        if (!IsEmpty(model, group) && model.RenderedGeometry is { } rendered &&
            rendered.Groups.TryGetValue(group.Id, out var frame) && rendered.GroupDelta(model, group) is { } delta)
            return frame.Bounds with { X = frame.Bounds.X + delta.X, Y = frame.Bounds.Y + delta.Y };
        if (model.EntityOptions is not null) return EntityDiagramGeometry.GroupBounds(model, group);
        return Bounds(model, group, new(StringComparer.Ordinal));
    }

    private static DiagramRect Bounds(DiagramModel model, DiagramSubgraph group, HashSet<string> path)
    {
        if (!path.Add(group.Id)) throw new ArgumentException("Group nesting contains a cycle.");
        var rectangles = Members(model, group).Select(n => new DiagramRect(n.X, n.Y, n.Width, n.Height)).ToList();
        if (model.ClassOptions?.HierarchicalNamespaces != false)
            foreach (var child in model.Subgraphs.Where(g => g.ParentId == group.Id))
                rectangles.Add(Bounds(model, child, path));
        path.Remove(group.Id);
        if (rectangles.Count == 0)
            return model.GroupLayouts.GetValueOrDefault(group.Id, new(24 + model.Subgraphs.IndexOf(group) * 280, 24, 240, 100));
        var x = rectangles.Min(r => r.X) - 18;
        var y = rectangles.Min(r => r.Y) - 34;
        return new(x, y, rectangles.Max(r => r.X + r.Width) - x + 18, rectangles.Max(r => r.Y + r.Height) - y + 18);
    }
}
