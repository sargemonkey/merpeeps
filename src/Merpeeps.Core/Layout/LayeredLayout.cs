using Merpeeps.Core.Model;

namespace Merpeeps.Core.Layout;

/// <summary>
/// Deterministic layered (Sugiyama-style) layout, used to seed positions for
/// nodes that carry no Merpeeps coordinates.
///
/// <para>Three phases: assign layers by longest path, order within layers to
/// reduce crossings (median heuristic), then assign coordinates. Fully
/// deterministic — the same model always yields the same picture, which matters
/// because layout output is written into files.</para>
/// </summary>
public static class LayeredLayout
{
    public sealed record Options(
        double NodeSpacing = 60,
        double LayerSpacing = 110,
        int CrossingPasses = 4);

    public static void Apply(DiagramModel m, Options? options = null)
    {
        var opt = options ?? new Options();
        if (m.Nodes.Count == 0) return;

        var index = m.Nodes.Select((n, i) => (n, i))
                           .ToDictionary(t => t.n.Id, t => t.i);
        var outEdges = BuildAdjacency(m, index, forward: true);
        var inEdges = BuildAdjacency(m, index, forward: false);

        // Layering needs a DAG; crossing reduction wants every edge.
        var layer = AssignLayers(m, Acyclic(outEdges));
        ApplySameRank(m, index, layer);
        var order = OrderWithinLayers(m, layer, outEdges, inEdges, opt.CrossingPasses);
        ApplyOrder(m, index, layer, order);
        AssignCoordinates(m, layer, order, opt);
    }

    /// <summary>
    /// Pull each <c>same-rank</c> group onto one layer.
    ///
    /// <para>The group moves to the <em>deepest</em> layer any member reached.
    /// Moving to the shallowest would push nodes above their own predecessors
    /// and turn forward edges into back edges, which looks like a bug in the
    /// diagram rather than a constraint being honoured.</para>
    ///
    /// <para>Members that no longer exist are ignored, and a group with fewer
    /// than two surviving members does nothing. A stale constraint is inert, not
    /// harmful — which is the whole reason to prefer constraints to
    /// coordinates.</para>
    /// </summary>
    private static void ApplySameRank(
        DiagramModel m, Dictionary<string, int> index, int[] layer)
    {
        if (m.Constraints.SameRank.Count == 0) return;

        var known = index.Keys.ToHashSet(StringComparer.Ordinal);

        foreach (var group in LayoutConstraints.Applicable(m.Constraints.SameRank, known))
        {
            var members = group.Select(id => index[id]).ToList();
            var deepest = members.Max(i => layer[i]);
            foreach (var i in members) layer[i] = deepest;
        }
    }

    /// <summary>
    /// Sequence the members of each <c>order</c> group within their layer.
    ///
    /// <para>Only members that ended up on the same layer are reordered; the
    /// constraint expresses left-to-right intent, not a demand to move nodes
    /// between layers. The slots the group already occupies are reused, so
    /// unconstrained neighbours keep their crossing-optimised positions.</para>
    /// </summary>
    private static void ApplyOrder(
        DiagramModel m, Dictionary<string, int> index, int[] layer,
        Dictionary<int, List<int>> order)
    {
        if (m.Constraints.Order.Count == 0) return;

        var known = index.Keys.ToHashSet(StringComparer.Ordinal);

        foreach (var group in LayoutConstraints.Applicable(m.Constraints.Order, known))
        {
            var wanted = group.Select(id => index[id]).ToList();

            foreach (var byLayer in wanted.GroupBy(i => layer[i]))
            {
                if (!order.TryGetValue(byLayer.Key, out var row)) continue;

                var members = byLayer.ToList();
                if (members.Count < 2) continue;

                // Reuse the slots the group already holds, in ascending position,
                // so the group as a whole stays where crossing reduction put it.
                var slots = members.Select(i => row.IndexOf(i)).Where(p => p >= 0)
                                   .OrderBy(p => p).ToList();
                if (slots.Count != members.Count) continue;

                var sequence = wanted.Where(members.Contains).ToList();
                for (var k = 0; k < slots.Count; k++) row[slots[k]] = sequence[k];
            }
        }
    }

    private static List<int>[] BuildAdjacency(
        DiagramModel m, Dictionary<string, int> index, bool forward)
    {
        var adj = new List<int>[m.Nodes.Count];
        for (var i = 0; i < adj.Length; i++) adj[i] = new List<int>();
        foreach (var e in m.Edges)
        {
            if (!index.TryGetValue(e.From, out var a)) continue;
            if (!index.TryGetValue(e.To, out var b)) continue;
            if (a == b) continue;                       // ignore self loops
            if (forward) adj[a].Add(b); else adj[b].Add(a);
        }
        return adj;
    }

    /// <summary>
    /// Drops back edges so the graph is acyclic, the standard first phase of
    /// Sugiyama layout.
    ///
    /// <para>This is not optional for real diagrams. State machines, retry
    /// loops and approval cycles all contain cycles, and longest-path layering
    /// on a cycle never settles — each sweep pushes the members one layer
    /// further down until the iteration cap stops it, leaving a five-state
    /// machine spread over twenty layers. Removing the back edges first makes
    /// layering both correct and quick to converge.</para>
    ///
    /// <para>A dropped edge is only excluded from <em>layering</em>. It is
    /// still drawn, and still counted when reducing crossings.</para>
    /// </summary>
    private static List<int>[] Acyclic(List<int>[] outEdges)
    {
        const int White = 0, Grey = 1, Black = 2;
        var n = outEdges.Length;
        var acyclic = new List<int>[n];
        for (var i = 0; i < n; i++) acyclic[i] = new List<int>();

        var colour = new int[n];
        // Iterative DFS: a deep chain must not overflow the call stack.
        var stack = new Stack<(int Node, int Next)>();

        for (var root = 0; root < n; root++)
        {
            if (colour[root] != White) continue;
            colour[root] = Grey;
            stack.Push((root, 0));

            while (stack.Count > 0)
            {
                var (v, k) = stack.Pop();
                if (k >= outEdges[v].Count) { colour[v] = Black; continue; }

                stack.Push((v, k + 1));
                var w = outEdges[v][k];

                // Grey means w is still open on this path, so v -> w closes a
                // cycle. Every other edge is a tree, forward or cross edge.
                if (colour[w] == Grey) continue;

                acyclic[v].Add(w);
                if (colour[w] != White) continue;
                colour[w] = Grey;
                stack.Push((w, 0));
            }
        }
        return acyclic;
    }

    /// <summary>Longest-path layering. Requires an acyclic adjacency.</summary>
    private static int[] AssignLayers(DiagramModel m, List<int>[] outEdges)
    {
        var n = m.Nodes.Count;
        var layer = new int[n];

        // Sources (no incoming) start at layer 0; others are pushed down.
        // On a DAG this settles in at most n sweeps; the guard is a backstop.
        var changed = true;
        var guard = 0;
        while (changed && guard++ < n + 4)
        {
            changed = false;
            for (var i = 0; i < n; i++)
            {
                foreach (var j in outEdges[i])
                {
                    if (layer[j] >= layer[i] + 1) continue;
                    layer[j] = layer[i] + 1;
                    changed = true;
                }
            }
        }
        return layer;
    }

    /// <summary>Median heuristic to reduce edge crossings. Deterministic.</summary>
    private static Dictionary<int, List<int>> OrderWithinLayers(
        DiagramModel m, int[] layer, List<int>[] outEdges, List<int>[] inEdges,
        int passes)
    {
        var byLayer = new Dictionary<int, List<int>>();
        for (var i = 0; i < layer.Length; i++)
        {
            if (!byLayer.TryGetValue(layer[i], out var list))
                byLayer[layer[i]] = list = new List<int>();
            list.Add(i);
        }

        var pos = new double[layer.Length];
        foreach (var (_, list) in byLayer)
            for (var k = 0; k < list.Count; k++) pos[list[k]] = k;

        var layers = byLayer.Keys.OrderBy(x => x).ToArray();

        for (var p = 0; p < passes; p++)
        {
            var downward = p % 2 == 0;
            foreach (var l in downward ? layers : layers.Reverse())
            {
                var list = byLayer[l];
                var neighbours = downward ? inEdges : outEdges;
                var keyed = list
                    .Select(i => (i, key: Median(neighbours[i], pos, pos[i])))
                    .OrderBy(t => t.key)
                    .ThenBy(t => t.i)          // stable: keeps output deterministic
                    .Select(t => t.i)
                    .ToList();
                byLayer[l] = keyed;
                for (var k = 0; k < keyed.Count; k++) pos[keyed[k]] = k;
            }
        }
        return byLayer;
    }

    private static double Median(List<int> neighbours, double[] pos, double fallback)
    {
        if (neighbours.Count == 0) return fallback;
        var vals = neighbours.Select(x => pos[x]).OrderBy(x => x).ToArray();
        var mid = vals.Length / 2;
        return vals.Length % 2 == 1 ? vals[mid] : (vals[mid - 1] + vals[mid]) / 2.0;
    }

    private static void AssignCoordinates(
        DiagramModel m, int[] layer, Dictionary<int, List<int>> byLayer, Options opt)
    {
        var horizontal = m.Direction is Direction.LR or Direction.RL;
        var flip = m.Direction is Direction.BT or Direction.RL;
        var layers = byLayer.Keys.OrderBy(x => x).ToArray();
        var maxLayer = layers.Length == 0 ? 0 : layers[^1];

        // Width of the widest layer sets the cross-axis extent, so the result
        // is centred rather than ragged.
        var extent = byLayer.Values
            .Select(list => list.Sum(i => Cross(m.Nodes[i], horizontal) + opt.NodeSpacing))
            .DefaultIfEmpty(0).Max();

        foreach (var l in layers)
        {
            var list = byLayer[l];
            var total = list.Sum(i => Cross(m.Nodes[i], horizontal) + opt.NodeSpacing)
                        - opt.NodeSpacing;
            var cursor = (extent - total) / 2.0;

            foreach (var i in list)
            {
                var node = m.Nodes[i];
                if (node.HasExplicitPosition) { cursor += Cross(node, horizontal) + opt.NodeSpacing; continue; }

                var depth = (flip ? maxLayer - l : l)
                            * (Main(node, horizontal) + opt.LayerSpacing);

                if (horizontal) { node.X = depth; node.Y = cursor; }
                else { node.X = cursor; node.Y = depth; }

                cursor += Cross(node, horizontal) + opt.NodeSpacing;
            }
        }
    }

    private static double Cross(DiagramNode n, bool horizontal) =>
        horizontal ? n.Height : n.Width;

    private static double Main(DiagramNode n, bool horizontal) =>
        horizontal ? n.Width : n.Height;
}
