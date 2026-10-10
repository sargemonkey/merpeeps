using Merpeeps.Core.Model;
using Merpeeps.Core.Parsing;
using Merpeeps.Core.Editing;
using Merpeeps.Core.Layout;
using Xunit;

namespace Merpeeps.Core.Tests;

public class BlockDiagramTests
{
    [Fact]
    public void OrderedNestedGridKeepsSpacesAndColumnSpans()
    {
        const string source = """
            block
            columns 3
            a["A label"] b:2
            block:group:2
              columns 2
              h i
              space j
            end
            g
            group --> g
            h -- "owns" --> g
            """;
        var adapter = new BlockDiagramAdapter();
        var model = adapter.Parse(source);
        Assert.Empty(model.Nodes);
        var document = model.Blocks!;
        Assert.Equal(3, document.Columns);
        Assert.Equal(4, document.Items.Count);
        Assert.Equal(2, document.Items[1].Span);
        var group = document.Items[2];
        Assert.Equal(BlockItemKind.Composite, group.Kind);
        Assert.Equal(2, group.Columns);
        Assert.Equal(BlockItemKind.Space, group.Children[2].Kind);
        Assert.Equal(2, document.Connections.Count);
        Assert.Equal("owns", document.Connections[1].Label);
        Assert.Equal(adapter.Write(model), adapter.Write(adapter.Parse(adapter.Write(model))));
    }

    [Fact]
    public void LaterReferenceUpdatesTheShapeWithoutAddingAnotherPlacement()
    {
        var model = new BlockDiagramAdapter().Parse("block\nA space B\nA(\"Start\") --> B(\"Stop\")\n");
        Assert.Equal(3, model.Blocks!.Items.Count);
        Assert.Equal("Start", model.Blocks.Items[0].Label);
        Assert.Equal(NodeShape.RoundedRect, model.Blocks.Items[0].Shape);
        Assert.Equal("Stop", model.Blocks.Items[2].Label);
    }

    [Theory]
    [InlineData("A((\"circle\"))", NodeShape.Circle)]
    [InlineData("A(((\"double\")))", NodeShape.DoubleCircle)]
    [InlineData("A[/\"bottom\"\\]", NodeShape.Trapezoid)]
    [InlineData("A[\\\"top\"/]", NodeShape.TrapezoidAlt)]
    [InlineData("A[(\"store\")]", NodeShape.Cylinder)]
    [InlineData("A[[\"subroutine\"]]", NodeShape.Subroutine)]
    public void ShapeSyntaxUsesItsOwnGridPlacement(string statement, NodeShape shape)
    {
        var document = new BlockDiagramAdapter().Parse("block\n" + statement + "\n").Blocks!;
        Assert.Equal(shape, document.Items.Single().Shape);
    }

    [Fact]
    public void BlockArrowsAndConnectionEndsAreSeparate()
    {
        var model = new BlockDiagramAdapter().Parse("block\nA<[\"Directions\"]>(x,down) space B\nA o--x B\n");
        Assert.Equal(BlockArrowDirections.Left | BlockArrowDirections.Right | BlockArrowDirections.Down, model.Blocks!.Items[0].Arrows);
        Assert.Equal(BlockLinkEnd.Circle, model.Blocks.Connections[0].FromEnd);
        Assert.Equal(BlockLinkEnd.Cross, model.Blocks.Connections[0].ToEnd);
    }

    [Fact]
    public void MeasuredGridSpansAndNestedCellsProduceRealPlacements()
    {
        var model = new BlockDiagramAdapter().Parse("block\ncolumns 3\nA[\"Wide label\"] B:2\nblock:group:2\ncolumns 1\nC[\"Nested\"]\nend\nspace\nstyle A stroke-width:4px,stroke-dasharray:5 5\n");
        var layout = BlockDiagramLayout.Create(model, (text, font) => new(text.Length * font.Size, font.Size * 1.4));
        var items = model.Blocks!.Items;
        Assert.Equal(0, layout.Placements[items[0].Id].Column);
        Assert.Equal(1, layout.Placements[items[1].Id].Column);
        Assert.Equal(2, layout.Placements[items[1].Id].Span);
        Assert.Equal(1, layout.Placements[items[2].Id].Row);
        var parent = layout.Placements[items[2].Id].Bounds;
        var child = layout.Placements[items[2].Children[0].Id].Bounds;
        Assert.True(child.X >= parent.X && child.Y >= parent.Y);
        Assert.True(child.X + child.Width <= parent.X + parent.Width);
        var figure = layout.Scene.Figures.Single(f => f.Id == items[0].Id);
        Assert.Equal(4, figure.StrokeWidth);
        Assert.Equal(new[] { 5d, 5d }, figure.DashPattern);
        Assert.DoesNotContain(layout.Scene.Figures, f => f.Owner.Id == items[3].Id);
    }

    [Fact]
    public void GridMoveAndColumnRefusalAreOneReversibleModelEdit()
    {
        var model = new BlockDiagramAdapter().Parse("block\ncolumns 3\nA B:2 C\n");
        var before = model.Blocks!;
        var history = new EditHistory(model);
        Assert.Throws<DataDiagramNotEditableException>(() => history.Apply(BlockDiagramEditing.SetColumns(null, 1)));
        Assert.Same(before, model.Blocks);
        history.Apply(BlockDiagramEditing.Move(before.Items[2].Id, null, 0));
        Assert.Equal("C", model.Blocks!.Items[0].SourceId);
        history.Undo(); Assert.Same(before, model.Blocks);
    }

    [Fact]
    public void GridIdentityAndSourceSurviveRealSaveReopenAfterReparenting()
    {
        const string source = "block\nA space\nblock:group\nB\nend\nA --> B\n";
        var model = MerpeepsDocument.Load(source);
        var id = model.Blocks!.Items[0].Id; var group = model.Blocks.Items[2].Id;
        new EditHistory(model).Apply(BlockDiagramEditing.Move(id, group, 1));
        var saved = MerpeepsDocument.Save(source, model);
        var reopened = MerpeepsDocument.Load(saved);
        Assert.Equal(id, reopened.Blocks!.Items[1].Children[1].Id);
        Assert.Equal(id, reopened.Blocks.Connections[0].From);
        Assert.Empty(reopened.Nodes);
    }

    [Fact]
    public void InvalidClassNameCannotCreateUnparseableSource()
    {
        var model = MerpeepsDocument.Load("block\nA\n"); var before = model.Blocks;
        Assert.Throws<DataDiagramNotEditableException>(() => new EditHistory(model).Apply(
            BlockDiagramEditing.SetStyleClass("", new System.Text.Json.Nodes.JsonObject { ["fill"] = "red" })));
        Assert.Same(before, model.Blocks);
    }
}
