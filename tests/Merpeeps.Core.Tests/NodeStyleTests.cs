using Merpeeps.Core;
using Merpeeps.Core.Editing;
using Merpeeps.Core.Model;
using Xunit;

namespace Merpeeps.Core.Tests;

/// <summary>
/// Styling and icons are the whole argument for the metadata layer: mermaid
/// syntax cannot express them, and Merpeeps can, precisely because a stock renderer
/// ignores the block entirely.
///
/// <para>(For context on why these matter: "styling components of the sequence
/// diagram" is the most-reacted open issue in mermaid at 571, and "svg images
/// instead of boxes ... e.g. AWS Icons" sits at 143.)</para>
/// </summary>
public class NodeStyleTests
{
    private const string Source = """
        flowchart TD
            api[API] --> db[(Database)]
            classDef keepme fill:#eee
        """;

    [Fact]
    public void StylingNeverTouchesTheDiagramBody()
    {
        var m = MerpeepsDocument.Load(Source);
        var h = new EditHistory(m);
        h.Apply(new StyleNodesCommand(new[] { m.Node("api")! },
            new StyleNodesCommand.Style(
                Fill: "#FFE6CC", Stroke: "#D79B00", TextColor: "#663300",
                StrokeWidth: 2.5, Dashed: true, Icon: "gateway")));

        var saved = MerpeepsDocument.SavePositions(Source, m);

        // The strongest guarantee in the product: presentation cannot corrupt
        // the diagram, because it is not stored in the diagram.
        Assert.Equal(MerpeepsDoc.BodyOf(Source), MerpeepsDoc.BodyOf(saved));
        Assert.Contains("fill: '#FFE6CC'", saved);
        Assert.Contains("icon: gateway", saved);
    }

    [Fact]
    public void StyleSurvivesARoundTrip()
    {
        var m = MerpeepsDocument.Load(Source);
        var h = new EditHistory(m);
        h.Apply(new StyleNodesCommand(new[] { m.Node("db")! },
            new StyleNodesCommand.Style(
                Fill: "#D5E8D4", Stroke: "#82B366", TextColor: "#1A1A1A",
                StrokeWidth: 3, Dashed: true, Icon: "database")));

        var reloaded = MerpeepsDocument.Load(MerpeepsDocument.SavePositions(Source, m));
        var db = reloaded.Node("db")!;

        Assert.Equal("#D5E8D4", db.Fill);
        Assert.Equal("#82B366", db.Stroke);
        Assert.Equal("#1A1A1A", db.TextColor);
        Assert.Equal(3, db.StrokeWidth);
        Assert.True(db.Dashed);
        Assert.Equal("database", db.Icon);
    }

    [Fact]
    public void StylingIsUndoable()
    {
        var m = MerpeepsDocument.Load(Source);
        var h = new EditHistory(m);
        var node = m.Node("api")!;

        h.Apply(new StyleNodesCommand(new[] { node },
            new StyleNodesCommand.Style(Fill: "#F8CECC", Icon: "server")));
        Assert.Equal("#F8CECC", node.Fill);
        Assert.Equal("server", node.Icon);

        h.Undo();
        Assert.Null(node.Fill);
        Assert.Null(node.Icon);

        h.Redo();
        Assert.Equal("#F8CECC", node.Fill);
    }

    [Fact]
    public void StylingManyNodesIsOneUndoStep()
    {
        var m = MerpeepsDocument.Load(Source);
        var h = new EditHistory(m);
        h.Apply(new StyleNodesCommand(m.Nodes,
            new StyleNodesCommand.Style(Fill: "#E1D5E7")));

        Assert.All(m.Nodes, n => Assert.Equal("#E1D5E7", n.Fill));
        h.Undo();
        Assert.All(m.Nodes, n => Assert.Null(n.Fill));
    }

    [Fact]
    public void IconsApplyToAnyDiagramTypeBecauseTheLayerIsTypeAgnostic()
    {
        // The same metadata layer works on a state diagram, which is the point:
        // presentation is not tied to flowchart syntax.
        const string state = """
            stateDiagram-v2
                [*] --> Idle
                Idle --> Running
            """;
        var merpeeps = MerpeepsDoc.Inject(state, new Dictionary<string, object?>
        {
            ["spec"] = 1,
            ["style"] = new Dictionary<string, object?>
            {
                ["nodes"] = new Dictionary<string, object?>
                {
                    ["Running"] = new Dictionary<string, object?>
                        { ["fill"] = "#D5E8D4", ["icon"] = "cog" },
                },
            },
        });

        Assert.Equal(MerpeepsDoc.BodyOf(state), MerpeepsDoc.BodyOf(merpeeps));
        Assert.Contains("icon: cog", merpeeps);
    }

    [Fact]
    public void UnstyledNodesAddNoMetadataNoise()
    {
        // A diagram nobody has styled should not gain a style block at all.
        var m = MerpeepsDocument.Load(Source);
        var saved = MerpeepsDocument.SavePositions(Source, m);
        Assert.DoesNotContain("style:", saved);
    }
}
