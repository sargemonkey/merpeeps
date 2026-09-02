using Merpeeps.Core;
using Merpeeps.Core.Model;
using Xunit;

namespace Merpeeps.Core.Tests;

/// <summary>
/// The mermaid theme lives in mermaid's own <c>config: theme:</c> key, not in
/// the Merpeeps block.
/// </summary>
/// <remarks>
/// That choice is the whole point of the feature. A theme under
/// <c>x-merpeeps</c> would make a diagram look right in this editor and nowhere
/// else — the exact failure the format exists to avoid. Written to mermaid's
/// key, a themed diagram renders identically on GitHub with nothing installed.
///
/// <para>Writing to someone else's key comes with an obligation: leave
/// everything around it alone, and leave no trace when the theme is removed.
/// Those are the properties these tests defend.</para>
/// </remarks>
public class DiagramThemeTests
{
    private const string Plain =
        "flowchart TD\n    a[One] --> b[Two]\n";

    private const string Themed =
        "---\nconfig:\n  theme: forest\n---\nflowchart TD\n    a[One] --> b[Two]\n";

    [Fact]
    public void TheThemeIsReadFromMermaidsOwnKey()
    {
        Assert.Equal("forest", MerpeepsDocument.Load(Themed).Theme);
        Assert.Null(MerpeepsDocument.Load(Plain).Theme);
    }

    [Theory]
    [InlineData("default")] [InlineData("dark")] [InlineData("forest")]
    [InlineData("neutral")] [InlineData("base")]
    public void SettingAThemeRoundTrips(string theme)
    {
        var model = MerpeepsDocument.Load(Plain);
        model.Theme = theme;

        var saved = MerpeepsDocument.Save(Plain, model);

        Assert.Equal(theme, MerpeepsDocument.Load(saved).Theme);
        Assert.Equal(theme, MerpeepsDoc.ThemeOf(saved));

        // mermaid's key, not ours: it must sit outside the Merpeeps block.
        Assert.Contains("config:", saved);
        Assert.Contains($"theme: {theme}", saved);
    }

    /// <summary>
    /// Setting a theme and clearing it again must leave the document as it
    /// started — no empty <c>config:</c> stub left behind.
    /// </summary>
    /// <remarks>
    /// Someone trying a theme on a file they do not own should be able to change
    /// their mind and produce an empty diff. A leftover stub is a change they
    /// did not ask for and would have to explain in review.
    /// </remarks>
    [Fact]
    public void ClearingAThemeLeavesNoTrace()
    {
        var model = MerpeepsDocument.Load(Plain);
        model.Theme = "dark";
        var themed = MerpeepsDocument.Save(Plain, model);

        var back = MerpeepsDocument.Load(themed);
        back.Theme = null;
        var cleared = MerpeepsDocument.Save(themed, back);

        // No stub left behind, and identical to the same document saved without
        // a theme ever being set.
        Assert.DoesNotContain("config:", cleared);
        Assert.DoesNotContain("theme:", cleared);
        Assert.Equal(MerpeepsDocument.Save(Plain, MerpeepsDocument.Load(Plain)), cleared);
    }

    /// <summary>
    /// Other keys inside <c>config</c> are the author's, and survive.
    /// </summary>
    [Fact]
    public void OtherConfigKeysSurvive()
    {
        const string src =
            "---\nconfig:\n  look: handDrawn\n  flowchart:\n    curve: linear\n---\n"
            + "flowchart TD\n    a[One] --> b[Two]\n";

        var model = MerpeepsDocument.Load(src);
        model.Theme = "neutral";
        var saved = MerpeepsDocument.Save(src, model);

        Assert.Contains("look: handDrawn", saved);
        Assert.Contains("curve: linear", saved);
        Assert.Equal("neutral", MerpeepsDoc.ThemeOf(saved));
    }

    /// <summary>
    /// A structural save must not delete the author's mermaid config.
    /// </summary>
    /// <remarks>
    /// No theme involved — this is about <see cref="MerpeepsDocument.Save"/>
    /// itself. Found while testing themes: the theme merge appeared to lose
    /// sibling keys, but the keys were already gone before the theme code ran.
    /// Renaming a node in the editor would have silently deleted a
    /// <c>config:</c> block the author wrote.
    /// </remarks>
    [Fact]
    public void AStructuralSaveKeepsTheAuthorsConfig()
    {
        const string src =
            "---\nconfig:\n  look: handDrawn\n  flowchart:\n    curve: linear\n---\n"
            + "flowchart TD\n    a[One] --> b[Two]\n";

        var saved = MerpeepsDocument.Save(src, MerpeepsDocument.Load(src));

        Assert.Contains("look: handDrawn", saved);
        Assert.Contains("curve: linear", saved);
    }

    /// <summary>
    /// A theme sits happily beside the Merpeeps block; neither erases the other.
    /// </summary>
    [Fact]
    public void ThemeAndMerpeepsBlockCoexist()
    {
        var model = MerpeepsDocument.Load(Themed);
        model.Theme = "dark";
        model.Node("a")!.X = 42;
        model.Node("a")!.Fill = "#ABCDEF";

        var saved = MerpeepsDocument.SavePositions(Themed, model);

        Assert.Equal("dark", MerpeepsDoc.ThemeOf(saved));
        Assert.Contains("x-merpeeps", saved);
        Assert.Contains("#ABCDEF", saved);
        Assert.Equal(42, MerpeepsDocument.Load(saved).Node("a")!.X);
    }

    /// <summary>Changing the theme must never touch the diagram body.</summary>
    [Fact]
    public void TheBodyIsUntouched()
    {
        var model = MerpeepsDocument.Load(Themed);
        model.Theme = "base";

        var saved = MerpeepsDocument.SavePositions(Themed, model);
        Assert.Equal(MerpeepsDoc.BodyOf(Themed), MerpeepsDoc.BodyOf(saved));
    }

    /// <summary>
    /// A document nobody themed keeps a frontmatter free of our fingerprints.
    /// </summary>
    [Fact]
    public void SavingAnUnthemedDocumentAddsNoConfigKey()
    {
        var model = MerpeepsDocument.Load(Plain);
        model.Node("a")!.X = 10;

        var saved = MerpeepsDocument.SavePositions(Plain, model);
        Assert.DoesNotContain("config:", saved);
        Assert.DoesNotContain("theme:", saved);
    }

    /// <summary>
    /// An unknown theme name is preserved, not corrected.
    /// </summary>
    /// <remarks>
    /// mermaid may add themes after this build ships. Rewriting a name we do not
    /// recognise would silently change someone's diagram to defend a list that
    /// is merely out of date.
    /// </remarks>
    [Fact]
    public void AnUnknownThemeNameIsPreserved()
    {
        const string src =
            "---\nconfig:\n  theme: someFutureTheme\n---\nflowchart TD\n    a[One] --> b[Two]\n";

        var model = MerpeepsDocument.Load(src);
        Assert.Equal("someFutureTheme", model.Theme);

        var saved = MerpeepsDocument.SavePositions(src, model);
        Assert.Equal("someFutureTheme", MerpeepsDoc.ThemeOf(saved));
    }
}
