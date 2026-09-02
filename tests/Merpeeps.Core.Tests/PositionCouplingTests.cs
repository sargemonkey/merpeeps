using Merpeeps.Core;
using Merpeeps.Core.Editing;
using Merpeeps.Core.Model;
using Xunit;

namespace Merpeeps.Core.Tests;

/// <summary>
/// Measures what storing positions actually costs an agent.
///
/// <para>Written in response to a specific criticism: that node coordinates are
/// presentational data coupled to the semantic node list, and that this is
/// structurally the same coupling which makes Excalidraw agent-hostile — an
/// agent maintaining two representations that must be kept in step.</para>
///
/// <para>The criticism is fair enough to deserve measurement rather than
/// argument. These tests pin down exactly where the coupling bites, so the claim
/// can be answered with numbers and so any future change that makes the tax
/// worse shows up as a failing test rather than as a slow drift.</para>
/// </summary>
public class PositionCouplingTests
{
    private const string Positioned = """
        ---
        x-merpeeps:
          spec: 1
          layout:
            nodes:
              api: {x: 0, y: 0}
              db: {x: 200, y: 0}
          style:
            nodes:
              api: {fill: '#DAE8FC'}
        ---
        flowchart LR
            api[API] --> db[(Database)]
        """;

    private static (DiagramModel Model, EditHistory History) Open(string src)
    {
        var model = MerpeepsDocument.Load(src);
        return (model, new EditHistory(model));
    }

    // ------------------------------------------------- the cheap cases

    /// <summary>
    /// The single most important mitigation: the stable key is the node
    /// <em>id</em>, and renaming changes only the <em>label</em>. Positions are
    /// keyed by id, so the commonest edit an agent makes does not touch the
    /// layout block at all.
    /// </summary>
    [Fact]
    public void RenamingALabelDoesNotDisturbPositions()
    {
        var (model, history) = Open(Positioned);

        OperationApplier.Apply(model, history, [new DiagramOperation { Op = "rename", Id = "api", Label = "API Gateway" }]);

        var saved = MerpeepsDocument.Save(Positioned, model);
        var reloaded = MerpeepsDocument.Load(saved);

        Assert.Equal("API Gateway", reloaded.Node("api")!.Label);
        Assert.Equal(0, reloaded.Node("api")!.X);
        Assert.Equal(200, reloaded.Node("db")!.X);
    }

    /// <summary>
    /// Adding a node costs nothing in the layout block. A document may carry
    /// positions for some nodes and not others, so an agent never has to invent
    /// coordinates just to add a box — the layout engine places it.
    /// </summary>
    [Fact]
    public void AddingANodeRequiresNoCoordinates()
    {
        var (model, history) = Open(Positioned);

        OperationApplier.Apply(model, history, [new DiagramOperation { Op = "addNode", Id = "cache", Label = "Redis" }]);

        var saved = MerpeepsDocument.Save(Positioned, model);

        Assert.Contains("cache", saved, StringComparison.Ordinal);
        Assert.Contains("api[API]", saved, StringComparison.Ordinal);
    }

    /// <summary>
    /// Connecting two nodes is a body-only edit; edges carry no stored geometry
    /// unless someone routes them by hand.
    /// </summary>
    [Fact]
    public void ConnectingNodesDoesNotTouchTheLayoutBlock()
    {
        var (model, history) = Open(Positioned);
        var before = MerpeepsDoc.Parse(Positioned);

        OperationApplier.Apply(model, history, [new DiagramOperation { Op = "connect", From = "db", To = "api" }]);

        var saved = MerpeepsDocument.Save(Positioned, model);
        var after = MerpeepsDocument.Load(saved);

        Assert.NotNull(before);
        Assert.Equal(0, after.Node("api")!.X);
        Assert.Equal(200, after.Node("db")!.X);
    }

    // ------------------------------------------------- where it does bite

    /// <summary>
    /// Deleting a node leaves its stored coordinates behind as a stale entry.
    ///
    /// <para>This looks like a bug and is a deliberate tension worth naming. The
    /// format's rule (§2) is that entries which no longer resolve are retained
    /// and flagged, never deleted — because on load we cannot tell "this node
    /// was removed" from "this node has not been added yet", and the tool being
    /// out of step with the data is not grounds for destroying the data.</para>
    ///
    /// <para>The asymmetry decides it: a stale entry costs a few bytes and never
    /// affects rendering, whereas discarding a position someone arranged by hand
    /// costs real work. So the cruft is accepted and made visible rather than
    /// silently collected. If it ever becomes a nuisance the answer is an
    /// explicit prune, not a change to the default.</para>
    /// </summary>
    [Fact]
    public void DeletingANodeLeavesItsPositionAsAStaleEntry()
    {
        var (model, history) = Open(Positioned);

        OperationApplier.Apply(model, history, [new DiagramOperation { Op = "delete", Ids = ["db"] }]);

        var saved = MerpeepsDocument.Save(Positioned, model);

        // The node is gone from the diagram...
        Assert.DoesNotContain("db[(Database)]", saved, StringComparison.Ordinal);
        Assert.Null(MerpeepsDocument.Load(saved).Node("db"));

        // ...but its coordinates are kept, in case the deletion is reversed or
        // the node returns under the same id.
        Assert.Contains("db:", saved, StringComparison.Ordinal);
    }

    /// <summary>
    /// The honest worst case: an agent edits the body directly and changes an
    /// id, so the stored position no longer resolves.
    ///
    /// <para>This is real coupling and the format does not pretend otherwise.
    /// What it does is fail softly — the orphan is retained rather than deleted,
    /// because the alternative is destroying a colleague's layout on the
    /// strength of a guess about intent. The diagram still renders; it simply
    /// falls back to automatic placement for that node.</para>
    /// </summary>
    [Fact]
    public void AnIdChangedInTheBodyLeavesAStaleEntryRatherThanBreaking()
    {
        // An agent rewrites the body, renaming the id api -> gateway.
        var edited = Positioned.Replace("api[API] -->", "gateway[API] -->");

        var model = MerpeepsDocument.Load(edited);

        // The diagram is intact and usable.
        Assert.NotNull(model.Node("gateway"));
        Assert.Equal(2, model.Nodes.Count);

        // The new node simply has no stored position; nothing threw, nothing
        // was lost, and the old entry is still there to be reconciled.
        Assert.False(model.Node("gateway")!.HasExplicitPosition);

        var saved = MerpeepsDocument.Save(edited, model);
        Assert.Contains("api", saved, StringComparison.Ordinal);
    }

    /// <summary>
    /// A document with no layout block is completely ordinary. This is the
    /// difference from formats where geometry is mandatory: the coupling is
    /// opt-in, so an agent that never positions anything never pays for it.
    /// </summary>
    [Fact]
    public void ADocumentWithNoPositionsIsFullyValid()
    {
        const string plain = "flowchart LR\n    api[API] --> db[(Database)]\n";

        var model = MerpeepsDocument.Load(plain);
        Assert.False(model.AnyExplicitPositions);

        var (m2, history) = Open(plain);
        OperationApplier.Apply(m2, history, [new DiagramOperation { Op = "addNode", Id = "cache", Label = "Redis" }]);

        var saved = MerpeepsDocument.Save(plain, m2);

        // No layout block appears merely because the document was edited.
        Assert.DoesNotContain("layout:", saved, StringComparison.Ordinal);
    }

    /// <summary>
    /// Positions may be partial. An agent can place the two nodes it cares about
    /// and leave the rest to the layout engine, which is what stops "store
    /// positions" from meaning "store every position".
    /// </summary>
    [Fact]
    public void PositionsMayCoverOnlySomeNodes()
    {
        const string partial = """
            ---
            x-merpeeps:
              spec: 1
              layout:
                nodes:
                  api: {x: 40, y: 40}
            ---
            flowchart LR
                api[API] --> db[(Database)]
            """;

        var model = MerpeepsDocument.Load(partial);

        Assert.True(model.Node("api")!.HasExplicitPosition);
        Assert.False(model.Node("db")!.HasExplicitPosition);
    }
}
