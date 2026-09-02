using System.Globalization;
using Merpeeps.Core;
using Merpeeps.Core.Export;
using Xunit;

namespace Merpeeps.Core.Tests;

/// <summary>
/// Covers the native mermaid export.
///
/// <para>Baking is the one operation that deliberately breaks the project's
/// central invariant — it writes styling into the diagram body, which everything
/// else is built to avoid. That makes it the operation least able to afford a
/// quiet bug: it is lossy by design, so a mistake here does not look like a
/// crash, it looks like a diagram that came out slightly wrong.</para>
/// </summary>
public class MermaidBakeTests
{
    private const string Styled = """
        ---
        x-merpeeps:
          spec: 1
          layout:
            nodes:
              a: {x: 10, y: 20}
              b: {x: 200, y: 20}
          style:
            nodes:
              a: {fill: '#DAE8FC', stroke: '#6C8EBF', icon: database}
              b: {fill: '#D5E8D4', stroke: '#82B366', strokeWidth: 3, dashed: true, text: '#111111'}
        ---
        flowchart LR
            a[Alpha] --> b[Beta]
        """;

    // ------------------------------------------------------ the style vocabulary

    [Fact]
    public void FillAndStrokeBecomeAStyleStatement()
    {
        var baked = MermaidBake.Bake(Styled).Output;

        Assert.Contains("style a fill:#DAE8FC,stroke:#6C8EBF", baked, StringComparison.Ordinal);
    }

    [Fact]
    public void WidthDashAndTextColourAreCarried()
    {
        var baked = MermaidBake.Bake(Styled).Output;

        Assert.Contains("stroke-width:3px", baked, StringComparison.Ordinal);
        Assert.Contains("stroke-dasharray:5 3", baked, StringComparison.Ordinal);
        Assert.Contains("color:#111111", baked, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryStyledNodeIsReported()
    {
        var result = MermaidBake.Bake(Styled);

        Assert.Equal(new[] { "a", "b" }, result.StyledNodes.OrderBy(x => x));
    }

    [Fact]
    public void AnUnstyledNodeGetsNoStatement()
    {
        var src = """
            ---
            x-merpeeps:
              spec: 1
              style:
                nodes:
                  a: {fill: '#DAE8FC'}
            ---
            flowchart LR
                a[Alpha] --> b[Beta]
            """;

        var result = MermaidBake.Bake(src);

        Assert.Equal(["a"], result.StyledNodes);
        Assert.DoesNotContain("style b ", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void ADocumentWithNoStylingIsLeftAlone()
    {
        const string plain = "flowchart LR\n    a[Alpha] --> b[Beta]\n";

        var result = MermaidBake.Bake(plain);

        Assert.Equal(plain, result.Output);
        Assert.Empty(result.StyledNodes);
        Assert.False(result.Lossy);
    }

    // --------------------------------------------------------- body preservation

    /// <summary>
    /// The original statements must survive untouched. Baking appends; it is not
    /// licence to rewrite what the author wrote.
    /// </summary>
    [Fact]
    public void TheOriginalBodyIsPreservedVerbatim()
    {
        var body = MerpeepsDoc.BodyOf(Styled);
        var baked = MermaidBake.Bake(Styled).Output;

        Assert.StartsWith(body.TrimEnd('\n'), baked.TrimEnd('\n').Split("    style")[0].TrimEnd('\n'),
            StringComparison.Ordinal);
        Assert.Contains("a[Alpha] --> b[Beta]", baked, StringComparison.Ordinal);
    }

    [Fact]
    public void TheMetadataBlockIsGoneFromTheOutput()
    {
        var baked = MermaidBake.Bake(Styled).Output;

        Assert.DoesNotContain("x-merpeeps", baked, StringComparison.Ordinal);
        Assert.DoesNotContain("---", baked, StringComparison.Ordinal);
    }

    /// <summary>
    /// Baking a baked file must not double the statements. It does not, because
    /// the second pass finds no metadata to read — but that is worth pinning, as
    /// it would be an easy property to lose.
    /// </summary>
    [Fact]
    public void BakingTwiceChangesNothingTheSecondTime()
    {
        var once = MermaidBake.Bake(Styled).Output;
        var twice = MermaidBake.Bake(once).Output;

        Assert.Equal(once, twice);
    }

    [Fact]
    public void ExistingStyleStatementsAreNotDisturbed()
    {
        var src = """
            ---
            x-merpeeps:
              spec: 1
              style:
                nodes:
                  a: {fill: '#DAE8FC'}
            ---
            flowchart LR
                a[Alpha] --> b[Beta]
                style b fill:#996699
                classDef hot fill:#f96
            """;

        var baked = MermaidBake.Bake(src).Output;

        Assert.Contains("style b fill:#996699", baked, StringComparison.Ordinal);
        Assert.Contains("classDef hot fill:#f96", baked, StringComparison.Ordinal);
        Assert.Contains("style a fill:#DAE8FC", baked, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------- honest loss

    /// <summary>
    /// Icons cannot cross. Mermaid's icon shapes need an icon pack registered by
    /// the host at runtime, and the one technique that draws artwork — an
    /// <c>&lt;img&gt;</c> in a label — dies under <c>htmlLabels: false</c>.
    /// Measured in <c>oracle/svg-artwork-hardened-probe.mjs</c>.
    /// </summary>
    [Fact]
    public void DroppedIconsAreNamedNotSwallowed()
    {
        var result = MermaidBake.Bake(Styled);

        Assert.Equal(["a"], result.DroppedIcons);
        Assert.True(result.Lossy);
        Assert.DoesNotContain("icon", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DroppedPositionsAreNamedNotSwallowed()
    {
        var result = MermaidBake.Bake(Styled);

        Assert.Equal(new[] { "a", "b" }, result.DroppedPositions.OrderBy(x => x));
    }

    [Fact]
    public void ADocumentThatLosesNothingSaysSo()
    {
        var src = """
            ---
            x-merpeeps:
              spec: 1
              style:
                nodes:
                  a: {fill: '#DAE8FC'}
            ---
            flowchart LR
                a[Alpha] --> b[Beta]
            """;

        var result = MermaidBake.Bake(src);

        Assert.False(result.Lossy);
        Assert.Empty(result.DroppedIcons);
        Assert.Empty(result.DroppedPositions);
    }

    // ------------------------------------------- diagram types that reject style

    /// <summary>
    /// Verified against mermaid 11.16.1 in <c>oracle/style-support-probe.mjs</c>:
    /// these five accept a <c>style</c> statement, the rest reject it with a
    /// parse error.
    /// </summary>
    [Theory]
    [InlineData("flowchart", true)]
    [InlineData("graph", true)]
    [InlineData("stateDiagram-v2", true)]
    [InlineData("classDiagram", true)]
    [InlineData("erDiagram", true)]
    [InlineData("sequenceDiagram", false)]
    [InlineData("C4Context", false)]
    [InlineData("architecture-beta", false)]
    [InlineData("journey", false)]
    [InlineData("mindmap", false)]
    public void OnlyGrammarsThatAcceptStyleAreBaked(string kind, bool accepted)
    {
        Assert.Equal(accepted, MermaidBake.AcceptsStyle(kind));
    }

    /// <summary>
    /// Emitting <c>style</c> into a sequence diagram would turn a working file
    /// into a parse error — strictly worse than doing nothing. So it does
    /// nothing, and says why.
    /// </summary>
    [Fact]
    public void AnUnsupportedDiagramIsLeftIntactAndFlagged()
    {
        var src = """
            ---
            x-merpeeps:
              spec: 1
              style:
                nodes:
                  Bob: {fill: '#DAE8FC'}
            ---
            sequenceDiagram
                Alice->>Bob: Hello
            """;

        var result = MermaidBake.Bake(src);

        Assert.NotNull(result.UnsupportedKind);
        Assert.DoesNotContain("style ", result.Output, StringComparison.Ordinal);
        Assert.Contains("Alice->>Bob: Hello", result.Output, StringComparison.Ordinal);
        Assert.True(result.Lossy);
    }

    // ------------------------------------------------------------------ culture

    /// <summary>
    /// Stroke width is a double, and formatting a double with the ambient
    /// culture is precisely the bug that silently destroyed stored coordinates
    /// on German machines: <c>3.5</c> becomes <c>"3,5"</c>, which is not valid
    /// CSS and would break the whole statement.
    /// </summary>
    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    public void StrokeWidthIsCultureInvariant(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);

            var src = """
                ---
                x-merpeeps:
                  spec: 1
                  style:
                    nodes:
                      a: {stroke: '#333333', strokeWidth: 3.5}
                ---
                flowchart LR
                    a[Alpha] --> b[Beta]
                """;

            var baked = MermaidBake.Bake(src).Output;

            Assert.Contains("stroke-width:3.5px", baked, StringComparison.Ordinal);
            Assert.DoesNotContain("3,5", baked, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
