using Merpeeps.Core.Model;
using Merpeeps.Core.Parsing;

namespace Merpeeps.Core.Layout;

public static class BoardPacketLayout
{
    public static DrawingScene Create(DiagramModel model, DrawingTextMeasure? measure = null, string? themeOverride = null)
    {
        if (model.Packet is { } packet)
        {
            PacketDiagramAdapter.Validate(packet);
            var b = new DataDiagramLayouts.Builder(measure, packet.ThemeVariables, packet.GlobalConfiguration, themeOverride ?? model.Theme);
            var bitWidth = Number(packet, "bitWidth", 32); var rowHeight = Number(packet, "rowHeight", 32);
            var bitsPerRow = (int)Number(packet, "bitsPerRow", 32); var paddingX = Number(packet, "paddingX", 1); var paddingY = Number(packet, "paddingY", 10);
            var showBits = packet.Configuration["showBits"]?.GetValue<bool>() ?? true;
            if (showBits) paddingY += 10;
            var bottom = 0d;
            foreach (var field in packet.Resolve())
            {
                var start = field.Start;
                while (start <= field.End)
                {
                    var row = start / bitsPerRow;
                    var end = Math.Min(field.End, checked((row + 1) * bitsPerRow - 1));
                    var x = start % bitsPerRow * bitWidth;
                    var y = row * (rowHeight + paddingY) + paddingY;
                    var rect = new DiagramRect(x, y, (end - start + 1) * bitWidth - paddingX, rowHeight);
                    b.Box(field.Field.Id + "-" + row, "Node", field.Field.Id, rect, b.Tint(b.Scale(0)), b.Ink);
                    var text = b.MeasureWrapped(field.Field.Label, 12, Math.Max(1, rect.Width - 6));
                    b.Text(field.Field.Id + "-label-" + row, "Node", field.Field.Id, field.Field.Label, 12, x + (rect.Width - text.Width) / 2, y + (rowHeight - text.Height) / 2, Math.Max(1, rect.Width - 6));
                    if (showBits)
                    {
                        b.Text(field.Field.Id + "-start-" + row, "Node", field.Field.Id, start.ToString(), 10, x, y - 14, 70);
                        if (start != end) b.Text(field.Field.Id + "-end-" + row, "Node", field.Field.Id, end.ToString(), 10, x + rect.Width - 25, y - 14, 70);
                    }
                    b.Hit("Node", field.Field.Id, rect);
                    start = end + 1; bottom = Math.Max(bottom, y + rowHeight);
                }
            }
            if (packet.Title.Length > 0) b.Text("title", "Diagram", "", packet.Title, 18, 20, bottom + 20, bitsPerRow * bitWidth - 40, true);
            return b.Finish();
        }
        if (model.Kanban is { } board)
        {
            KanbanDiagramAdapter.Validate(board);
            var b = new DataDiagramLayouts.Builder(measure, board.ThemeVariables, board.GlobalConfiguration, themeOverride ?? model.Theme);
            var x = 20d;
            for (var index = 0; index < board.Columns.Count; index++)
            {
                var column = board.Columns[index]; var y = 20d;
                var title = b.MeasureWrapped(column.Label, 18, 220, true, markdown: true);
                b.Box(column.Id, "Section", column.Id, new(x, y, 250, title.Height + 22), b.Scale(index), b.Ink);
                b.Text(column.Id + "-label", "Section", column.Id, column.Label, 18, x + 15, y + 10, 220, true, b.ScaleLabel(index, b.Scale(index)), markdown: true);
                b.Hit("Section", column.Id, new(x, y, 250, title.Height + 22));
                y += title.Height + 38;
                foreach (var card in column.Cards)
                {
                    var text = b.MeasureWrapped(card.Display, 14, 210, markdown: true);
                    var details = new[] { card.Metadata["assigned"]?.ToString(), card.Metadata["ticket"]?.ToString(), card.Metadata["priority"]?.ToString() }
                        .Where(s => !string.IsNullOrEmpty(s)).ToArray();
                    var height = text.Height + 24 + details.Length * 20;
                    var rect = new DiagramRect(x + 5, y, 240, height);
                    b.Box(card.Id, "Node", card.Id, rect, b.Tint(b.Scale(index)), b.Ink);
                    b.Text(card.Id + "-text", "Node", card.Id, card.Display, 14, x + 20, y + 12, 210, markdown: true);
                    for (var i = 0; i < details.Length; i++) b.Text(card.Id + "-meta-" + i, "Node", card.Id, details[i]!, 12, x + 20, y + text.Height + 18 + i * 20, 210);
                    b.Hit("Node", card.Id, rect, column.Id); y += height + 14;
                }
                x += 274;
            }
            return b.Finish();
        }
        throw new ArgumentException("Choose a Packet or Kanban document.");
    }
    private static double Number(PacketDocument packet, string key, double fallback) =>
        packet.Configuration[key] is { } value ? ChartConfiguration.ParseNumber(ChartConfiguration.Raw(value)) : fallback;
}
