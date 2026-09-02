using Merpeeps.Core;
using Merpeeps.Core.Model;
using Merpeeps.Core.Parsing;
using Xunit;

namespace Merpeeps.Core.Tests;

public class ParserTests
{
    [Fact]
    public void ParsesHeaderAndDirection()
    {
        var m = FlowchartParser.Parse("flowchart LR\n    A --> B");
        Assert.Equal("flowchart", m.Kind);
        Assert.Equal(Direction.LR, m.Direction);
        Assert.Equal(2, m.Nodes.Count);
        Assert.Single(m.Edges);
    }

    [Theory]
    [InlineData("A[Rect]", NodeShape.Rectangle)]
    [InlineData("A(Round)", NodeShape.RoundedRect)]
    [InlineData("A([Stadium])", NodeShape.Stadium)]
    [InlineData("A[[Sub]]", NodeShape.Subroutine)]
    [InlineData("A[(Cyl)]", NodeShape.Cylinder)]
    [InlineData("A((Circ))", NodeShape.Circle)]
    [InlineData("A{Diamond}", NodeShape.Rhombus)]
    [InlineData("A{{Hex}}", NodeShape.Hexagon)]
    public void ParsesNodeShapes(string decl, NodeShape expected)
    {
        var m = FlowchartParser.Parse($"flowchart TD\n    {decl}");
        var n = Assert.Single(m.Nodes);
        Assert.Equal(expected, n.Shape);
    }

    [Theory]
    [InlineData("A --> B", EdgeKind.Arrow)]
    [InlineData("A --- B", EdgeKind.Open)]
    [InlineData("A -.-> B", EdgeKind.DottedArrow)]
    [InlineData("A ==> B", EdgeKind.ThickArrow)]
    public void ParsesEdgeKinds(string stmt, EdgeKind expected)
    {
        var m = FlowchartParser.Parse($"flowchart TD\n    {stmt}");
        Assert.Equal(expected, Assert.Single(m.Edges).Kind);
    }

    [Fact]
    public void ParsesPipeEdgeLabel()
    {
        var m = FlowchartParser.Parse("flowchart TD\n    A -->|yes| B");
        Assert.Equal("yes", Assert.Single(m.Edges).Label);
    }

    [Fact]
    public void ParsesMidTextEdgeLabel()
    {
        var m = FlowchartParser.Parse("flowchart TD\n    A -- maybe --> B");
        Assert.Equal("maybe", Assert.Single(m.Edges).Label);
    }

    [Fact]
    public void ParsesChainedStatement()
    {
        var m = FlowchartParser.Parse("flowchart TD\n    A[Start] --> B{Ok?} -->|yes| C[Ship]");
        Assert.Equal(3, m.Nodes.Count);
        Assert.Equal(2, m.Edges.Count);
        Assert.Equal("yes", m.Edges[1].Label);
        Assert.Equal(NodeShape.Rhombus, m.Node("B")!.Shape);
    }

    [Fact]
    public void ParallelEdgesGetDistinctOrdinals()
    {
        var m = FlowchartParser.Parse("flowchart TD\n    A --> B\n    A --> B");
        Assert.Equal(2, m.Edges.Count);
        Assert.Equal("A->B#0", m.Edges[0].Key);
        Assert.Equal("A->B#1", m.Edges[1].Key);
    }

    [Fact]
    public void ParsesSubgraphMembership()
    {
        var m = FlowchartParser.Parse("""
            flowchart TD
                subgraph api [API Layer]
                    A[Gateway]
                    B[Auth]
                end
                A --> B
            """);
        var sg = Assert.Single(m.Subgraphs);
        Assert.Equal("API Layer", sg.Title);
        Assert.Equal(new[] { "A", "B" }, sg.NodeIds);
        Assert.Equal("api", m.Node("A")!.SubgraphId);
    }

    [Fact]
    public void UnknownSyntaxIsPreservedNotDropped()
    {
        // The safety property: a visual editor must not silently delete
        // directives it does not model.
        var src = """
            flowchart TD
                A --> B
                classDef important fill:#f96,stroke:#333
                class A important
                click A "https://example.com" _blank
                linkStyle 0 stroke:#ff3,stroke-width:4px
            """;
        var m = FlowchartParser.Parse(src);
        var kept = string.Join("\n", m.Passthrough.Values);
        Assert.Contains("classDef important", kept);
        Assert.Contains("class A important", kept);
        Assert.Contains("click A", kept);
        Assert.Contains("linkStyle 0", kept);

        var written = FlowchartWriter.Write(m);
        Assert.Contains("classDef important", written);
        Assert.Contains("linkStyle 0", written);
    }

    [Fact]
    public void SemanticRoundTripIsStable()
    {
        var src = """
            flowchart LR
                A[Start] --> B{Decision}
                B -->|yes| C([Ship])
                B -->|no| D[(Store)]
                C --> E((Done))
            """;
        var first = FlowchartParser.Parse(src);
        var second = FlowchartParser.Parse(FlowchartWriter.Write(first));

        Assert.Equal(first.Direction, second.Direction);
        Assert.Equal(first.Nodes.Count, second.Nodes.Count);
        Assert.Equal(first.Edges.Count, second.Edges.Count);
        foreach (var n in first.Nodes)
        {
            var o = second.Node(n.Id);
            Assert.NotNull(o);
            Assert.Equal(n.Shape, o!.Shape);
            Assert.Equal(n.Display, o.Display);
        }
        foreach (var e in first.Edges)
            Assert.Contains(second.Edges, x =>
                x.From == e.From && x.To == e.To && x.Label == e.Label && x.Kind == e.Kind);
    }

    [Fact]
    public void WriterOutputIsIdempotent()
    {
        var src = "flowchart TD\n    A[Start] --> B{Ok?}\n    B -->|yes| C[Ship]";
        var once = FlowchartWriter.Write(FlowchartParser.Parse(src));
        var twice = FlowchartWriter.Write(FlowchartParser.Parse(once));
        Assert.Equal(once, twice);
    }
}
