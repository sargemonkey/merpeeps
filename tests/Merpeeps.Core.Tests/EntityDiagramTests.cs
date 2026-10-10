using Merpeeps.Core.Model;
using Merpeeps.Core.Parsing;
using Xunit;

namespace Merpeeps.Core.Tests;

public class EntityDiagramTests
{
    private readonly EntityDiagramAdapter _adapter = new();

    [Fact]
    public void EmptyErSourceHasNoFakeClassesOrFlowchartNodes()
    {
        var model = _adapter.Parse("erDiagram\n");
        Assert.NotNull(model.EntityOptions);
        Assert.Null(model.ClassOptions);
        Assert.Empty(model.Nodes);
        Assert.Empty(model.Edges);
        Assert.Equal("erDiagram\n", _adapter.Write(model));
    }

    [Fact]
    public void AttributesKeepTypeNameKeySetCommentAndAlias()
    {
        const string source = """
            erDiagram
            person["Customer account"] {
              string? middleName
              int *id
              string[] names PK, FK "Names comment"
              string(99) phone UK
            }
            """;
        var model = _adapter.Parse(source);
        var node = Assert.Single(model.Nodes);
        Assert.Equal("person", node.Id);
        Assert.Equal("Customer account", node.Label);
        Assert.Null(node.Class);
        var attributes = node.Entity!.Attributes;
        Assert.Equal(4, attributes.Count);
        Assert.Equal("string?", attributes[0].Type);
        Assert.Equal(EntityKey.Primary, attributes[1].EffectiveKeys);
        Assert.Equal(EntityKey.Primary | EntityKey.Foreign, attributes[2].Keys);
        Assert.Equal("Names comment", attributes[2].Comment);
        Assert.Equal(EntityKey.None, attributes[1].WithKeys(EntityKey.None).EffectiveKeys);
        var reopened = _adapter.Parse(_adapter.Write(model)).Nodes.Single();
        Assert.Equal(attributes.Select(a => a.Declaration), reopened.Entity!.Attributes.Select(a => a.Declaration));
        Assert.Equal("id", EntityDiagramText.Content(node).Attributes[1].Name);
    }

    [Theory]
    [InlineData("||--o{", EntityCardinality.ExactlyOne, EntityCardinality.ZeroOrMore, true)]
    [InlineData("}|..|{", EntityCardinality.OneOrMore, EntityCardinality.OneOrMore, false)]
    [InlineData("|o--o|", EntityCardinality.ZeroOrOne, EntityCardinality.ZeroOrOne, true)]
    [InlineData("1 to zero or more", EntityCardinality.ExactlyOne, EntityCardinality.ZeroOrMore, true)]
    [InlineData("many(0) optionally to 0+", EntityCardinality.ZeroOrMore, EntityCardinality.ZeroOrMore, false)]
    [InlineData("one or zero to one or many", EntityCardinality.ZeroOrOne, EntityCardinality.OneOrMore, true)]
    public void CardinalityAliasesHaveRealIndependentMeaning(string syntax, EntityCardinality from, EntityCardinality to, bool identifying)
    {
        var source = $"erDiagram\n PERSON {syntax} CAR : \"drives\"\n";
        var model = _adapter.Parse(source);
        var edge = Assert.Single(model.Edges);
        Assert.Equal(new EntityRelationship(from, to, identifying), edge.EntityRelationship);
        Assert.Null(edge.ClassRelationship);
        var rewritten = _adapter.Write(model);
        Assert.Equal(edge.EntityRelationship, _adapter.Parse(rewritten).Edges.Single().EntityRelationship);
        Assert.Equal(source, _adapter.Apply(source, model));
    }

    [Fact]
    public void SubgraphsAreNotSyntheticEntitiesAndCanBeRelationshipEndpoints()
    {
        const string source = """
            erDiagram
            direction LR
            subgraph "Customer Domain"
              direction TB
              CUSTOMER {
                int id PK
              }
              subgraph orders [Order history]
                direction RL
                ORDER
              end
            end
            subgraph empty
            end
            "Customer Domain" ||--o{ orders : contains
            orders ||--o{ ORDER : tracks
            """;
        var model = _adapter.Parse(source);
        Assert.Equal(2, model.Nodes.Count);
        Assert.Equal(3, model.Subgraphs.Count);
        Assert.Null(model.Node("orders"));
        var group = model.Subgraphs.Single(g => g.Id == "orders");
        Assert.Equal("Customer Domain", group.ParentId);
        Assert.Equal(Direction.RL, group.Direction);
        Assert.Equal("orders", model.Node("ORDER")!.SubgraphId);
        var endpoint = EntityDiagramGeometry.Endpoint(model, "orders");
        Assert.Equal(EntityEndpointKind.Subgraph, endpoint.Kind);
        Assert.True(EntityDiagramGeometry.Endpoint(model, "empty").Bounds.Width > 0);
        Assert.Equal(2, EntityDiagramGeometry.Route(model, model.Edges[0]).Count);
        var reopened = _adapter.Parse(_adapter.Write(model));
        Assert.Equal(model.Subgraphs.Select(g => (g.Id, g.Title, g.ParentId, g.Direction)),
            reopened.Subgraphs.Select(g => (g.Id, g.Title, g.ParentId, g.Direction)));
    }

    [Fact]
    public void StylesUseErGrammarAndResolveListsDefaultAndInlinePrecedence()
    {
        const string source = """
            erDiagram
            A:::blue ||--|| B : relation
            C
            classDef default fill:#ffffff,stroke:#111111
            classDef blue,other color:#123456
            class A,B blue,other
            style A,B fill:#abcdef
            """;
        var model = _adapter.Parse(source);
        Assert.Equal(3, model.Nodes.Count);
        Assert.Equal("#abcdef", model.Node("A")!.Entity!.ResolvedStyle["fill"]);
        Assert.Equal("#abcdef", model.Node("B")!.Entity!.ResolvedStyle["fill"]);
        Assert.Equal("#123456", model.Node("B")!.Entity!.ResolvedStyle["color"]);
        Assert.Equal("#ffffff", model.Node("C")!.Entity!.ResolvedStyle["fill"]);
        Assert.Equal("ABCDEF", EntityDiagramStyle.Resolve(model.Node("A")!).Fill!.Value.RgbHex);
        var reopened = _adapter.Parse(_adapter.Write(model));
        Assert.Equal(model.Node("B")!.Entity!.ResolvedStyle, reopened.Node("B")!.Entity!.ResolvedStyle);
    }

    [Fact]
    public void MarkdownAndUnicodeNamesHaveSharedDisplayRuns()
    {
        var model = _adapter.Parse("erDiagram\n\"This **is** _Markdown_\"\n\"Customer \u2764\"\n");
        var content = EntityDiagramText.Content(model.Nodes[0]);
        Assert.Equal("This is Markdown", content.Name);
        Assert.Contains(content.NameRuns, run => run.Text == "is" && run.Bold);
        Assert.Contains(content.NameRuns, run => run.Text == "Markdown" && run.Italic);
        Assert.Equal("LINE_ITEM", string.Concat(EntityDiagramText.NameRuns("LINE_ITEM").Select(r => r.Text)));
        Assert.Equal(model.Nodes.Select(n => n.Id), _adapter.Parse(_adapter.Write(model)).Nodes.Select(n => n.Id));
    }

    [Fact]
    public void EveryCardinalityHasDistinctNativeMarkerGeometry()
    {
        var signatures = Enum.GetValues<EntityCardinality>().Select(value =>
        {
            var marker = EntityDiagramGeometry.Marker(value, new(0, 0), new(100, 0));
            Assert.Equal(24, marker.Length);
            return string.Join(";", marker.Lines) + string.Join(";", marker.Circles);
        });
        Assert.Equal(4, signatures.Distinct().Count());
    }

    [Fact]
    public void SelfAndParallelRelationshipsHaveNondegenerateRoutes()
    {
        var model = _adapter.Parse("erDiagram\nA ||--o{ A : parent\nA ||--|| B : first\nA }o..o{ B : second\n");
        model.Node("B")!.X = 500;
        Assert.True(EntityDiagramGeometry.Route(model, model.Edges[0]).Count > 2);
        Assert.Equal(2, EntityDiagramGeometry.Route(model, model.Edges[1]).Count);
        Assert.Equal(3, EntityDiagramGeometry.Route(model, model.Edges[2]).Count);
    }

    [Fact]
    public void NativeHostsCanMeasureEachColumnInsteadOfGuessingGlyphWidths()
    {
        var node = _adapter.Parse("erDiagram\nA {\nWideType field PK \"Wide comment\"\n}\n").Nodes.Single();
        var columns = EntityDiagramGeometry.Columns(node, 14, text => text switch
        {
            "WideType" => 180,
            "field" => 60,
            "PK" => 20,
            "Wide comment" => 240,
            _ => 0,
        });
        Assert.Equal(194, columns[0]);
        Assert.Equal(254, columns[3]);
        Assert.Throws<ArgumentException>(() => EntityDiagramGeometry.Columns(node, 14, _ => double.NaN));
    }
}
