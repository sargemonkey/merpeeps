using Merpeeps.Core;
using Merpeeps.Core.Layout;
using Merpeeps.Core.Model;
using Xunit;

namespace Merpeeps.Core.Tests;

/// <summary>
/// Layout has no "wrong answer" the compiler can catch — a broken layout still
/// produces numbers, still saves, and still passes every round-trip test. These
/// assert the properties a human would notice immediately on screen.
/// </summary>
public class LayoutTests
{
    private const string StateMachine = """
        stateDiagram-v2
            [*] --> Still
            Still --> [*]
            Still --> Moving : go
            Moving --> Still : halt
            Moving --> Crash : hit wall
            Crash --> [*]
        """;

    /// <summary>
    /// Regression: longest-path layering on a cycle pushes its members one
    /// layer lower on every sweep, so this five-state machine was laid out
    /// across twenty layers — roughly 3,000px of empty space. Cycles are
    /// normal in state machines, so this is the common case, not an edge case.
    /// </summary>
    [Fact]
    public void CyclesDoNotInflateTheLayerCount()
    {
        var m = MerpeepsDocument.Load(StateMachine);

        // Five nodes cannot legitimately occupy more than five layers.
        var span = m.Nodes.Max(n => n.Y) - m.Nodes.Min(n => n.Y);
        var maxPlausible = m.Nodes.Count * (56 + 110);

        Assert.True(span <= maxPlausible,
            $"layout spans {span:F0}px for {m.Nodes.Count} nodes; " +
            $"expected at most {maxPlausible:F0}px — cycles are inflating layers");
    }

    [Fact]
    public void MutualEdgesLandOnAdjacentLayers()
    {
        var m = MerpeepsDocument.Load("flowchart TD\n    a --> b\n    b --> a");

        var a = m.Node("a")!;
        var b = m.Node("b")!;
        Assert.Equal(56 + 110, Math.Abs(a.Y - b.Y), 1);
    }

    [Fact]
    public void SelfLoopDoesNotMoveTheNode()
    {
        var m = MerpeepsDocument.Load("flowchart TD\n    a --> a");
        Assert.Equal(0, m.Node("a")!.Y, 1);
    }

    /// <summary>
    /// Coordinates are written into the user's file, so an unstable layout
    /// would produce a diff every time the document is opened.
    /// </summary>
    [Fact]
    public void LayoutIsDeterministic()
    {
        var a = MerpeepsDocument.Load(StateMachine);
        var b = MerpeepsDocument.Load(StateMachine);

        foreach (var node in a.Nodes)
        {
            var other = b.Node(node.Id)!;
            Assert.Equal(node.X, other.X, 6);
            Assert.Equal(node.Y, other.Y, 6);
        }
    }

    [Fact]
    public void LongChainStillLayersInOrder()
    {
        var src = "flowchart TD\n" + string.Join("\n",
            Enumerable.Range(0, 40).Select(i => $"    n{i} --> n{i + 1}"));
        var m = MerpeepsDocument.Load(src);

        for (var i = 0; i < 40; i++)
            Assert.True(m.Node($"n{i}")!.Y < m.Node($"n{i + 1}")!.Y,
                $"n{i} should sit above n{i + 1}");
    }

    /// <summary>A cycle reachable only part-way into the graph still resolves.</summary>
    [Fact]
    public void CycleDownstreamOfAChainIsBounded()
    {
        var m = MerpeepsDocument.Load("""
            flowchart TD
                start --> a
                a --> b
                b --> c
                c --> a
                c --> done
            """);

        var span = m.Nodes.Max(n => n.Y) - m.Nodes.Min(n => n.Y);
        Assert.True(span <= m.Nodes.Count * (56 + 110), $"span was {span:F0}px");
    }

    [Fact]
    public void ManualPositionsSurviveLayout()
    {
        var m = MerpeepsDocument.Load(StateMachine);
        var still = m.Node("Still")!;
        still.X = 999; still.Y = 777; still.HasExplicitPosition = true;

        LayeredLayout.Apply(m);

        Assert.Equal(999, still.X, 6);
        Assert.Equal(777, still.Y, 6);
    }

    /// <summary>
    /// An agent adding nodes over MCP supplies no coordinates. Without a
    /// fallback they all land on (0,0), stacked invisibly on top of each other
    /// and on top of whatever was already there.
    /// </summary>
    [Fact]
    public void AgentAddedNodesDoNotPileUpOnTheOrigin()
    {
        var m = MerpeepsDocument.Load("flowchart TD\n    a --> b\n    b --> c");
        var h = new Editing.EditHistory(m);

        h.Apply(new Editing.AddNodeCommand(new DiagramNode { Id = "x", Label = "X" }));
        h.Apply(new Editing.AddNodeCommand(new DiagramNode { Id = "y", Label = "Y" }));

        var seen = new HashSet<(double, double)>();
        foreach (var n in m.Nodes)
            Assert.True(seen.Add((n.X, n.Y)),
                $"{n.Id} sits exactly on top of another node at ({n.X}, {n.Y})");
    }

    /// <summary>A fallback is not a human's choice, so layout may still move it.</summary>
    [Fact]
    public void FallbackPlacementIsNotTreatedAsDeliberate()
    {
        var m = MerpeepsDocument.Load("flowchart TD\n    a --> b");
        var h = new Editing.EditHistory(m);
        h.Apply(new Editing.AddNodeCommand(new DiagramNode { Id = "x" }));

        Assert.False(m.Node("x")!.HasExplicitPosition);
    }
}
