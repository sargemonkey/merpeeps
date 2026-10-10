using Merpeeps.Core.Export;
using Merpeeps.Core.Editing;
using Merpeeps.Core.Model;
using Xunit;

namespace Merpeeps.Core.Tests;

public class RenderedGeometryTests
{
    private const string Source = "flowchart LR\nsubgraph G[Group]\nA[First] -->|Calls| B[Second]\nend\n";
    private const string Geometry = """
        {
          "spec":1,
          "nodes":{
            "A":{"x":20.125,"y":50.25,"w":180.5,"h":90.75,"text":"First","label":{"fontFamily":"Segoe UI","fontSize":16,"fontWeight":400,"lines":[{"text":"First","x":80,"y":82,"w":50,"h":20}]}},
            "B":{"x":350.125,"y":50.25,"w":180.5,"h":90.75,"text":"Second"}
          },
          "groups":{"G":{"x":-10,"y":0,"w":600,"h":220,"text":"Group","members":["A","B"],"fill":"#eff6ff","stroke":"#93c5fd","strokeWidth":2}},
          "edges":{"A->B#0":{"text":"Calls","points":[[200.625,95.625],[250.125,170.25],[350.125,95.625]],"label":{"fontFamily":"Segoe UI","fontSize":16,"fontWeight":400,"lines":[{"text":"Calls","x":235,"y":150,"w":45,"h":20}]}}}
        }
        """;

    [Fact]
    public void StoredWaypointsAreRenderedInsteadOfIgnored()
    {
        const string source = "---\nx-merpeeps:\n  spec: 1\n  layout:\n    nodes:\n      A: {x: 0, y: 0, w: 100, h: 50}\n      B: {x: 300, y: 0, w: 100, h: 50}\n    edges:\n      \"A->B#0\":\n        waypoints: [[120, 140], [280, 140]]\n---\nflowchart LR\nA --> B\n";
        var model = MerpeepsDocument.Load(source);
        var svg = SvgExporter.ToSvg(model);
        Assert.Contains("120,140", svg);
        Assert.Contains("280,140", svg);
    }

    [Fact]
    public void ProgrammaticWaypointsPersistAndExplicitRemovalDoesNotResurrectSavedBends()
    {
        const string source = "flowchart LR\nA --> B\n";
        var model = MerpeepsDocument.Load(source);
        model.EdgeWaypoints["A->B#0"] = [new(120, 140), new(280, 140)];
        var saved = MerpeepsDocument.SavePositions(source, model);
        Assert.Equal(model.EdgeWaypoints["A->B#0"], MerpeepsDocument.Load(saved).EdgeWaypoints["A->B#0"]);
        model.EdgeWaypoints.Clear();
        Assert.Empty(MerpeepsDocument.Load(MerpeepsDocument.SavePositions(saved, model)).EdgeWaypoints);
        Assert.Equal(source, MerpeepsDoc.BodyOf(saved));
    }

    [Fact]
    public void PositionOnlySaveDoesNotInventDimensionsButKeepsExistingExplicitSizes()
    {
        const string source = "flowchart LR\nA[First]\n";
        var model = MerpeepsDocument.Load(source);
        model.Nodes[0].X += 70;
        var saved = MerpeepsDocument.SavePositions(source, model, writeDimensions: false);
        Assert.DoesNotContain("w:", saved);
        Assert.DoesNotContain("h:", saved);
        var sized = MerpeepsDocument.SavePositions(source, model);
        var moved = MerpeepsDocument.SavePositions(sized, model, writeDimensions: false);
        Assert.Equal(model.Nodes[0].Width, MerpeepsDocument.Load(moved).Nodes[0].Width);
    }

    [Fact]
    public void ExplicitAdoptionKeepsDimensionsGroupsRoutesAndContentAcrossSaveReopenUndoAndRedo()
    {
        var model = MerpeepsDocument.Load(Source);
        var geometry = RenderedGraphGeometry.Parse(Geometry);
        var before = model.Nodes.Select(n => (n.X, n.Y, n.Width, n.Height, n.HasExplicitPosition, n.HasExplicitSize)).ToArray();
        var history = new EditHistory(model);
        history.Apply(new AdoptRenderedGeometryCommand(model, geometry));
        var saved = MerpeepsDocument.SavePositions(Source, model);
        Assert.Equal(Source, MerpeepsDoc.BodyOf(saved));
        var reopened = MerpeepsDocument.Load(saved);
        Assert.Equal(geometry.Nodes["A"].Bounds, new DiagramRect(reopened.Nodes[0].X, reopened.Nodes[0].Y, reopened.Nodes[0].Width, reopened.Nodes[0].Height));
        Assert.True(reopened.Nodes[0].HasExplicitSize);
        Assert.Equal(geometry.Groups["G"].Bounds, GraphGroupGeometry.Bounds(reopened, reopened.Subgraphs.Single()));
        Assert.Equal(geometry.Edges["A->B#0"].Points, GraphEdgeGeometry.Route(reopened, reopened.Edges.Single()));
        Assert.Equal(saved, MerpeepsDocument.SavePositions(saved, reopened));
        var svg = System.Xml.Linq.XDocument.Parse(SvgExporter.ToSvg(reopened));
        var points = svg.Descendants(svg.Root!.Name.Namespace + "polyline").Single().Attribute("points")!.Value.Split(' ');
        var bend = points[1].Split(',').Select(v => double.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        Assert.InRange(Math.Abs(bend[0] - 250.125), 0, .006);
        Assert.Equal(170.25, bend[1]);
        history.Undo();
        Assert.Equal(before, model.Nodes.Select(n => (n.X, n.Y, n.Width, n.Height, n.HasExplicitPosition, n.HasExplicitSize)));
        Assert.Null(model.RenderedGeometry);
        history.Redo();
        Assert.Equal(saved, MerpeepsDocument.SavePositions(Source, model));
    }

    [Fact]
    public void WholeGroupMovementTranslatesReferenceGeometryButIndividualEditsDoNotReuseStalePathsOrText()
    {
        var model = MerpeepsDocument.Load(Source);
        var geometry = RenderedGraphGeometry.Parse(Geometry);
        new AdoptRenderedGeometryCommand(model, geometry).Do(model);
        var history = new EditHistory(model);
        history.Apply(new MoveGroupCommand(model, model.Subgraphs.Single(), 70, 40));
        Assert.Equal(geometry.Groups["G"].Bounds with { X = 60, Y = 40 }, GraphGroupGeometry.Bounds(model, model.Subgraphs.Single()));
        Assert.Equal(geometry.Edges["A->B#0"].Points.Select(p => new DiagramPoint(p.X + 70, p.Y + 40)),
            GraphEdgeGeometry.Route(model, model.Edges.Single()));
        history.Undo();
        model.Nodes[0].X += 80;
        Assert.Equal(2, GraphEdgeGeometry.Route(model, model.Edges.Single()).Count);
        model.Nodes[0].Label = "Changed";
        Assert.Null(geometry.NodeLabel(model, model.Nodes[0]));
        model.Edges[0].Label = "Changed";
        Assert.Null(geometry.EdgeLabel(model, model.Edges[0]));
    }

    [Fact]
    public void AdoptionRejectsIncompleteStaleOrMalformedGeometryWithoutMutatingModel()
    {
        var model = MerpeepsDocument.Load(Source);
        var before = model.Nodes.Select(n => (n.X, n.Y, n.Width, n.Height)).ToArray();
        var geometry = RenderedGraphGeometry.Parse(Geometry);
        geometry.Nodes.Remove("B");
        Assert.Throws<ArgumentException>(() => new AdoptRenderedGeometryCommand(model, geometry));
        geometry = RenderedGraphGeometry.Parse(Geometry);
        geometry.Nodes["A"] = geometry.Nodes["A"] with { Text = "Wrong diagram" };
        Assert.Throws<ArgumentException>(() => new AdoptRenderedGeometryCommand(model, geometry));
        Assert.Equal(before, model.Nodes.Select(n => (n.X, n.Y, n.Width, n.Height)));
        Assert.Throws<FormatException>(() => RenderedGraphGeometry.Parse(Geometry.Replace("\"w\":180.5", "\"w\":-1")));
        Assert.Throws<FormatException>(() => RenderedGraphGeometry.Parse(Geometry.Replace("\"spec\":1", "\"spec\":2")));
    }
}
