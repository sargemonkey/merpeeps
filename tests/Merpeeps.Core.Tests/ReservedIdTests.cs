using Merpeeps.Core;
using Merpeeps.Core.Editing;
using Merpeeps.Core.Parsing;
using Xunit;

namespace Merpeeps.Core.Tests;

/// <summary>
/// Node ids that collide with mermaid's own keywords.
///
/// <para>Mermaid cannot use <c>end</c>, <c>class</c>, <c>style</c>, <c>graph</c>
/// and friends as node ids — in <em>any</em> form. Bare, labelled, quoted,
/// inside a subgraph, as an edge source: all are parse errors. Verified against
/// 11.16.1 in <c>oracle/reserved-word-probe2.mjs</c>, which also confirms the
/// escapes that do work (a trailing underscore, or changing case).</para>
///
/// <para>This matters here because Merpeeps <em>writes</em> mermaid. An agent
/// asking for a node called <c>end</c> is entirely reasonable — it is the
/// obvious id for a terminal state — and emitting it verbatim would produce a
/// file that renders nowhere. Since the format's whole promise is that the body
/// stays valid for a stock renderer, generating an unrenderable body is about
/// the worst bug available.</para>
/// </summary>
public class ReservedIdTests
{
    private const string Source = "flowchart TD\n    start[Start] --> ok[Fine]\n";

    private static string AddNode(string id, string label = "Done")
    {
        var model = MerpeepsDocument.Load(Source);
        var history = new EditHistory(model);

        OperationApplier.Apply(model, history,
            [new DiagramOperation { Op = "addNode", Id = id, Label = label }]);

        return MerpeepsDocument.Save(Source, model);
    }

    [Theory]
    [InlineData("end")]
    [InlineData("class")]
    [InlineData("style")]
    [InlineData("graph")]
    [InlineData("subgraph")]
    [InlineData("click")]
    [InlineData("linkStyle")]
    [InlineData("flowchart")]
    public void AReservedWordIsRecognised(string word)
    {
        Assert.True(MermaidKeywords.IsReserved(word), $"'{word}' should be reserved");
    }

    [Theory]
    [InlineData("endpoint")]
    [InlineData("classroom")]
    [InlineData("styles")]
    [InlineData("start")]
    [InlineData("api")]
    public void AnOrdinaryIdIsNotReserved(string word)
    {
        Assert.False(MermaidKeywords.IsReserved(word), $"'{word}' should not be reserved");
    }

    /// <summary>
    /// Case matters: mermaid's keywords are lowercase, and <c>End</c> parses
    /// perfectly well. Over-reserving would rename ids that were never a problem.
    /// </summary>
    [Theory]
    [InlineData("End")]
    [InlineData("END")]
    [InlineData("Class")]
    public void OnlyTheLowercaseFormIsReserved(string word)
    {
        Assert.False(MermaidKeywords.IsReserved(word));
    }

    [Theory]
    [InlineData("end", "end_")]
    [InlineData("class", "class_")]
    [InlineData("graph", "graph_")]
    public void ASafeIdIsTheWordWithATrailingUnderscore(string word, string expected)
    {
        // A trailing underscore is the smallest change that parses: the id stays
        // readable and obviously derived, unlike a rename to something unrelated.
        Assert.Equal(expected, MermaidKeywords.MakeSafe(word));
    }

    [Fact]
    public void AnAlreadySafeIdIsLeftAlone()
    {
        Assert.Equal("api", MermaidKeywords.MakeSafe("api"));
        Assert.Equal("End", MermaidKeywords.MakeSafe("End"));
    }

    [Fact]
    public void CollisionWithTheEscapedFormIsAvoided()
    {
        // If end_ is taken, end__ and so on, until something is free — otherwise
        // escaping would silently merge two distinct nodes.
        Assert.Equal("end__", MermaidKeywords.MakeSafe("end", taken: id => id == "end_"));
    }

    // ------------------------------------------------------- the actual writer

    /// <summary>
    /// The one that matters: adding a node called <c>end</c> must not produce a
    /// document that fails to parse.
    /// </summary>
    [Theory]
    [InlineData("end")]
    [InlineData("class")]
    [InlineData("subgraph")]
    public void AddingANodeWithAReservedIdStillProducesValidMermaid(string word)
    {
        var saved = AddNode(word);

        Assert.DoesNotContain($" {word}[", saved, StringComparison.Ordinal);
        Assert.DoesNotContain($"    {word}\n", saved, StringComparison.Ordinal);
        Assert.Contains($"{word}_", saved, StringComparison.Ordinal);
        Assert.Contains("Done", saved, StringComparison.Ordinal);
    }

    [Fact]
    public void TheEscapedNodeIsStillFindableInTheModel()
    {
        var saved = AddNode("end");
        var model = MerpeepsDocument.Load(saved);

        Assert.NotNull(model.Node("end_"));
        Assert.Equal("Done", model.Node("end_")!.Label);
    }

    [Fact]
    public void AnOrdinaryIdIsUnaffected()
    {
        var saved = AddNode("finish");

        Assert.Contains("finish[Done]", saved, StringComparison.Ordinal);
        Assert.DoesNotContain("finish_", saved, StringComparison.Ordinal);
    }
}
