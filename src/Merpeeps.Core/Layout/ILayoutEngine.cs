using Merpeeps.Core.Model;

namespace Merpeeps.Core.Layout;

/// <summary>
/// One way of arranging a diagram.
///
/// <para>Mermaid ships several layout engines as plugins (dagre, ELK,
/// tidy-tree) because no single algorithm suits every diagram: a call graph
/// wants layers, an org chart wants a tree, a network wants a force
/// simulation. This is the same idea for the editor, implemented natively
/// rather than by shelling out to JavaScript.</para>
///
/// <para><b>Every engine must be deterministic.</b> Coordinates are written
/// into the user's file, so a layout that varies between runs would produce a
/// diff every time the document is opened. Anything random must be seeded from
/// the model itself.</para>
/// </summary>
public interface ILayoutEngine
{
    /// <summary>Stable id, recorded as <c>layout.engine</c> in the metadata.</summary>
    string Id { get; }

    /// <summary>Name shown in the editor.</summary>
    string Name { get; }

    /// <summary>One line explaining when to reach for this engine.</summary>
    string Description { get; }

    /// <summary>
    /// Position every node that does not already have an explicit position.
    /// Nodes the user placed by hand are never moved.
    /// </summary>
    void Apply(DiagramModel model, LayoutOptions options);
}

/// <summary>Shared tuning. Engines ignore what does not apply to them.</summary>
public sealed record LayoutOptions(
    double NodeSpacing = 60,
    double LayerSpacing = 110,
    int CrossingPasses = 4)
{
    public static readonly LayoutOptions Default = new();
}

/// <summary>The engines available to the editor, the CLI and agents.</summary>
public static class LayoutEngines
{
    private static readonly ILayoutEngine[] All =
    [
        new LayeredEngine(),
        new TreeEngine(),
        new ForceEngine(),
        new CircularEngine(),
        new RadialEngine(),
        new GridEngine(),
    ];

    /// <summary>Registered engines, in the order they should be offered.</summary>
    public static IReadOnlyList<ILayoutEngine> Available => All;

    /// <summary>What runs when nothing is chosen.</summary>
    public static ILayoutEngine Default => All[0];

    /// <summary>
    /// Look up by id. Unknown ids fall back to the default rather than
    /// throwing: a file may name an engine a future version added, and losing
    /// the diagram over it would be worse than laying it out differently.
    /// </summary>
    public static ILayoutEngine ById(string? id) =>
        All.FirstOrDefault(e =>
            string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase)) ?? Default;

    /// <summary>True when <paramref name="id"/> names an engine we implement.</summary>
    public static bool IsKnown(string? id) =>
        id is not null &&
        All.Any(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));

    public static void Apply(DiagramModel model, string? engineId,
        LayoutOptions? options = null) =>
        ById(engineId).Apply(model, options ?? LayoutOptions.Default);
}

/// <summary>Helpers every engine needs.</summary>
internal static class LayoutSupport
{
    /// <summary>Nodes this engine is allowed to move.</summary>
    internal static List<DiagramNode> Movable(DiagramModel m) =>
        m.Nodes.Where(n => !n.HasExplicitPosition).ToList();

    /// <summary>Adjacency over node indices, ignoring self-loops.</summary>
    internal static List<int>[] Adjacency(DiagramModel m, bool directed)
    {
        var index = m.Nodes.Select((n, i) => (n, i))
                           .ToDictionary(t => t.n.Id, t => t.i);
        var adj = new List<int>[m.Nodes.Count];
        for (var i = 0; i < adj.Length; i++) adj[i] = [];

        foreach (var e in m.Edges)
        {
            if (!index.TryGetValue(e.From, out var a)) continue;
            if (!index.TryGetValue(e.To, out var b)) continue;
            if (a == b) continue;
            adj[a].Add(b);
            if (!directed) adj[b].Add(a);
        }
        return adj;
    }

    /// <summary>
    /// Shift the laid-out nodes so the diagram starts near the origin.
    /// Without this a force layout can drift into negative space and open
    /// off-screen.
    /// </summary>
    internal static void Normalise(IReadOnlyList<DiagramNode> nodes, double margin = 0)
    {
        if (nodes.Count == 0) return;
        var minX = nodes.Min(n => n.X);
        var minY = nodes.Min(n => n.Y);
        foreach (var n in nodes)
        {
            n.X = Math.Round(n.X - minX + margin, 2);
            n.Y = Math.Round(n.Y - minY + margin, 2);
        }
    }
}
