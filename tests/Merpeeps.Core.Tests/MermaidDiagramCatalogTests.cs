using Merpeeps.Core.Editing;
using Merpeeps.Core.Model;
using Xunit;

namespace Merpeeps.Core.Tests;

public class MermaidDiagramCatalogTests
{
    [Fact]
    public void CatalogCoversEveryFrozenDocumentationTopic()
    {
        string[] topics =
        [
            "agentflow", "architecture", "block", "c4", "classDiagram", "cynefin",
            "entityRelationshipDiagram", "eventmodeling", "flowchart", "gantt",
            "gitgraph", "ishikawa", "kanban", "mindmap", "packet", "pie",
            "quadrantChart", "radar", "railroad", "requirementDiagram", "sankey",
            "sequenceDiagram", "stateDiagram", "swimlanes", "timeline", "treeView",
            "treemap", "usecase", "userJourney", "venn", "wardley", "xyChart", "zenuml",
        ];

        Assert.Equal("12.0.0", MermaidDiagramCatalog.MermaidVersion);
        var families = MermaidDiagramCatalog.Families;
        Assert.Equal(topics.Length, families.Count);
        Assert.Equal(families.Count, families.Select(f => f.Id).Distinct().Count());
        const string prefix = "https://github.com/mermaid-js/mermaid/blob/mermaid%4012.0.0/packages/mermaid/src/docs/syntax/";
        Assert.Equal(
            topics.Select(t => prefix + t + ".md").Order(),
            families.Select(f => f.DocumentationUrl).Order());
        Assert.All(families, family =>
        {
            Assert.False(string.IsNullOrWhiteSpace(family.DisplayName));
            Assert.NotEmpty(family.Dialects);
            Assert.Equal(family.Dialects[0].Directive, family.Directive);
            Assert.Same(family, MermaidDiagramCatalog.GetFamily(family.Id));
        });
        var dialects = families.SelectMany(f => f.Dialects).ToArray();
        Assert.Equal(dialects.Length, dialects.Select(d => d.Directive).Distinct().Count());
    }

    [Theory]
    [InlineData("c4", "C4Context,C4Container,C4Component,C4Dynamic,C4Deployment")]
    [InlineData("railroad", "railroad-ebnf-beta,railroad-abnf-beta,railroad-peg-beta,railroad-beta")]
    [InlineData("flowchart", "flowchart,graph,flowchart-elk")]
    [InlineData("state", "stateDiagram-v2,stateDiagram")]
    [InlineData("class", "classDiagram,classDiagram-v2")]
    [InlineData("xy", "xychart,xychart-beta")]
    [InlineData("sankey", "sankey,sankey-beta")]
    [InlineData("packet", "packet,packet-beta")]
    [InlineData("block", "block,block-beta")]
    [InlineData("usecase", "usecase-beta")]
    [InlineData("agentflow", "agentflow-beta")]
    [InlineData("treeview", "treeView-beta")]
    public void RetainsDocumentedVariantsAndExactDirectiveSpelling(string familyId, string directives)
    {
        var family = MermaidDiagramCatalog.GetFamily(familyId);
        Assert.Equal(directives.Split(','), family.Dialects.Select(d => d.Directive));
    }

    [Fact]
    public void SharedCatalogCannotBeMutatedByConsumers()
    {
        var families = Assert.IsAssignableFrom<IList<MermaidDiagramFamily>>(MermaidDiagramCatalog.Families);
        Assert.Throws<NotSupportedException>(() => families.RemoveAt(0));
        var dialects = Assert.IsAssignableFrom<IList<MermaidDiagramDialect>>(families[0].Dialects);
        Assert.Throws<NotSupportedException>(() => dialects.Clear());
    }

    [Theory]
    [InlineData("flowchart", null, "flowchart TD\n")]
    [InlineData("flowchart", "graph", "graph TD\n")]
    [InlineData("flowchart", "flowchart-elk", "flowchart-elk TD\n")]
    [InlineData("state", null, "stateDiagram-v2\n")]
    [InlineData("state", "stateDiagram", "stateDiagram\n")]
    [InlineData("class", null, "classDiagram\n")]
    [InlineData("class", "classDiagram-v2", "classDiagram-v2\n")]
    [InlineData("er", null, "erDiagram\n")]
    [InlineData("sequence", null, "sequenceDiagram\n")]
    public void EmptyNativeDraftContainsNoExampleContent(string familyId, string? dialectId, string source)
    {
        var draft = MermaidDiagramCatalog.CreateEmpty(familyId, dialectId);
        Assert.Equal(source, draft.Source);
        Assert.Empty(draft.Model.Nodes);
        Assert.Empty(draft.Model.Edges);
        Assert.Empty(draft.Model.Subgraphs);
        Assert.True(draft.Capabilities.CanEdit);
        Assert.Equal(MerpeepsDocument.GetCapabilities(source), draft.Capabilities);
        Assert.Empty(MerpeepsDocument.Load(MerpeepsDocument.Save(source, draft.Model)).Nodes);
    }

    [Theory]
    [InlineData("flowchart")]
    [InlineData("state")]
    public void EmptyDraftSupportsRealAuthoringSaveReopenAndUndo(string familyId)
    {
        var draft = MermaidDiagramCatalog.CreateEmpty(familyId);
        var history = new EditHistory(draft.Model);
        history.Apply(new AddNodeCommand(new DiagramNode { Id = "request", Label = "Request" }));
        history.Apply(new AddNodeCommand(new DiagramNode { Id = "review", Label = "Review" }));
        history.Apply(new AddEdgeCommand(new DiagramEdge { From = "request", To = "review" }));

        var saved = MerpeepsDocument.Save(draft.Source, draft.Model);
        var reopened = MerpeepsDocument.Load(saved);
        Assert.Equal(new[] { "request", "review" }, reopened.Nodes.Select(n => n.Id));
        Assert.Single(reopened.Edges);
        history.Undo();
        Assert.Empty(draft.Model.Edges);
        history.Undo();
        history.Undo();
        Assert.Empty(draft.Model.Nodes);
        history.Redo();
        Assert.Equal("request", Assert.Single(draft.Model.Nodes).Id);
    }

    [Fact]
    public void EveryCatalogDialectCreatesAnIndependentEmptyTypedDraft()
    {
        Assert.Equal(33, MermaidDiagramCatalog.Families.Count);
        foreach (var family in MermaidDiagramCatalog.Families)
        {
            foreach (var dialect in family.Dialects)
            {
                var first = MermaidDiagramCatalog.CreateEmpty(family.Id, dialect.Id);
                var second = MermaidDiagramCatalog.CreateEmpty(family.Id, dialect.Id);
                Assert.Equal(dialect.Directive, first.Capabilities.Kind);
                Assert.True(first.Capabilities.CanEdit);
                Assert.Empty(first.Model.Nodes);
                Assert.Empty(first.Model.Edges);
                Assert.NotSame(first.Model, second.Model);
                var saved = MerpeepsDocument.Save(first.Source, first.Model);
                Assert.Equal(dialect.Directive, MerpeepsDocument.GetCapabilities(saved).Kind);
                Assert.Empty(MerpeepsDocument.Load(saved).Nodes);
            }
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("unknown")]
    public void UnknownFamilyIsAnExplicitInputError(string? familyId)
    {
        Assert.ThrowsAny<ArgumentException>(() => MermaidDiagramCatalog.GetFamily(familyId!));
        Assert.ThrowsAny<ArgumentException>(() => MermaidDiagramCatalog.CreateEmpty(familyId!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("sequenceDiagram")]
    [InlineData("stateDiagram-v2")]
    public void WrongDialectCannotCreateAnUnrelatedFamily(string dialectId) =>
        Assert.Throws<ArgumentException>(() => MermaidDiagramCatalog.CreateEmpty("flowchart", dialectId));

    [Fact]
    public void EachDraftOwnsItsModel()
    {
        var first = MermaidDiagramCatalog.CreateEmpty("flowchart");
        var second = MermaidDiagramCatalog.CreateEmpty("flowchart");
        first.Model.Nodes.Add(new DiagramNode { Id = "first" });
        Assert.NotSame(first.Model, second.Model);
        Assert.Empty(second.Model.Nodes);
    }
}
