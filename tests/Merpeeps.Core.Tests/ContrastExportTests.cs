using System.Xml.Linq;
using Merpeeps.Core.Export;
using Merpeeps.Core.Editing;
using Merpeeps.Core.Model;
using Xunit;

namespace Merpeeps.Core.Tests;

public class ContrastExportTests
{
    [Theory]
    [InlineData("#FFFFFF", "#DDDDDD")]
    [InlineData("#EFF6FF", "#EEEEEE")]
    [InlineData("#0E0B1A", "#333333")]
    [InlineData("#64748B", "#AAAAAA")]
    public void InheritedTextMeetsReadableContrastOnTheActualSurface(string fill, string preferred)
    {
        var surface = DiagramContrast.Parse(fill);
        Assert.True(DiagramContrast.Ratio(DiagramContrast.ReadableInk(DiagramContrast.Parse(preferred), surface), surface) >= 4.5);
    }

    [Fact]
    public void AllOpaqueNeutralSurfacesHaveAReadableFallback()
    {
        for (var value = 0; value <= 255; value++)
        {
            var surface = new ClassColor((byte)value, (byte)value, (byte)value);
            Assert.True(DiagramContrast.Ratio(DiagramContrast.ReadableInk(new(170, 170, 170), surface), surface) >= 4.5);
        }
    }

    [Fact]
    public void NestedTranslucentGroupTitleUsesTheParentSurfaceNotJustTheCanvas()
    {
        const string source = "flowchart LR\nsubgraph Outer[Outer]\nsubgraph G[Owned]\nA[Node]\nend\nend\n";
        var model = MerpeepsDocument.Load(source);
        var geometry = RenderedGraphGeometry.Parse("""
            {"spec":1,"nodes":{"A":{"x":80,"y":120,"w":240,"h":64,"text":"Node"}},
             "groups":{"Outer":{"x":-20,"y":-20,"w":440,"h":280,"text":"Outer","members":["A"],"fill":"#FFFFFF"},
             "G":{"x":0,"y":0,"w":400,"h":240,"text":"Owned","members":["A"],"fill":"#00000010",
             "label":{"fontFamily":"Segoe UI","fontSize":20,"fontWeight":400,"lines":[{"text":"Owned","x":120,"y":20,"w":120,"h":26}]}}},"edges":{}}
            """);
        new AdoptRenderedGeometryCommand(model, geometry).Do(model);
        var surface = DiagramContrast.SurfaceAt(model, new(120, 20), DiagramContrast.Parse("#1E1E1E"));
        Assert.Equal(new ClassColor(239, 239, 239), surface);
        foreach (var theme in new[] { "default", "dark" })
        {
            var xml = XDocument.Parse(SvgExporter.ToSvg(model, theme));
            var title = xml.Descendants().Single(e => e.Name.LocalName == "text" && e.Value == "Owned");
            Assert.True(DiagramContrast.Ratio(DiagramContrast.Parse(title.Attribute("fill")!.Value), surface) >= 4.5);
        }
        model.Nodes.Single().X += 700;
        Assert.Equal(surface, DiagramContrast.SurfaceAt(model, new(820, 20), DiagramContrast.Parse("#1E1E1E")));
        Assert.Equal(DiagramContrast.Parse("#1E1E1E"), DiagramContrast.SurfaceAt(model, new(120, 20), DiagramContrast.Parse("#1E1E1E")));
    }

    [Fact]
    public void TransparencyDoesNotGuessThatAFullSizeAuthoredRectangleIsTheCanvas()
    {
        const string svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="200" height="100">
              <rect data-canvas-background="true" width="100%" height="100%" fill="white"/>
              <rect id="authored-plate" width="100%" height="100%" fill="#EFF6FF"/>
              <g><rect data-canvas-background="true" width="200" height="100" fill="#FFF7ED"/></g>
            </svg>
            """;
        var xml = XDocument.Parse(SvgBackground.MakeTransparent(svg));
        Assert.Single(xml.Root!.Elements(), e => e.Name.LocalName == "rect");
        Assert.Contains(xml.Descendants(), e => (string?)e.Attribute("id") == "authored-plate");
        Assert.Contains(xml.Descendants(), e => (string?)e.Attribute("fill") == "#FFF7ED");
    }

    [Theory]
    [InlineData("-->")]
    [InlineData("-.->")]
    public void ArrowInkUsesItsEndpointSurfaceEvenWhenTheRouteCenterNeedsDifferentInk(string arrow)
    {
        const string source = "flowchart LR\nsubgraph Dark[Dark]\nA[First]\nend\nsubgraph Light[Light]\nB[Second]\nend\nA --> B\n";
        var model = MerpeepsDocument.Load(source.Replace("A --> B", "A " + arrow + " B"));
        var geometry = RenderedGraphGeometry.Parse("""
            {"spec":1,"nodes":{"A":{"x":20,"y":50,"w":100,"h":50,"text":"First"},"B":{"x":400,"y":50,"w":100,"h":50,"text":"Second"}},
             "groups":{"Dark":{"x":0,"y":0,"w":150,"h":160,"text":"Dark","members":["A"],"fill":"#111111"},
             "Light":{"x":370,"y":0,"w":160,"h":160,"text":"Light","members":["B"],"fill":"#FFFFFF"}},
             "edges":{"A->B#0":{"text":"","points":[[120,75],[250,75],[400,75]]}}}
            """);
        new AdoptRenderedGeometryCommand(model, geometry).Do(model);
        var xml = XDocument.Parse(SvgExporter.ToSvg(model, "dark"));
        var marker = xml.Descendants().Single(e => (string?)e.Attribute("id") == "readable-arrow-0");
        var ink = DiagramContrast.Parse(marker.Elements().Single().Attribute("fill")!.Value);
        Assert.True(DiagramContrast.Ratio(ink, DiagramContrast.Parse("#FFFFFF")) >= 3);
        var title = xml.Descendants().Single(e => e.Name.LocalName == "text" && e.Value == "Light");
        Assert.True(DiagramContrast.Ratio(DiagramContrast.Parse(title.Attribute("fill")!.Value), DiagramContrast.Parse("#FFFFFF")) >= 4.5);
        if (arrow == "-.->")
            Assert.All(xml.Descendants().Where(e => (string?)e.Attribute("data-contrast-halo") == "true"),
                halo => Assert.Equal("5,4", halo.Attribute("stroke-dasharray")!.Value));
    }

    [Theory]
    [InlineData("flowchart LR\nA[First] --> B[Second]\n")]
    [InlineData("pie\n\"First\":30\n\"Second\":70\n")]
    [InlineData("sequenceDiagram\nA->>B: Message\n")]
    [InlineData("timeline\nPeriod:Event\n")]
    [InlineData("classDiagram\nclass A\n")]
    [InlineData("erDiagram\nA\n")]
    public void TransparentSvgRemovesOnlyOuterCanvasAndKeepsDiagramContent(string source)
    {
        var model = MerpeepsDocument.Load(source);
        var opaque = XDocument.Parse(SvgExporter.ToSvg(model));
        var transparent = XDocument.Parse(SvgExporter.ToSvg(model, transparentBackground: true));
        XNamespace ns = "http://www.w3.org/2000/svg";
        Assert.Equal("transparent", transparent.Root!.Attribute("data-canvas-background")!.Value);
        Assert.Equal(opaque.Root!.Attribute("viewBox")!.Value, transparent.Root.Attribute("viewBox")!.Value);
        Assert.Equal(opaque.Descendants(ns + "text").Select(t => t.Value), transparent.Descendants(ns + "text").Select(t => t.Value));
        Assert.DoesNotContain(transparent.Root.Elements(ns + "rect"), e =>
            (string?)e.Attribute("width") == "100%" ||
            ((string?)e.Attribute("width") == (string?)transparent.Root.Attribute("width") &&
             (string?)e.Attribute("height") == (string?)transparent.Root.Attribute("height")));
        Assert.Contains(transparent.Descendants(), e => e.Attribute("fill")?.Value is not (null or "none" or "transparent"));
    }

    [Fact]
    public void ExplicitNodeColorsAreNotRewrittenByAppearanceOrTransparentOutput()
    {
        const string source = "---\nx-merpeeps:\n  spec: 1\n  style:\n    nodes:\n      A: {fill: '#EFF6FF', stroke: '#2563EB', text: '#1E3A8A'}\n---\nflowchart LR\nA[Fixed]\n";
        var model = MerpeepsDocument.Load(source);
        foreach (var theme in new[] { "default", "dark" })
        {
            var svg = SvgExporter.ToSvg(model, theme, transparentBackground: true);
            Assert.Contains("#EFF6FF", svg);
            Assert.Contains("#2563EB", svg);
            Assert.Contains("#1E3A8A", svg);
        }
        Assert.Equal("#1E3A8A", model.Nodes.Single().TextColor);
    }
}
