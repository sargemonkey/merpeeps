using Merpeeps.Core.Model;

namespace Merpeeps.Core.Layout;

/// <summary>
/// Layered (Sugiyama) layout — the default, and the closest match to what
/// mermaid's own dagre engine produces. Good for anything that flows in one
/// direction: pipelines, call graphs, decision trees.
/// </summary>
public sealed class LayeredEngine : ILayoutEngine
{
    public string Id => "layered";
    public string Name => "Layered";
    public string Description => "Ranks nodes by depth. Best for flows and pipelines.";

    public void Apply(DiagramModel model, LayoutOptions options) =>
        LayeredLayout.Apply(model, new LayeredLayout.Options(
            options.NodeSpacing, options.LayerSpacing, options.CrossingPasses));
}

/// <summary>
/// Tidy tree layout (Reingold–Tilford). Children are centred beneath their
/// parent and subtrees never overlap, which reads far better than a layered
/// layout when the graph really is a hierarchy.
///
/// <para>Graphs that are not trees still work: the spanning forest is taken
/// from a breadth-first walk, and any remaining edges are simply drawn across
/// the tree.</para>
/// </summary>
public sealed class TreeEngine : ILayoutEngine
{
    public string Id => "tree";
    public string Name => "Tree";
    public string Description => "Centres children under parents. Best for hierarchies.";

    public void Apply(DiagramModel model, LayoutOptions options)
    {
        if (model.Nodes.Count == 0) return;

        var adj = LayoutSupport.Adjacency(model, directed: true);
        var undirected = LayoutSupport.Adjacency(model, directed: false);
        var n = model.Nodes.Count;

        // Roots first, so the tree hangs the way the author wrote it. A cyclic
        // graph has none, in which case the first node stands in — deterministic
        // because node order comes from the source.
        var hasParent = new bool[n];
        foreach (var list in adj)
            foreach (var t in list) hasParent[t] = true;

        var roots = Enumerable.Range(0, n).Where(i => !hasParent[i]).ToList();
        if (roots.Count == 0) roots.Add(0);

        var parent = Enumerable.Repeat(-1, n).ToArray();
        var children = new List<int>[n];
        for (var i = 0; i < n; i++) children[i] = [];

        // Breadth-first so the spanning tree is shallow and stable.
        var seen = new bool[n];
        var queue = new Queue<int>();
        foreach (var r in roots) { seen[r] = true; queue.Enqueue(r); }

        while (queue.Count > 0)
        {
            var v = queue.Dequeue();
            foreach (var w in adj[v])
            {
                if (seen[w]) continue;
                seen[w] = true;
                parent[w] = v;
                children[v].Add(w);
                queue.Enqueue(w);
            }
        }

        // Nodes unreachable by direction (or in a cycle) join through any edge,
        // else become roots of their own.
        for (var i = 0; i < n; i++)
        {
            if (seen[i]) continue;
            var attach = undirected[i].FirstOrDefault(x => seen[x], -1);
            seen[i] = true;
            if (attach >= 0) { parent[i] = attach; children[attach].Add(i); }
            else roots.Add(i);
        }

        var horizontal = model.Direction is Direction.LR or Direction.RL;
        var flip = model.Direction is Direction.BT or Direction.RL;

        // Place each root's subtree, then shift it clear of the previous one.
        var cursor = 0.0;
        var depthOf = new int[n];
        var crossOf = new double[n];

        foreach (var root in roots)
        {
            var used = new List<double>();
            var next = cursor;
            PlaceSubtree(root, 0, ref next, children, model, options, horizontal,
                depthOf, crossOf, used);
            cursor = (used.Count > 0 ? used.Max() : cursor) + options.NodeSpacing * 2;
        }

        var maxDepth = n == 0 ? 0 : depthOf.Max();
        foreach (var node in LayoutSupport.Movable(model))
        {
            var i = model.Nodes.IndexOf(node);
            var d = flip ? maxDepth - depthOf[i] : depthOf[i];
            var main = d * (Main(node, horizontal) + options.LayerSpacing);

            if (horizontal) { node.X = main; node.Y = crossOf[i]; }
            else { node.X = crossOf[i]; node.Y = main; }
        }

        LayoutSupport.Normalise(LayoutSupport.Movable(model));
    }

    /// <summary>
    /// Post-order placement: lay out the children first, then centre the parent
    /// over them. A leaf simply takes the next free slot.
    /// </summary>
    private static void PlaceSubtree(
        int v, int depth, ref double cursor, List<int>[] children,
        DiagramModel m, LayoutOptions opt, bool horizontal,
        int[] depthOf, double[] crossOf, List<double> used)
    {
        depthOf[v] = depth;
        var kids = children[v];

        if (kids.Count == 0)
        {
            crossOf[v] = cursor;
            used.Add(cursor + Cross(m.Nodes[v], horizontal));
            cursor += Cross(m.Nodes[v], horizontal) + opt.NodeSpacing;
            return;
        }

        var first = 0.0;
        var last = 0.0;
        for (var i = 0; i < kids.Count; i++)
        {
            PlaceSubtree(kids[i], depth + 1, ref cursor, children, m, opt,
                horizontal, depthOf, crossOf, used);
            if (i == 0) first = crossOf[kids[i]];
            last = crossOf[kids[i]] + Cross(m.Nodes[kids[i]], horizontal);
        }

        crossOf[v] = (first + last) / 2 - Cross(m.Nodes[v], horizontal) / 2;
        used.Add(crossOf[v] + Cross(m.Nodes[v], horizontal));
    }

    private static double Cross(DiagramNode n, bool horizontal) =>
        horizontal ? n.Height : n.Width;

    private static double Main(DiagramNode n, bool horizontal) =>
        horizontal ? n.Width : n.Height;
}

/// <summary>
/// Force-directed layout (Fruchterman–Reingold). Connected nodes attract,
/// all nodes repel, and the whole thing cools over a fixed number of steps.
/// Suits networks and dependency graphs, where there is no natural direction
/// and clustering is the thing worth seeing.
///
/// <para>Deterministic by construction: the starting positions come from a
/// fixed spiral rather than a random seed, and the iteration count is fixed.
/// The same diagram always produces the same picture, which it must, because
/// the coordinates are written to the user's file.</para>
/// </summary>
public sealed class ForceEngine : ILayoutEngine
{
    public string Id => "force";
    public string Name => "Force";
    public string Description => "Simulates attraction and repulsion. Best for networks.";

    private const int Iterations = 300;

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

        var n = model.Nodes.Count;
        var adj = LayoutSupport.Adjacency(model, directed: false);

        // Ideal edge length, scaled to the drawing so bigger graphs spread out.
        var area = n * (160 + options.NodeSpacing) * (56 + options.NodeSpacing);
        var k = Math.Sqrt(area / n);

        var px = new double[n];
        var py = new double[n];

        // A golden-angle spiral: even, deterministic, and free of the symmetric
        // starting configurations that leave a grid stuck in a local minimum.
        const double golden = 2.399963229728653;
        for (var i = 0; i < n; i++)
        {
            var r = k * Math.Sqrt(i + 0.5);
            px[i] = r * Math.Cos(i * golden);
            py[i] = r * Math.Sin(i * golden);
        }

        // Fixed nodes anchor the layout instead of drifting with it.
        for (var i = 0; i < n; i++)
        {
            if (!model.Nodes[i].HasExplicitPosition) continue;
            px[i] = model.Nodes[i].X;
            py[i] = model.Nodes[i].Y;
        }

        var dx = new double[n];
        var dy = new double[n];
        var temperature = k * 2;
        var cooling = temperature / (Iterations + 1);

        for (var step = 0; step < Iterations; step++)
        {
            Array.Clear(dx);
            Array.Clear(dy);

            // Repulsion, every pair.
            for (var i = 0; i < n; i++)
            {
                for (var j = i + 1; j < n; j++)
                {
                    var ux = px[i] - px[j];
                    var uy = py[i] - py[j];
                    var d2 = ux * ux + uy * uy;
                    // Coincident nodes would divide by zero; nudge them apart
                    // along a fixed axis so the result stays reproducible.
                    if (d2 < 1e-6) { ux = (i - j) * 1e-3; uy = 1e-3; d2 = ux * ux + uy * uy; }
                    var d = Math.Sqrt(d2);
                    var force = k * k / d;
                    var fx = ux / d * force;
                    var fy = uy / d * force;
                    dx[i] += fx; dy[i] += fy;
                    dx[j] -= fx; dy[j] -= fy;
                }
            }

            // Attraction along edges.
            for (var i = 0; i < n; i++)
            {
                foreach (var j in adj[i])
                {
                    if (j <= i) continue;   // each edge once
                    var ux = px[i] - px[j];
                    var uy = py[i] - py[j];
                    var d = Math.Sqrt(ux * ux + uy * uy);
                    if (d < 1e-6) continue;
                    var force = d * d / k;
                    var fx = ux / d * force;
                    var fy = uy / d * force;
                    dx[i] -= fx; dy[i] -= fy;
                    dx[j] += fx; dy[j] += fy;
                }
            }

            for (var i = 0; i < n; i++)
            {
                if (model.Nodes[i].HasExplicitPosition) continue;
                var d = Math.Sqrt(dx[i] * dx[i] + dy[i] * dy[i]);
                if (d < 1e-9) continue;
                var limit = Math.Min(d, temperature);
                px[i] += dx[i] / d * limit;
                py[i] += dy[i] / d * limit;
            }

            temperature -= cooling;
        }

        for (var i = 0; i < n; i++)
        {
            if (model.Nodes[i].HasExplicitPosition) continue;
            model.Nodes[i].X = px[i];
            model.Nodes[i].Y = py[i];
        }

        LayoutSupport.Normalise(movable);
    }
}

/// <summary>
/// Grid layout. Ignores the edges entirely and packs nodes into rows.
/// Not clever, but predictable — useful for a legend, an inventory, or as a
/// starting point before arranging by hand.
/// </summary>
public sealed class GridEngine : ILayoutEngine
{
    public string Id => "grid";
    public string Name => "Grid";
    public string Description => "Packs nodes into even rows. Predictable, ignores edges.";

    public void Apply(DiagramModel model, LayoutOptions options)
    {
        var movable = LayoutSupport.Movable(model);
        if (movable.Count == 0) return;

        var columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(movable.Count)));
        var cellW = movable.Max(n => n.Width) + options.NodeSpacing;
        var cellH = movable.Max(n => n.Height) + options.NodeSpacing;

        for (var i = 0; i < movable.Count; i++)
        {
            movable[i].X = Math.Round(i % columns * cellW, 2);
            movable[i].Y = Math.Round(i / columns * cellH, 2);
        }
    }
}
