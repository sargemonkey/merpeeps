using Merpeeps.Core;
using Merpeeps.Core.Editing;
using Merpeeps.Core.Model;
using Merpeeps.Core.Parsing;
using Xunit;

namespace Merpeeps.Core.Tests;

/// <summary>
/// State diagrams exercise the adapter pattern: a second diagram type reusing
/// the whole canvas, command layer and metadata layer, with only a parser and
/// writer added. If Merpeeps is genuinely type-agnostic, positions and styling must
/// work here with no extra code.
/// </summary>
public class StateAdapterTests
{
    private const string Source = """
        stateDiagram-v2
            [*] --> Idle
            Idle --> Running : start
            Running --> Idle : stop
            Running --> Crashed : fault
            Crashed --> [*]
            note right of Crashed : needs an operator
        """;

    [Fact]
    public void DetectsTheDiagramType()
    {
        var adapter = DiagramAdapters.For(Source);
        Assert.IsType<StateAdapter>(adapter);
        Assert.IsType<FlowchartAdapter>(
            DiagramAdapters.For("flowchart TD\n  A --> B"));
        Assert.IsType<FlowchartAdapter>(
            DiagramAdapters.For("graph LR\n  A --> B"));
    }

    [Fact]
    public void ParsesStatesAndTransitions()
    {
        var m = MerpeepsDocument.Load(Source);

        // Idle, Running, Crashed plus the two pseudo-states
        Assert.Equal(5, m.Nodes.Count);
        Assert.NotNull(m.Node("Idle"));
        Assert.NotNull(m.Node(StateAdapter.StartId));
        Assert.NotNull(m.Node(StateAdapter.EndId));

        Assert.Equal(5, m.Edges.Count);
        Assert.Contains(m.Edges, e =>
            e.From == "Idle" && e.To == "Running" && e.Label == "start");
    }

    [Fact]
    public void StartAndEndAreDistinctNodes()
    {
        // `[*]` means "start" as a source and "end" as a target; conflating them
        // would wire the diagram into a loop that does not exist.
        var m = MerpeepsDocument.Load(Source);
        Assert.Contains(m.Edges, e => e.From == StateAdapter.StartId && e.To == "Idle");
        Assert.Contains(m.Edges, e => e.From == "Crashed" && e.To == StateAdapter.EndId);
        Assert.DoesNotContain(m.Edges, e => e.From == StateAdapter.EndId);
    }

    [Fact]
    public void UnmodelledSyntaxIsPreserved()
    {
        var m = MerpeepsDocument.Load(Source);
        Assert.Contains(m.Passthrough.Values, v => v.Contains("note right of Crashed"));

        var written = new StateAdapter().Write(m);
        Assert.Contains("note right of Crashed", written);
    }

    [Fact]
    public void SemanticRoundTripIsStable()
    {
        var first = MerpeepsDocument.Load(Source);
        var written = new StateAdapter().Write(first);
        var second = MerpeepsDocument.Load(written);

        Assert.Equal(first.Nodes.Count, second.Nodes.Count);
        Assert.Equal(first.Edges.Count, second.Edges.Count);
        foreach (var e in first.Edges)
            Assert.Contains(second.Edges,
                x => x.From == e.From && x.To == e.To && x.Label == e.Label);
    }

    [Fact]
    public void PseudoStatesRenderBackAsStarBrackets()
    {
        var written = new StateAdapter().Write(MerpeepsDocument.Load(Source));
        Assert.Contains("[*] --> Idle", written);
        Assert.Contains("Crashed --> [*]", written);
        Assert.DoesNotContain("__start", written);
        Assert.DoesNotContain("__end", written);
    }

    [Fact]
    public void StateDescriptionsRoundTrip()
    {
        const string src = """
            stateDiagram-v2
                state "Waiting for payment" as pending
                [*] --> pending
            """;
        var m = MerpeepsDocument.Load(src);
        Assert.Equal("Waiting for payment", m.Node("pending")!.Label);

        var written = new StateAdapter().Write(m);
        Assert.Contains("state \"Waiting for payment\" as pending", written);
    }

    [Fact]
    public void CompositeStatesBecomeGroups()
    {
        const string src = """
            stateDiagram-v2
                [*] --> Active
                state Active {
                    [*] --> Numlock
                    Numlock --> Capslock
                }
            """;
        var m = MerpeepsDocument.Load(src);
        var sg = Assert.Single(m.Subgraphs);
        Assert.Equal("Active", sg.Id);
        Assert.Contains("Numlock", sg.NodeIds);
    }

    // ------------------------------------------------- the type-agnostic claim

    [Fact]
    public void PositionsWorkOnStateDiagramsWithNoExtraCode()
    {
        var m = MerpeepsDocument.Load(Source);
        m.Node("Running")!.X = 480;
        m.Node("Running")!.Y = 250;
        m.Node("Running")!.HasExplicitPosition = true;

        var saved = MerpeepsDocument.SavePositions(Source, m);

        Assert.Equal(MerpeepsDoc.BodyOf(Source), MerpeepsDoc.BodyOf(saved));
        var reloaded = MerpeepsDocument.Load(saved);
        Assert.Equal(480, reloaded.Node("Running")!.X);
    }

    [Fact]
    public void StylingAndIconsWorkOnStateDiagrams()
    {
        var m = MerpeepsDocument.Load(Source);
        var h = new EditHistory(m);
        h.Apply(new StyleNodesCommand(new[] { m.Node("Crashed")! },
            new StyleNodesCommand.Style(Fill: "#F8CECC", Icon: "shield")));

        var saved = MerpeepsDocument.SavePositions(Source, m);
        Assert.Equal(MerpeepsDoc.BodyOf(Source), MerpeepsDoc.BodyOf(saved));

        var reloaded = MerpeepsDocument.Load(saved);
        Assert.Equal("#F8CECC", reloaded.Node("Crashed")!.Fill);
        Assert.Equal("shield", reloaded.Node("Crashed")!.Icon);
    }

    [Fact]
    public void EditingCommandsWorkUnchangedOnStateDiagrams()
    {
        // The command layer knows nothing about diagram types.
        var m = MerpeepsDocument.Load(Source);
        var h = new EditHistory(m);

        h.Apply(new AddNodeCommand(new DiagramNode { Id = "Paused" }));
        h.Apply(new AddEdgeCommand(new DiagramEdge
            { From = "Running", To = "Paused", Label = "pause" }));

        var saved = MerpeepsDocument.Save(Source, m);
        Assert.Contains("Running --> Paused : pause", saved);

        var reloaded = MerpeepsDocument.Load(saved);
        Assert.NotNull(reloaded.Node("Paused"));

        while (h.CanUndo) h.Undo();
        Assert.Null(m.Node("Paused"));
    }

    [Fact]
    public void AgentOperationsWorkOnStateDiagrams()
    {
        var m = MerpeepsDocument.Load(Source);
        var h = new EditHistory(m);
        var result = OperationApplier.Apply(m, h, new[]
        {
            new DiagramOperation { Op = "addNode", Id = "Retrying" },
            new DiagramOperation
                { Op = "connect", From = "Crashed", To = "Retrying", Label = "retry" },
        });

        Assert.True(result.Ok);
        Assert.Contains(m.Edges, e => e.From == "Crashed" && e.To == "Retrying");
    }
}
