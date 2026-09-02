using Merpeeps.Core;
using Merpeeps.Core.Editing;
using Merpeeps.Core.Model;
using Xunit;

namespace Merpeeps.Core.Tests;

/// <summary>
/// The operations layer is what an agent drives. It must go through the same
/// commands the editor uses, so agent edits are undoable and behave identically
/// to a human's — and must produce the same minimal diffs.
/// </summary>
public class OperationTests
{
    private const string Source = """
        flowchart TD
            api[API] --> db[(Database)]
            classDef external fill:#f96
        """;

    private static (DiagramModel Model, EditHistory History) Fresh()
    {
        var m = MerpeepsDocument.Load(Source);
        return (m, new EditHistory(m));
    }

    private static DiagramOperation Op(string op, params (string, object?)[] fields)
    {
        var d = new DiagramOperation { Op = op };
        foreach (var (k, v) in fields)
            d = k switch
            {
                "id" => d with { Id = (string?)v },
                "label" => d with { Label = (string?)v },
                "shape" => d with { Shape = (string?)v },
                "from" => d with { From = (string?)v },
                "to" => d with { To = (string?)v },
                "kind" => d with { Kind = (string?)v },
                "x" => d with { X = (double?)v },
                "y" => d with { Y = (double?)v },
                "ids" => d with { Ids = (string[]?)v },
                _ => d,
            };
        return d;
    }

    [Fact]
    public void BuildsADiagramFromABatch()
    {
        var (m, h) = Fresh();
        var result = OperationApplier.Apply(m, h, new[]
        {
            Op("addNode", ("id", "cache"), ("label", "Redis"), ("shape", "cylinder")),
            Op("addNode", ("id", "queue"), ("label", "Queue"), ("shape", "subroutine")),
            Op("connect", ("from", "api"), ("to", "cache"), ("label", "reads")),
            Op("connect", ("from", "api"), ("to", "queue"), ("kind", "dotted")),
        });

        Assert.True(result.Ok);
        Assert.Equal(4, result.Applied.Count);
        Assert.Equal(4, m.Nodes.Count);
        Assert.Equal(3, m.Edges.Count);
        Assert.Equal(NodeShape.Cylinder, m.Node("cache")!.Shape);
        // "dotted" means a dotted ARROW, which is what an agent asking for a
        // dotted link intends and what mermaid users write as `-.->`.
        Assert.Equal(EdgeKind.DottedArrow,
            m.Edges.First(e => e.To == "queue").Kind);
    }

    [Fact]
    public void DottedAndThickDefaultToArrowedVariants()
    {
        // Getting this wrong is silent: the diagram still renders, it just
        // means something different from what was asked for.
        Assert.Equal(EdgeKind.DottedArrow, OperationApplier.ParseKind("dotted"));
        Assert.Equal(EdgeKind.ThickArrow, OperationApplier.ParseKind("thick"));

        Assert.Equal(EdgeKind.Dotted, OperationApplier.ParseKind("dottedLine"));
        Assert.Equal(EdgeKind.Thick, OperationApplier.ParseKind("thickLine"));

        Assert.Equal(EdgeKind.Arrow, OperationApplier.ParseKind(null));
        Assert.Equal(EdgeKind.Open, OperationApplier.ParseKind("open"));
    }

    [Fact]
    public void AgentEditsAreUndoableLikeHumanEdits()
    {
        var (m, h) = Fresh();
        var before = m.Nodes.Count;

        OperationApplier.Apply(m, h, new[]
        {
            Op("addNode", ("id", "x1")),
            Op("connect", ("from", "api"), ("to", "x1")),
            Op("rename", ("id", "api"), ("label", "Gateway")),
        });

        while (h.CanUndo) h.Undo();

        Assert.Equal(before, m.Nodes.Count);
        Assert.Equal("API", m.Node("api")!.Label);
    }

    [Fact]
    public void FailedOperationsAreReportedAndDoNotClaimSuccess()
    {
        var (m, h) = Fresh();
        var result = OperationApplier.Apply(m, h, new[]
        {
            Op("addNode", ("id", "ok1")),
            Op("connect", ("from", "ok1"), ("to", "does-not-exist")),
            Op("rename", ("id", "nope"), ("label", "x")),
        });

        Assert.False(result.Ok);
        Assert.Single(result.Applied);
        Assert.Equal(2, result.Errors.Count);
        Assert.Contains(result.Errors, e => e.Contains("does-not-exist"));
    }

    [Fact]
    public void DuplicateNodeIdIsRejected()
    {
        var (m, h) = Fresh();
        var result = OperationApplier.Apply(m, h,
            new[] { Op("addNode", ("id", "api")) });

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, e => e.Contains("already exists"));
    }

    [Fact]
    public void UnknownShapeAndKindAreRejectedClearly()
    {
        var (m, h) = Fresh();
        var shape = OperationApplier.Apply(m, h,
            new[] { Op("addNode", ("id", "z"), ("shape", "octagon")) });
        Assert.Contains(shape.Errors, e => e.Contains("unknown shape"));

        var kind = OperationApplier.Apply(m, h,
            new[] { Op("connect", ("from", "api"), ("to", "db"), ("kind", "squiggly")) });
        Assert.Contains(kind.Errors, e => e.Contains("unknown link kind"));
    }

    [Fact]
    public void AgentAddingOneNodeProducesAMinimalDiff()
    {
        // The whole point of the surgical writer for agents: adding a node must
        // not rewrite the document.
        var (m, h) = Fresh();
        OperationApplier.Apply(m, h, new[]
        {
            Op("addNode", ("id", "cache"), ("label", "Redis"), ("shape", "cylinder")),
            Op("connect", ("from", "api"), ("to", "cache")),
        });

        var saved = MerpeepsDocument.Save(Source, m);
        var before = MerpeepsDoc.BodyOf(Source).Replace("\r\n", "\n")
            .TrimEnd('\n').Split('\n');
        var after = MerpeepsDoc.BodyOf(saved).Replace("\r\n", "\n")
            .TrimEnd('\n').Split('\n');

        // Two lines added, none of the originals modified.
        Assert.Equal(before.Length + 2, after.Length);
        var cursor = 0;
        foreach (var line in before)
        {
            var found = Array.IndexOf(after, line, cursor);
            Assert.True(found >= 0, $"original line was modified: '{line}'");
            cursor = found + 1;
        }
        Assert.Contains("cache[(Redis)]", saved);
    }

    [Fact]
    public void DisconnectAndDeleteWork()
    {
        var (m, h) = Fresh();
        var r1 = OperationApplier.Apply(m, h,
            new[] { Op("disconnect", ("from", "api"), ("to", "db")) });
        Assert.True(r1.Ok);
        Assert.Empty(m.Edges);

        var r2 = OperationApplier.Apply(m, h,
            new[] { Op("delete", ("ids", new[] { "db" })) });
        Assert.True(r2.Ok);
        Assert.Null(m.Node("db"));
    }

    [Fact]
    public void MoveStoresPositionWithoutTouchingTheBody()
    {
        var (m, h) = Fresh();
        var r = OperationApplier.Apply(m, h,
            new[] { Op("move", ("id", "api"), ("x", 400.0), ("y", 250.0)) });

        Assert.True(r.Ok);
        Assert.Equal(400, m.Node("api")!.X);

        var saved = MerpeepsDocument.SavePositions(Source, m);
        Assert.Equal(MerpeepsDoc.BodyOf(Source), MerpeepsDoc.BodyOf(saved));
    }
}
