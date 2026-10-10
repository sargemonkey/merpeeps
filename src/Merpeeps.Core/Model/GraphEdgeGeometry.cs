namespace Merpeeps.Core.Model;

public static class GraphEdgeGeometry
{
    public static IReadOnlyList<DiagramPoint> Route(DiagramModel model, DiagramEdge edge)
    {
        if (model.RenderedGeometry is { } rendered && rendered.Edges.TryGetValue(edge.Key, out var imported) &&
            rendered.EdgeDelta(model, edge) is { } delta)
            return imported.Points.Select(p => new DiagramPoint(p.X + delta.X, p.Y + delta.Y)).ToArray();
        var from = model.Node(edge.From); var to = model.Node(edge.To);
        if (from is null || to is null) return [];
        if (model.EdgeWaypoints.TryGetValue(edge.Key, out var bends) && bends.Count > 0)
            return new[] { Anchor(from, bends[0]) }.Concat(bends).Append(Anchor(to, bends[^1])).ToArray();
        return [Anchor(from, new(to.X + to.Width / 2, to.Y + to.Height / 2)),
            Anchor(to, new(from.X + from.Width / 2, from.Y + from.Height / 2))];
    }
    public static DiagramPoint LabelPoint(IReadOnlyList<DiagramPoint> route)
    {
        var lengths = route.Zip(route.Skip(1), (a, b) => Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2))).ToArray();
        var remaining = lengths.Sum() / 2;
        for (var i = 0; i < lengths.Length; i++)
        {
            if (lengths[i] > 0 && remaining <= lengths[i])
            {
                var t = remaining / lengths[i];
                return new(route[i].X + (route[i + 1].X - route[i].X) * t, route[i].Y + (route[i + 1].Y - route[i].Y) * t);
            }
            remaining -= lengths[i];
        }
        return route.Count == 0 ? default : route[0];
    }
    private static DiagramPoint Anchor(DiagramNode node, DiagramPoint toward)
    {
        var cx = node.X + node.Width / 2; var cy = node.Y + node.Height / 2;
        var dx = toward.X - cx; var dy = toward.Y - cy;
        if (Math.Abs(dx) + Math.Abs(dy) < 1e-9) return new(cx, cy);
        var scale = Math.Min(dx == 0 ? double.MaxValue : node.Width / 2 / Math.Abs(dx),
            dy == 0 ? double.MaxValue : node.Height / 2 / Math.Abs(dy));
        return new(cx + dx * scale, cy + dy * scale);
    }
}
