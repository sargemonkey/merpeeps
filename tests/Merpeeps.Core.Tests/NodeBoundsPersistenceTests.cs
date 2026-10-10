using Merpeeps.Core.Editing;
using Merpeeps.Core.Model;
using Xunit;

namespace Merpeeps.Core.Tests;

public class NodeBoundsPersistenceTests
{
    private const string Label = "Review the proposed design and approve changes";
    private const string Source = "flowchart TD\n A[Review the proposed design and approve changes]\n";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DefaultSizedBoundsInFreshSourceStayDefault(bool wasExplicit)
    {
        var model = MerpeepsDocument.Load(Source);
        var node = Assert.Single(model.Nodes);
        node.Width = 160;
        node.Height = 56;
        node.HasExplicitPosition = wasExplicit;

        var saved = MerpeepsDocument.SavePositions(Source, model);
        var reopened = Assert.Single(MerpeepsDocument.Load(saved).Nodes);

        Assert.Equal(160, reopened.Width);
        Assert.Equal(56, reopened.Height);
        Assert.Equal(Label, reopened.Label);
        Assert.Equal(Source, MerpeepsDoc.BodyOf(saved));
        Assert.Equal(wasExplicit, node.HasExplicitPosition);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UndoToDefaultBoundsSurvivesSavingOverALargerSize(bool wasExplicit)
    {
        var draft = MermaidDiagramCatalog.CreateEmpty("flowchart");
        var node = new DiagramNode
        {
            Id = "A", Label = Label, TextColor = "#FF0000",
            Fill = "#FFFFFF", Stroke = "#123456", Icon = "database",
            HasExplicitPosition = wasExplicit,
        };
        new AddNodeCommand(node).Do(draft.Model);
        node.Width = 600;
        node.Height = 120;
        node.HasExplicitPosition = true;
        var enlarged = MerpeepsDocument.Save(draft.Source, draft.Model);
        var enlargedNode = Assert.Single(MerpeepsDocument.Load(enlarged).Nodes);
        Assert.Equal(600, enlargedNode.Width);
        Assert.Equal(120, enlargedNode.Height);

        node.Width = 160;
        node.Height = 56;
        node.HasExplicitPosition = wasExplicit;
        var restored = MerpeepsDocument.SavePositions(enlarged, draft.Model);
        Assert.Equal(160, node.Width);
        Assert.Equal(56, node.Height);
        var reopened = Assert.Single(MerpeepsDocument.Load(restored).Nodes);

        Assert.Equal(160, reopened.Width);
        Assert.Equal(56, reopened.Height);
        Assert.Equal(Label, reopened.Label);
        Assert.Equal(node.Fill, reopened.Fill);
        Assert.Equal(node.Stroke, reopened.Stroke);
        Assert.Equal(node.TextColor, reopened.TextColor);
        Assert.Equal(node.Icon, reopened.Icon);
        Assert.Equal(MerpeepsDoc.BodyOf(enlarged), MerpeepsDoc.BodyOf(restored));
    }

    [Fact]
    public void OrdinarySaveAlsoRetainsDefaultBoundsWhenGeometryIsExplicit()
    {
        var model = MerpeepsDocument.Load(Source);
        var node = Assert.Single(model.Nodes);
        node.Width = 600;
        node.Height = 120;
        node.HasExplicitPosition = true;
        var enlarged = MerpeepsDocument.Save(Source, model);

        node.Width = 160;
        node.Height = 56;
        var saved = MerpeepsDocument.Save(enlarged, model);
        var reopened = Assert.Single(MerpeepsDocument.Load(saved).Nodes);
        Assert.Equal(160, reopened.Width);
        Assert.Equal(56, reopened.Height);
        Assert.Equal(Source, MerpeepsDoc.BodyOf(saved));
    }

    [Theory]
    [InlineData(600, 56)]
    [InlineData(160, 120)]
    public void DefaultDimensionsOverwriteOldMetadataIndependently(double width, double height)
    {
        var model = MerpeepsDocument.Load(Source);
        var node = Assert.Single(model.Nodes);
        node.Width = width;
        node.Height = height;
        var enlarged = MerpeepsDocument.SavePositions(Source, model);
        node.Width = 160;
        node.Height = 56;
        var saved = MerpeepsDocument.SavePositions(enlarged, model);
        var metadata = System.Text.Json.Nodes.JsonNode.Parse(
            MerpeepsDoc.ToJson(MerpeepsDoc.Parse(saved)));
        var bounds = metadata!["layout"]!["nodes"]!["A"]!;

        Assert.NotNull(bounds["w"]);
        Assert.NotNull(bounds["h"]);
        Assert.Equal(160, bounds["w"]!.GetValue<double>());
        Assert.Equal(56, bounds["h"]!.GetValue<double>());
        var reopened = Assert.Single(MerpeepsDocument.Load(saved).Nodes);
        Assert.Equal(160, reopened.Width);
        Assert.Equal(56, reopened.Height);
    }

    [Fact]
    public void OrdinarySaveDoesNotPinUnpositionedGeometry()
    {
        var model = MerpeepsDocument.Load(Source);
        var saved = MerpeepsDocument.Save(Source, model);
        Assert.DoesNotContain("layout:", saved);
        Assert.False(model.AnyExplicitPositions);
    }

    [Theory]
    [InlineData("flowchart LR\nA[One]\n")]
    [InlineData("stateDiagram-v2\nA\n")]
    [InlineData("classDiagram\nclass A\n")]
    [InlineData("erDiagram\nA\n")]
    public void SavingAfterMoveUndoClearsAlreadySavedPinCoordinates(string source)
    {
        var model = MerpeepsDocument.Load(source);
        var node = model.Nodes.Single();
        var before = (node.X, node.Y);
        var history = new EditHistory(model);
        history.Apply(new MoveNodesCommand([(node, node.X, node.Y, node.X + 100, node.Y + 50)]));
        var saved = MerpeepsDocument.Save(source, model);
        history.Undo();
        var reopened = MerpeepsDocument.Load(MerpeepsDocument.Save(saved, model)).Nodes.Single();
        Assert.False(reopened.HasExplicitPosition);
        Assert.Equal(before, (reopened.X, reopened.Y));
    }
}
