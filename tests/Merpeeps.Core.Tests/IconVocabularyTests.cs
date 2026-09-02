using Merpeeps.Core.Model;
using Xunit;

namespace Merpeeps.Core.Tests;

/// <summary>
/// Covers SPEC §6b — what an icon name means, and what happens when the pack
/// that defines it is not installed.
///
/// <para>These are format rules, not tooling behaviour, which is why they are
/// tested this seriously. Files outlive tools: whatever a name means today it
/// has to keep meaning, and a reader that quietly discards names it cannot
/// resolve would corrupt a colleague's diagram simply by opening it.</para>
/// </summary>
public class IconVocabularyTests
{
    [Theory]
    [InlineData("server")]
    [InlineData("database")]
    [InlineData("cog")]
    public void UnprefixedNamesAreCore(string name)
    {
        Assert.Equal(IconVocabulary.Kind.Core, IconVocabulary.Classify(name));
        Assert.Null(IconVocabulary.PackOf(name));
    }

    [Fact]
    public void TheEmptyNameMeansNoIcon()
    {
        Assert.Equal(IconVocabulary.Kind.None, IconVocabulary.Classify(""));
        Assert.Equal(IconVocabulary.Kind.None, IconVocabulary.Classify(null));
    }

    [Theory]
    [InlineData("aws:lambda", "aws", "lambda")]
    [InlineData("azure:sql", "azure", "sql")]
    [InlineData("bpmn:gateway", "bpmn", "gateway")]
    [InlineData("my-pack:some_thing", "my-pack", "some_thing")]
    public void PrefixedNamesResolveToAPackAndLocalName(string name, string pack, string local)
    {
        Assert.Equal(IconVocabulary.Kind.Pack, IconVocabulary.Classify(name));
        Assert.Equal(pack, IconVocabulary.PackOf(name));
        Assert.Equal(local, IconVocabulary.LocalNameOf(name));
    }

    /// <summary>
    /// A pack name is legal whether or not that pack exists here. Classification
    /// deliberately does not depend on what happens to be installed — otherwise
    /// the same file would be valid on one machine and not on another.
    /// </summary>
    [Fact]
    public void APackNameIsWellFormedEvenWhenThePackIsMissing()
    {
        Assert.Equal(IconVocabulary.Kind.Pack, IconVocabulary.Classify("nobody-has-this:thing"));
    }

    [Theory]
    [InlineData("aws:lambda:extra")]   // more than one separator is ambiguous
    [InlineData("AWS:Lambda")]         // uppercase is not permitted
    [InlineData(":lambda")]            // no pack id
    [InlineData("aws:")]               // no local name
    [InlineData("aws lambda")]         // space
    [InlineData("aws:lam bda")]
    public void MalformedNamesAreRejectedRatherThanGuessedAt(string name)
    {
        Assert.Equal(IconVocabulary.Kind.Malformed, IconVocabulary.Classify(name));
        Assert.Null(IconVocabulary.PackOf(name));
    }

    [Fact]
    public void ADocumentReportsThePacksItNeeds()
    {
        var model = new DiagramModel();
        model.Nodes.Add(new DiagramNode { Id = "a", Icon = "aws:lambda" });
        model.Nodes.Add(new DiagramNode { Id = "b", Icon = "aws:s3" });
        model.Nodes.Add(new DiagramNode { Id = "c", Icon = "azure:sql" });
        model.Nodes.Add(new DiagramNode { Id = "d", Icon = "server" });
        model.Nodes.Add(new DiagramNode { Id = "e", Icon = null });

        var packs = IconVocabulary.PacksReferencedBy(model);

        // Deduplicated, core and empty names excluded, first-seen order kept so
        // the message is stable rather than reshuffling between runs.
        Assert.Equal(new[] { "aws", "azure" }, packs);
    }

    [Fact]
    public void ADocumentUsingOnlyCoreIconsNeedsNoPacks()
    {
        var model = new DiagramModel();
        model.Nodes.Add(new DiagramNode { Id = "a", Icon = "server" });

        Assert.Empty(IconVocabulary.PacksReferencedBy(model));
    }
}
