using Merpeeps.Core;
using Merpeeps.Core.Model;
using Xunit;

namespace Merpeeps.Core.Tests;

/// <summary>
/// What a <c>spec: 1</c> reader does with metadata it was never taught.
///
/// <para>This is the check that decides whether layout <em>constraints</em> —
/// "these nodes share a rank", "flow left to right" — can be added additively or
/// need a spec bump. The direction at risk is not the obvious one:</para>
///
/// <list type="bullet">
///   <item><b>New reader, old file</b> — trivially fine, there is nothing to
///   misread.</item>
///   <item><b>Old reader, new file</b> — safe <em>only if</em> the already-published
///   v1 reader ignores unknown keys harmlessly. If it rejects or mangles them,
///   an additive key silently breaks every existing reader the moment someone
///   writes a new file, and the version has to move instead.</item>
/// </list>
///
/// <para>Files outlive tools, so this cannot be assumed from the fact that the
/// spec says unknown keys are preserved. It has to be true of the code.</para>
/// </summary>
public class ForwardCompatibilityTests
{
    /// <summary>
    /// A plausible future document: Tier 2 coordinates the current reader
    /// understands, alongside Tier 1 constraints it has never heard of.
    /// </summary>
    private const string FutureDocument = """
        ---
        x-merpeeps:
          spec: 1
          layout:
            nodes:
              a: {x: 10, y: 20}
              b: {x: 200, y: 20}
            constraints:
              - same-rank: [a, b]
              - direction: LR
              - relative: {b: right-of a}
            engine: manual
          style:
            nodes:
              a: {fill: '#DAE8FC'}
        ---
        flowchart LR
            a[Alpha] --> b[Beta]
        """;

    [Fact]
    public void AnUnknownLayoutKeyDoesNotStopTheDocumentLoading()
    {
        var model = MerpeepsDocument.Load(FutureDocument);

        Assert.Equal(2, model.Nodes.Count);
        Assert.Equal("Alpha", model.Node("a")!.Label);
    }

    /// <summary>
    /// The keys it does understand must keep working. An unknown sibling must
    /// not poison the block it appears in.
    /// </summary>
    [Fact]
    public void KnownKeysStillWorkAlongsideAnUnknownOne()
    {
        var model = MerpeepsDocument.Load(FutureDocument);

        Assert.Equal(10, model.Node("a")!.X);
        Assert.Equal(200, model.Node("b")!.X);
        Assert.True(model.Node("a")!.HasExplicitPosition);
        Assert.Equal("#DAE8FC", model.Node("a")!.Fill);
        Assert.Equal("manual", model.LayoutEngine);
    }

    /// <summary>
    /// The decisive one. Opening a constrained document in a reader that
    /// predates constraints, and saving, must not strip them — otherwise the
    /// older tool quietly destroys the newer tool's work, which is the failure
    /// mode the whole retain-what-you-do-not-understand rule exists to prevent.
    /// </summary>
    [Fact]
    public void SavingFromAnOlderReaderDoesNotDestroyTheUnknownKey()
    {
        var model = MerpeepsDocument.Load(FutureDocument);
        var saved = MerpeepsDocument.Save(FutureDocument, model);

        Assert.Contains("constraints", saved, StringComparison.Ordinal);
        Assert.Contains("same-rank", saved, StringComparison.Ordinal);
        Assert.Contains("direction", saved, StringComparison.Ordinal);
        Assert.Contains("right-of", saved, StringComparison.Ordinal);
    }

    [Fact]
    public void TheUnknownKeySurvivesRepeatedRoundTrips()
    {
        var once = MerpeepsDocument.Save(FutureDocument, MerpeepsDocument.Load(FutureDocument));
        var twice = MerpeepsDocument.Save(once, MerpeepsDocument.Load(once));

        Assert.Contains("same-rank", twice, StringComparison.Ordinal);

        // And it settles rather than drifting a little on each pass.
        Assert.Equal(once, twice);
    }

    /// <summary>
    /// The body a stock renderer sees is untouched either way. Constraints are
    /// metadata; they must never leak into the diagram.
    /// </summary>
    [Fact]
    public void TheDiagramBodyIsUnaffected()
    {
        var saved = MerpeepsDocument.Save(FutureDocument, MerpeepsDocument.Load(FutureDocument));

        Assert.Equal(MerpeepsDoc.BodyOf(FutureDocument), MerpeepsDoc.BodyOf(saved));
    }

    /// <summary>
    /// Constraints with no coordinates — the Tier 1 shape — must degrade to a
    /// perfectly ordinary auto-laid-out document rather than to an error. This
    /// is the graceful failure that makes constraints safe to adopt: an old
    /// reader gets a correct diagram, merely an unconstrained one.
    /// </summary>
    [Fact]
    public void ConstraintsWithoutCoordinatesDegradeToAutoLayout()
    {
        const string tierOneOnly = """
            ---
            x-merpeeps:
              spec: 1
              layout:
                constraints:
                  - same-rank: [a, b]
            ---
            flowchart LR
                a[Alpha] --> b[Beta]
            """;

        var model = MerpeepsDocument.Load(tierOneOnly);

        Assert.Equal(2, model.Nodes.Count);
        Assert.False(model.AnyExplicitPositions);

        var saved = MerpeepsDocument.Save(tierOneOnly, model);
        Assert.Contains("same-rank", saved, StringComparison.Ordinal);

        // No coordinate map is invented merely because the file was opened.
        Assert.DoesNotContain("nodes:", saved, StringComparison.Ordinal);
    }

    /// <summary>
    /// Unknown keys at the top of the block, not just inside layout — a future
    /// revision might add a sibling to layout and style.
    /// </summary>
    [Fact]
    public void AnUnknownTopLevelKeyIsAlsoPreserved()
    {
        const string src = """
            ---
            x-merpeeps:
              spec: 1
              semantics:
                a: {kind: container, tech: 'Node.js'}
              layout:
                nodes:
                  a: {x: 0, y: 0}
            ---
            flowchart LR
                a[Alpha] --> b[Beta]
            """;

        var saved = MerpeepsDocument.Save(src, MerpeepsDocument.Load(src));

        Assert.Contains("semantics", saved, StringComparison.Ordinal);
        Assert.Contains("Node.js", saved, StringComparison.Ordinal);
    }
}
