using Merpeeps.Core;
using Merpeeps.Core.Model;
using Xunit;

namespace Merpeeps.Core.Tests;

/// <summary>
/// What happens to a document that references a pack this machine does not have.
///
/// <para>This is the promise that makes packs safe to depend on. Without it,
/// sharing a diagram becomes a hazard: a colleague opens your AWS architecture,
/// their editor does not have the AWS pack, and saving quietly strips every icon
/// out of your work. The format's answer is that a missing <em>tool</em> must
/// never cost you <em>data</em> — the same rule §2 applies to unresolvable node
/// ids.</para>
/// </summary>
public class MissingPackTests
{
    private const string UsesAws = """
        ---
        x-merpeeps:
          spec: 1
          layout:
            nodes:
              fn: {x: 0, y: 0}
              store: {x: 200, y: 0}
          style:
            nodes:
              fn: {fill: '#FF9900', icon: 'aws:lambda'}
              store: {fill: '#569A31', icon: 'aws:s3'}
        ---
        flowchart LR
            fn[Handler] --> store[(Bucket)]
        """;

    [Fact]
    public void ADocumentReferencingAnUnknownPackStillLoads()
    {
        var model = MerpeepsDocument.Load(UsesAws);

        Assert.Equal(2, model.Nodes.Count);
        Assert.Equal("Handler", model.Node("fn")!.Label);
    }

    /// <summary>
    /// The clause that matters most. Load and save on a machine without the pack
    /// must not erase the icon names.
    /// </summary>
    [Fact]
    public void SavingWithoutThePackDoesNotStripTheIconNames()
    {
        var model = MerpeepsDocument.Load(UsesAws);
        var saved = MerpeepsDocument.Save(UsesAws, model);

        Assert.Contains("aws:lambda", saved, StringComparison.Ordinal);
        Assert.Contains("aws:s3", saved, StringComparison.Ordinal);
    }

    [Fact]
    public void TheStoredPositionsSurviveToo()
    {
        var model = MerpeepsDocument.Load(UsesAws);
        var saved = MerpeepsDocument.Save(UsesAws, model);
        var reloaded = MerpeepsDocument.Load(saved);

        Assert.Equal(200, reloaded.Node("store")!.X);
    }

    /// <summary>
    /// A missing pack degrades the picture, never the document. The node still
    /// has its shape, its label and its colour — only the glyph is absent.
    /// </summary>
    [Fact]
    public void ThePictureDegradesButTheDocumentDoesNot()
    {
        var model = MerpeepsDocument.Load(UsesAws);
        var node = model.Node("fn")!;

        Assert.Equal("aws:lambda", node.Icon);
        Assert.Equal("#FF9900", node.Fill);

        // The renderer cannot draw it, and says so by classification rather than
        // by discarding it.
        Assert.Equal(IconVocabulary.Kind.Pack, IconVocabulary.Classify(node.Icon));
        Assert.False(IconVocabulary.IsKnown(node.Icon));
    }

    /// <summary>
    /// SPEC §6b: a reader SHOULD surface which packs are referenced but missing.
    /// Silent degradation is the failure mode this is meant to prevent — a
    /// person seeing plain boxes with no explanation assumes the file is broken.
    /// </summary>
    [Fact]
    public void TheMissingPacksCanBeNamed()
    {
        var model = MerpeepsDocument.Load(UsesAws);

        Assert.Equal(["aws"], IconVocabulary.PacksReferencedBy(model));
    }

    [Fact]
    public void AMalformedIconNameIsAlsoRetained()
    {
        var src = """
            ---
            x-merpeeps:
              spec: 1
              style:
                nodes:
                  a: {icon: 'not::valid'}
            ---
            flowchart LR
                a[Alpha] --> b[Beta]
            """;

        var model = MerpeepsDocument.Load(src);
        var saved = MerpeepsDocument.Save(src, model);

        // We cannot interpret it, so we do not touch it. Deleting would be a
        // guess about intent, and the format never guesses.
        Assert.Equal(IconVocabulary.Kind.Malformed, IconVocabulary.Classify(model.Node("a")!.Icon));
        Assert.Contains("not::valid", saved, StringComparison.Ordinal);
    }

    /// <summary>
    /// Baking is the one lossy operation, and even it names what it dropped
    /// rather than dropping it quietly.
    /// </summary>
    [Fact]
    public void BakingNamesThePackIconsItCannotCarry()
    {
        var result = Export.MermaidBake.Bake(UsesAws);

        Assert.Equal(new[] { "fn", "store" }, result.DroppedIcons.OrderBy(x => x));
        Assert.True(result.Lossy);

        // The fills it *can* carry still make it across.
        Assert.Contains("fill:#FF9900", result.Output, StringComparison.Ordinal);
    }

    /// <summary>
    /// The body is what a stock renderer sees, and it must be untouched whether
    /// or not the pack is present.
    /// </summary>
    [Fact]
    public void TheDiagramBodyIsUnaffectedByAMissingPack()
    {
        var model = MerpeepsDocument.Load(UsesAws);
        var saved = MerpeepsDocument.Save(UsesAws, model);

        Assert.Equal(MerpeepsDoc.BodyOf(UsesAws), MerpeepsDoc.BodyOf(saved));
    }
}
