using System.Linq;
using Merpeeps.Core;
using Merpeeps.Core.Editing;
using Merpeeps.Core.Model;
using Xunit;

namespace Merpeeps.Core.Tests;

/// <summary>
/// Editing a link changes the diagram body.
/// </summary>
/// <remarks>
/// Unlike node positions and styling, a link's label and arrow style are part of
/// mermaid's own syntax — <c>a --&gt;|text| b</c>. So these edits genuinely
/// rewrite the document, and the writer has to carry them through. Node styling
/// never touches the body, which makes it easy to assume nothing does.
/// </remarks>
public class EdgeEditingTests
{
    private const string Source =
        "flowchart TD\n    a[One] -->|old| b[Two]\n    b --> c[Three]\n";

    private static (DiagramModel Model, EditHistory History) Load()
    {
        var model = MerpeepsDocument.Load(Source);
        return (model, new EditHistory(model));
    }

    private static DiagramEdge Edge(DiagramModel m, string from, string to) =>
        m.Edges.First(e => e.From == from && e.To == to);

    [Fact]
    public void RelabellingALinkReachesTheBody()
    {
        var (m, h) = Load();
        h.Apply(new RelabelEdgeCommand(Edge(m, "a", "b"), "new"));

        var saved = MerpeepsDocument.Save(Source, m);

        Assert.Contains("|new|", saved);
        Assert.DoesNotContain("|old|", saved);
        Assert.Equal("new", MerpeepsDocument.Load(saved).Edges.First().Label);
    }

    /// <summary>
    /// Clearing a label removes the pipes rather than leaving an empty pair.
    /// </summary>
    /// <remarks>
    /// <c>a --&gt;|| b</c> is not valid mermaid, so an empty string has to become
    /// no label at all — which is why the command stores whitespace as null.
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ClearingALabelRemovesThePipes(string? empty)
    {
        var (m, h) = Load();
        h.Apply(new RelabelEdgeCommand(Edge(m, "a", "b"), empty));

        var saved = MerpeepsDocument.Save(Source, m);

        Assert.DoesNotContain("|", saved);

        // The declarations survive, because the edit patches the operator alone.
        Assert.Contains("a[One] --> b[Two]", saved);
        Assert.Null(MerpeepsDocument.Load(saved).Edges.First().Label);
    }

    [Theory]
    [InlineData(EdgeKind.Open, "a[One] --- b[Two]")]
    [InlineData(EdgeKind.Dotted, "a[One] -.- b[Two]")]
    [InlineData(EdgeKind.DottedArrow, "a[One] -.-> b[Two]")]
    [InlineData(EdgeKind.Thick, "a[One] === b[Two]")]
    [InlineData(EdgeKind.ThickArrow, "a[One] ==> b[Two]")]
    [InlineData(EdgeKind.Invisible, "a[One] ~~~ b[Two]")]
    public void ChangingTheArrowStyleReachesTheBody(EdgeKind kind, string expected)
    {
        var (m, h) = Load();
        var edge = Edge(m, "a", "b");

        h.Apply(new RelabelEdgeCommand(edge, null));   // labels complicate the match
        h.Apply(new ChangeEdgeKindCommand(edge, kind));

        var saved = MerpeepsDocument.Save(Source, m);
        Assert.Contains(expected, saved);
        Assert.Equal(kind, MerpeepsDocument.Load(saved).Edges.First().Kind);
    }

    [Fact]
    public void DeletingALinkReachesTheBody()
    {
        var (m, h) = Load();
        h.Apply(new DeleteEdgeCommand(Edge(m, "a", "b")));

        var saved = MerpeepsDocument.Save(Source, m);
        var back = MerpeepsDocument.Load(saved);

        Assert.Single(back.Edges);
        Assert.Equal(("b", "c"), (back.Edges[0].From, back.Edges[0].To));

        // The nodes survive: deleting the link between two boxes must not delete
        // the boxes, even though `a` now appears in no link at all.
        Assert.Equal(3, back.Nodes.Count);
        Assert.NotNull(back.Node("a"));
    }

    /// <summary>Every link edit is exactly reversible.</summary>
    [Fact]
    public void EveryEdgeEditUndoes()
    {
        var (m, h) = Load();
        var edge = Edge(m, "a", "b");

        h.Apply(new RelabelEdgeCommand(edge, "changed"));
        h.Undo();
        Assert.Equal("old", edge.Label);

        h.Apply(new ChangeEdgeKindCommand(edge, EdgeKind.Thick));
        h.Undo();
        Assert.Equal(EdgeKind.Arrow, edge.Kind);

        h.Apply(new DeleteEdgeCommand(edge));
        Assert.Single(m.Edges);
        h.Undo();
        Assert.Equal(2, m.Edges.Count);
    }

    /// <summary>
    /// A deleted link is restored to its original position in the list.
    /// </summary>
    /// <remarks>
    /// Order is not cosmetic: mermaid draws links in source order, and the
    /// writer emits them the same way. Undo appending to the end would silently
    /// reorder the document.
    /// </remarks>
    [Fact]
    public void UndoingADeleteRestoresTheOrder()
    {
        var (m, h) = Load();
        var first = m.Edges[0];

        h.Apply(new DeleteEdgeCommand(first));
        h.Undo();

        Assert.Same(first, m.Edges[0]);
    }

    /// <summary>
    /// Editing a link leaves every other line of the document alone.
    /// </summary>
    /// <remarks>
    /// The promise the whole surgical writer exists for. A tool that rewrites
    /// the file whenever you retitle an arrow is unusable in a git repository.
    /// </remarks>
    [Fact]
    public void OtherLinesAreUntouched()
    {
        const string src =
            "---\ntitle: Keep me\n---\n"
            + "flowchart TD\n"
            + "    a[One] -->|old| b[Two]\n"
            + "    b --> c[Three]\n"
            + "    c --> d[Four]\n";

        var m = MerpeepsDocument.Load(src);
        var h = new EditHistory(m);
        h.Apply(new RelabelEdgeCommand(Edge(m, "a", "b"), "new"));

        var saved = MerpeepsDocument.Save(src, m);

        Assert.Contains("title: Keep me", saved);
        Assert.Contains("b --> c[Three]", saved);
        Assert.Contains("c --> d[Four]", saved);
    }

    /// <summary>
    /// A relabel rewrites the operator and nothing else — not even the node
    /// declarations on the same line.
    /// </summary>
    /// <remarks>
    /// <para>This is the property, stated exactly. Edges used to be matched by
    /// content, so a relabelled link looked like "one link vanished and a
    /// different one appeared" and the writer regenerated the whole document.
    /// Everything still round-tripped, so nothing failed — the file was just
    /// rewritten from end to end, and a diff that should have been one word
    /// touched every line.</para>
    ///
    /// <para>Asserted by counting changed lines rather than checking a
    /// substring: a regenerated document contains the right text too.</para>
    /// </remarks>
    [Fact]
    public void ARelabelChangesExactlyOneLine()
    {
        const string src =
            "flowchart TD\n"
            + "    a[One] -->|old| b[Two]\n"
            + "    b --> c[Three]\n"
            + "    c -.-> d{{Four}}\n"
            + "    d ==> e([Five])\n";

        var m = MerpeepsDocument.Load(src);
        var h = new EditHistory(m);
        h.Apply(new RelabelEdgeCommand(Edge(m, "a", "b"), "new"));

        var before = src.Split('\n');
        var after = MerpeepsDoc.BodyOf(MerpeepsDocument.Save(src, m)).Split('\n');

        Assert.Equal(before.Length, after.Length);

        var changed = before.Zip(after).Count(p => p.First != p.Second);
        Assert.True(changed == 1,
            $"expected 1 changed line, got {changed}:\n"
            + string.Join("\n", before.Zip(after)
                .Where(p => p.First != p.Second)
                .Select(p => $"  - {p.First}\n  + {p.Second}")));

        // And the one line that changed kept its node declarations.
        Assert.Contains("a[One] -->|new| b[Two]", after[1]);
    }

    /// <summary>
    /// Changing the arrow style is equally surgical.
    /// </summary>
    [Fact]
    public void AStyleChangeChangesExactlyOneLine()
    {
        const string src =
            "flowchart TD\n"
            + "    a[One] --> b[Two]\n"
            + "    b --> c[Three]\n";

        var m = MerpeepsDocument.Load(src);
        var h = new EditHistory(m);
        h.Apply(new ChangeEdgeKindCommand(Edge(m, "a", "b"), EdgeKind.DottedArrow));

        var before = src.Split('\n');
        var after = MerpeepsDoc.BodyOf(MerpeepsDocument.Save(src, m)).Split('\n');

        Assert.Equal(1, before.Zip(after).Count(p => p.First != p.Second));
        Assert.Contains("a[One] -.-> b[Two]", after[1]);
    }

    /// <summary>
    /// A label containing a pipe survives, quoted, rather than destroying the
    /// link.
    /// </summary>
    /// <remarks>
    /// <para>An unquoted <c>|</c> closes the pipe pair early: <c>--&gt;|a|b|</c>
    /// parses back as a link labelled <c>a</c> followed by wreckage, and the
    /// link is simply lost. This was a pre-existing hole in the writer — nothing
    /// could reach it until edges became editable, because a label with a pipe
    /// in it had to be typed.</para>
    ///
    /// <para>Checked over two round trips, not one. The escape and its decode
    /// are separate pieces of code, and a missing decode looks perfect once and
    /// then corrupts the label on the next save — which is exactly how it would
    /// reach someone's file.</para>
    /// </remarks>
    [Theory]
    [InlineData("a|b")]
    [InlineData("say \"hi\"")]
    [InlineData("a|b and \"q\"")]
    [InlineData("plain")]
    public void AwkwardLabelsSurviveTwoRoundTrips(string label)
    {
        var (m, h) = Load();
        h.Apply(new RelabelEdgeCommand(Edge(m, "a", "b"), label));

        var once = MerpeepsDocument.Save(Source, m);
        Assert.Equal(label, MerpeepsDocument.Load(once).Edges.First().Label);

        var twice = MerpeepsDocument.Save(once, MerpeepsDocument.Load(once));
        Assert.Equal(label, MerpeepsDocument.Load(twice).Edges.First().Label);

        // The graph is intact, not just the text.
        var back = MerpeepsDocument.Load(twice);
        Assert.Equal(2, back.Edges.Count);
        Assert.Equal(3, back.Nodes.Count);
    }
}
