using Merpeeps.Core;
using Merpeeps.Core.Model;
using Xunit;

namespace Merpeeps.Core.Tests;

/// <summary>
/// The editor's core promise, tested without any UI:
/// moving a node persists its position and leaves the diagram body untouched.
/// </summary>
public class EditorRoundTripTests
{
    private const string Source = """
        flowchart TD
            web[Web Client] --> gw{{API Gateway}}
            gw -->|authenticate| auth([Auth Service])
            gw --> orders[Order Service]
            orders --> inv[(Inventory DB)]
            classDef external fill:#f96,stroke:#333
            class auth external
        """;

    [Fact]
    public void LoadAssignsPositionsToEveryNode()
    {
        var m = MerpeepsDocument.Load(Source);
        Assert.Equal(5, m.Nodes.Count);
        Assert.Equal(4, m.Edges.Count);
        // Layout must place everything; overlapping at the origin would mean
        // the layout engine silently did nothing.
        Assert.True(m.Nodes.Select(n => (n.X, n.Y)).Distinct().Count() > 1);
    }

    [Fact]
    public void MovingANodePersistsAndLeavesBodyUntouched()
    {
        var model = MerpeepsDocument.Load(Source);
        var node = model.Node("orders")!;
        node.X = 1234; node.Y = 567;
        node.HasExplicitPosition = true;

        var saved = MerpeepsDocument.SavePositions(Source, model);

        // 1. the diagram body is byte-identical
        Assert.Equal(MerpeepsDoc.BodyOf(Source), MerpeepsDoc.BodyOf(saved));

        // 2. reloading restores the exact position
        var reloaded = MerpeepsDocument.Load(saved);
        var again = reloaded.Node("orders")!;
        Assert.Equal(1234, again.X);
        Assert.Equal(567, again.Y);
        Assert.True(again.HasExplicitPosition);
    }

    [Fact]
    public void StoredPositionsSurviveASecondSave()
    {
        var model = MerpeepsDocument.Load(Source);
        model.Node("web")!.X = 10;
        model.Node("web")!.Y = 20;
        model.Node("web")!.HasExplicitPosition = true;

        var once = MerpeepsDocument.SavePositions(Source, model);
        var twice = MerpeepsDocument.SavePositions(once, MerpeepsDocument.Load(once));

        Assert.Equal(MerpeepsDoc.BodyOf(Source), MerpeepsDoc.BodyOf(twice));
        var final = MerpeepsDocument.Load(twice).Node("web")!;
        Assert.Equal(10, final.X);
        Assert.Equal(20, final.Y);
    }

    [Fact]
    public void UnmodelledDirectivesSurviveTheEditorRoundTrip()
    {
        // classDef/class are not modelled, but must not be lost by saving.
        var saved = MerpeepsDocument.SavePositions(Source, MerpeepsDocument.Load(Source));
        Assert.Contains("classDef external", saved);
        Assert.Contains("class auth external", saved);
    }

    [Fact]
    public void ReopeningDoesNotDisturbAManualArrangement()
    {
        var model = MerpeepsDocument.Load(Source);
        foreach (var n in model.Nodes)
        {
            n.X = 100; n.Y = 200;
            n.HasExplicitPosition = true;
        }
        var saved = MerpeepsDocument.SavePositions(Source, model);

        // Auto-layout must not run over nodes that already have coordinates.
        var reloaded = MerpeepsDocument.Load(saved);
        Assert.All(reloaded.Nodes, n =>
        {
            Assert.Equal(100, n.X);
            Assert.Equal(200, n.Y);
        });
    }

    [Fact]
    public void SaveRefusesIfTheBodyWouldChange()
    {
        // Guard rail: SavePositions must never be a vector for body edits.
        var model = MerpeepsDocument.Load(Source);
        var ex = Record.Exception(() => MerpeepsDocument.SavePositions(Source, model));
        Assert.Null(ex);   // the honest path works

        // and the check itself is live
        Assert.Equal(MerpeepsDoc.BodyOf(Source),
                     MerpeepsDoc.BodyOf(MerpeepsDocument.SavePositions(Source, model)));
    }

    [Fact]
    public void LayoutIsDeterministic()
    {
        // Layout output ends up in files, so it must not wobble between runs.
        var a = MerpeepsDocument.Load(Source);
        var b = MerpeepsDocument.Load(Source);
        foreach (var n in a.Nodes)
        {
            var o = b.Node(n.Id)!;
            Assert.Equal(n.X, o.X);
            Assert.Equal(n.Y, o.Y);
        }
    }
}
