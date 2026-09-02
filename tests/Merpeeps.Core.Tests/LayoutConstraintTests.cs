using Merpeeps.Core;
using Merpeeps.Core.Layout;
using Merpeeps.Core.Model;
using Xunit;

namespace Merpeeps.Core.Tests;

/// <summary>
/// Layout expressed as relationships rather than coordinates.
///
/// <para>The property being defended is the one that makes constraints worth
/// having: a stale coordinate puts a node in the wrong place while the diagram
/// still looks plausible, whereas a stale constraint simply stops applying and
/// the engine produces an ordinary correct layout. These tests pin both the
/// applying and the not-applying.</para>
/// </summary>
public class LayoutConstraintTests
{
    private const string Diamond = """
        flowchart TD
            web[Web] --> api[API]
            web --> worker[Worker]
            api --> db[(Database)]
            worker --> db
        """;

    private static DiagramModel LaidOut(string source, Action<DiagramModel> constrain)
    {
        var model = MerpeepsDocument.Load(source);
        constrain(model);
        LayoutEngines.Apply(model, "layered");
        return model;
    }

    // ------------------------------------------------------------- same-rank

    [Fact]
    public void SameRankPutsTheNamedNodesOnOneLayer()
    {
        var model = LaidOut(Diamond, m => m.Constraints.SameRank.Add(["api", "worker"]));

        Assert.Equal(model.Node("api")!.Y, model.Node("worker")!.Y);
    }

    /// <summary>
    /// The interesting case: two nodes the layout would otherwise separate.
    /// Without a constraint <c>api</c> sits above <c>extra</c>, because the
    /// longer chain pushes it down.
    /// </summary>
    [Fact]
    public void SameRankOverridesWhatTheEngineWouldOtherwiseDo()
    {
        const string uneven = """
            flowchart TD
                web[Web] --> a[A]
                a --> b[B]
                b --> deep[Deep]
                web --> shallow[Shallow]
            """;

        var free = LaidOut(uneven, _ => { });
        Assert.NotEqual(free.Node("deep")!.Y, free.Node("shallow")!.Y);

        var pinned = LaidOut(uneven, m => m.Constraints.SameRank.Add(["deep", "shallow"]));
        Assert.Equal(pinned.Node("deep")!.Y, pinned.Node("shallow")!.Y);
    }

    /// <summary>
    /// The group moves to the deepest member's layer, not the shallowest.
    /// Pulling nodes upwards would place them above their own predecessors and
    /// turn forward edges into back edges — which reads as a broken diagram
    /// rather than as a constraint being honoured.
    /// </summary>
    [Fact]
    public void SameRankPullsDownwardsSoEdgesKeepTheirDirection()
    {
        const string chain = """
            flowchart TD
                a[A] --> b[B]
                b --> c[C]
                a --> d[D]
            """;

        var model = LaidOut(chain, m => m.Constraints.SameRank.Add(["c", "d"]));

        // c was the deepest, so d joins c rather than c rising to meet d.
        Assert.Equal(model.Node("c")!.Y, model.Node("d")!.Y);
        Assert.True(model.Node("d")!.Y > model.Node("a")!.Y,
            "d must stay below its predecessor a");
    }

    [Fact]
    public void ThreeNodesCanShareARank()
    {
        var model = LaidOut(Diamond, m => m.Constraints.SameRank.Add(["api", "worker", "db"]));

        Assert.Equal(model.Node("api")!.Y, model.Node("worker")!.Y);
        Assert.Equal(model.Node("api")!.Y, model.Node("db")!.Y);
    }

    // ------------------------------------------------------- graceful staleness

    /// <summary>
    /// The whole argument for constraints. A group naming a node that no longer
    /// exists must produce an ordinary correct layout — not a wrong one, and not
    /// an exception.
    /// </summary>
    [Fact]
    public void AConstraintNamingAMissingNodeIsInert()
    {
        var free = LaidOut(Diamond, _ => { });
        var stale = LaidOut(Diamond, m => m.Constraints.SameRank.Add(["api", "ghost"]));

        foreach (var node in free.Nodes)
            Assert.Equal(node.Y, stale.Node(node.Id)!.Y);
    }

    [Fact]
    public void AGroupThatLosesAllButOneMemberDoesNothing()
    {
        var free = LaidOut(Diamond, _ => { });
        var stale = LaidOut(Diamond, m => m.Constraints.SameRank.Add(["ghost", "phantom", "api"]));

        Assert.Equal(free.Node("api")!.Y, stale.Node("api")!.Y);
    }

    [Fact]
    public void UnresolvedIdsAreReportedRatherThanRemoved()
    {
        var model = MerpeepsDocument.Load(Diamond);
        model.Constraints.SameRank.Add(["api", "ghost"]);
        model.Constraints.Order.Add(["web", "phantom"]);

        Assert.Equal(new[] { "ghost", "phantom" }, model.Constraints.UnresolvedIds(model));

        // Still present: a constraint mentioning a missing node is inert, and
        // deleting it would destroy intent the moment someone renamed a node.
        Assert.Equal(2, model.Constraints.SameRank[0].Count);
    }

    // ----------------------------------------------------------------- order

    [Fact]
    public void OrderSequencesNodesWithinTheirLayer()
    {
        var model = LaidOut(Diamond, m =>
        {
            m.Constraints.SameRank.Add(["api", "worker"]);
            m.Constraints.Order.Add(["worker", "api"]);
        });

        Assert.True(model.Node("worker")!.X < model.Node("api")!.X);
    }

    [Fact]
    public void TheReverseOrderIsAlsoHonoured()
    {
        var model = LaidOut(Diamond, m =>
        {
            m.Constraints.SameRank.Add(["api", "worker"]);
            m.Constraints.Order.Add(["api", "worker"]);
        });

        Assert.True(model.Node("api")!.X < model.Node("worker")!.X);
    }

    // ------------------------------------------------------------- round trip

    [Fact]
    public void ConstraintsSurviveASaveAndReload()
    {
        var model = MerpeepsDocument.Load(Diamond);
        model.Constraints.SameRank.Add(["api", "worker"]);
        model.Constraints.Order.Add(["worker", "api"]);

        var saved = MerpeepsDocument.Save(Diamond, model);
        var reloaded = MerpeepsDocument.Load(saved);

        Assert.Equal(new[] { "api", "worker" }, reloaded.Constraints.SameRank.Single());
        Assert.Equal(new[] { "worker", "api" }, reloaded.Constraints.Order.Single());
    }

    /// <summary>
    /// Constraints are written without dragging a coordinate map along with
    /// them. That is the point of Tier 1: influence the layout without taking on
    /// the coupling of per-node positions.
    /// </summary>
    [Fact]
    public void ConstraintsAreStoredWithoutCoordinates()
    {
        var model = MerpeepsDocument.Load(Diamond);
        model.Constraints.SameRank.Add(["api", "worker"]);

        var saved = MerpeepsDocument.Save(Diamond, model);

        Assert.Contains("same-rank", saved, StringComparison.Ordinal);
        Assert.DoesNotContain("nodes:", saved, StringComparison.Ordinal);
        Assert.DoesNotContain("x:", saved, StringComparison.Ordinal);
    }

    [Fact]
    public void ConstraintsAndCoordinatesCoexist()
    {
        var model = MerpeepsDocument.Load(Diamond);
        model.Constraints.SameRank.Add(["api", "worker"]);
        model.Node("web")!.HasExplicitPosition = true;
        model.Node("web")!.X = 42;

        var saved = MerpeepsDocument.Save(Diamond, model);
        var reloaded = MerpeepsDocument.Load(saved);

        Assert.Single(reloaded.Constraints.SameRank);
        Assert.Equal(42, reloaded.Node("web")!.X);
    }

    [Fact]
    public void TheDiagramBodyIsNeverTouched()
    {
        var model = MerpeepsDocument.Load(Diamond);
        model.Constraints.SameRank.Add(["api", "worker"]);

        var saved = MerpeepsDocument.Save(Diamond, model);

        Assert.Equal(MerpeepsDoc.BodyOf(Diamond), MerpeepsDoc.BodyOf(saved));
    }

    [Fact]
    public void ADocumentWithNoConstraintsWritesNone()
    {
        var saved = MerpeepsDocument.Save(Diamond, MerpeepsDocument.Load(Diamond));

        Assert.DoesNotContain("constraints", saved, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadingIsIdempotent()
    {
        var model = MerpeepsDocument.Load(Diamond);
        model.Constraints.SameRank.Add(["api", "worker"]);

        var once = MerpeepsDocument.Save(Diamond, model);
        var twice = MerpeepsDocument.Save(once, MerpeepsDocument.Load(once));

        Assert.Equal(once, twice);
    }

    /// <summary>
    /// Regression test for a trap worth naming: teaching the reader about
    /// <c>same-rank</c> and <c>order</c> made it <em>lossy</em> for every other
    /// constraint kind. It would parse the list, re-emit only what it understood
    /// and silently drop the rest — so partial knowledge was worse than none,
    /// because before it understood constraints at all it preserved them
    /// perfectly.
    ///
    /// <para>The failure mode is the bad one: a newer tool's work destroyed by
    /// an older one merely opening the file and saving.</para>
    /// </summary>
    [Fact]
    public void ConstraintKindsThisBuildDoesNotModelAreKeptVerbatim()
    {
        const string future = """
            ---
            x-merpeeps:
              spec: 1
              layout:
                constraints:
                  - same-rank: [api, worker]
                  - direction: LR
                  - relative: {worker: right-of api}
                  - cluster: {name: backend, members: [api, db]}
            ---
            flowchart TD
                web[Web] --> api[API]
                web --> worker[Worker]
                api --> db[(Database)]
            """;

        var model = MerpeepsDocument.Load(future);

        // The ones we model are understood...
        Assert.Equal(new[] { "api", "worker" }, model.Constraints.SameRank.Single());

        // ...and the rest are retained rather than interpreted.
        Assert.Equal(3, model.Constraints.Unrecognised.Count);

        var saved = MerpeepsDocument.Save(future, model);

        Assert.Contains("same-rank", saved, StringComparison.Ordinal);
        Assert.Contains("direction", saved, StringComparison.Ordinal);
        Assert.Contains("right-of", saved, StringComparison.Ordinal);
        Assert.Contains("cluster", saved, StringComparison.Ordinal);
    }

    [Fact]
    public void UnmodelledConstraintsSurviveRepeatedRoundTrips()
    {
        const string future = """
            ---
            x-merpeeps:
              spec: 1
              layout:
                constraints:
                  - direction: LR
            ---
            flowchart TD
                a[A] --> b[B]
            """;

        var once = MerpeepsDocument.Save(future, MerpeepsDocument.Load(future));
        var twice = MerpeepsDocument.Save(once, MerpeepsDocument.Load(once));

        Assert.Contains("direction", twice, StringComparison.Ordinal);
        Assert.Equal(once, twice);
    }
}
