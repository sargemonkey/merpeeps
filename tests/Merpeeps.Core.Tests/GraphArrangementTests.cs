using Merpeeps.Core.Editing;
using Merpeeps.Core.Model;
using Xunit;

namespace Merpeeps.Core.Tests;

public class GraphArrangementTests
{
    [Theory]
    [InlineData(GraphArrangement.Left, 10, 10, 10)]
    [InlineData(GraphArrangement.Right, 300, 320, 330)]
    [InlineData(GraphArrangement.HorizontalCenter, 155, 165, 170)]
    [InlineData(GraphArrangement.Top, 20, 20, 20)]
    [InlineData(GraphArrangement.Bottom, 400, 380, 390)]
    [InlineData(GraphArrangement.VerticalCenter, 210, 200, 205)]
    public void AlignUsesOuterBoundsAndRestoresPositionsAndPinFlags(GraphArrangement arrangement, double a, double b, double c)
    {
        var model = Create();
        var before = model.Nodes.Select(n => (n.X, n.Y, n.HasExplicitPosition)).ToArray();
        var history = new EditHistory(model);
        var command = new ArrangeGraphObjectsCommand(model, Objects(model), arrangement);
        Assert.True(command.HasChanges);
        history.Apply(command);
        var horizontal = arrangement is GraphArrangement.Left or GraphArrangement.Right or GraphArrangement.HorizontalCenter;
        Assert.Equal(new[] { a, b, c }, model.Nodes.Select(n => horizontal ? n.X : n.Y));
        Assert.Equal(before.Select(n => horizontal ? n.Y : n.X), model.Nodes.Select(n => horizontal ? n.Y : n.X));
        history.Undo();
        Assert.Equal(before, model.Nodes.Select(n => (n.X, n.Y, n.HasExplicitPosition)));
        Assert.False(history.CanUndo);
        history.Redo();
        Assert.Equal(new[] { a, b, c }, model.Nodes.Select(n => horizontal ? n.X : n.Y));
    }

    [Theory]
    [InlineData(GraphArrangement.DistributeHorizontally)]
    [InlineData(GraphArrangement.DistributeVertically)]
    public void DistributionCreatesEqualEdgeGapsAndKeepsEndObjects(GraphArrangement arrangement)
    {
        var model = Create();
        var before = model.Nodes.Select(n => (n.X, n.Y, n.HasExplicitPosition)).ToArray();
        var command = new ArrangeGraphObjectsCommand(model, Objects(model).Reverse(), arrangement);
        var history = new EditHistory(model);
        history.Apply(command);
        var horizontal = arrangement == GraphArrangement.DistributeHorizontally;
        var nodes = model.Nodes.OrderBy(n => horizontal ? n.X : n.Y).ToArray();
        var gaps = nodes.Zip(nodes.Skip(1), (a, b) => horizontal ? b.X - a.X - a.Width : b.Y - a.Y - a.Height).ToArray();
        Assert.Equal(gaps[0], gaps[1], 8);
        Assert.Equal(before[0], (model.Nodes[0].X, model.Nodes[0].Y, model.Nodes[0].HasExplicitPosition));
        Assert.Equal(before[2], (model.Nodes[2].X, model.Nodes[2].Y, model.Nodes[2].HasExplicitPosition));
        history.Undo();
        Assert.Equal(before, model.Nodes.Select(n => (n.X, n.Y, n.HasExplicitPosition)));
    }

    [Theory]
    [InlineData("flowchart LR\nsubgraph G\nA\nsubgraph Child\nB\nend\nsubgraph Empty\nend\nend\nC\n")]
    [InlineData("stateDiagram-v2\nstate G {\nA\nstate Child {\nB\n}\nstate Empty {\n}\n}\nC\n")]
    [InlineData("classDiagram\nnamespace G {\nclass A\nnamespace Child {\nclass B\n}\nnamespace Empty {\n}\n}\nclass C\nnote for B \"Attached\"\n")]
    [InlineData("erDiagram\nsubgraph G\nA\nsubgraph Child\nB\nend\nsubgraph Empty\nend\nend\nC\n")]
    public void GroupAndItsMembersAreOneObjectAndAlignmentTranslatesDescendantsExactlyOnce(string source)
    {
        var model = MerpeepsDocument.Load(source);
        var group = model.Subgraphs.Single(g => g.Id == "G");
        var child = model.Subgraphs.Single(g => g.Id.EndsWith("Child", StringComparison.Ordinal));
        var objects = new[] { new GraphObject(GraphObjectKind.Group, "G"), new(GraphObjectKind.Group, child.Id),
            new(GraphObjectKind.Node, "A"), new(GraphObjectKind.Node, "B"), new(GraphObjectKind.Node, "C") };
        Assert.Equal(2, GraphObjectGeometry.Normalize(model, objects).Count);
        var members = GraphGroupGeometry.Members(model, group);
        var before = model.Nodes.ToDictionary(n => n.Id, n => (n.X, n.Y, n.HasExplicitPosition));
        var bounds = GraphGroupGeometry.Bounds(model, group);
        model.Node("C")!.X = bounds.X - 150;
        var history = new EditHistory(model);
        history.Apply(new ArrangeGraphObjectsCommand(model, objects, GraphArrangement.Left));
        Assert.All(members, n =>
        {
            Assert.Equal(before[n.Id].X - 150, n.X, 8);
            Assert.Equal(before[n.Id].Y, n.Y, 8);
        });
        var saved = MerpeepsDocument.Save(source, model);
        var reopened = MerpeepsDocument.Load(saved);
        Assert.Equal(MerpeepsDoc.BodyOf(source), MerpeepsDoc.BodyOf(saved));
        Assert.Equal(GraphGroupGeometry.Bounds(model, group), GraphGroupGeometry.Bounds(reopened, reopened.Subgraphs.Single(g => g.Id == "G")));
        history.Undo();
        Assert.All(members, n => Assert.Equal(before[n.Id], (n.X, n.Y, n.HasExplicitPosition)));
        Assert.Empty(model.GroupLayouts);
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void AlreadyAlignedDoesNotChangePinFlags()
    {
        var model = Create();
        foreach (var n in model.Nodes) n.X = 25;
        var before = model.Nodes.Select(n => n.HasExplicitPosition).ToArray();
        var command = new ArrangeGraphObjectsCommand(model, Objects(model), GraphArrangement.Left);
        Assert.False(command.HasChanges);
        command.Do(model);
        Assert.Equal(before, model.Nodes.Select(n => n.HasExplicitPosition));
    }

    [Fact]
    public void LayoutOwnedFamiliesAndInsufficientIndependentObjectsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new ArrangeGraphObjectsCommand(MerpeepsDocument.Load("pie\n\"A\":1\n"), [], GraphArrangement.Left));
        var model = Create();
        Assert.Throws<ArgumentException>(() => new ArrangeGraphObjectsCommand(model, Objects(model).Take(1), GraphArrangement.Left));
        Assert.Throws<ArgumentException>(() => new ArrangeGraphObjectsCommand(model, Objects(model).Take(2), GraphArrangement.DistributeHorizontally));
        Assert.Throws<ArgumentException>(() => new ArrangeGraphObjectsCommand(model, [new(GraphObjectKind.Node, "Missing")], GraphArrangement.Top));
    }

    [Fact]
    public void EmptyFramesAlignWithoutInventingNodesOrLosingUndoMetadata()
    {
        const string source = "erDiagram\nsubgraph A\nend\nsubgraph B\nend\n";
        var model = MerpeepsDocument.Load(source);
        var first = model.Subgraphs[0]; var second = model.Subgraphs[1];
        new MoveGroupCommand(model, first, -40.25, 10.75).Do(model);
        new MoveGroupCommand(model, second, 20, 120).Do(model);
        var before = model.GroupLayouts.ToArray();
        var expected = GraphGroupGeometry.Bounds(model, first).Y;
        var history = new EditHistory(model);
        history.Apply(new ArrangeGraphObjectsCommand(model,
            [new(GraphObjectKind.Group, "A"), new(GraphObjectKind.Group, "B")], GraphArrangement.Top));
        Assert.Empty(model.Nodes);
        Assert.Equal(expected, GraphGroupGeometry.Bounds(model, second).Y);
        var reopened = MerpeepsDocument.Load(MerpeepsDocument.Save(source, model));
        Assert.Equal(expected, GraphGroupGeometry.Bounds(reopened, reopened.Subgraphs[1]).Y);
        history.Undo();
        Assert.Equal(before, model.GroupLayouts);
    }

    private static IEnumerable<GraphObject> Objects(DiagramModel model) => model.Nodes.Select(n => new GraphObject(GraphObjectKind.Node, n.Id));
    private static DiagramModel Create()
    {
        var model = new DiagramModel();
        model.Nodes.AddRange([
            new() { Id = "A", X = 10, Y = 20, Width = 100, Height = 50 },
            new() { Id = "B", X = 140, Y = 170, Width = 80, Height = 70, HasExplicitPosition = true },
            new() { Id = "C", X = 330, Y = 390, Width = 70, Height = 60 },
        ]);
        return model;
    }
}
