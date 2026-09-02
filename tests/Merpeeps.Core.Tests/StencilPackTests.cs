using Merpeeps.Core.Model;
using Xunit;

namespace Merpeeps.Core.Tests;

/// <summary>
/// Covers loading stencil packs.
///
/// <para>The rules being enforced here are trust rules. A pack that could claim
/// a core icon name, or silently override another pack's, would make icons
/// unreliable — installing something would change how existing diagrams look.
/// So the loader rejects rather than merges, and refuses partial loads: a
/// half-loaded stencil set is worse than an absent one, because it looks like
/// it worked.</para>
/// </summary>
public class StencilPackTests
{
    private const string Aws = """
        id: aws
        name: AWS Architecture
        spec: 1
        author: Example
        licence: MIT
        icons:
          lambda:
            path: "M4 4 L20 4 L12 20 Z"
            viewBox: "0 0 24 24"
          s3:
            path: "M2 6 h20 v12 h-20 Z"
            filled: false
        stencils:
          - label: Lambda function
            text: Function
            shape: Rectangle
            icon: lambda
            fill: "#FF9900"
            stroke: "#CC7A00"
            hint: A serverless function
          - label: S3 bucket
            shape: Cylinder
            icon: s3
        """;

    private static StencilPack Load(string yaml)
    {
        var pack = StencilPackLoader.Parse(yaml, out var problem);
        Assert.Null(problem?.ToString());
        Assert.NotNull(pack);
        return pack!;
    }

    // ------------------------------------------------------------ happy path

    [Fact]
    public void AWellFormedPackLoads()
    {
        var pack = Load(Aws);

        Assert.Equal("aws", pack.Id);
        Assert.Equal("AWS Architecture", pack.Name);
        Assert.Equal("MIT", pack.Licence);
        Assert.Equal(2, pack.Icons.Count);
        Assert.Equal(2, pack.Stencils.Count);
    }

    [Fact]
    public void IconGeometryIsRead()
    {
        var pack = Load(Aws);

        Assert.Equal("M4 4 L20 4 L12 20 Z", pack.Icons["lambda"].Path);
        Assert.Equal("0 0 24 24", pack.Icons["lambda"].ViewBox);
        Assert.True(pack.Icons["lambda"].Filled);
        Assert.False(pack.Icons["s3"].Filled);
    }

    [Fact]
    public void AMissingNameFallsBackToTheId()
    {
        var pack = Load("id: minimal\n");

        Assert.Equal("minimal", pack.Name);
    }

    // ------------------------------------------------------------ namespacing

    /// <summary>
    /// Stencils reference their own icons unprefixed for readability; the loader
    /// scopes them. Doing it here rather than trusting the author means a pack
    /// physically cannot reference another pack's artwork.
    /// </summary>
    [Fact]
    public void StencilIconsAreScopedToTheDeclaringPack()
    {
        var collection = StencilPackLoader.ToCollection(Load(Aws));

        Assert.Equal("aws:lambda", collection.Items[0].Icon);
        Assert.Equal("aws:s3", collection.Items[1].Icon);
    }

    [Fact]
    public void ACoreIconNameInAStencilIsLeftUnprefixed()
    {
        var pack = Load("""
            id: demo
            stencils:
              - label: Box
                icon: server
            """);

        var collection = StencilPackLoader.ToCollection(pack);

        Assert.Equal("server", collection.Items[0].Icon);
    }

    [Fact]
    public void AnAlreadyQualifiedNameIsNotDoublePrefixed()
    {
        // A pack may legitimately reference another pack's icon; scoping must
        // not mangle it into "demo:other:thing", which would then be malformed.
        var pack = Load("""
            id: demo
            stencils:
              - label: Borrowed
                icon: other:thing
            """);

        Assert.Equal("other:thing", StencilPackLoader.ToCollection(pack).Items[0].Icon);
    }

    [Fact]
    public void StencilDefaultsFillIn()
    {
        var collection = StencilPackLoader.ToCollection(Load(Aws));

        // "S3 bucket" declares no text, so the label is used.
        Assert.Equal("S3 bucket", collection.Items[1].Label);
        Assert.Equal(NodeShape.Cylinder, collection.Items[1].Shape);
    }

    [Fact]
    public void AnUnknownShapeFallsBackToARectangleRatherThanFailing()
    {
        var pack = Load("""
            id: demo
            stencils:
              - label: Odd
                shape: NotAShape
            """);

        Assert.Equal(NodeShape.Rectangle, StencilPackLoader.ToCollection(pack).Items[0].Shape);
    }

    // ------------------------------------------------------------- rejections

    private static string Reject(string yaml)
    {
        var pack = StencilPackLoader.Parse(yaml, out var problem);
        Assert.Null(pack);
        Assert.NotNull(problem);
        return problem!.Value.Message;
    }

    /// <summary>
    /// The rule that makes icons trustworthy: installing a pack must never
    /// change how an existing diagram looks.
    /// </summary>
    [Fact]
    public void APackMayNotClaimACoreIconName()
    {
        var message = Reject("""
            id: sneaky
            icons:
              database:
                path: "M0 0"
            """);

        Assert.Contains("core icon name", message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("name: no id here\n")]
    [InlineData("id: UPPERCASE\n")]
    [InlineData("id: has spaces\n")]
    [InlineData("id: has_underscore\n")]
    public void AnInvalidIdIsRejected(string yaml)
    {
        Assert.Contains("valid id", Reject(yaml), StringComparison.Ordinal);
    }

    /// <summary>
    /// A pack from the future is refused with an explanation rather than
    /// half-understood. Guessing at unknown schema is how a format forks.
    /// </summary>
    [Fact]
    public void APackFromAFutureSpecIsRefusedPolitely()
    {
        var message = Reject("id: ahead\nspec: 99\n");

        Assert.Contains("spec 99", message, StringComparison.Ordinal);
        Assert.Contains("this build understands", message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnIconWithNoPathIsRejected()
    {
        Assert.Contains("no path data", Reject("""
            id: demo
            icons:
              broken:
                viewBox: "0 0 24 24"
            """), StringComparison.Ordinal);
    }

    [Fact]
    public void AStencilWithNoLabelIsRejected()
    {
        Assert.Contains("no label", Reject("""
            id: demo
            stencils:
              - shape: Rectangle
            """), StringComparison.Ordinal);
    }

    [Fact]
    public void GarbageIsRejectedWithoutThrowing()
    {
        Assert.NotEmpty(Reject("this is not: [a valid: pack"));
    }

    // ---------------------------------------------------------- loading a dir

    /// <summary>
    /// Packs are optional. An editor with none installed must work, so a missing
    /// directory is not an error.
    /// </summary>
    [Fact]
    public void AMissingDirectoryIsNotAnError()
    {
        var result = StencilPackLoader.LoadFrom(
            Path.Combine(Path.GetTempPath(), $"no-such-{Guid.NewGuid():N}"));

        Assert.True(result.Ok);
        Assert.Empty(result.Packs);
    }

    [Fact]
    public void PacksLoadFromADirectory()
    {
        var dir = NewDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "aws.merpeeps-pack.yml"), Aws);
            File.WriteAllText(Path.Combine(dir, "b.merpeeps-pack.yml"), "id: beta\n");

            var result = StencilPackLoader.LoadFrom(dir);

            Assert.True(result.Ok);
            Assert.Equal(2, result.Packs.Count);
        }
        finally { Directory.Delete(dir, true); }
    }

    /// <summary>
    /// One bad pack must not take the good ones with it — otherwise a single
    /// malformed file left in the directory disables the whole palette.
    /// </summary>
    [Fact]
    public void ABadPackIsReportedWithoutStoppingTheOthers()
    {
        var dir = NewDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "a-good.merpeeps-pack.yml"), Aws);
            File.WriteAllText(Path.Combine(dir, "b-bad.merpeeps-pack.yml"), "id: UPPERCASE\n");

            var result = StencilPackLoader.LoadFrom(dir);

            Assert.Single(result.Packs);
            Assert.Single(result.Problems);
            Assert.False(result.Ok);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ADuplicateIdIsIgnoredAndReported()
    {
        var dir = NewDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "a.merpeeps-pack.yml"), "id: dup\nname: First\n");
            File.WriteAllText(Path.Combine(dir, "b.merpeeps-pack.yml"), "id: dup\nname: Second\n");

            var result = StencilPackLoader.LoadFrom(dir);

            // First wins, so load order is the tie-break and a later pack can
            // never quietly repaint an earlier one's icons.
            Assert.Single(result.Packs);
            Assert.Equal("First", result.Packs[0].Name);
            Assert.Contains("already loaded", result.Problems[0].Message, StringComparison.Ordinal);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void UnrelatedFilesAreIgnored()
    {
        var dir = NewDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "readme.md"), "not a pack");
            File.WriteAllText(Path.Combine(dir, "notes.yml"), "also: not a pack");

            var result = StencilPackLoader.LoadFrom(dir);

            Assert.True(result.Ok);
            Assert.Empty(result.Packs);
        }
        finally { Directory.Delete(dir, true); }
    }

    private static string NewDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"merpeeps-packs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    // ------------------------------------------------------------ dogfooding

    /// <summary>
    /// The four built-in collections are embedded packs run through this same
    /// loader.
    ///
    /// <para>A pack format only used by other people is a format nobody notices
    /// is broken. Because the built-ins go through the same door, any change
    /// that breaks third-party packs breaks the editor's own palette first, and
    /// loudly.</para>
    /// </summary>
    [Theory]
    [InlineData("flowchart", "Flowchart", 10)]
    [InlineData("c4", "C4 model", 9)]
    [InlineData("architecture", "Architecture", 12)]
    [InlineData("state", "State machine", 4)]
    public void TheBuiltInCollectionsAreThemselvesPacks(string id, string name, int count)
    {
        using var stream = typeof(NodePalette).Assembly
            .GetManifestResourceStream($"Merpeeps.Core.Packs.{id}.merpeeps-pack.yml");

        Assert.NotNull(stream);

        using var reader = new StreamReader(stream!);
        var pack = Load(reader.ReadToEnd());

        Assert.Equal(id, pack.Id);
        Assert.Equal(name, pack.Name);
        Assert.Equal(count, pack.Stencils.Count);
    }

    /// <summary>
    /// Built-ins use only core icon names, so they work with no packs installed
    /// and cannot be broken by one being missing.
    /// </summary>
    [Fact]
    public void TheBuiltInsReferenceOnlyCoreIcons()
    {
        foreach (var collection in NodePalette.All)
        {
            foreach (var item in collection.Items)
            {
                if (item.Icon is null) continue;
                Assert.True(IconVocabulary.IsKnown(item.Icon),
                    $"{collection.Name}/{item.Name} uses '{item.Icon}', which is not a core icon");
            }
        }
    }
}
