using System.Globalization;
using System.Linq;
using System.Threading;
using System.Xml;
using Merpeeps.Core;
using Merpeeps.Core.Export;
using Merpeeps.Core.Model;
using Xunit;

namespace Merpeeps.Core.Tests;

/// <summary>
/// The SVG exporter renders what the editor shows — positions, Merpeeps styling
/// and icons — without a browser.
/// </summary>
public class SvgExporterTests
{
    private const string Source = """
        ---
        x-merpeeps:
          spec: 1
          layout:
            nodes:
              a: {x: 0, y: 0}
              b: {x: 240, y: 0}
          style:
            nodes:
              a: {fill: '#D5E8D4', stroke: '#82B366', icon: server}
        ---
        flowchart LR
            a[Alpha] -->|calls| b[Beta]
        """;

    private static string Export(string? theme = null) =>
        SvgExporter.ToSvg(MerpeepsDocument.Load(Source), theme);

    /// <summary>
    /// The output must be well-formed XML. An SVG that does not parse is not a
    /// picture, it is a text file with the wrong extension.
    /// </summary>
    [Fact]
    public void TheOutputIsWellFormedXml()
    {
        var doc = new XmlDocument();
        doc.LoadXml(Export());   // throws if malformed

        Assert.Equal("svg", doc.DocumentElement!.LocalName);
        Assert.Equal("http://www.w3.org/2000/svg", doc.DocumentElement.NamespaceURI);
    }

    /// <summary>
    /// Numbers must always use a dot for the decimal point, whatever the
    /// machine's locale says.
    /// </summary>
    /// <remarks>
    /// The failure this guards against is nasty precisely because it is
    /// invisible to most people: on a machine with a comma decimal separator,
    /// unqualified formatting emits <c>x="12,5"</c> and every exported file is
    /// silently invalid — but only for those users, and never in CI.
    /// </remarks>
    [Fact]
    public void CoordinatesUseADotWhateverTheLocale()
    {
        var original = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
            var svg = Export();

            new XmlDocument().LoadXml(svg);
            Assert.DoesNotContain("=\"12,", svg);

            // Something in this diagram genuinely has a fractional coordinate,
            // or the assertion above would pass on a document with no decimals
            // at all and prove nothing.
            Assert.Contains(".", svg.Split('\n').First(l => l.Contains("<line")));
        }
        finally { Thread.CurrentThread.CurrentCulture = original; }
    }

    [Fact]
    public void StoredPositionsAndStylingAreUsed()
    {
        var svg = Export();

        Assert.Contains("#D5E8D4", svg);      // the node's fill
        Assert.Contains("#82B366", svg);      // and its stroke
        Assert.Contains("<g id=\"a\">", svg);
        Assert.Contains("<g id=\"b\">", svg);
        Assert.Contains("Alpha", svg);
        Assert.Contains("calls", svg);        // the edge label
    }

    /// <summary>
    /// Icons must be drawn. They are the whole reason a native exporter exists
    /// rather than reading mermaid's own SVG, which cannot represent them.
    /// </summary>
    [Fact]
    public void IconsAreDrawn()
    {
        var withIcon = Export();

        var model = MerpeepsDocument.Load(Source);
        model.Node("a")!.Icon = null;
        var without = SvgExporter.ToSvg(model);

        // The control: the same diagram minus the icon is meaningfully smaller.
        // Asserting only that some shape exists would pass on the node's own box.
        Assert.True(withIcon.Length > without.Length + 100,
            $"icon added only {withIcon.Length - without.Length} characters");
    }

    /// <summary>
    /// Edges must stop at the node outline, not its centre.
    /// </summary>
    /// <remarks>
    /// Nodes are drawn after edges, so an arrowhead aimed at a centre is hidden
    /// underneath the node it points to — the diagram loses its arrows and looks
    /// like the export is broken.
    /// </remarks>
    [Fact]
    public void EdgesStopAtTheNodeOutline()
    {
        var doc = new XmlDocument();
        doc.LoadXml(Export());

        var ns = new XmlNamespaceManager(doc.NameTable);
        ns.AddNamespace("s", "http://www.w3.org/2000/svg");
        var line = doc.SelectSingleNode("//s:line", ns)!;

        var x1 = double.Parse(line.Attributes!["x1"]!.Value, CultureInfo.InvariantCulture);
        var x2 = double.Parse(line.Attributes["x2"]!.Value, CultureInfo.InvariantCulture);

        // a spans x 0..160, b starts at x 240. The line must start at a's right
        // edge and stop at b's left one, not run between the two centres.
        var model = MerpeepsDocument.Load(Source);
        var a = model.Node("a")!;
        var b = model.Node("b")!;

        Assert.True(x1 >= a.X + a.Width - 1, $"line starts inside a (x1={x1})");
        Assert.True(x2 <= b.X + 1, $"line ends inside b (x2={x2})");
    }

    [Theory]
    [InlineData("dark", "#1F2020")]
    [InlineData("forest", "#CDE498")]
    [InlineData("neutral", "#EEEEEE")]
    public void AThemeColoursUnstyledNodes(string theme, string expectedFill)
    {
        // b carries no styling of its own, so it takes the theme's colours.
        Assert.Contains(expectedFill, Export(theme));
    }

    /// <summary>
    /// An explicitly styled node keeps its colour under every theme — that is
    /// the author's data, not the theme's decoration.
    /// </summary>
    [Fact]
    public void AThemeDoesNotOverrideExplicitStyling()
    {
        foreach (var entry in new[] { "dark", "forest", "neutral", "base", "default" })
            Assert.Contains("#D5E8D4", Export(entry));
    }

    /// <summary>
    /// Label text is escaped, so a diagram cannot produce broken XML.
    /// </summary>
    [Fact]
    public void LabelsAreEscaped()
    {
        var model = MerpeepsDocument.Load(Source);
        model.Node("a")!.Label = "a < b & \"c\"";

        var svg = SvgExporter.ToSvg(model);
        new XmlDocument().LoadXml(svg);      // throws if the escaping is wrong

        Assert.Contains("&lt;", svg);
        Assert.Contains("&amp;", svg);
    }

    /// <summary>
    /// The viewBox must contain the diagram even when nodes sit at negative
    /// coordinates, which they do as soon as anyone drags one up or left.
    /// </summary>
    [Fact]
    public void NegativeCoordinatesAreInsideTheViewBox()
    {
        var model = MerpeepsDocument.Load(Source);
        model.Node("a")!.X = -300;
        model.Node("a")!.Y = -200;

        var doc = new XmlDocument();
        doc.LoadXml(SvgExporter.ToSvg(model));

        var w = double.Parse(doc.DocumentElement!.Attributes!["width"]!.Value,
            CultureInfo.InvariantCulture);
        var h = double.Parse(doc.DocumentElement.Attributes["height"]!.Value,
            CultureInfo.InvariantCulture);

        // 540 wide of content (-300..240) plus the node and padding.
        Assert.True(w > 540, $"width {w} cannot contain the diagram");
        Assert.True(h > 200, $"height {h} cannot contain the diagram");
    }

    /// <summary>An empty diagram produces a valid file rather than throwing.</summary>
    [Fact]
    public void AnEmptyDiagramStillProducesValidSvg()
    {
        var svg = SvgExporter.ToSvg(new DiagramModel());
        new XmlDocument().LoadXml(svg);
    }
}
