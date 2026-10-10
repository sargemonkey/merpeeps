using System.Globalization;
using Merpeeps.Core.Editing;
using Merpeeps.Core.Model;

namespace Merpeeps.App;

internal sealed partial class DiagramInspector
{
    private void ShowBoardPacketSelection()
    {
        if (Model().Packet is { } packet)
        {
            if (_canvas.SelectedDataTarget is { Role: DataDiagramRole.Node } selected) { Open(new(TaskKind.PacketField, selected), false); return; }
            Heading(packet.Title.Length == 0 ? "Packet" : packet.Title);
            Button("Add field", AddClass);
            foreach (var item in packet.Resolve()) Button($"{item.Start}–{item.End}: {item.Field.Label}", () => _canvas.SelectData(new(DataDiagramRole.Node, item.Field.Id)));
            Button("Packet options…", DisplayOptions);
        }
        else if (Model().Kanban is { } board)
        {
            if (_canvas.SelectedDataTarget is { } selected)
            {
                if (selected.Role == DataDiagramRole.Node) { Open(new(TaskKind.KanbanCard, selected), false); return; }
                if (selected.Role == DataDiagramRole.Section)
                {
                    var column = board.Columns.Single(c => c.Id == selected.Id);
                    Open(new(TaskKind.KanbanColumn, selected), false);
                    Button("Add card", () => Open(new(TaskKind.KanbanCard, Model()) { Values = { ["$column"] = column.Id } }));
                    foreach (var card in column.Cards) Button(card.Display, () => _canvas.SelectData(new(DataDiagramRole.Node, card.Id, column.Id)));
                    Button("Delete column and cards", () => ApplyBoardPacket(KanbanEditing.RemoveColumn(column.Id), selected));
                    return;
                }
            }
            Heading("Kanban board");
            Button("Add column", AddClass);
            foreach (var column in board.Columns) Button(column.Label, () => _canvas.SelectData(new(DataDiagramRole.Section, column.Id)));
            Button("Board options…", DisplayOptions);
        }
    }
    private bool BuildBoardPacketEditor(Draft draft)
    {
        switch (draft.Task)
        {
            case TaskKind.PacketField: PacketFieldEditor(draft); return true;
            case TaskKind.KanbanColumn: KanbanColumnEditor(draft); return true;
            case TaskKind.KanbanCard: KanbanCardEditor(draft); return true;
            case TaskKind.KanbanMove:
                var target = DataTarget(draft)!;
                var board = Model().Kanban!;
                Heading("Move card");
                ChoiceInput(draft, "column", "Destination column", board.Columns.Select(c => new Choice(c.Id, c.Label)), target.ParentId ?? "");
                Footer("Move card", () =>
                {
                    var destination = Value(draft, "column");
                    ApplyBoardPacket(KanbanEditing.MoveCard(target.ParentId!, destination, target.Id,
                        board.Columns.Single(c => c.Id == destination).Cards.Count - (destination == target.ParentId ? 1 : 0)), draft.Owner);
                });
                return true;
            default: return false;
        }
    }
    private void PacketFieldEditor(Draft draft)
    {
        var packet = Model().Packet!;
        var original = DataTarget(draft) is { } target ? Remember(draft, () => packet.Fields.Single(f => f.Id == target.Id)) : null;
        Heading(original is null ? "Add packet field" : "Edit packet field");
        Input(draft, "label", "Field label", original?.Label ?? "");
        Input(draft, "bits", "Bit count", (original?.Bits ?? 8).ToString(CultureInfo.InvariantCulture));
        Input(draft, "start", "Explicit start (blank uses +bits)", original?.ExplicitStart?.ToString(CultureInfo.InvariantCulture) ?? "");
        if (original is not null) Check(draft, "reflow", "Reflow following explicit starts", false);
        Footer("Apply field", () =>
        {
            var field = new PacketField(original?.Id ?? TemporalEditing.NewId(), Value(draft, "label"), int.Parse(Value(draft, "bits"), CultureInfo.InvariantCulture),
                Optional(draft, "start") is { } start ? int.Parse(start, CultureInfo.InvariantCulture) : null);
            ApplyBoardPacket(original is null ? PacketEditing.Add(field) : draft.Get("reflow", false) ? PacketEditing.UpdateAndReflow(field) : PacketEditing.Update(field), draft.Owner);
        });
        if (original is not null)
        {
            var index = packet.Fields.FindIndex(f => f.Id == original.Id);
            Button("Move earlier and reflow", () => ApplyBoardPacket(PacketEditing.Move(original.Id, index - 1), draft.Owner)).IsEnabled = index > 0;
            Button("Move later and reflow", () => ApplyBoardPacket(PacketEditing.Move(original.Id, index + 1), draft.Owner)).IsEnabled = index + 1 < packet.Fields.Count;
            Button("Delete and reflow", () => ApplyBoardPacket(PacketEditing.Remove(original.Id), draft.Owner));
        }
    }
    private void KanbanColumnEditor(Draft draft)
    {
        var board = Model().Kanban!;
        var original = DataTarget(draft) is { } target ? Remember(draft, () => board.Columns.Single(c => c.Id == target.Id)) : null;
        Heading("Board column");
        Input(draft, "label", "Column label", original?.Label ?? "");
        Input(draft, "sourceId", "Source identity (optional)", original?.SourceId ?? "");
        Footer("Apply column", () => ApplyBoardPacket(original is null
            ? KanbanEditing.AddColumn(new(TemporalEditing.NewId(), Value(draft, "label"), [], Optional(draft, "sourceId")))
            : KanbanEditing.UpdateColumn(original.Id, Value(draft, "label"), Optional(draft, "sourceId")), draft.Owner));
        if (original is not null)
        {
            var index = board.Columns.FindIndex(c => c.Id == original.Id);
            Button("Move column left", () => ApplyBoardPacket(KanbanEditing.MoveColumn(original.Id, index - 1), draft.Owner)).IsEnabled = index > 0;
            Button("Move column right", () => ApplyBoardPacket(KanbanEditing.MoveColumn(original.Id, index + 1), draft.Owner)).IsEnabled = index + 1 < board.Columns.Count;
        }
    }
    private void KanbanCardEditor(Draft draft)
    {
        var target = DataTarget(draft);
        var columnId = target?.ParentId ?? draft.Get("$column", "");
        var board = Model().Kanban!;
        var column = board.Columns.Single(c => c.Id == columnId);
        var original = target is null ? null : Remember(draft, () => column.Cards.Single(c => c.Id == target.Id));
        Heading("Board card");
        Input(draft, "label", "Card label", original?.Display ?? "");
        Input(draft, "sourceId", "Source identity (optional)", original?.SourceId ?? "");
        Input(draft, "assigned", "Assigned to", original?.Metadata["assigned"]?.ToString() ?? "");
        Input(draft, "ticket", "Ticket", original?.Metadata["ticket"]?.ToString() ?? "");
        ChoiceInput(draft, "priority", "Priority", new[] { "", "Very High", "High", "Low", "Very Low" }.Select(p => new Choice(p, p.Length == 0 ? "(none)" : p)), original?.Metadata["priority"]?.ToString() ?? "");
        Footer("Apply card", () =>
        {
            var metadata = original is null ? new System.Text.Json.Nodes.JsonObject() : (System.Text.Json.Nodes.JsonObject)original.Metadata.DeepClone();
            metadata.Remove("label");
            foreach (var key in new[] { "assigned", "ticket", "priority" })
                if (Optional(draft, key) is { } value) metadata[key] = value; else metadata.Remove(key);
            var card = new KanbanCard(original?.Id ?? TemporalEditing.NewId(), Value(draft, "label"), Optional(draft, "sourceId")) { Metadata = metadata };
            ApplyBoardPacket(original is null ? KanbanEditing.AddCard(columnId, card) : KanbanEditing.UpdateCard(columnId, card), draft.Owner);
        });
        if (original is not null)
        {
            Button("Move card…", () =>
            {
                if (draft.Dirty) throw new ArgumentException("Apply or cancel card edits before moving it.");
                Open(new(TaskKind.KanbanMove, draft.Owner));
            });
            Button("Delete card", () => ApplyBoardPacket(KanbanEditing.RemoveCard(columnId, original.Id), draft.Owner));
        }
    }
    private void BoardPacketOptions(Draft draft)
    {
        if (Model().Packet is { } packet)
        {
            Heading("Packet options"); Input(draft, "title", "Packet title", packet.Title);
            Check(draft, "show", "Show bit indices", packet.Configuration["showBits"]?.GetValue<bool>() ?? true);
            foreach (var (key, value) in new[] { ("bitsPerRow", "32"), ("bitWidth", "32"), ("rowHeight", "32") })
                Input(draft, key, key, packet.Configuration[key]?.ToString() ?? value);
            Footer("Apply packet options", () => Apply(new BatchCommand("Edit packet options", [
                PacketEditing.SetTitle(Value(draft, "title")), PacketEditing.SetOption("showBits", System.Text.Json.Nodes.JsonValue.Create(draft.Get("show", true))),
                PacketEditing.SetOption("bitsPerRow", System.Text.Json.Nodes.JsonValue.Create(Numeric(draft, "bitsPerRow"))),
                PacketEditing.SetOption("bitWidth", System.Text.Json.Nodes.JsonValue.Create(Numeric(draft, "bitWidth"))),
                PacketEditing.SetOption("rowHeight", System.Text.Json.Nodes.JsonValue.Create(Numeric(draft, "rowHeight"))),
            ]), draft.Owner, after: () => _canvas.SelectData(null)));
        }
        else
        {
            Heading("Kanban options"); Input(draft, "url", "Ticket URL template (#TICKET#)", Model().Kanban!.Configuration["ticketBaseUrl"]?.ToString() ?? "");
            Text("Ticket links are stored as data; they are not opened automatically.");
            Footer("Apply board options", () => ApplyBoardPacket(KanbanEditing.SetTicketBaseUrl(Optional(draft, "url")), draft.Owner));
        }
    }
    private void ApplyBoardPacket(BoardPacketEditCommand command, object owner) => Apply(command, owner, after: () => _canvas.SelectData(command.Result));
}
