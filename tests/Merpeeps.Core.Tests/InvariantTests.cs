using System.Text.Json;
using Merpeeps.Core;
using Xunit;

namespace Merpeeps.Core.Tests;

/// <summary>
/// The INNER LOOP: render-free Merpeeps invariants, executed over the 33 official
/// Mermaid syntax samples. No browser, no network — milliseconds.
///
/// The rendering invariants (graceful degradation, no-leak) live in the outer
/// loop under /oracle, where real mermaid.js acts as the oracle.
/// </summary>
public class InvariantTests
{
    private static readonly Dictionary<string, string> Samples = LoadSamples();

    private static Dictionary<string, string> LoadSamples()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "samples.json");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(path))!;
    }

    public static TheoryData<string> AllTypes()
    {
        var data = new TheoryData<string>();
        foreach (var k in Samples.Keys) data.Add(k);
        return data;
    }

    /// <summary>Payload includes an unknown key to prove forward compatibility.</summary>
    private static Dictionary<string, object?> Patch() => D(
        ("spec", 1),
        ("layout", D(
            ("nodes", D(("ZZTOP", D(("x", 4242), ("y", 8484), ("w", 160), ("h", 60))))),
            ("edges", D(("ZZTOP->ZZBOT#0", D(("labelT", 0.5))))),
            ("engine", "manual"))),
        ("style", D(("nodes", D(("ZZTOP", D(("fill", "#abcdef"), ("stroke", "#fedcba"))))))),
        ("semantics", D(("ZZTOP", D(("kind", "container"), ("tech", "NodeJS"))))),
        ("futureUnknownKey", D(("nested", "PRESERVEME"))));

    private static Dictionary<string, object?> D(params (string k, object? v)[] kv)
    {
        var d = new Dictionary<string, object?>();
        foreach (var (k, v) in kv) d[k] = v;
        return d;
    }

    // ------------------------------------------------------------- invariants

    [Theory, MemberData(nameof(AllTypes))]
    public void Invariant1_BodyIsBytePreserved(string type)
    {
        var baseSrc = Samples[type];
        var ext = MerpeepsDoc.Inject(baseSrc, Patch());
        Assert.Equal(MerpeepsDoc.BodyOf(baseSrc), MerpeepsDoc.BodyOf(ext));
    }

    [Theory, MemberData(nameof(AllTypes))]
    public void Invariant2_RoundTripsExactly(string type)
    {
        var ext = MerpeepsDoc.Inject(Samples[type], Patch());
        Assert.Equal(MerpeepsDoc.ToJson(Patch()), MerpeepsDoc.ToJson(MerpeepsDoc.Parse(ext)));
    }

    [Theory, MemberData(nameof(AllTypes))]
    public void Invariant3_IsIdempotent(string type)
    {
        var once = MerpeepsDoc.Inject(Samples[type], Patch());
        Assert.Equal(once, MerpeepsDoc.Inject(once, Patch()));
    }

    [Theory, MemberData(nameof(AllTypes))]
    public void Invariant4_MergesIntoSingleFrontmatterBlock(string type)
    {
        var ext = MerpeepsDoc.Inject(Samples[type], Patch());
        var delims = ext.Split('\n').Count(l => l.TrimEnd('\r') == "---");
        Assert.True(delims <= 2, $"expected <=2 '---' delimiters, found {delims}");
    }

    [Theory, MemberData(nameof(AllTypes))]
    public void Invariant5_PreservesUnknownKeys(string type)
    {
        var ext = MerpeepsDoc.Inject(Samples[type], Patch());
        var parsed = Assert.IsType<Dictionary<string, object?>>(MerpeepsDoc.Parse(ext));
        var unknown = Assert.IsType<Dictionary<string, object?>>(
            parsed["futureUnknownKey"]);
        Assert.Equal("PRESERVEME", unknown["nested"]?.ToString());
    }

    [Theory, MemberData(nameof(AllTypes))]
    public void Invariant6_CommentCarrierIsEquivalent(string type)
    {
        var viaComment = MerpeepsDoc.Serialise(
            Samples[type], MerpeepsDoc.Normalise(Patch()), "comment");
        Assert.Equal(MerpeepsDoc.ToJson(Patch()), MerpeepsDoc.ToJson(MerpeepsDoc.Parse(viaComment)));
    }

    /// <summary>
    /// Regression: the removal pattern matched the comment's line content but
    /// not its terminator, so stripping left a blank line behind. Metadata
    /// round-tripped fine, which is why Invariant6 alone never caught it.
    /// </summary>
    [Theory, MemberData(nameof(AllTypes))]
    public void Invariant6b_CommentCarrierLeavesTheBodyByteIdentical(string type)
    {
        var src = Samples[type];
        var viaComment = MerpeepsDoc.Serialise(src, MerpeepsDoc.Normalise(Patch()), "comment");

        // A line-based carrier needs the body to end with a newline before it
        // can append its own line. Adding that one newline is the only change
        // it is permitted to make.
        var body = MerpeepsDoc.BodyOf(src);
        var expected = body.Length == 0 || body.EndsWith('\n') ? body : body + "\n";

        Assert.Equal(expected, MerpeepsDoc.BodyOf(viaComment));

        // Removing it again must not compound the change.
        var cleaned = MerpeepsDoc.Serialise(viaComment, null, "comment");
        Assert.Equal(cleaned, MerpeepsDoc.Serialise(cleaned, null, "comment"));
    }

    /// <summary>Blank lines the author wrote are theirs, not ours to tidy.</summary>
    [Fact]
    public void CommentCarrierPreservesABodyEndingInANewline()
    {
        const string src = "flowchart TD\n    a --> b\n\n\n\n    b --> c\n";

        var withMeta = MerpeepsDoc.Serialise(src, MerpeepsDoc.Normalise(Patch()), "comment");
        Assert.Equal(src, MerpeepsDoc.BodyOf(withMeta));
        Assert.Equal(src, MerpeepsDoc.Serialise(withMeta, null, "comment"));
    }

    /// <summary>
    /// Switching to the comment carrier must not silently delete the author's
    /// `title:` or `config:` keys along with our own.
    /// </summary>
    [Fact]
    public void CommentCarrierKeepsTheAuthorsOtherFrontmatterKeys()
    {
        const string src = "---\ntitle: Keep me\n---\nflowchart TD\n    a --> b\n";

        var withMeta = MerpeepsDoc.Serialise(src, MerpeepsDoc.Normalise(Patch()), "comment");

        Assert.Contains("title: Keep me", withMeta);
        Assert.DoesNotContain("x-merpeeps", withMeta);
        Assert.NotNull(MerpeepsDoc.Parse(withMeta));
        Assert.Equal(src, MerpeepsDoc.Serialise(withMeta, null, "comment"));
    }

    // ------------------------------------------------------- type fidelity

    [Fact]
    public void NumberLikeStringsSurviveAsStrings()
    {
        // Regression: YamlDotNet emits "0.5" unquoted unless told otherwise,
        // which reads back as a double and silently changes the type.
        var patch = D(("spec", 1), ("v", "0.5"), ("n", 0.5));
        var ext = MerpeepsDoc.Inject("flowchart TD\n  A --> B", patch);
        var parsed = (Dictionary<string, object?>)MerpeepsDoc.Parse(ext)!;
        Assert.IsType<string>(parsed["v"]);
        Assert.IsType<double>(parsed["n"]);
    }

    [Fact]
    public void ExistingFrontmatterKeysAreNotDisturbed()
    {
        var src = "---\ntitle: Keep Me\n---\nflowchart TD\n  A --> B";
        var ext = MerpeepsDoc.Inject(src, Patch());
        Assert.Contains("title: Keep Me", ext);
        Assert.Contains("x-merpeeps:", ext);
    }

    [Fact]
    public void JsonElementValuesAreConvertedNotStringified()
    {
        // Regression: callers deserialising with System.Text.Json hand us
        // JsonElement. Before this was handled, the MCP server silently wrote
        // "ValueKind: Object" into files and every existing check stayed green.
        using var doc = System.Text.Json.JsonDocument.Parse(
            """{"A":{"x":120,"y":80,"label":"Start","deep":{"ok":true},"list":[1,2]}}""");
        var nodes = new Dictionary<string, object?>();
        foreach (var p in doc.RootElement.EnumerateObject()) nodes[p.Name] = p.Value;

        var patch = D(("spec", 1),
                      ("layout", D(("nodes", nodes))));
        var ext = MerpeepsDoc.Inject("flowchart LR\n    A[Start] --> B[End]", patch);

        Assert.DoesNotContain("ValueKind", ext);
        Assert.Contains("x: 120", ext);
        Assert.Contains("y: 80", ext);

        var parsed = (Dictionary<string, object?>)MerpeepsDoc.Parse(ext)!;
        var layout = (Dictionary<string, object?>)parsed["layout"]!;
        var n = (Dictionary<string, object?>)layout["nodes"]!;
        var a = (Dictionary<string, object?>)n["A"]!;
        Assert.Equal("120", a["x"]!.ToString());
        Assert.Equal("Start", a["label"]!.ToString());
        Assert.IsType<Dictionary<string, object?>>(a["deep"]);
        Assert.IsType<List<object?>>(a["list"]);
    }

    [Fact]
    public void RemovingMerpeepsRestoresOriginalDocument()
    {
        var src = "---\ntitle: Keep Me\n---\nflowchart TD\n  A --> B";
        var ext = MerpeepsDoc.Inject(src, Patch());
        var stripped = MerpeepsDoc.Serialise(ext, null);
        Assert.Null(MerpeepsDoc.Parse(stripped));
        Assert.Contains("title: Keep Me", stripped);
        Assert.Equal(MerpeepsDoc.BodyOf(src), MerpeepsDoc.BodyOf(stripped));
    }
}
