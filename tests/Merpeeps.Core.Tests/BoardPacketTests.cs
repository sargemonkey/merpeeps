using Merpeeps.Core.Editing;
using Merpeeps.Core.Export;
using Merpeeps.Core.Layout;
using Merpeeps.Core.Model;
using Xunit;

namespace Merpeeps.Core.Tests;

public class BoardPacketTests
{
    [Theory]
    [InlineData("packet")]
    [InlineData("packet-beta")]
    public void PacketRelativeFieldsResolveContiguouslyAndSplitRows(string directive)
    {
        var source = directive + "\n+16: \"Source\"\n16-31: \"Destination\"\n+40: \"Payload\"\n";
        var model = MerpeepsDocument.Load(source);
        Assert.Empty(model.Nodes); Assert.Empty(model.Edges);
        var fields = model.Packet!.Resolve();
        Assert.Equal(new[] { 0, 16, 32 }, fields.Select(f => f.Start));
        Assert.Equal(71, fields[2].End);
        var scene = BoardPacketLayout.Create(model);
        Assert.Equal(2, scene.Figures.Count(f => f.Owner.Id == fields[2].Field.Id && f.Kind == DrawingFigureKind.Rectangle));
        var saved = MerpeepsDocument.Save(source, model);
        Assert.Equal(fields[2].Field.Id, MerpeepsDocument.Load(saved).Packet!.Fields[2].Id);
        Assert.Contains("Payload", SvgExporter.ToSvg(model));
    }
    [Fact]
    public void PacketInvalidResizeDoesNotPartiallyChangeDataAndExplicitReflowIsUndoable()
    {
        var model = MerpeepsDocument.Load("packet\n0-7: \"First\"\n8-15: \"Second\"\n");
        var field = model.Packet!.Fields[0];
        var history = new EditHistory(model);
        Assert.Throws<ArgumentException>(() => history.Apply(PacketEditing.Update(field with { Bits = 16 })));
        Assert.Equal(8, model.Packet.Fields[0].Bits);
        history.Apply(PacketEditing.Move(field.Id, 1));
        Assert.Equal(0, model.Packet!.Resolve()[0].Start);
        Assert.Equal("First", model.Packet.Fields[1].Label);
        history.Undo(); Assert.Equal("First", model.Packet!.Fields[0].Label);
    }
    [Fact]
    public void KanbanKeepsDuplicateCardSourceIdsAsDistinctOrderedCards()
    {
        const string source = """
            kanban
            todo[Todo]
              task[First]@{assigned: 'Me', ticket: X-1, priority: 'High', future: keep}
            done[Done]
              task[Second]
            """;
        var model = MerpeepsDocument.Load(source);
        var board = model.Kanban!;
        Assert.NotEqual(board.Columns[0].Cards[0].Id, board.Columns[1].Cards[0].Id);
        Assert.Equal("task", board.Columns[0].Cards[0].SourceId);
        Assert.Equal("task", board.Columns[1].Cards[0].SourceId);
        Assert.Equal("keep", board.Columns[0].Cards[0].Metadata["future"]!.GetValue<string>());
        var history = new EditHistory(model);
        var card = board.Columns[0].Cards[0];
        history.Apply(KanbanEditing.MoveCard(board.Columns[0].Id, board.Columns[1].Id, card.Id, 1));
        Assert.Empty(model.Kanban!.Columns[0].Cards);
        var saved = MerpeepsDocument.Save(source, model);
        Assert.Equal(card.Id, MerpeepsDocument.Load(saved).Kanban!.Columns[1].Cards[1].Id);
        Assert.Contains("First", SvgExporter.ToSvg(model));
        history.Undo(); Assert.Single(model.Kanban!.Columns[0].Cards);
    }
    [Fact]
    public void ImplicitBoardNamesDoNotRequireInventedSourceIds()
    {
        var model = MerpeepsDocument.Load("kanban\nTodo\n  [Unnumbered task]\n[Done]\n");
        Assert.Null(model.Kanban!.Columns[1].SourceId);
        Assert.Null(model.Kanban.Columns[0].Cards[0].SourceId);
        Assert.Equal("Unnumbered task", model.Kanban.Columns[0].Cards[0].Label);
    }
}
