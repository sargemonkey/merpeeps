using System.Text.RegularExpressions;
using Merpeeps.Core;
using Merpeeps.Core.Layout;
using Xunit;

namespace Merpeeps.Core.Tests;

/// <summary>
/// The program is meant to run on Windows, Linux and macOS, and the same file
/// is meant to survive being opened on one and saved on another. Line endings
/// are where that usually breaks: a Windows checkout is CRLF, a Linux one is
/// LF, and a tool that silently normalises either way turns a one-line edit
/// into a whole-file diff.
/// </summary>
public class CrossPlatformTests
{
    private const string Body = "flowchart LR\n    a[Alpha] --> b[Beta]\n";
    private static readonly string CrlfDoc =
        "---\r\ntitle: Payments\r\n---\r\nflowchart LR\r\n    a[Alpha] --> b[Beta]\r\n";
    private const string LfDoc =
        "---\ntitle: Payments\n---\nflowchart LR\n    a[Alpha] --> b[Beta]\n";

    private static int Crlf(string s) => Regex.Matches(s, "\r\n").Count;
    private static int BareLf(string s) => Regex.Matches(s, "(?<!\r)\n").Count;

    // ------------------------------------------------------------- parsing

    [Fact]
    public void CrlfAndLfDocumentsParseIdentically()
    {
        var a = MerpeepsDocument.Load(CrlfDoc);
        var b = MerpeepsDocument.Load(LfDoc);

        Assert.Equal(b.Nodes.Count, a.Nodes.Count);
        Assert.Equal(b.Edges.Count, a.Edges.Count);
        Assert.Equal(
            b.Nodes.Select(n => (n.Id, n.Label)),
            a.Nodes.Select(n => (n.Id, n.Label)));
    }

    [Fact]
    public void LabelsDoNotKeepACarriageReturn()
    {
        // A stray \r inside a label is invisible on screen and corrupts the
        // file the moment it is written back out.
        foreach (var n in MerpeepsDocument.Load(CrlfDoc).Nodes)
        {
            Assert.DoesNotContain('\r', n.Id);
            Assert.DoesNotContain('\r', n.Label ?? "");
        }
    }

    // ------------------------------------------------------------- writing

    /// <summary>
    /// Saving a CRLF document must not rewrite every line to LF. Doing so would
    /// show up as a whole-file diff for every Windows user, which is exactly
    /// the kind of churn this format exists to avoid.
    /// </summary>
    [Fact]
    public void SavingACrlfDocumentKeepsCrlf()
    {
        var model = MerpeepsDocument.Load(CrlfDoc);
        model.Node("b")!.X = 300;
        var saved = MerpeepsDocument.SavePositions(CrlfDoc, model);

        Assert.True(Crlf(saved) > 0, "CRLF endings were lost entirely");
        Assert.Equal(0, BareLf(saved));
    }

    [Fact]
    public void SavingAnLfDocumentKeepsLf()
    {
        var model = MerpeepsDocument.Load(LfDoc);
        model.Node("b")!.X = 300;
        var saved = MerpeepsDocument.SavePositions(LfDoc, model);

        Assert.Equal(0, Crlf(saved));
        Assert.True(BareLf(saved) > 0);
    }

    [Fact]
    public void StructuralEditsKeepTheDocumentsLineEndings()
    {
        var model = MerpeepsDocument.Load(CrlfDoc);
        var history = new Editing.EditHistory(model);
        history.Apply(new Editing.RenameNodeCommand(model.Node("a")!, "Renamed"));

        var saved = MerpeepsDocument.Save(CrlfDoc, model);

        Assert.Contains("Renamed", saved);
        Assert.Equal(0, BareLf(saved));
    }

    [Fact]
    public void StrippingRestoresACrlfDocumentExactly()
    {
        var model = MerpeepsDocument.Load(CrlfDoc);
        var saved = MerpeepsDocument.SavePositions(CrlfDoc, model);

        Assert.Equal(CrlfDoc, MerpeepsDoc.Serialise(saved, null));
    }

    /// <summary>
    /// Regression: real files are sometimes mixed — mostly LF with a stray CRLF
    /// from a pasted hunk. An earlier fix normalised the whole document before
    /// writing, which silently rewrote that one line and broke the
    /// byte-preservation promise. Only the generated block may be reformatted;
    /// the body is passed through untouched.
    ///
    /// <para>This was caught by the native CLI's own validate command, not by
    /// any unit test, which is why it is one now.</para>
    /// </summary>
    [Fact]
    public void AMixedEndingDocumentKeepsItsBodyByteForByte()
    {
        // Mostly LF, one CRLF in the middle.
        const string mixed =
            "flowchart TD\n    a[Alpha] --> b[Beta]\r\n    b --> c[Gamma]\n    c --> d[Delta]\n";

        var model = MerpeepsDocument.Load(mixed);
        var saved = MerpeepsDocument.SavePositions(mixed, model);

        Assert.Equal(MerpeepsDoc.BodyOf(mixed), MerpeepsDoc.BodyOf(saved));
        Assert.Contains("\r\n    b --> c", saved);
    }

    [Fact]
    public void AMixedEndingDocumentValidates()
    {
        const string mixed =
            "---\ntitle: T\n---\nflowchart TD\n    a[A] --> b[B]\r\n    b --> c[C]\n";

        var model = MerpeepsDocument.Load(mixed);
        var saved = MerpeepsDocument.SavePositions(mixed, model);

        // Idempotence: writing the same metadata twice must converge.
        var twice = MerpeepsDocument.SavePositions(saved, MerpeepsDocument.Load(saved));
        Assert.Equal(saved, twice);
    }

    // ------------------------------------------------ engines and metadata

    [Theory]
    [InlineData("layered")]
    [InlineData("tree")]
    [InlineData("force")]
    [InlineData("grid")]
    public void EveryEngineProducesTheSameResultRegardlessOfLineEndings(string engineId)
    {
        DiagramModelPositions Run(string src)
        {
            var m = MerpeepsDocument.Load(src);
            foreach (var n in m.Nodes) n.HasExplicitPosition = false;
            LayoutEngines.ById(engineId).Apply(m, LayoutOptions.Default);
            return new DiagramModelPositions(
                m.Nodes.ToDictionary(n => n.Id, n => (n.X, n.Y)));
        }

        var fromCrlf = Run(CrlfDoc);
        var fromLf = Run(LfDoc);

        foreach (var (id, pos) in fromLf.Positions)
        {
            Assert.True(fromCrlf.Positions.ContainsKey(id), $"{id} missing");
            Assert.Equal(pos.Item1, fromCrlf.Positions[id].Item1, 6);
            Assert.Equal(pos.Item2, fromCrlf.Positions[id].Item2, 6);
        }
    }

    private sealed record DiagramModelPositions(
        Dictionary<string, (double, double)> Positions);

    /// <summary>
    /// Coordinates are formatted into YAML, so a locale that writes decimals
    /// with a comma would emit `x: 12,5` and produce a file no other machine
    /// could read. This is a real difference between a German Linux box and a
    /// US Windows one.
    /// </summary>
    [Fact]
    public void CoordinatesUseAnInvariantDecimalPoint()
    {
        var original = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture =
                new System.Globalization.CultureInfo("de-DE");

            var model = MerpeepsDocument.Load(LfDoc);
            model.Node("a")!.X = 12.5;
            model.Node("a")!.Y = 0.25;
            var saved = MerpeepsDocument.SavePositions(LfDoc, model);

            Assert.Contains("12.5", saved);
            Assert.DoesNotContain("12,5", saved);

            // And it must read back as a number, not a string.
            var reloaded = MerpeepsDocument.Load(saved);
            Assert.Equal(12.5, reloaded.Node("a")!.X, 6);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void ParsingIsCultureIndependent()
    {
        var original = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            var stored = """
                ---
                x-merpeeps:
                  spec: 1
                  layout:
                    nodes:
                      a: { x: 12.5, y: 0.25 }
                ---
                flowchart LR
                    a[Alpha] --> b[Beta]
                """;

            System.Globalization.CultureInfo.CurrentCulture =
                new System.Globalization.CultureInfo("de-DE");
            var german = MerpeepsDocument.Load(stored).Node("a")!;

            System.Globalization.CultureInfo.CurrentCulture =
                System.Globalization.CultureInfo.InvariantCulture;
            var invariant = MerpeepsDocument.Load(stored).Node("a")!;

            Assert.Equal(12.5, german.X, 6);
            Assert.Equal(invariant.X, german.X, 6);
            Assert.Equal(invariant.Y, german.Y, 6);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = original;
        }
    }

    /// <summary>
    /// Node ids come from user text and may be non-ASCII. Case handling must
    /// not depend on the machine's locale — the Turkish dotless i is the
    /// classic way this breaks.
    /// </summary>
    [Fact]
    public void IdentifierMatchingIsNotLocaleSensitive()
    {
        var original = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture =
                new System.Globalization.CultureInfo("tr-TR");

            Assert.True(LayoutEngines.IsKnown("LAYERED"));
            Assert.True(LayoutEngines.IsKnown("Grid"));
            Assert.Equal("layered", LayoutEngines.ById("LAYERED").Id);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = original;
        }
    }

    // --------------------------------------------------- the comment carrier

    /// <summary>
    /// A fractional value written into the JSON carrier keeps its decimal
    /// point.
    /// </summary>
    /// <remarks>
    /// <para>The carrier is JSON, and JSON has exactly one numeric grammar. A
    /// comma-decimal locale turned <c>labelT: 0.5</c> into <c>"labelT":0,5</c>,
    /// which is not that value written oddly — it is a different document. The
    /// carrier is re-read as YAML, and YAML obligingly parses the flow mapping
    /// as <c>labelT: 0</c> followed by a second key <c>5</c> with no value. The
    /// edge label silently jumps to the start of the edge and the file grows a
    /// key nothing wrote.</para>
    /// <para>Nothing errors, which is what makes it worth a test: the failure
    /// is a plausible-looking document that means something else.</para>
    /// </remarks>
    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("ru-RU")]
    public void TheCommentCarrierKeepsADecimalPoint(string culture)
    {
        var original = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture =
                new System.Globalization.CultureInfo(culture);

            var merpeeps = new Dictionary<string, object?>
            {
                ["spec"] = 1L,
                ["edges"] = new Dictionary<string, object?>
                {
                    ["a->b#0"] = new Dictionary<string, object?> { ["labelT"] = 0.5 },
                },
            };

            var written = MerpeepsDoc.Serialise(LfDoc, merpeeps, carrier: "comment");

            Assert.Contains("0.5", written);
            Assert.DoesNotContain("0,5", written);

            // The value has to survive the round trip as a number, not merely
            // appear correct in the text.
            var reloaded = (Dictionary<string, object?>)MerpeepsDoc.Parse(written)!;
            var edges = (Dictionary<string, object?>)reloaded["edges"]!;
            var edge = (Dictionary<string, object?>)edges["a->b#0"]!;

            Assert.Equal(0.5, Assert.IsType<double>(edge["labelT"]), 6);

            // And the mis-parse must not have invented a sibling key.
            Assert.Equal(["labelT"], edge.Keys.OrderBy(k => k, StringComparer.Ordinal));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = original;
        }
    }

    /// <summary>
    /// The same document produces the same carrier on every machine.
    /// </summary>
    /// <remarks>
    /// Stated as an equality rather than a spelling, because that is the
    /// property that actually matters: two developers in different locales
    /// committing the same edit should produce no diff.
    /// </remarks>
    [Fact]
    public void TheCarrierIsByteIdenticalAcrossLocales()
    {
        var original = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            var merpeeps = new Dictionary<string, object?>
            {
                ["spec"] = 1L,
                ["layout"] = new Dictionary<string, object?>
                {
                    ["nodes"] = new Dictionary<string, object?>
                    {
                        ["a"] = new Dictionary<string, object?>
                        {
                            ["x"] = 12.5, ["y"] = 0.25,
                        },
                    },
                },
            };

            System.Globalization.CultureInfo.CurrentCulture =
                System.Globalization.CultureInfo.InvariantCulture;
            var invariant = MerpeepsDoc.Serialise(LfDoc, merpeeps, carrier: "comment");

            System.Globalization.CultureInfo.CurrentCulture =
                new System.Globalization.CultureInfo("de-DE");
            var german = MerpeepsDoc.Serialise(LfDoc, merpeeps, carrier: "comment");

            Assert.Equal(invariant, german);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = original;
        }
    }

    /// <summary>
    /// <see cref="MerpeepsDoc.ToJson"/> is also the comparison key for the
    /// round-trip checks in <c>merpeeps validate</c> and the MCP server, so a
    /// locale-dependent rendering would make a file appear to fail validation
    /// on one machine and pass on another.
    /// </summary>
    [Fact]
    public void ToJsonRendersNumbersInvariantly()
    {
        var original = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture =
                new System.Globalization.CultureInfo("de-DE");

            var values = new Dictionary<string, object?>
            {
                ["d"] = 0.5,
                ["f"] = 1.25f,
                ["m"] = 2.75m,
                ["negative"] = -0.5,
                ["whole"] = 3L,
            };

            var json = MerpeepsDoc.ToJson(values);

            Assert.DoesNotContain(",5", json);
            Assert.DoesNotContain(",25", json);
            Assert.DoesNotContain(",75", json);
            Assert.Contains("0.5", json);
            Assert.Contains("1.25", json);
            Assert.Contains("2.75", json);
            Assert.Contains("-0.5", json);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = original;
        }
    }
}
