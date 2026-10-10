using System.Text.Json.Nodes;
using Merpeeps.Core.Model;
using Merpeeps.Core.Parsing;

namespace Merpeeps.Core.Editing;

public sealed class BoardPacketEditCommand : IEditCommand
{
    private readonly Action<PacketDocument>? _packet;
    private readonly Action<KanbanDocument>? _board;
    private PacketDocument? _oldPacket;
    private KanbanDocument? _oldBoard;
    public string Description { get; }
    public DataDiagramTarget? Result { get; }
    internal BoardPacketEditCommand(string name, DataDiagramTarget? result, Action<PacketDocument> change) => (Description, Result, _packet) = (name, result, change);
    internal BoardPacketEditCommand(string name, DataDiagramTarget? result, Action<KanbanDocument> change) => (Description, Result, _board) = (name, result, change);
    public void Do(DiagramModel model)
    {
        if (_packet is not null)
        {
            var old = model.Packet ?? throw new ArgumentException("Choose a packet diagram.");
            var next = old.Clone(); _packet(next); PacketDiagramAdapter.Validate(next);
            _oldPacket = old; model.Packet = next;
        }
        else
        {
            var old = model.Kanban ?? throw new ArgumentException("Choose a Kanban diagram.");
            var next = old.Clone(); _board!(next); KanbanDiagramAdapter.Validate(next);
            _oldBoard = old; model.Kanban = next;
        }
    }
    public void Undo(DiagramModel model)
    {
        if (_packet is not null) model.Packet = _oldPacket ?? throw new InvalidOperationException("Not applied.");
        else model.Kanban = _oldBoard ?? throw new InvalidOperationException("Not applied.");
    }
}
public static class PacketEditing
{
    public static BoardPacketEditCommand Add(PacketField field) => new("Add packet field", new(DataDiagramRole.Node, field.Id), (PacketDocument p) => p.Fields.Add(field));
    public static BoardPacketEditCommand Update(PacketField field) => new("Edit packet field", new(DataDiagramRole.Node, field.Id), (PacketDocument p) =>
        p.Fields[Index(p, field.Id)] = field);
    public static BoardPacketEditCommand UpdateAndReflow(PacketField field) => new("Edit field and reflow following ranges", new(DataDiagramRole.Node, field.Id), (PacketDocument p) =>
    {
        p.Fields[Index(p, field.Id)] = field; Reflow(p);
    });
    public static BoardPacketEditCommand Remove(string id) => new("Remove field and reflow packet", null, (PacketDocument p) =>
    {
        p.Fields.RemoveAt(Index(p, id)); Reflow(p);
    });
    public static BoardPacketEditCommand Move(string id, int target) => new("Move field and reflow packet", new(DataDiagramRole.Node, id), (PacketDocument p) =>
    {
        if (target < 0 || target >= p.Fields.Count) throw new ArgumentOutOfRangeException(nameof(target));
        var index = Index(p, id); var field = p.Fields[index]; p.Fields.RemoveAt(index); p.Fields.Insert(target, field); Reflow(p);
    });
    public static BoardPacketEditCommand SetTitle(string title) => new("Rename packet", null, (PacketDocument p) => p.Title = title);
    public static BoardPacketEditCommand SetOption(string key, JsonNode? value)
    {
        var copy = value?.DeepClone();
        return new("Edit packet option", null, (PacketDocument p) => { if (copy is null) p.Configuration.Remove(key); else p.Configuration[key] = copy.DeepClone(); });
    }
    private static int Index(PacketDocument p, string id) => p.Fields.FindIndex(f => f.Id == id) is var index && index >= 0 ? index : throw new ArgumentException("Packet field does not exist.");
    private static void Reflow(PacketDocument p)
    {
        var start = 0;
        for (var i = 0; i < p.Fields.Count; i++)
        {
            if (p.Fields[i].ExplicitStart is not null) p.Fields[i] = p.Fields[i] with { ExplicitStart = start };
            start = checked(start + p.Fields[i].Bits);
        }
    }
}
public static class KanbanEditing
{
    public static BoardPacketEditCommand AddColumn(KanbanColumn column) => new("Add board column", new(DataDiagramRole.Section, column.Id), (KanbanDocument board) => board.Columns.Add(column with { Cards = column.Cards.Select(c => c.DeepCopy()).ToArray() }));
    public static BoardPacketEditCommand UpdateColumn(string id, string label, string? sourceId) => new("Edit board column", new(DataDiagramRole.Section, id), (KanbanDocument board) =>
    {
        var index = Column(board, id); board.Columns[index] = board.Columns[index] with { Label = label, SourceId = sourceId };
    });
    public static BoardPacketEditCommand MoveColumn(string id, int target) => new("Reorder board column", new(DataDiagramRole.Section, id), (KanbanDocument board) =>
    {
        if (target < 0 || target >= board.Columns.Count) throw new ArgumentOutOfRangeException(nameof(target));
        var index = Column(board, id); var column = board.Columns[index]; board.Columns.RemoveAt(index); board.Columns.Insert(target, column);
    });
    public static BoardPacketEditCommand RemoveColumn(string id) => new("Delete board column and cards", null, (KanbanDocument board) => board.Columns.RemoveAt(Column(board, id)));
    public static BoardPacketEditCommand AddCard(string columnId, KanbanCard card)
    {
        var copy = card.DeepCopy();
        return new("Add board card", new(DataDiagramRole.Node, card.Id, columnId), (KanbanDocument board) =>
        {
            var index = Column(board, columnId); board.Columns[index] = board.Columns[index] with { Cards = board.Columns[index].Cards.Append(copy.DeepCopy()).ToArray() };
        });
    }
    public static BoardPacketEditCommand UpdateCard(string columnId, KanbanCard card)
    {
        var copy = card.DeepCopy();
        return new("Edit board card", new(DataDiagramRole.Node, card.Id, columnId), (KanbanDocument board) =>
        {
            var index = Column(board, columnId); var cards = board.Columns[index].Cards.ToList();
            var at = cards.FindIndex(c => c.Id == card.Id); if (at < 0) throw new ArgumentException("Card does not exist.");
            cards[at] = copy.DeepCopy(); board.Columns[index] = board.Columns[index] with { Cards = cards };
        });
    }
    public static BoardPacketEditCommand MoveCard(string fromId, string toId, string cardId, int index) => new("Move board card", new(DataDiagramRole.Node, cardId, toId), (KanbanDocument board) =>
    {
        var from = Column(board, fromId); var to = Column(board, toId);
        var old = board.Columns[from].Cards.ToList(); var at = old.FindIndex(c => c.Id == cardId);
        if (at < 0) throw new ArgumentException("Card does not exist.");
        var card = old[at]; old.RemoveAt(at); board.Columns[from] = board.Columns[from] with { Cards = old };
        var next = from == to ? old : board.Columns[to].Cards.ToList();
        if (index < 0 || index > next.Count) throw new ArgumentOutOfRangeException(nameof(index));
        next.Insert(index, card); board.Columns[to] = board.Columns[to] with { Cards = next };
    });
    public static BoardPacketEditCommand RemoveCard(string columnId, string cardId) => new("Delete board card", null, (KanbanDocument board) =>
    {
        var index = Column(board, columnId); var cards = board.Columns[index].Cards.ToList();
        var at = cards.FindIndex(c => c.Id == cardId); if (at < 0) throw new ArgumentException("Card does not exist.");
        cards.RemoveAt(at); board.Columns[index] = board.Columns[index] with { Cards = cards };
    });
    public static BoardPacketEditCommand SetTicketBaseUrl(string? url) => new("Edit ticket link template", null, (KanbanDocument board) =>
    {
        if (url is null) board.Configuration.Remove("ticketBaseUrl"); else board.Configuration["ticketBaseUrl"] = url;
    });
    private static int Column(KanbanDocument board, string id) => board.Columns.FindIndex(c => c.Id == id) is var index && index >= 0 ? index : throw new ArgumentException("Column does not exist.");
}
