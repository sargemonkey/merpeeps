using Merpeeps.Render;
using Xunit;

namespace Merpeeps.App.Tests;

/// <summary>
/// Reading back the arrangement a real renderer produced.
///
/// <para>Freezing a mermaid-computed layout as Merpeeps coordinates, so the diagram
/// renders identically everywhere afterwards. The editor no longer does this —
/// borrowed layouts were removed for opening a browser pane mid-edit — but the
/// scripted path remains: <c>merpeeps preview</c> writes a page exposing
/// <c>window.merpeepsHarvest</c>, and <c>merpeeps adopt --positions</c> writes the
/// result into a file.</para>
///
/// <para>The JavaScript half runs in a browser and is covered by
/// <c>oracle/harvest-test.mjs</c> in real Chrome. This covers the parsing half,
/// which is where malformed input has to be survived rather than thrown on: a
/// harvest is a convenience, and failing it must cost a layout, never the
/// document.</para>
/// </summary>
public class HarvestReaderTests
{
    [Fact]
    public void AWellFormedMapIsRead()
    {
        var result = HarvestReader.Parse(
            """{"api":{"x":10,"y":20,"w":160,"h":56},"db":{"x":200,"y":20,"w":100,"h":40}}""");

        Assert.Equal(2, result.Count);
        Assert.Equal(10, result["api"].X);
        Assert.Equal(20, result["api"].Y);
        Assert.Equal(160, result["api"].W);
        Assert.Equal(200, result["db"].X);
    }

    /// <summary>
    /// Some webviews hand a string return value back already JSON-quoted. The
    /// same payload has to work either way, because which you get depends on the
    /// platform's web engine rather than on anything we control.
    /// </summary>
    [Fact]
    public void ADoubleEncodedPayloadIsUnwrapped()
    {
        var inner = """{"api":{"x":1,"y":2,"w":3,"h":4}}""";
        var quoted = System.Text.Json.JsonSerializer.Serialize(inner);

        var result = HarvestReader.Parse(quoted);

        Assert.Single(result);
        Assert.Equal(1, result["api"].X);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("null")]
    [InlineData("undefined")]
    [InlineData("not json at all")]
    [InlineData("[1,2,3]")]
    [InlineData("{ broken")]
    public void AnythingMalformedYieldsAnEmptyMapRatherThanThrowing(string? json)
    {
        Assert.Empty(HarvestReader.Parse(json));
    }

    /// <summary>
    /// A node reported without a usable position is dropped rather than pinned
    /// to the origin. A wrong position is worse than none: the layout engine
    /// would have placed it sensibly, and a node silently at (0,0) reads as a
    /// bug in the diagram.
    /// </summary>
    [Theory]
    [InlineData("""{"good":{"x":1,"y":2,"w":3,"h":4},"bad":{"x":null,"y":2}}""")]
    [InlineData("""{"good":{"x":1,"y":2,"w":3,"h":4},"bad":{"y":2}}""")]
    [InlineData("""{"good":{"x":1,"y":2,"w":3,"h":4},"bad":{}}""")]
    public void ANodeWithoutAUsablePositionIsDropped(string json)
    {
        var result = HarvestReader.Parse(json);

        Assert.True(result.ContainsKey("good"));
        Assert.False(result.ContainsKey("bad"));
    }

    [Fact]
    public void AnEmptyObjectIsAnEmptyMap()
    {
        Assert.Empty(HarvestReader.Parse("{}"));
    }

    [Fact]
    public void PropertyNamesAreCaseInsensitive()
    {
        var result = HarvestReader.Parse("""{"api":{"X":10,"Y":20,"W":30,"H":40}}""");

        Assert.Equal(10, result["api"].X);
        Assert.Equal(40, result["api"].H);
    }

    [Fact]
    public void MissingDimensionsDefaultRatherThanFailing()
    {
        var result = HarvestReader.Parse("""{"api":{"x":10,"y":20}}""");

        Assert.Single(result);
        Assert.Equal(0, result["api"].W);
    }

    // ------------------------------------------------- the shell's half

    /// <summary>
    /// The shell must expose the entry point the host calls, in both modes —
    /// harvesting is not specific to one renderer.
    /// </summary>
    [Theory]
    [InlineData(RenderAssets.Mode.Stock)]
    [InlineData(RenderAssets.Mode.WithMerpeeps)]
    public void TheShellExposesAHarvestEntryPoint(RenderAssets.Mode mode)
    {
        var html = RenderAssets.BuildShell(mode, dark: false, autoRender: null, layout: null);

        Assert.Contains("window.merpeepsHarvest", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// mermaid draws nodes from the centre and Merpeeps stores the top-left.
    /// Getting that wrong offsets every node by half its size — a diagram that
    /// looks almost right, which is the hardest kind of bug to notice.
    /// </summary>
    [Fact]
    public void TheHarvestConvertsFromCentreToTopLeft()
    {
        var html = RenderAssets.BuildShell(
            RenderAssets.Mode.Stock, dark: false, autoRender: null, layout: null);

        Assert.Contains("box.width / 2", html, StringComparison.Ordinal);
        Assert.Contains("box.height / 2", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Positions must come from the SVG transform, not a bounding rect: the
    /// transform is in user units and is what the layout engine decided, while a
    /// rect is viewport pixels and would change with zoom.
    /// </summary>
    /// <remarks>
    /// Scoped to our own harvest function. Asserting over the whole page is
    /// meaningless — mermaid's own bundle uses <c>getBoundingClientRect</c>
    /// dozens of times, so the naive version of this test failed on someone
    /// else's code.
    /// </remarks>
    [Fact]
    public void TheHarvestReadsUserUnitsNotViewportPixels()
    {
        var html = RenderAssets.BuildShell(
            RenderAssets.Mode.Stock, dark: false, autoRender: null, layout: null);

        var start = html.IndexOf("window.merpeepsHarvest", StringComparison.Ordinal);
        Assert.True(start > 0, "the harvest entry point is missing");

        var end = html.IndexOf("window.merpeepsReady", start, StringComparison.Ordinal);
        Assert.True(end > start, "could not delimit the harvest function");

        var harvest = html[start..end];

        Assert.Contains("translate", harvest, StringComparison.Ordinal);
        Assert.Contains("getBBox", harvest, StringComparison.Ordinal);
        Assert.DoesNotContain("getBoundingClientRect", harvest, StringComparison.Ordinal);
    }

    /// <summary>
    /// The harvest recovers the author's node id from mermaid's element id,
    /// which is <c>&lt;renderId&gt;-&lt;diagramType&gt;-&lt;authorId&gt;-&lt;counter&gt;</c>.
    /// It must anchor on the render id we pass rather than guessing at the
    /// leading segments.
    /// </summary>
    /// <remarks>
    /// A regression pin. The first version matched <c>^[a-zA-Z]+-(.+?)-\d+$</c>,
    /// which on the real element id <c>merpeeps-preview-flowchart-web-0</c> yields
    /// <c>preview-flowchart-web</c> — so every harvested id was wrong and
    /// nothing matched the model. The pattern is correct for the id shape its
    /// comment described; the comment described an id with no render prefix,
    /// which mermaid never produces. Only a browser could catch it.
    /// </remarks>
    [Fact]
    public void TheHarvestAnchorsIdRecoveryOnTheRenderId()
    {
        var html = RenderAssets.BuildShell(
            RenderAssets.Mode.Stock, dark: false, autoRender: null, layout: null);

        Assert.Contains("const RENDER_ID = 'merpeeps-preview'", html, StringComparison.Ordinal);

        // Both render paths must use the constant, or the prefix the harvest
        // strips stops matching the prefix mermaid stamps on.
        Assert.DoesNotContain("render('merpeeps-preview'", html, StringComparison.Ordinal);
        Assert.DoesNotContain("mermaid, 'merpeeps-preview'", html, StringComparison.Ordinal);

        var start = html.IndexOf("window.merpeepsHarvest", StringComparison.Ordinal);
        var end = html.IndexOf("window.merpeepsReady", start, StringComparison.Ordinal);
        Assert.Contains("RENDER_ID", html[start..end], StringComparison.Ordinal);
    }


    /// <summary>
    /// No shell embeds ELK, whatever layout it is asked for.
    /// </summary>
    /// <remarks>
    /// <para>The guard on the removal. ELK was vendored as a 1.6 MB embedded
    /// resource so the pane could show mermaid's own alternative engines and the
    /// editor could harvest their arrangements. Both went when the borrowed
    /// layouts did, and the payload went with them.</para>
    ///
    /// <para>Asked for a layout it no longer supports, the shell passes the name
    /// to mermaid and lets mermaid decide — which means falling back to dagre.
    /// That is the right failure: a diagram that draws, rather than one that
    /// throws. What must not happen is the plugin quietly returning.</para>
    ///
    /// <para>The tokens checked are ours, not mermaid's. The first version of
    /// this asserted on <c>registerLayoutLoaders</c> and failed immediately —
    /// that is a mermaid API and appears in the vendored bundle regardless of
    /// what we embed. Anything containing "elk" is worse still: mermaid's own
    /// source mentions <c>flowchart-elk</c> in a deprecation notice.</para>
    /// </remarks>
    [Theory]
    [InlineData("elk")] [InlineData("elk.mrtree")]
    [InlineData("elk.force")] [InlineData("elk.stress")]
    public void NoShellEmbedsElk(string layout)
    {
        var html = RenderAssets.BuildShell(
            RenderAssets.Mode.Stock, dark: false, autoRender: null, layout: layout);

        Assert.DoesNotContain("ELK_SOURCES", html, StringComparison.Ordinal);
        Assert.DoesNotContain("mermaid-layout-elk", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// The shell stays small whichever layout is requested.
    /// </summary>
    /// <remarks>
    /// The size check is the one that would actually catch a regression. A
    /// reintroduced plugin would add more than a megabyte, which no amount of
    /// renaming could hide — whereas a string check only catches the spelling it
    /// was told to look for.
    /// </remarks>
    [Fact]
    public void NoLayoutMakesTheShellBalloon()
    {
        var plain = RenderAssets.BuildShell(
            RenderAssets.Mode.Stock, dark: false, autoRender: null, layout: "dagre");

        foreach (var layout in new[] { "elk", "elk.mrtree", "elk.force", "elk.stress" })
        {
            var html = RenderAssets.BuildShell(
                RenderAssets.Mode.Stock, dark: false, autoRender: null, layout: layout);

            Assert.True(html.Length < plain.Length + 100_000,
                $"'{layout}' added {html.Length - plain.Length} characters — is a plugin back?");
        }
    }
}
