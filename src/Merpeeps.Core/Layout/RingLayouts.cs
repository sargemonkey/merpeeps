using Merpeeps.Core.Model;

namespace Merpeeps.Core.Layout;

/// <summary>
/// Places nodes evenly around a circle.
///
/// <para>The right shape for diagrams with no hierarchy to show: a service mesh,
/// a peer-to-peer network, a state machine whose cycles are the point. A layered
/// engine forced onto a cyclic graph invents a top and a bottom that the diagram
/// does not have, and the reader takes that ordering as meaningful.</para>
///
/// <para>Ordering around the ring is by connectivity — the best-connected node
/// first, then its neighbours — so related nodes sit together and chords stay
/// short. Ties break on node id, because determinism is not negotiable when the
/// output is written into a file.</para>
/// </summary>
public sealed class CircularEngine : ILayoutEngine
{
    public string Id => "circular";
    public string Name => "Circular";
    public string Description => "Even ring — for meshes and cycles with no hierarchy";

    public void Apply(DiagramModel model, LayoutOptions options)
    {
        var movable = LayoutSupport.Movable(model);
        if (movable.Count == 0) return;

        if (movable.Count == 1)
        {
            movable[0].X = 0;
            movable[0].Y = 0;
            return;
        }

        var sequence = RingOrder(model, movable);

        // Size the circle so neighbours clear each other: the chord between two
        // adjacent slots must exceed the widest node plus spacing.
        var widest = movable.Max(n => Math.Max(n.Width, n.Height));
        var step = 2 * Math.PI / sequence.Count;
        var minRadius = (widest + options.NodeSpacing) / (2 * Math.Sin(step / 2));
        var radius = Math.Max(minRadius, widest * 1.2);

        for (var i = 0; i < sequence.Count; i++)
        {
            // Start at the top and go clockwise, which is how people read a ring.
            var angle = -Math.PI / 2 + i * step;
            sequence[i].X = Math.Round(radius * Math.Cos(angle), 2);
            sequence[i].Y = Math.Round(radius * Math.Sin(angle), 2);
        }

        LayoutSupport.Normalise(movable, margin: 20);
    }

    /// <summary>
    /// Walk the graph from its best-connected node so neighbours land next to
    /// each other on the ring. Falls back to declaration order for anything the
    /// walk never reaches.
    /// </summary>
    private static List<DiagramNode> RingOrder(DiagramModel model, List<DiagramNode> movable)
    {
        var allowed = movable.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        var adjacency = LayoutSupport.Adjacency(model, directed: false);
        var byIndex = model.Nodes.Select((n, i) => (n, i)).ToDictionary(t => t.i, t => t.n);

        var degree = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (index, node) in byIndex)
            degree[node.Id] = adjacency[index].Count(j => allowed.Contains(byIndex[j].Id));

        var remaining = movable
            .OrderByDescending(n => degree.GetValueOrDefault(n.Id))
            .ThenBy(n => n.Id, StringComparer.Ordinal)
            .ToList();

        var placed = new List<DiagramNode>(remaining.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var indexOf = model.Nodes.Select((n, i) => (n.Id, i))
                                 .ToDictionary(t => t.Id, t => t.i, StringComparer.Ordinal);

        while (placed.Count < remaining.Count)
        {
            var seed = remaining.FirstOrDefault(n => !seen.Contains(n.Id));
            if (seed is null) break;

            var queue = new Queue<DiagramNode>();
            queue.Enqueue(seed);
            seen.Add(seed.Id);

            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                placed.Add(node);

                if (!indexOf.TryGetValue(node.Id, out var i)) continue;

                var neighbours = adjacency[i]
                    .Select(j => byIndex[j])
                    .Where(n => allowed.Contains(n.Id) && !seen.Contains(n.Id))
                    .OrderByDescending(n => degree.GetValueOrDefault(n.Id))
                    .ThenBy(n => n.Id, StringComparer.Ordinal);

                foreach (var neighbour in neighbours)
                {
                    seen.Add(neighbour.Id);
                    queue.Enqueue(neighbour);
                }
            }
        }

        return placed;
    }
}

/// <summary>
/// Concentric rings around a centre: the most connected node in the middle, its
/// neighbours on the first ring, theirs on the next.
///
/// <para>Suits diagrams with one obvious focus — a platform surrounded by its
/// consumers, a database and everything that reads it. A tree layout says
/// "these are levels of a hierarchy"; a radial layout says "these are distances
/// from a thing", which is often the truer claim.</para>
/// </summary>
public sealed class RadialEngine : ILayoutEngine
{
    public string Id => "radial";
    public string Name => "Radial";
    public string Description => "Concentric rings by distance from the busiest node";

    public void Apply(DiagramModel model, LayoutOptions options)
    {
        var movable = LayoutSupport.Movable(model);
        if (movable.Count == 0) return;

        var allowed = movable.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        var adjacency = LayoutSupport.Adjacency(model, directed: false);
        var byIndex = model.Nodes.Select((n, i) => (n, i)).ToDictionary(t => t.i, t => t.n);
        var indexOf = model.Nodes.Select((n, i) => (n.Id, i))
                                 .ToDictionary(t => t.Id, t => t.i, StringComparer.Ordinal);

        // Centre on the best-connected node; ties break on id so the choice is
        // reproducible rather than dependent on declaration order.
        var centre = movable
            .OrderByDescending(n => indexOf.TryGetValue(n.Id, out var i) ? adjacency[i].Count : 0)
            .ThenBy(n => n.Id, StringComparer.Ordinal)
            .First();

        var ring = BreadthFirstRings(movable, allowed, adjacency, byIndex, indexOf, centre);

        var widest = movable.Max(n => Math.Max(n.Width, n.Height));
        var spacing = widest + options.NodeSpacing;

        foreach (var (depth, members) in ring.OrderBy(r => r.Key))
        {
            if (depth == 0)
            {
                members[0].X = 0;
                members[0].Y = 0;
                continue;
            }

            // Grow the radius enough that a crowded ring does not overlap.
            var step = 2 * Math.PI / members.Count;
            var needed = members.Count < 2
                ? spacing * depth
                : spacing / (2 * Math.Sin(step / 2));
            var radius = Math.Max(spacing * depth, needed);

            for (var i = 0; i < members.Count; i++)
            {
                var angle = -Math.PI / 2 + i * step;
                members[i].X = Math.Round(radius * Math.Cos(angle), 2);
                members[i].Y = Math.Round(radius * Math.Sin(angle), 2);
            }
        }

        LayoutSupport.Normalise(movable, margin: 20);
    }

    private static Dictionary<int, List<DiagramNode>> BreadthFirstRings(
        List<DiagramNode> movable,
        HashSet<string> allowed,
        List<int>[] adjacency,
        Dictionary<int, DiagramNode> byIndex,
        Dictionary<string, int> indexOf,
        DiagramNode centre)
    {
        var rings = new Dictionary<int, List<DiagramNode>>();
        var depth = new Dictionary<string, int>(StringComparer.Ordinal) { [centre.Id] = 0 };
        var queue = new Queue<DiagramNode>();
        queue.Enqueue(centre);

        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            var d = depth[node.Id];
            (rings.TryGetValue(d, out var list) ? list : rings[d] = []).Add(node);

            if (!indexOf.TryGetValue(node.Id, out var i)) continue;

            var neighbours = adjacency[i]
                .Select(j => byIndex[j])
                .Where(n => allowed.Contains(n.Id) && !depth.ContainsKey(n.Id))
                .OrderBy(n => n.Id, StringComparer.Ordinal);

            foreach (var neighbour in neighbours)
            {
                depth[neighbour.Id] = d + 1;
                queue.Enqueue(neighbour);
            }
        }

        // Anything disconnected goes on an outer ring rather than at the origin,
        // where it would sit on top of the centre node.
        var stranded = movable.Where(n => !depth.ContainsKey(n.Id))
                              .OrderBy(n => n.Id, StringComparer.Ordinal).ToList();
        if (stranded.Count > 0)
        {
            var outer = rings.Count == 0 ? 1 : rings.Keys.Max() + 1;
            rings[outer] = stranded;
        }

        return rings;
    }
}
