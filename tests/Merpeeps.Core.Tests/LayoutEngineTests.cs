using Merpeeps.Core;
using Merpeeps.Core.Layout;
using Merpeeps.Core.Model;
using Xunit;

namespace Merpeeps.Core.Tests;

/// <summary>
/// Every engine writes coordinates into the user's file, so the properties
/// that matter are the ones a compiler cannot check: the same input must give
/// the same output, nodes must not land on top of each other, and a person's
/// manual placement must survive.
/// </summary>
public class LayoutEngineTests
{
    private const string Flow = """
        flowchart TD
            a[Alpha] --> b[Beta]
            a --> c[Gamma]
            b --> d[Delta]
            c --> d
            d --> e[Epsilon]
        """;

    private const string Tree = """
        flowchart TD
            root[Root] --> l[Left]
            root --> r[Right]
            l --> l1[L1]
            l --> l2[L2]
            r --> r1[R1]
        """;

    private const string Cyclic = """
        stateDiagram-v2
            [*] --> Still
            Still --> Moving : go
            Moving --> Still : halt
            Moving --> Crash
            Crash --> [*]
        """;

    public static TheoryData<string> EngineIds()
    {
        var d = new TheoryData<string>();
        foreach (var e in LayoutEngines.Available) d.Add(e.Id);
        return d;
    }

    public static TheoryData<string, string> EnginesAndSources()
    {
        var d = new TheoryData<string, string>();
        foreach (var e in LayoutEngines.Available)
            foreach (var s in new[] { Flow, Tree, Cyclic })
                d.Add(e.Id, s);
        return d;
    }

    private static DiagramModel LaidOut(string source, string engineId)
    {
        var m = MerpeepsDocument.Load(source);
        foreach (var n in m.Nodes) n.HasExplicitPosition = false;
        LayoutEngines.ById(engineId).Apply(m, LayoutOptions.Default);
        return m;
    }

    // ------------------------------------------------------------ registry

    [Fact]
    public void TheAdvertisedEnginesAreAllDistinctAndResolvable()
    {
        var ids = LayoutEngines.Available.Select(e => e.Id).ToList();

        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(ids, id => Assert.Same(
            LayoutEngines.Available.First(e => e.Id == id), LayoutEngines.ById(id)));
        Assert.All(LayoutEngines.Available, e =>
        {
            Assert.False(string.IsNullOrWhiteSpace(e.Name));
            Assert.False(string.IsNullOrWhiteSpace(e.Description));
        });
    }

    /// <summary>
    /// A file may name an engine a later version introduced. Losing the diagram
    /// over that would be far worse than laying it out differently.
    /// </summary>
    [Fact]
    public void AnUnknownEngineFallsBackInsteadOfThrowing()
    {
        Assert.Same(LayoutEngines.Default, LayoutEngines.ById("no-such-engine"));
        Assert.Same(LayoutEngines.Default, LayoutEngines.ById(null));
        Assert.False(LayoutEngines.IsKnown("manual"));
        Assert.False(LayoutEngines.IsKnown(null));
    }

    // --------------------------------------------------------- determinism

    /// <summary>
    /// The single most important property. Coordinates are written to disk, so
    /// an engine that wobbles would produce a diff every time the document is
    /// opened — and force-directed layouts are the classic offender, because
    /// they normally start from random positions.
    /// </summary>
    [Theory, MemberData(nameof(EnginesAndSources))]
    public void LayoutIsDeterministic(string engineId, string source)
    {
        var a = LaidOut(source, engineId);
        var b = LaidOut(source, engineId);

        foreach (var node in a.Nodes)
        {
            var other = b.Node(node.Id)!;
            Assert.Equal(node.X, other.X, 6);
            Assert.Equal(node.Y, other.Y, 6);
        }
    }

    [Theory, MemberData(nameof(EnginesAndSources))]
    public void EveryNodeGetsAFinitePosition(string engineId, string source)
    {
        foreach (var n in LaidOut(source, engineId).Nodes)
        {
            Assert.True(double.IsFinite(n.X), $"{n.Id} has X = {n.X}");
            Assert.True(double.IsFinite(n.Y), $"{n.Id} has Y = {n.Y}");
        }
    }

    [Theory, MemberData(nameof(EnginesAndSources))]
    public void NoTwoNodesLandOnTheSamePoint(string engineId, string source)
    {
        var seen = new HashSet<(double, double)>();
        foreach (var n in LaidOut(source, engineId).Nodes)
            Assert.True(seen.Add((Math.Round(n.X, 3), Math.Round(n.Y, 3))),
                $"{n.Id} sits exactly on another node at ({n.X}, {n.Y})");
    }

    /// <summary>A layout that opens off-screen looks broken even when it is not.</summary>
    [Theory, MemberData(nameof(EnginesAndSources))]
    public void LayoutStartsNearTheOrigin(string engineId, string source)
    {
        var m = LaidOut(source, engineId);
        Assert.True(m.Nodes.Min(n => n.X) >= -1, "diagram extends left of the origin");
        Assert.True(m.Nodes.Min(n => n.Y) >= -1, "diagram extends above the origin");
    }

    /// <summary>Cycles must not inflate any engine's output, not just the layered one.</summary>
    [Theory, MemberData(nameof(EngineIds))]
    public void CyclesDoNotInflateTheLayout(string engineId)
    {
        var m = LaidOut(Cyclic, engineId);
        var span = Math.Max(
            m.Nodes.Max(n => n.Y) - m.Nodes.Min(n => n.Y),
            m.Nodes.Max(n => n.X) - m.Nodes.Min(n => n.X));

        Assert.True(span <= m.Nodes.Count * 400,
            $"{engineId} spread {m.Nodes.Count} nodes over {span:F0}px");
    }

    // ------------------------------------------------------ manual override

    [Theory, MemberData(nameof(EngineIds))]
    public void ManualPositionsAreNeverMoved(string engineId)
    {
        var m = MerpeepsDocument.Load(Flow);
        foreach (var n in m.Nodes) n.HasExplicitPosition = false;

        var pinned = m.Node("a")!;
        pinned.X = 1234; pinned.Y = 5678;
        pinned.HasExplicitPosition = true;

        LayoutEngines.ById(engineId).Apply(m, LayoutOptions.Default);

        Assert.Equal(1234, pinned.X, 6);
        Assert.Equal(5678, pinned.Y, 6);
    }

    [Theory, MemberData(nameof(EngineIds))]
    public void AnEmptyDiagramIsHandled(string engineId)
    {
        var m = new DiagramModel();
        LayoutEngines.ById(engineId).Apply(m, LayoutOptions.Default);
        Assert.Empty(m.Nodes);
    }

    [Theory, MemberData(nameof(EngineIds))]
    public void ASingleNodeIsHandled(string engineId)
    {
        var m = LaidOut("flowchart TD\n    solo[Only]", engineId);
        Assert.Single(m.Nodes);
        Assert.True(double.IsFinite(m.Nodes[0].X));
    }

    [Theory, MemberData(nameof(EngineIds))]
    public void DisconnectedNodesAreStillPlaced(string engineId)
    {
        var m = LaidOut("flowchart TD\n    a[A]\n    b[B]\n    c[C]", engineId);
        Assert.Equal(3, m.Nodes.Count);
        Assert.Equal(3, m.Nodes.Select(n => (n.X, n.Y)).Distinct().Count());
    }

    // ------------------------------------------------------- engine shapes

    /// <summary>
    /// The tree engine's whole purpose: children centred beneath their parent.
    /// If that does not hold it is just a worse layered layout.
    /// </summary>
    [Fact]
    public void TreeCentresAParentOverItsChildren()
    {
        var m = LaidOut(Tree, "tree");

        var root = m.Node("root")!;
        var l = m.Node("l")!;
        var r = m.Node("r")!;

        var childMid = ((l.X + l.Width / 2) + (r.X + r.Width / 2)) / 2;
        Assert.Equal(childMid, root.X + root.Width / 2, 1);
        Assert.True(root.Y < l.Y, "the root should sit above its children");
    }

    [Fact]
    public void TreeSeparatesSiblingSubtrees()
    {
        var m = LaidOut(Tree, "tree");
        var l1 = m.Node("l1")!;
        var r1 = m.Node("r1")!;

        Assert.True(Math.Abs(l1.X - r1.X) > 20,
            "leaves from different subtrees should not overlap");
    }

    [Fact]
    public void GridPacksIntoRows()
    {
        var m = LaidOut("flowchart TD\n" +
            string.Join("\n", Enumerable.Range(0, 9).Select(i => $"    n{i}[N{i}]")), "grid");

        var rows = m.Nodes.Select(n => Math.Round(n.Y, 1)).Distinct().Count();
        var cols = m.Nodes.Select(n => Math.Round(n.X, 1)).Distinct().Count();

        Assert.Equal(3, rows);
        Assert.Equal(3, cols);
    }

    /// <summary>
    /// A force layout should place connected nodes nearer each other than
    /// unconnected ones — otherwise it is just scattering boxes.
    /// </summary>
    [Fact]
    public void ForcePullsConnectedNodesTogether()
    {
        var m = LaidOut("""
            flowchart TD
                a1 --> a2
                a2 --> a3
                a3 --> a1
                b1 --> b2
                b2 --> b3
                b3 --> b1
            """, "force");

        double Dist(string p, string q)
        {
            var x = m.Node(p)!;
            var y = m.Node(q)!;
            return Math.Sqrt(Math.Pow(x.X - y.X, 2) + Math.Pow(x.Y - y.Y, 2));
        }

        var withinCluster = (Dist("a1", "a2") + Dist("b1", "b2")) / 2;
        var acrossClusters = (Dist("a1", "b1") + Dist("a2", "b2")) / 2;

        Assert.True(acrossClusters > withinCluster,
            $"clusters not separated: within {withinCluster:F0}px, " +
            $"across {acrossClusters:F0}px");
    }

    // -------------------------------------------------------- round-tripping

    /// <summary>
    /// The chosen engine is recorded so a reopened document keeps arranging new
    /// nodes the same way instead of silently reverting to the default.
    /// </summary>
    [Fact]
    public void TheChosenEngineRoundTripsThroughTheFile()
    {
        var m = MerpeepsDocument.Load(Flow);
        m.LayoutEngine = "force";
        var saved = MerpeepsDocument.SavePositions(Flow, m);

        Assert.Contains("engine: force", saved);
        Assert.Equal("force", MerpeepsDocument.Load(saved).LayoutEngine);
    }

    [Fact]
    public void HandPlacedDocumentsStayMarkedManual()
    {
        var m = MerpeepsDocument.Load(Flow);
        var saved = MerpeepsDocument.SavePositions(Flow, m);

        Assert.Contains("engine: manual", saved);
    }

    [Theory, MemberData(nameof(EngineIds))]
    public void LayoutNeverTouchesTheDiagramBody(string engineId)
    {
        var m = LaidOut(Flow, engineId);
        foreach (var n in m.Nodes) n.HasExplicitPosition = true;
        m.LayoutEngine = engineId;

        // SavePositions throws if the body would change, so this asserts itself.
        var saved = MerpeepsDocument.SavePositions(Flow, m);
        Assert.Equal(MerpeepsDoc.BodyOf(Flow), MerpeepsDoc.BodyOf(saved));
    }
}
