using Merpeeps.Core;
using Merpeeps.Core.Layout;
using Merpeeps.Core.Model;
using Xunit;

namespace Merpeeps.Core.Tests;

/// <summary>
/// The circular and radial engines.
///
/// <para>Both exist because a layered layout forced onto a graph with no
/// hierarchy invents a top and a bottom that the diagram does not have, and
/// readers take that ordering as meaningful. A ring says "these are peers"; a
/// radial layout says "these are distances from a thing".</para>
/// </summary>
public class RingLayoutTests
{
    private const string Mesh = """
        flowchart LR
            a[Alpha] --> b[Beta]
            b --> c[Gamma]
            c --> d[Delta]
            d --> a
        """;

    private const string Hub = """
        flowchart LR
            core[Platform] --> one[Consumer 1]
            core --> two[Consumer 2]
            core --> three[Consumer 3]
            one --> leaf[Downstream]
        """;

    private static DiagramModel LaidOut(string source, string engine)
    {
        var model = MerpeepsDocument.Load(source);
        LayoutEngines.Apply(model, engine);
        return model;
    }

    [Theory]
    [InlineData("circular")]
    [InlineData("radial")]
    public void TheEngineIsRegisteredAndReachable(string id)
    {
        Assert.True(LayoutEngines.IsKnown(id));
        Assert.Equal(id, LayoutEngines.ById(id).Id);
        Assert.NotEmpty(LayoutEngines.ById(id).Description);
    }

    /// <summary>
    /// Non-negotiable for every engine: coordinates are written into files, so a
    /// layout that varied between runs would produce a diff each time a document
    /// was opened.
    /// </summary>
    [Theory]
    [InlineData("circular")]
    [InlineData("radial")]
    public void LayoutIsDeterministic(string engine)
    {
        var first = LaidOut(Mesh, engine);
        var second = LaidOut(Mesh, engine);

        foreach (var node in first.Nodes)
        {
            Assert.Equal(node.X, second.Node(node.Id)!.X);
            Assert.Equal(node.Y, second.Node(node.Id)!.Y);
        }
    }

    [Theory]
    [InlineData("circular")]
    [InlineData("radial")]
    public void EveryNodeIsPlacedAndNothingOverlaps(string engine)
    {
        var model = LaidOut(Mesh, engine);

        var seen = new HashSet<(double, double)>();
        foreach (var n in model.Nodes)
            Assert.True(seen.Add((n.X, n.Y)), $"{n.Id} landed on another node");

        Assert.Equal(4, seen.Count);
    }

    [Theory]
    [InlineData("circular")]
    [InlineData("radial")]
    public void NothingLandsInNegativeSpace(string engine)
    {
        var model = LaidOut(Mesh, engine);

        // A layout that drifts negative opens off-screen.
        Assert.All(model.Nodes, n => Assert.True(n.X >= 0 && n.Y >= 0));
    }

    [Theory]
    [InlineData("circular")]
    [InlineData("radial")]
    public void HandPlacedNodesAreNeverMoved(string engine)
    {
        var model = MerpeepsDocument.Load(Mesh);
        var pinned = model.Node("a")!;
        pinned.X = 999;
        pinned.Y = 888;
        pinned.HasExplicitPosition = true;

        LayoutEngines.Apply(model, engine);

        Assert.Equal(999, pinned.X);
        Assert.Equal(888, pinned.Y);
    }

    [Theory]
    [InlineData("circular")]
    [InlineData("radial")]
    public void ASingleNodeIsHandled(string engine)
    {
        var model = LaidOut("flowchart LR\n    only[Only]\n", engine);

        Assert.Single(model.Nodes);
    }

    [Theory]
    [InlineData("circular")]
    [InlineData("radial")]
    public void DisconnectedNodesAreStillPlacedApart(string engine)
    {
        var model = LaidOut("flowchart LR\n    a[A] --> b[B]\n    lonely[Lonely]\n", engine);

        var lonely = model.Node("lonely")!;
        Assert.All(model.Nodes.Where(n => n.Id != "lonely"),
            n => Assert.False(n.X == lonely.X && n.Y == lonely.Y));
    }

    // ------------------------------------------------------------- circular

    /// <summary>
    /// The defining property: every node the same distance from the centre of
    /// the arrangement. Without it this is just another scatter.
    /// </summary>
    [Fact]
    public void CircularPlacesEveryNodeOnOneRing()
    {
        var model = LaidOut(Mesh, "circular");

        var cx = model.Nodes.Average(n => n.X);
        var cy = model.Nodes.Average(n => n.Y);

        var radii = model.Nodes
            .Select(n => Math.Sqrt(Math.Pow(n.X - cx, 2) + Math.Pow(n.Y - cy, 2)))
            .ToList();

        Assert.All(radii, r => Assert.True(Math.Abs(r - radii[0]) < 1.0,
            $"radii differ: {string.Join(", ", radii.Select(x => x.ToString("F1")))}"));
    }

    [Fact]
    public void CircularKeepsNeighboursApartEnoughNotToCollide()
    {
        var model = LaidOut(Mesh, "circular");

        foreach (var a in model.Nodes)
        foreach (var b in model.Nodes.Where(x => x.Id != a.Id))
        {
            var dx = Math.Abs(a.X - b.X);
            var dy = Math.Abs(a.Y - b.Y);
            Assert.True(dx > 1 || dy > 1, $"{a.Id} and {b.Id} are on top of each other");
        }
    }

    // --------------------------------------------------------------- radial

    /// <summary>
    /// The busiest node goes in the middle — that is the claim the layout makes,
    /// so it had better be true.
    /// </summary>
    [Fact]
    public void RadialPutsTheBusiestNodeAtTheCentre()
    {
        var model = LaidOut(Hub, "radial");

        var cx = model.Nodes.Average(n => n.X);
        var cy = model.Nodes.Average(n => n.Y);

        var closest = model.Nodes
            .OrderBy(n => Math.Pow(n.X - cx, 2) + Math.Pow(n.Y - cy, 2))
            .First();

        Assert.Equal("core", closest.Id);
    }

    /// <summary>
    /// Distance from the centre should track distance through the graph: a
    /// node two hops out sits further than one that is one hop out.
    /// </summary>
    [Fact]
    public void RadialPlacesFurtherNodesOnOuterRings()
    {
        var model = LaidOut(Hub, "radial");

        var core = model.Node("core")!;
        double From(string id)
        {
            var n = model.Node(id)!;
            return Math.Sqrt(Math.Pow(n.X - core.X, 2) + Math.Pow(n.Y - core.Y, 2));
        }

        Assert.True(From("leaf") > From("one"),
            "the two-hop node should sit outside the one-hop node");
    }

    [Fact]
    public void RadialKeepsOneHopNeighboursOnTheSameRing()
    {
        var model = LaidOut(Hub, "radial");

        var core = model.Node("core")!;
        double From(string id)
        {
            var n = model.Node(id)!;
            return Math.Sqrt(Math.Pow(n.X - core.X, 2) + Math.Pow(n.Y - core.Y, 2));
        }

        Assert.True(Math.Abs(From("one") - From("two")) < 1.0);
        Assert.True(Math.Abs(From("one") - From("three")) < 1.0);
    }

    // --------------------------------------------------------- body integrity

    [Theory]
    [InlineData("circular")]
    [InlineData("radial")]
    public void LayingOutNeverTouchesTheDiagramBody(string engine)
    {
        var model = MerpeepsDocument.Load(Mesh);
        LayoutEngines.Apply(model, engine);
        foreach (var n in model.Nodes) n.HasExplicitPosition = true;
        model.LayoutEngine = engine;

        // SavePositions throws if the body would change, so this asserts itself.
        var saved = MerpeepsDocument.SavePositions(Mesh, model);

        Assert.Equal(MerpeepsDoc.BodyOf(Mesh), MerpeepsDoc.BodyOf(saved));
        Assert.Contains($"engine: {engine}", saved, StringComparison.Ordinal);
    }
}
