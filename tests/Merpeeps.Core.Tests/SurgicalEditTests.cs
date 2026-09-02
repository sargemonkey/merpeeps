using Merpeeps.Core;
using Merpeeps.Core.Editing;
using Merpeeps.Core.Model;
using Xunit;

namespace Merpeeps.Core.Tests;

/// <summary>
/// Edits must produce minimal diffs. A tool that rewrites every line each time
/// you rename a box is unusable in a git repository, however correct its output.
/// </summary>
public class SurgicalEditTests
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

    private static string[] BodyLines(string doc) =>
        MerpeepsDoc.BodyOf(doc).Replace("\r\n", "\n").TrimEnd('\n').Split('\n');

    private static int ChangedLineCount(string before, string after)
    {
        var a = BodyLines(before);
        var b = BodyLines(after);
        var changed = 0;
        for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            var l = i < a.Length ? a[i] : null;
            var r = i < b.Length ? b[i] : null;
            if (l != r) changed++;
        }
        return changed;
    }

    [Fact]
    public void RenamingANodeChangesExactlyOneLine()
    {
        var m = MerpeepsDocument.Load(Source);
        var h = new EditHistory(m);
        h.Apply(new RenameNodeCommand(m.Node("orders")!, "Order Orchestration"));

        var saved = MerpeepsDocument.Save(Source, m);

        Assert.Equal(1, ChangedLineCount(Source, saved));
        Assert.Contains("orders[Order Orchestration]", saved);
        // the surrounding lines survive untouched, including their indentation
        Assert.Contains("    web[Web Client] --> gw{{API Gateway}}", saved);
        Assert.Contains("    gw -->|authenticate| auth([Auth Service])", saved);
    }

    [Fact]
    public void ChangingAShapeChangesExactlyOneLine()
    {
        var m = MerpeepsDocument.Load(Source);
        var h = new EditHistory(m);
        h.Apply(new ChangeShapeCommand(m.Node("inv")!, NodeShape.Hexagon));

        var saved = MerpeepsDocument.Save(Source, m);

        Assert.Equal(1, ChangedLineCount(Source, saved));
        Assert.Contains("inv{{Inventory DB}}", saved);
    }

    [Fact]
    public void AddingANodeAppendsWithoutTouchingExistingLines()
    {
        var m = MerpeepsDocument.Load(Source);
        var h = new EditHistory(m);
        h.Apply(new AddNodeCommand(new DiagramNode
            { Id = "cache", Label = "Redis", Shape = NodeShape.Cylinder }));
        h.Apply(new AddEdgeCommand(new DiagramEdge { From = "orders", To = "cache" }));

        var saved = MerpeepsDocument.Save(Source, m);

        // Every original line survives unmodified and in the same relative
        // order. Indexes may shift, because new statements are inserted above
        // trailing directives rather than dumped at the end of the file.
        var before = BodyLines(Source);
        var after = BodyLines(saved);
        var cursor = 0;
        foreach (var line in before)
        {
            var found = Array.IndexOf(after, line, cursor);
            Assert.True(found >= 0,
                $"original line vanished or was modified: '{line}'");
            cursor = found + 1;
        }

        Assert.Contains("cache[(Redis)]", saved);
        Assert.Contains("orders --> cache", saved);
    }

    [Fact]
    public void NewStatementsGoAboveTrailingDirectives()
    {
        var m = MerpeepsDocument.Load(Source);
        var h = new EditHistory(m);
        h.Apply(new AddNodeCommand(new DiagramNode { Id = "cache", Label = "Redis" }));

        var lines = BodyLines(MerpeepsDocument.Save(Source, m));
        var newNode = Array.FindIndex(lines, l => l.Contains("cache"));
        var classDef = Array.FindIndex(lines, l => l.Contains("classDef"));

        Assert.True(newNode >= 0 && classDef >= 0);
        Assert.True(newNode < classDef,
            "new statements should sit with the other statements, not below directives");
    }

    [Fact]
    public void UnmodelledDirectivesKeepTheirExactText()
    {
        var m = MerpeepsDocument.Load(Source);
        var h = new EditHistory(m);
        h.Apply(new RenameNodeCommand(m.Node("web")!, "Browser"));

        var saved = MerpeepsDocument.Save(Source, m);

        Assert.Contains("    classDef external fill:#f96,stroke:#333", saved);
        Assert.Contains("    class auth external", saved);
    }

    [Fact]
    public void DeletionFallsBackToRegenerationButStaysCorrect()
    {
        // Removing a node can reshape a chain line, so a full rewrite is the
        // honest option there — it just has to remain valid and complete.
        var m = MerpeepsDocument.Load(Source);
        var h = new EditHistory(m);
        h.Apply(new DeleteNodesCommand(new[] { m.Node("inv")! }));

        var saved = MerpeepsDocument.Save(Source, m);
        var reloaded = MerpeepsDocument.Load(saved);

        Assert.Null(reloaded.Node("inv"));
        Assert.NotNull(reloaded.Node("orders"));
        Assert.Contains("classDef external", saved);
    }

    [Fact]
    public void RepeatedSavesAreStable()
    {
        var m = MerpeepsDocument.Load(Source);
        var h = new EditHistory(m);
        h.Apply(new RenameNodeCommand(m.Node("gw")!, "Gateway"));

        var once = MerpeepsDocument.Save(Source, m);
        var twice = MerpeepsDocument.Save(once, MerpeepsDocument.Load(once));

        Assert.Equal(BodyLines(once), BodyLines(twice));
    }

    [Fact]
    public void SurgicalOutputStillParses()
    {
        var m = MerpeepsDocument.Load(Source);
        var h = new EditHistory(m);
        h.Apply(new RenameNodeCommand(m.Node("auth")!, "Identity"));
        h.Apply(new ChangeShapeCommand(m.Node("auth")!, NodeShape.Rhombus));

        var saved = MerpeepsDocument.Save(Source, m);
        var reloaded = MerpeepsDocument.Load(saved);

        Assert.Equal("Identity", reloaded.Node("auth")!.Label);
        Assert.Equal(NodeShape.Rhombus, reloaded.Node("auth")!.Shape);
        Assert.Equal(m.Edges.Count, reloaded.Edges.Count);
    }
}
