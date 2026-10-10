using Merpeeps.Core.Editing;
using Merpeeps.Core.Export;
using Merpeeps.Core.Model;
using Xunit;

namespace Merpeeps.Core.Tests;

public class GroupMovementTests
{
    public static TheoryData<string> NestedSources => new()
    {
        "flowchart LR\nsubgraph G\nA[One]\nsubgraph Child\nB[Two]\nend\nsubgraph Empty\nend\nend\nC[Outside]\n",
        "stateDiagram-v2\nstate G {\nA\nstate Child {\nB\n}\nstate Empty {\n}\n}\nC\n",
        "classDiagram\nnamespace G {\nclass A\nnamespace Child {\nclass B\n}\nnamespace Empty {\n}\n}\nclass C\nnote for B \"Attached\"\nB --() Port\n",
        "erDiagram\nsubgraph G\nA\nsubgraph Child\nB\nend\nsubgraph Empty\nend\nend\nC\n",
    };

    [Theory]
    [MemberData(nameof(NestedSources))]
    public void NestedMovementTranslatesMembersAndEmptyFramesOnceAndRoundTrips(string source)
    {
        var model = MerpeepsDocument.Load(source);
        var group = model.Subgraphs.Single(g => g.Id == "G");
        var children = GraphGroupGeometry.Descendants(model, group);
        Assert.Equal(3, children.Count);
        var members = GraphGroupGeometry.Members(model, group);
        Assert.Contains(model.Node("A")!, members);
        Assert.Contains(model.Node("B")!, members);
        Assert.DoesNotContain(model.Node("C")!, members);
        if (model.ClassOptions is not null)
            Assert.All(model.Nodes.Where(n => n.Class?.Kind is ClassNodeKind.Note or ClassNodeKind.Lollipop), n => Assert.Contains(n, members));
        var before = model.Nodes.ToDictionary(n => n.Id, n => (n.X, n.Y, n.HasExplicitPosition));
        var frames = children.ToDictionary(g => g.Id, g => GraphGroupGeometry.Bounds(model, g));
        model.Node("A")!.HasExplicitPosition = true;
        before["A"] = (model.Node("A")!.X, model.Node("A")!.Y, true);
        var history = new EditHistory(model);
        history.Apply(new MoveGroupCommand(model, group, -70.25, 110.5));
        foreach (var node in members)
        {
            Assert.Equal(before[node.Id].X - 70.25, node.X, 6);
            Assert.Equal(before[node.Id].Y + 110.5, node.Y, 6);
            Assert.True(node.HasExplicitPosition);
        }
        Assert.Equal(before["C"], (model.Node("C")!.X, model.Node("C")!.Y, model.Node("C")!.HasExplicitPosition));
        foreach (var child in children)
            Assert.Equal(frames[child.Id] with { X = frames[child.Id].X - 70.25, Y = frames[child.Id].Y + 110.5 }, GraphGroupGeometry.Bounds(model, child));
        var saved = MerpeepsDocument.Save(source, model);
        Assert.Equal(MerpeepsDoc.BodyOf(source), MerpeepsDoc.BodyOf(saved));
        var reopened = MerpeepsDocument.Load(saved);
        foreach (var child in children)
            Assert.Equal(GraphGroupGeometry.Bounds(model, child), GraphGroupGeometry.Bounds(reopened, reopened.Subgraphs.Single(g => g.Id == child.Id)));
        history.Undo();
        Assert.False(history.CanUndo);
        Assert.Empty(model.GroupLayouts);
        Assert.All(model.Nodes, n => Assert.Equal(before[n.Id], (n.X, n.Y, n.HasExplicitPosition)));
        history.Redo();
        Assert.Equal(saved, MerpeepsDocument.Save(source, model));
    }

    [Theory]
    [InlineData("flowchart LR\nsubgraph G\nend\n")]
    [InlineData("stateDiagram-v2\nstate G {\n}\n")]
    [InlineData("classDiagram\nnamespace G {\n}\n")]
    [InlineData("erDiagram\nsubgraph G\nend\n")]
    public void EmptyGroupsKeepTheirMovedFramesWithoutInventingNodes(string source)
    {
        var model = MerpeepsDocument.Load(source);
        var group = Assert.Single(model.Subgraphs);
        var original = GraphGroupGeometry.Bounds(model, group);
        var history = new EditHistory(model);
        history.Apply(new MoveGroupCommand(model, group, 300, -100));
        var expected = original with { X = original.X + 300, Y = original.Y - 100 };
        var saved = MerpeepsDocument.Save(source, model);
        var reopened = MerpeepsDocument.Load(saved);
        Assert.Empty(reopened.Nodes);
        Assert.Equal(expected, GraphGroupGeometry.Bounds(reopened, reopened.Subgraphs.Single()));
        history.Undo();
        Assert.Equal(original, GraphGroupGeometry.Bounds(model, group));
        Assert.Empty(model.GroupLayouts);
        Assert.Empty(MerpeepsDocument.Load(MerpeepsDocument.Save(saved, model)).GroupLayouts);
        history.Redo();
        history.Apply(new MoveGroupCommand(model, group, 30, 10));
        history.Undo();
        Assert.Equal(expected, GraphGroupGeometry.Bounds(model, group));
        if (model.ClassOptions is not null) Assert.Contains("data-namespace=\"G\"", SvgExporter.ToSvg(model));
        if (model.EntityOptions is not null) Assert.Contains("data-er-subgraph=\"G\"", SvgExporter.ToSvg(model));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EmptyFrameFollowsNamespaceOrEntityGroupRenameDeleteAndUndo(bool entity)
    {
        var source = entity ? "erDiagram\nsubgraph G\nend\n" : "classDiagram\nnamespace G {\n}\n";
        var model = MerpeepsDocument.Load(source);
        var group = model.Subgraphs.Single();
        var history = new EditHistory(model);
        history.Apply(new MoveGroupCommand(model, group, 70, 40));
        var frame = model.GroupLayouts["G"];
        history.Apply(entity ? new SetEntityGroupCommand(group, "Renamed", "Title", null, null)
            : new SetClassNamespaceDefinitionCommand(group, "Renamed", "Title", null));
        Assert.False(model.GroupLayouts.ContainsKey("G"));
        Assert.Equal(frame, model.GroupLayouts["Renamed"]);
        var saved = MerpeepsDocument.Save(source, model);
        Assert.Equal(frame, MerpeepsDocument.Load(saved).GroupLayouts["Renamed"]);
        var renamed = model.Subgraphs.Single();
        history.Apply(entity ? new DeleteEntityGroupCommand(renamed) : new DeleteClassNamespaceCommand(renamed));
        Assert.Empty(model.GroupLayouts);
        Assert.Empty(MerpeepsDocument.Load(MerpeepsDocument.Save(saved, model)).GroupLayouts);
        history.Undo();
        Assert.Equal(frame, model.GroupLayouts["Renamed"]);
        history.Undo();
        Assert.Equal(frame, model.GroupLayouts["G"]);
    }

    [Fact]
    public void InvalidGroupFrameIsReportedInsteadOfIgnored()
    {
        const string source = "erDiagram\nsubgraph G\nend\n";
        var invalid = MerpeepsDoc.Inject(source, new Dictionary<string, object?>
        {
            ["spec"] = 1,
            ["layout"] = new Dictionary<string, object?>
            {
                ["groups"] = new Dictionary<string, object?>
                {
                    ["G"] = new Dictionary<string, object?> { ["x"] = 0, ["y"] = 0, ["w"] = -1, ["h"] = 100 },
                },
            },
        });
        Assert.Throws<FormatException>(() => MerpeepsDocument.Load(invalid));
    }

    [Fact]
    public void ClearingAFramePreservesUnknownFieldsAndStaleIdentities()
    {
        const string source = "erDiagram\nsubgraph G\nend\n";
        var model = MerpeepsDocument.Load(source);
        var history = new EditHistory(model);
        history.Apply(new MoveGroupCommand(model, model.Subgraphs.Single(), 20, 30));
        var saved = MerpeepsDoc.Inject(MerpeepsDocument.Save(source, model), new Dictionary<string, object?>
        {
            ["layout"] = new Dictionary<string, object?>
            {
                ["groups"] = new Dictionary<string, object?>
                {
                    ["G"] = new Dictionary<string, object?> { ["futureOption"] = "preserved" },
                    ["Missing"] = new Dictionary<string, object?> { ["futureShape"] = "retained" },
                },
            },
        });
        history.Undo();
        var cleared = MerpeepsDocument.Save(saved, model);
        Assert.Empty(MerpeepsDocument.Load(cleared).GroupLayouts);
        Assert.Contains("futureOption: preserved", cleared);
        Assert.Contains("futureShape: retained", cleared);
        Assert.Contains("Missing:", cleared);
    }
}
