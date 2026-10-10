using Merpeeps.Render;
using Xunit;

namespace Merpeeps.App.Tests;

/// <summary>
/// Covers the page the reference renderer loads.
///
/// <para>The webview that displays this page cannot run on a headless runner,
/// but the page itself is just text, so everything about how it is assembled is
/// testable here. What it actually draws is checked separately by
/// <c>oracle/preview-page-test.mjs</c>, which loads it in real Chrome.</para>
/// </summary>
public class RenderAssetsTests
{
    private const string Sample = """
        ---
        x-merpeeps:
          spec: 1
          layout:
            nodes:
              a: {x: 0, y: 0}
        ---
        flowchart LR
            a[Alpha] --> b[Beta]
        """;

    [Fact]
    public void EveryVendoredAssetIsEmbedded()
    {
        RenderAssets.Verify();

        Assert.Contains("mermaid", RenderAssets.MermaidBundle, StringComparison.Ordinal);
        Assert.Contains("MIT", RenderAssets.MermaidLicence, StringComparison.Ordinal);
        Assert.Contains("MIT", RenderAssets.JsYamlLicence, StringComparison.Ordinal);
    }

    /// <summary>
    /// The minified bundle carries no licence banner, so MIT compliance depends
    /// entirely on the licence file travelling with it.
    /// </summary>
    [Fact]
    public void TheMermaidLicenceIsShippedBecauseTheBundleDropsIt()
    {
        Assert.DoesNotContain("Knut Sveidqvist", RenderAssets.MermaidBundle, StringComparison.Ordinal);
        Assert.Contains("Knut Sveidqvist", RenderAssets.MermaidLicence, StringComparison.Ordinal);
        Assert.Contains("above copyright notice", RenderAssets.MermaidLicence, StringComparison.Ordinal);
    }

    /// <summary>
    /// The published package is developed independently of this app. If it gains
    /// a dependency, the pane would silently render nothing — so the mismatch has
    /// to be an error, not a surprise.
    /// </summary>
    [Fact]
    public void EveryImportThePublishedPackageMakesCanBeResolved()
    {
        var imports = RenderAssets.DiscoverImports(RenderAssets.MerpeepsPlugin)
            .Concat(RenderAssets.DiscoverImports(RenderAssets.MerpeepsIndex))
            .ToArray();

        Assert.Contains("js-yaml", imports);
        Assert.Contains("./index.js", imports);

        RenderAssets.Verify();
    }

    [Theory]
    [InlineData(RenderAssets.Mode.Stock)]
    [InlineData(RenderAssets.Mode.WithMerpeeps)]
    public void TheShellIsWellFormed(RenderAssets.Mode mode)
    {
        var html = RenderAssets.BuildShell(mode, dark: false);

        Assert.StartsWith("<!DOCTYPE html>", html, StringComparison.Ordinal);
        Assert.EndsWith("</body></html>\n", html, StringComparison.Ordinal);
        Assert.Contains("window.merpeepsRender", html, StringComparison.Ordinal);
        Assert.Contains("<script type=\"module\">", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// The plugin only takes effect once registered. Skipping this produced a
    /// diagram that rendered perfectly and ignored every stored coordinate,
    /// which is the most expensive kind of failure: a correct-looking one.
    /// </summary>
    [Fact]
    public void TheMerpeepsShellRegistersTheLayoutBeforeRendering()
    {
        var html = RenderAssets.BuildShell(RenderAssets.Mode.WithMerpeeps, dark: false);

        var register = html.IndexOf("registerMerpeepsLayout(mermaid)", StringComparison.Ordinal);
        var render = html.IndexOf("window.merpeepsRender = async", StringComparison.Ordinal);

        Assert.True(register > 0, "the shell never registers the merpeeps layout");
        Assert.True(register < render, "the layout must be registered before rendering");
    }

    [Fact]
    public void TheStockShellDoesNotShipThePluginAtAll()
    {
        var html = RenderAssets.BuildShell(RenderAssets.Mode.Stock, dark: false);

        Assert.DoesNotContain("registerMerpeepsLayout", html, StringComparison.Ordinal);
        Assert.DoesNotContain("SOURCES.set", html, StringComparison.Ordinal);
        Assert.Contains("mermaid.render(", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Regression test for a genuinely nasty bug. The page used to be assembled
    /// by rendering a finished shell and then splicing the auto-render script in
    /// ahead of <c>&lt;/body&gt;&lt;/html&gt;</c>. The mermaid bundle embeds
    /// DOMPurify, whose source contains that exact string as a JavaScript
    /// literal, so <c>String.Replace</c> — which replaces every occurrence —
    /// injected the script into the middle of a third-party library and
    /// corrupted it. The page then failed with "Unexpected identifier 'module'".
    /// </summary>
    [Fact]
    public void ThePageIsComposedInOnePassRatherThanSplicedIntoTheBundle()
    {
        var page = RenderAssets.BuildPage(RenderAssets.Mode.Stock, dark: false, Sample);

        // Exactly one auto-render block, and it comes after the bundle closes.
        var occurrences = CountOf(page, "window.merpeepsRendered = true");
        Assert.Equal(1, occurrences);

        var bundleEnd = page.IndexOf("\n</script>", StringComparison.Ordinal);
        var autoRun = page.IndexOf("window.merpeepsRendered = true", StringComparison.Ordinal);
        Assert.True(autoRun > bundleEnd,
            "the auto-render script landed inside the mermaid bundle");

        // The marker that caused the bug is still present inside the bundle, so
        // this test keeps meaning something.
        Assert.True(CountOf(page, "</body></html>") > 1,
            "the bundle no longer contains the string that made splicing unsafe — " +
            "this test has stopped guarding anything and should be revisited");
    }

    [Fact]
    public void ThePageCarriesTheDiagramItShouldRender()
    {
        var page = RenderAssets.BuildPage(RenderAssets.Mode.Stock, dark: false, Sample);

        Assert.Contains("const source =", page, StringComparison.Ordinal);
        Assert.Contains("flowchart LR", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// A page waits rather than assuming module scripts have finished. Relying
    /// on document order failed in practice, because the shell uses top-level
    /// await to import the plugin.
    /// </summary>
    [Fact]
    public void ThePageWaitsForTheShellInsteadOfAssumingOrder()
    {
        var page = RenderAssets.BuildPage(RenderAssets.Mode.WithMerpeeps, dark: false, Sample);

        Assert.Contains("whenReady", page, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, "#1e1e1e")]
    [InlineData(false, "#ffffff")]
    public void ThemeReachesThePage(bool dark, string background)
    {
        var html = RenderAssets.BuildShell(RenderAssets.Mode.Stock, dark);

        Assert.Contains($"background:{background}", html, StringComparison.Ordinal);
        Assert.Contains(dark ? "theme: 'dark'" : "theme: 'default'", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(RenderAssets.Mode.Stock)]
    [InlineData(RenderAssets.Mode.WithMerpeeps)]
    public void UpstreamUpgradeDoesNotChangeDefaultReferenceAppearance(RenderAssets.Mode mode)
    {
        var html = RenderAssets.BuildShell(mode, dark: false);
        Assert.Contains("layout: \"dagre\"", html, StringComparison.Ordinal);
        Assert.Contains("look: 'classic'", html, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------- escaping

    /// <summary>
    /// A literal <c>&lt;/script&gt;</c> inside a JavaScript string still ends the
    /// enclosing HTML script element — the HTML tokeniser does not know it is
    /// inside a string. Any payload embedded as a literal must neutralise it.
    /// </summary>
    [Fact]
    public void ScriptClosersInsideALiteralAreNeutralised()
    {
        var literal = InvokeJsLiteral("var x = '</script><script>alert(1)</script>';");

        Assert.DoesNotContain("</script>", literal, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\\u003c", literal, StringComparison.Ordinal);
    }

    [Fact]
    public void JavaScriptLineTerminatorsAreEscaped()
    {
        // U+2028 and U+2029 are ordinary characters in most contexts but line
        // terminators in JavaScript, so leaving them raw breaks the literal.
        var literal = InvokeJsLiteral("a\u2028b\u2029c");

        Assert.Contains("\\u2028", literal, StringComparison.Ordinal);
        Assert.Contains("\\u2029", literal, StringComparison.Ordinal);
        Assert.DoesNotContain("\u2028", literal, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("plain text")]
    [InlineData("quotes \" and ' and `")]
    [InlineData("back\\slash")]
    [InlineData("new\nline\ttab")]
    [InlineData("control \u0001 char")]
    public void LiteralsRoundTripThroughJsonSemantics(string input)
    {
        var literal = InvokeJsLiteral(input);

        // The escaper targets JavaScript, which for these constructs shares
        // JSON's grammar — so JSON is a valid independent decoder.
        var decoded = System.Text.Json.JsonSerializer.Deserialize<string>(literal);

        Assert.Equal(input, decoded);
    }

    private static int CountOf(string haystack, string needle)
    {
        var n = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal);
             i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
        {
            n++;
        }
        return n;
    }

    private static string InvokeJsLiteral(string input) =>
        (string)typeof(RenderAssets)
            .GetMethod("JsLiteral", System.Reflection.BindingFlags.NonPublic
                                    | System.Reflection.BindingFlags.Static)!
            .Invoke(null, new object[] { input })!;
}
