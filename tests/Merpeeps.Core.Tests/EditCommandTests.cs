using Merpeeps.Core;
using Merpeeps.Core.Editing;
using Merpeeps.Core.Model;
using Xunit;

namespace Merpeeps.Core.Tests;

/// <summary>
/// Every command must be exactly reversible. If undo does not restore the prior
/// state precisely, the editor quietly corrupts documents.
/// </summary>
public class EditCommandTests
{
    private const string Source = """
        flowchart TD
            a[Alpha] --> b[Beta]
            b --> c[Gamma]
            classDef keepme fill:#eee
        """;

    private static (DiagramModel Model, EditHistory History) Fresh()
    {
        var m = MerpeepsDocument.Load(Source);
        return (m, new EditHistory(m));
    }

    private static string Snapshot(DiagramModel m) =>
        string.Join("|",
            m.Nodes.Select(n => $"{n.Id}:{n.Label}:{n.Shape}:{n.X},{n.Y}")) +
        "//" +
        string.Join("|", m.Edges.Select(e => $"{e.From}->{e.To}:{e.Label}:{e.Kind}"));

    [Fact]
    public void AddNodeUndoesCleanly()
    {
        var (m, h) = Fresh();
        var before = Snapshot(m);

        h.Apply(new AddNodeCommand(new DiagramNode
            { Id = "d", Label = "Delta", X = 10, Y = 20 }));
        Assert.Equal(4, m.Nodes.Count);

        h.Undo();
        Assert.Equal(before, Snapshot(m));

        h.Redo();
        Assert.Equal(4, m.Nodes.Count);
    }

    [Fact]
    public void DeleteNodeAlsoRemovesItsEdgesAndRestoresThem()
    {
        var (m, h) = Fresh();
        var before = Snapshot(m);

        h.Apply(new DeleteNodesCommand(new[] { m.Node("b")! }));
        Assert.Equal(2, m.Nodes.Count);
        Assert.Empty(m.Edges);            // both edges touched b

        h.Undo();
        Assert.Equal(before, Snapshot(m));
    }

    [Fact]
    public void AddAndDeleteEdgeUndoCleanly()
    {
        var (m, h) = Fresh();
        var before = Snapshot(m);

        h.Apply(new AddEdgeCommand(new DiagramEdge
            { From = "a", To = "c", Label = "skip" }));
        Assert.Equal(3, m.Edges.Count);
        h.Undo();
        Assert.Equal(before, Snapshot(m));

        h.Apply(new DeleteEdgeCommand(m.Edges[0]));
        Assert.Single(m.Edges);
        h.Undo();
        Assert.Equal(before, Snapshot(m));
    }

    [Fact]
    public void RenameAndShapeUndoCleanly()
    {
        var (m, h) = Fresh();
        var before = Snapshot(m);

        h.Apply(new RenameNodeCommand(m.Node("a")!, "Renamed"));
        Assert.Equal("Renamed", m.Node("a")!.Label);

        h.Apply(new ChangeShapeCommand(m.Node("a")!, NodeShape.Rhombus));
        Assert.Equal(NodeShape.Rhombus, m.Node("a")!.Shape);

        h.Undo();
        h.Undo();
        Assert.Equal(before, Snapshot(m));
    }

    [Fact]
    public void MoveUndoesToTheOriginalPosition()
    {
        var (m, h) = Fresh();
        var n = m.Node("a")!;
        var (ox, oy) = (n.X, n.Y);

        n.X = 999; n.Y = 888;
        h.Record(new MoveNodesCommand(new[] { (n, ox, oy, 999.0, 888.0) }));

        h.Undo();
        Assert.Equal(ox, n.X);
        Assert.Equal(oy, n.Y);

        h.Redo();
        Assert.Equal(999, n.X);
        Assert.Equal(888, n.Y);
    }

    [Fact]
    public void RedoStackClearsOnANewEdit()
    {
        var (m, h) = Fresh();
        h.Apply(new RenameNodeCommand(m.Node("a")!, "One"));
        h.Undo();
        Assert.True(h.CanRedo);

        h.Apply(new RenameNodeCommand(m.Node("b")!, "Two"));
        Assert.False(h.CanRedo);
    }

    [Fact]
    public void DeepUndoRestoresTheStartingStateExactly()
    {
        var (m, h) = Fresh();
        var before = Snapshot(m);

        h.Apply(new AddNodeCommand(new DiagramNode { Id = "d", Label = "Delta" }));
        h.Apply(new AddEdgeCommand(new DiagramEdge { From = "c", To = "d" }));
        h.Apply(new RenameNodeCommand(m.Node("d")!, "Delta Prime"));
        h.Apply(new ChangeShapeCommand(m.Node("d")!, NodeShape.Hexagon));
        h.Apply(new DeleteNodesCommand(new[] { m.Node("a")! }));

        while (h.CanUndo) h.Undo();
        Assert.Equal(before, Snapshot(m));
    }

    // ------------------------------------------------------------ full save

    [Fact]
    public void FullSaveWritesStructuralEditsIntoTheBody()
    {
        var (m, h) = Fresh();
        h.Apply(new AddNodeCommand(new DiagramNode
            { Id = "d", Label = "Delta", Shape = NodeShape.Hexagon }));
        h.Apply(new AddEdgeCommand(new DiagramEdge
            { From = "c", To = "d", Label = "then" }));

        var saved = MerpeepsDocument.Save(Source, m);

        Assert.Contains("d{{Delta}}", saved);
        Assert.Contains("c -->|then| d", saved);
        Assert.Contains("classDef keepme", saved);   // passthrough survives
        Assert.Contains("x-merpeeps:", saved);            // positions still written
    }

    [Fact]
    public void FullSaveRoundTripsThroughTheParser()
    {
        var (m, h) = Fresh();
        h.Apply(new AddNodeCommand(new DiagramNode { Id = "d", Label = "Delta" }));
        h.Apply(new AddEdgeCommand(new DiagramEdge { From = "c", To = "d" }));

        var saved = MerpeepsDocument.Save(Source, m);
        var reloaded = MerpeepsDocument.Load(saved);

        Assert.Equal(m.Nodes.Count, reloaded.Nodes.Count);
        Assert.Equal(m.Edges.Count, reloaded.Edges.Count);
        Assert.NotNull(reloaded.Node("d"));
        Assert.Contains(reloaded.Edges, e => e.From == "c" && e.To == "d");
    }

    [Fact]
    public void DeletingANodeRemovesItFromTheSavedBody()
    {
        var (m, h) = Fresh();
        h.Apply(new DeleteNodesCommand(new[] { m.Node("b")! }));

        var saved = MerpeepsDocument.Save(Source, m);
        var reloaded = MerpeepsDocument.Load(saved);

        Assert.Null(reloaded.Node("b"));
        Assert.DoesNotContain("b[Beta]", saved);
        Assert.Contains("classDef keepme", saved);
    }

    [Fact]
    public void PositionOnlySaveStillRefusesToTouchTheBody()
    {
        var (m, _) = Fresh();
        m.Node("a")!.X = 77;
        var saved = MerpeepsDocument.SavePositions(Source, m);
        Assert.Equal(MerpeepsDoc.BodyOf(Source), MerpeepsDoc.BodyOf(saved));
    }

    /// <summary>
    /// Undoing a move must un-pin nodes that were not pinned before it.
    /// </summary>
    /// <remarks>
    /// Restoring coordinates alone looks correct and is not. Dragging pins a
    /// node that was already pinned, so undo is symmetric and the omission is
    /// invisible. Adopting a reference layout is the case that exposes it: it
    /// pins every node in a diagram that had no stored positions, and an undo
    /// that only rewound the coordinates would leave them all pinned at stale
    /// values with the layout engine switched off — strictly worse than before
    /// the command ran.
    /// </remarks>
    [Fact]
    public void UndoingAMoveUnpinsNodesThatWereNotPinnedBefore()
    {
        var (m, h) = Fresh();
        foreach (var n in m.Nodes) n.HasExplicitPosition = false;

        var moves = m.Nodes
            .Select(n => (Node: n, FromX: n.X, FromY: n.Y, ToX: n.X + 50, ToY: n.Y + 50))
            .ToList();

        h.Apply(new MoveNodesCommand(moves));
        Assert.All(m.Nodes, n => Assert.True(n.HasExplicitPosition));

        h.Undo();
        Assert.All(m.Nodes, n => Assert.False(n.HasExplicitPosition));

        // And a node that WAS pinned stays pinned, so the fix does not swap one
        // asymmetry for another.
        var pinned = m.Nodes[0];
        pinned.HasExplicitPosition = true;
        h.Apply(new MoveNodesCommand(
            new[] { (pinned, pinned.X, pinned.Y, 10.0, 20.0) }));
        h.Undo();
        Assert.True(pinned.HasExplicitPosition);
    }
}
