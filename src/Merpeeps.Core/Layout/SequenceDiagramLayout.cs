using System.Globalization;
using Merpeeps.Core.Model;
using Merpeeps.Core.Parsing;

namespace Merpeeps.Core.Layout;

public enum SequenceElementKind { Participant, Message, Note, Activation, Fragment, Branch, ParticipantBox }
public enum SequenceFigureKind { Rectangle, Ellipse, Polyline, Polygon }
public readonly record struct SequenceTextSize(double Width, double Height);
public delegate SequenceTextSize SequenceTextMeasure(string text, SequenceFont font);
public sealed record SequenceFigure(
    string Id, string OwnerId, SequenceElementKind ElementKind, SequenceFigureKind Kind,
    DiagramRect Bounds, IReadOnlyList<DiagramPoint> Points, ClassColor? Fill, ClassColor? Stroke,
    double StrokeWidth = 1.4, bool Dashed = false, int Layer = 0);
public sealed record SequenceLabel(
    string Id, string OwnerId, SequenceElementKind ElementKind, string Text, SequenceFont Font,
    ClassColor Color, DiagramRect Bounds, string Alignment = "left");
public sealed record SequenceRegion(string OwnerId, SequenceElementKind Kind, DiagramRect Bounds);
public sealed record SequenceLayout(
    double Width, double Height, IReadOnlyList<SequenceFigure> Figures,
    IReadOnlyList<SequenceLabel> Labels, IReadOnlyList<SequenceRegion> Regions, bool UsesApproximateTextMetrics);

public static class SequenceDiagramLayout
{
    public static SequenceLayout Create(DiagramModel document, SequenceTextMeasure? measure = null, string? themeOverride = null)
    {
        var model = document.Sequence ?? throw new ArgumentException("The document has no sequence semantics.");
        var diagnostics = SequenceValidation.Validate(model);
        if (diagnostics.Count > 0) throw new ArgumentException(string.Join("\n", diagnostics.Select(d => d.Message)));
        var approximate = measure is null;
        measure ??= (text, font) =>
        {
            var lines = text.Split('\n');
            return new(lines.Max(l => l.EnumerateRunes().Count()) * font.Size * .6, Math.Max(1, lines.Length) * font.Size * 1.4);
        };
        var options = model.Options;
        var dark = (themeOverride ?? document.Theme) == "dark";
        var background = dark ? new ClassColor(31, 32, 32) : new ClassColor(255, 255, 255);
        var ink = dark ? new ClassColor(242, 244, 248) : new ClassColor(36, 59, 83);
        var participantFill = dark ? new ClassColor(41, 62, 80) : new ClassColor(225, 237, 250);
        var noteFill = dark ? new ClassColor(74, 65, 31) : new ClassColor(255, 247, 211);
        var figures = new List<SequenceFigure>();
        var labels = new List<SequenceLabel>();
        var regions = new List<SequenceRegion>();
        var lanes = new Dictionary<string, Lane>(StringComparer.Ordinal);
        var items = SequenceDiagramModel.Walk(model.Statements).ToArray();
        var created = items.OfType<SequenceMessage>().Where(m => m.CreateParticipantId is not null).Select(m => m.CreateParticipantId!).ToHashSet();
        var destroyed = new HashSet<string>();
        var sizes = model.Participants.ToDictionary(p => p.Id, p => Measure(p.Display, options.ActorFont));
        var maxHeaderHeight = sizes.Count == 0 ? options.ActorHeight : sizes.Values.Max(s => s.Height) + 58;
        maxHeaderHeight = Math.Max(options.ActorHeight, maxHeaderHeight);
        var actorTop = model.Boxes.Count > 0 ? 54d : 24d;
        var cursor = 24d;
        var spacing = options.ActorMargin;
        foreach (var message in items.OfType<SequenceMessage>().Where(m => m.From != m.To))
        {
            var distance = Math.Abs(model.Participants.FindIndex(p => p.Id == message.From) - model.Participants.FindIndex(p => p.Id == message.To));
            if (distance > 0) spacing = Math.Max(spacing, (Measure(message.Text, options.MessageFont).Width + 40) / distance - options.ActorWidth);
        }
        foreach (var participant in model.Participants)
        {
            var width = Math.Max(options.ActorWidth, sizes[participant.Id].Width + 24);
            lanes.Add(participant.Id, new(participant, cursor + width / 2, width,
                created.Contains(participant.Id) ? null : actorTop, maxHeaderHeight));
            cursor += width + spacing;
        }
        var contentLeft = lanes.Count == 0 ? 24 : lanes.Values.Min(l => l.X - l.Width / 2);
        var contentRight = lanes.Count == 0 ? 380 : lanes.Values.Max(l => l.X + l.Width / 2);
        var y = actorTop + maxHeaderHeight + 22;
        var active = model.Participants.ToDictionary(p => p.Id, _ => new Stack<Activation>());
        var sequenceNumber = 1m;
        var increment = 1m;
        var numbering = options.ShowSequenceNumbers;
        var eventYs = new Dictionary<string, double>();
        Walk(model.Statements, 0);
        var bottom = y + 20;
        foreach (var participant in model.Participants)
        {
            var lane = lanes[participant.Id];
            while (active[participant.Id].TryPop(out var activation)) DrawActivation(lane, activation, bottom);
            if (lane.Top is not { } top) continue;
            var end = lane.End ?? bottom;
            Line(participant.Id + "-lifeline", participant.Id, SequenceElementKind.Participant,
                new(lane.X, top + lane.Height), new(lane.X, end), true, 2);
            regions.Add(new(participant.Id, SequenceElementKind.Participant,
                new(lane.X - 4, top + lane.Height, 8, Math.Max(0, end - top - lane.Height))));
            Participant(participant, lane, top, "header");
            if (options.MirrorActors && !destroyed.Contains(participant.Id)) Participant(participant, lane, bottom + 8, "mirror");
        }
        foreach (var box in model.Boxes)
        {
            var members = model.Participants.Where(p => p.BoxId == box.Id).Select(p => lanes[p.Id]).ToArray();
            var emptyIndex = model.Boxes.IndexOf(box);
            var left = members.Length == 0 ? contentRight + 24 + emptyIndex * 220 : members.Min(l => l.X - l.Width / 2) - options.BoxMargin;
            var right = members.Length == 0 ? left + Math.Max(180, Measure(box.Title, options.NoteFont).Width + 20)
                : members.Max(l => l.X + l.Width / 2) + options.BoxMargin;
            var bounds = new DiagramRect(left, 12, right - left, bottom + (options.MirrorActors ? maxHeaderHeight + 16 : 0) - 4);
            SequenceColors.TryParse(box.Color, out var color);
            Rectangle(box.Id + "-box", box.Id, SequenceElementKind.ParticipantBox, bounds, color, ink, layer: 0);
            Label(box.Id + "-title", box.Id, SequenceElementKind.ParticipantBox, box.Title, options.NoteFont, left + 8, 18, Contrast(color));
            regions.Add(new(box.Id, SequenceElementKind.ParticipantBox, new(left, 12, right - left, 32)));
        }

        var extents = figures.Select(f => f.Bounds).Concat(labels.Select(l => l.Bounds)).ToArray();
        var minX = extents.Length == 0 ? 0 : Math.Min(0, extents.Min(r => r.X));
        var minY = extents.Length == 0 ? 0 : Math.Min(0, extents.Min(r => r.Y));
        var maxX = extents.Length == 0 ? 380 : extents.Max(r => r.X + r.Width);
        var maxY = extents.Length == 0 ? 180 : extents.Max(r => r.Y + r.Height);
        var dx = 20 - minX; var dy = 20 - minY;
        var widthTotal = Math.Max(160, maxX - minX + 40);
        var heightTotal = Math.Max(120, maxY - minY + 40);
        DiagramRect Shift(DiagramRect r) => r with { X = r.X + dx, Y = r.Y + dy };
        var ordered = figures.OrderBy(f => f.Layer).Select(f => f with
        {
            Bounds = Shift(f.Bounds), Points = f.Points.Select(p => new DiagramPoint(p.X + dx, p.Y + dy)).ToArray(),
        }).ToList();
        ordered.Insert(0, new("sequence-background", "", SequenceElementKind.Fragment, SequenceFigureKind.Rectangle,
            new(0, 0, widthTotal, heightTotal), [], background, null, Layer: -1));
        return new(widthTotal, heightTotal, ordered,
            labels.Select(l => l with { Bounds = Shift(l.Bounds) }).ToArray(),
            regions.Select(r => r with { Bounds = Shift(r.Bounds) }).ToArray(), approximate);

        SequenceTextSize Measure(string text, SequenceFont font)
        {
            var size = measure(text, font);
            if (!double.IsFinite(size.Width) || !double.IsFinite(size.Height) || size.Width < 0 || size.Height < 0)
                throw new ArgumentException("Sequence text measurement must return finite nonnegative dimensions.");
            return size;
        }
        void Walk(IEnumerable<SequenceStatement> statements, int depth)
        {
            foreach (var statement in statements)
            {
                switch (statement)
                {
                    case SequenceMessage message:
                        Message(message); break;
                    case SequenceActivation activation:
                        if (activation.Active) active[activation.ParticipantId].Push(new(activation.Id, y, active[activation.ParticipantId].Count));
                        else DrawActivation(lanes[activation.ParticipantId], active[activation.ParticipantId].Pop(), y);
                        regions.Add(new(activation.Id, SequenceElementKind.Activation, new(lanes[activation.ParticipantId].X - 9, y - 5, 18, 14)));
                        y += 12;
                        break;
                    case SequenceNote note:
                        Note(note); break;
                    case SequenceNumbering numbers:
                        numbering = numbers.Enabled || options.ShowSequenceNumbers;
                        if (numbers.Start is { } start) sequenceNumber = start;
                        if (numbers.Step is { } step) increment = step;
                        break;
                    case SequenceBlock block:
                        var top = y;
                        var x = contentLeft - 22 + depth * 8;
                        var width = Math.Max(100, contentRight - contentLeft + 44 - depth * 16);
                        if (block.Kind != SequenceBlockKind.Rect)
                        {
                            var header = SequenceDiagramAdapter.BlockKeyword(block.Kind) + "  " + block.Text;
                            var headerSize = Measure(header, options.NoteFont);
                            Rectangle(block.Id + "-label-background", block.Id, SequenceElementKind.Fragment,
                                new(x + 4, y + 3, headerSize.Width + 8, headerSize.Height + 8), background, null, 2);
                            Label(block.Id + "-label", block.Id, SequenceElementKind.Fragment, header, options.NoteFont, x + 8, y + 7, ink);
                            regions.Add(new(block.Id, SequenceElementKind.Fragment, new(x, y, width, headerSize.Height + 16)));
                            y += headerSize.Height + 20;
                        }
                        else
                        {
                            regions.Add(new(block.Id, SequenceElementKind.Fragment, new(x, y, width, 12)));
                            y += 12;
                        }
                        for (var branchIndex = 0; branchIndex < block.Branches.Count; branchIndex++)
                        {
                            var branch = block.Branches[branchIndex];
                            if (branchIndex > 0)
                            {
                                Line(branch.Id + "-line", branch.Id, SequenceElementKind.Branch, new(x, y), new(x + width, y), true, 2);
                                var text = SequenceDiagramAdapter.BranchKeyword(block.Kind) + "  " + branch.Text;
                                var measured = Label(branch.Id + "-label", branch.Id, SequenceElementKind.Branch, text, options.NoteFont, x + 8, y + 6, ink);
                                regions.Add(new(branch.Id, SequenceElementKind.Branch, new(x, y, width, measured.Height + 14)));
                                y += measured.Height + 18;
                            }
                            Walk(branch.Statements, depth + 1);
                        }
                        y += 16;
                        ClassColor? fill = null;
                        if (block.Kind == SequenceBlockKind.Rect && SequenceColors.TryParse(block.Color ?? block.Text, out var color)) fill = color;
                        Rectangle(block.Id + "-frame", block.Id, SequenceElementKind.Fragment, new(x, top, width, y - top), fill, ink, 1);
                        y += 10;
                        break;
                }
            }
        }
        void Message(SequenceMessage message)
        {
            var from = lanes[message.From];
            var to = lanes[message.To];
            var text = Measure(message.Text, options.MessageFont);
            y += Math.Max(options.MessageMargin, text.Height + 18);
            eventYs[message.Id] = y;
            if (message.CreateParticipantId is not null) to.Top = y - to.Height / 2;
            var fromLevel = active[message.From].Count;
            var toLevel = active[message.To].Count + (message.ActivateTarget ? 1 : 0);
            var fromX = from.X + (fromLevel > 0 ? (fromLevel - 1) * 6 : 0);
            var toX = to.X + (toLevel > 0 ? (toLevel - 1) * 6 : 0);
            var right = toX >= fromX;
            if (!message.CentralSource && fromLevel > 0) fromX += right ? 6 : -6;
            if (message.CreateParticipantId is not null) toX += right ? -to.Width / 2 : to.Width / 2;
            else if (!message.CentralTarget && toLevel > 0) toX += right ? -6 : 6;
            var arrow = SequenceArrows.Get(message.ArrowId);
            DiagramPoint[] route;
            double labelX;
            if (message.From == message.To)
            {
                var outX = fromX + Math.Max(50, text.Width + 28);
                route = [new(fromX, y), new(outX, y), new(outX, y + 28), new(toX, y + 28)];
                labelX = fromX + 16;
            }
            else
            {
                route = [new(fromX, y), new(toX, y)];
                labelX = (fromX + toX - text.Width) / 2;
            }
            Polyline(message.Id + "-line", message.Id, SequenceElementKind.Message, route, arrow.Dashed, 5);
            var labelBounds = new DiagramRect(labelX - 3, y - text.Height - 9, text.Width + 6, text.Height + 4);
            Rectangle(message.Id + "-text-background", message.Id, SequenceElementKind.Message, labelBounds, background, null, 4);
            Label(message.Id + "-text", message.Id, SequenceElementKind.Message, message.Text, options.MessageFont, labelX, y - text.Height - 7, ink, "center");
            if (arrow.Reverse || arrow.BothEnds) Arrow(message.Id + "-start", message.Id, arrow.Head, route[0], route[1]);
            if (!arrow.Reverse || arrow.BothEnds) Arrow(message.Id + "-end", message.Id, arrow.Head, route[^1], route[^2]);
            if (message.CentralSource) Ellipse(message.Id + "-central-source", message.Id, SequenceElementKind.Message, new(fromX - 5, y - 5, 10, 10), background, ink, 7);
            if (message.CentralTarget) Ellipse(message.Id + "-central-target", message.Id, SequenceElementKind.Message, new(toX - 5, route[^1].Y - 5, 10, 10), background, ink, 7);
            foreach (var pair in route.Zip(route.Skip(1)))
                regions.Add(new(message.Id, SequenceElementKind.Message, new(Math.Min(pair.First.X, pair.Second.X) - 5,
                    Math.Min(pair.First.Y, pair.Second.Y) - 5, Math.Abs(pair.First.X - pair.Second.X) + 10, Math.Abs(pair.First.Y - pair.Second.Y) + 10)));
            regions.Add(new(message.Id, SequenceElementKind.Message, labelBounds));
            if (numbering)
            {
                var number = sequenceNumber.ToString(CultureInfo.InvariantCulture);
                var font = options.NoteFont with { Size = Math.Min(12, options.NoteFont.Size) };
                var measured = Measure(number, font);
                var numberX = fromX + (right ? -22 : 22);
                Ellipse(message.Id + "-number", message.Id, SequenceElementKind.Message, new(numberX - measured.Width / 2 - 4, y - measured.Height / 2 - 3, measured.Width + 8, measured.Height + 6), background, ink, 8);
                Label(message.Id + "-number-text", message.Id, SequenceElementKind.Message, number, font, numberX - measured.Width / 2, y - measured.Height / 2, ink);
            }
            sequenceNumber += increment;
            if (message.ActivateTarget) active[message.To].Push(new(message.Id, route[^1].Y, active[message.To].Count));
            if (message.DeactivateSource) DrawActivation(from, active[message.From].Pop(), route[^1].Y);
            if (message.DestroyParticipantId is { } destroyedId)
            {
                var lane = lanes[destroyedId];
                lane.End = route[^1].Y;
                destroyed.Add(destroyedId);
                while (active[destroyedId].TryPop(out var activation)) DrawActivation(lane, activation, lane.End.Value);
                Arrow(message.Id + "-destroy", message.Id, SequenceArrowHead.Cross, new(lane.X, lane.End.Value), new(lane.X + 1, lane.End.Value));
            }
            y = route[^1].Y + 12;
        }
        void DrawActivation(Lane lane, Activation activation, double end)
        {
            var bounds = new DiagramRect(lane.X - 6 + activation.Level * 6, activation.Top, 12, Math.Max(3, end - activation.Top));
            Rectangle(activation.Id + "-activation-" + activation.Level, activation.Id, SequenceElementKind.Activation, bounds, participantFill, ink, 3);
            regions.Add(new(activation.Id, SequenceElementKind.Activation, bounds));
        }
        void Note(SequenceNote note)
        {
            var a = lanes[note.ParticipantId];
            var b = note.SecondParticipantId is { } other ? lanes[other] : a;
            var text = Measure(note.Text, options.NoteFont);
            var width = Math.Max(text.Width + 24, note.Placement == SequenceNotePlacement.Over ? Math.Abs(b.X - a.X) + 40 : 80);
            var x = note.Placement switch
            {
                SequenceNotePlacement.Left => a.X - options.NoteMargin - width,
                SequenceNotePlacement.Right => a.X + options.NoteMargin,
                _ => (a.X + b.X - width) / 2,
            };
            var bounds = new DiagramRect(x, y + 5, width, text.Height + 20);
            Rectangle(note.Id + "-note", note.Id, SequenceElementKind.Note, bounds, noteFill, ink, 6);
            var textX = options.NoteAlign switch { "left" => x + 12, "right" => x + width - text.Width - 12, _ => x + (width - text.Width) / 2 };
            Label(note.Id + "-text", note.Id, SequenceElementKind.Note, note.Text, options.NoteFont, textX, y + 15, ink, options.NoteAlign);
            regions.Add(new(note.Id, SequenceElementKind.Note, bounds));
            y += bounds.Height + 16;
        }
        void Participant(SequenceParticipant participant, Lane lane, double top, string occurrence)
        {
            var kind = participant.Shape == SequenceParticipantShape.Default
                ? participant.Kind == SequenceParticipantKind.Actor ? SequenceParticipantShape.Actor : SequenceParticipantShape.Rectangle : participant.Shape;
            var rect = new DiagramRect(lane.X - lane.Width / 2, top, lane.Width, lane.Height);
            var owner = participant.Id;
            var id = owner + "-" + occurrence;
            var text = sizes[owner];
            if (kind == SequenceParticipantShape.Rectangle)
            {
                Rectangle(id, owner, SequenceElementKind.Participant, rect, participantFill, ink, 6);
                Label(id + "-text", owner, SequenceElementKind.Participant, participant.Display, options.ActorFont,
                    lane.X - text.Width / 2, top + (lane.Height - text.Height) / 2, ink, "center");
            }
            else
            {
                Rectangle(id + "-backplate", owner, SequenceElementKind.Participant, rect, background, null, 5);
                var x = lane.X;
                var iconY = top + 10;
                switch (kind)
                {
                    case SequenceParticipantShape.Actor:
                        Ellipse(id + "-head", owner, SequenceElementKind.Participant, new(x - 8, iconY, 16, 16), participantFill, ink, 6);
                        Line(id + "-body", owner, SequenceElementKind.Participant, new(x, iconY + 16), new(x, iconY + 34), false, 6);
                        Line(id + "-arms", owner, SequenceElementKind.Participant, new(x - 16, iconY + 22), new(x + 16, iconY + 22), false, 6);
                        Line(id + "-leg-a", owner, SequenceElementKind.Participant, new(x, iconY + 34), new(x - 13, iconY + 45), false, 6);
                        Line(id + "-leg-b", owner, SequenceElementKind.Participant, new(x, iconY + 34), new(x + 13, iconY + 45), false, 6);
                        break;
                    case SequenceParticipantShape.Database:
                        Rectangle(id + "-body", owner, SequenceElementKind.Participant, new(x - 23, iconY + 6, 46, 30), participantFill, ink, 6);
                        Ellipse(id + "-bottom", owner, SequenceElementKind.Participant, new(x - 23, iconY + 30, 46, 12), participantFill, ink, 6);
                        Ellipse(id + "-top", owner, SequenceElementKind.Participant, new(x - 23, iconY, 46, 12), participantFill, ink, 7);
                        break;
                    case SequenceParticipantShape.Queue:
                        Rectangle(id + "-body", owner, SequenceElementKind.Participant, new(x - 20, iconY + 3, 40, 32), participantFill, ink, 6);
                        Ellipse(id + "-back", owner, SequenceElementKind.Participant, new(x + 13, iconY + 3, 14, 32), participantFill, ink, 6);
                        Ellipse(id + "-front", owner, SequenceElementKind.Participant, new(x - 27, iconY + 3, 14, 32), participantFill, ink, 7);
                        break;
                    case SequenceParticipantShape.Collections:
                        Rectangle(id + "-back", owner, SequenceElementKind.Participant, new(x - 18, iconY, 42, 32), participantFill, ink, 6);
                        Rectangle(id + "-front", owner, SequenceElementKind.Participant, new(x - 24, iconY + 6, 42, 32), participantFill, ink, 7);
                        break;
                    default:
                        Ellipse(id + "-circle", owner, SequenceElementKind.Participant, new(x - 17, iconY + 3, 34, 34), participantFill, ink, 6);
                        if (kind == SequenceParticipantShape.Entity) Line(id + "-base", owner, SequenceElementKind.Participant, new(x - 22, iconY + 40), new(x + 22, iconY + 40), false, 6);
                        if (kind == SequenceParticipantShape.Boundary)
                        {
                            Line(id + "-bar", owner, SequenceElementKind.Participant, new(x - 31, iconY), new(x - 31, iconY + 40), false, 6);
                            Line(id + "-join", owner, SequenceElementKind.Participant, new(x - 31, iconY + 20), new(x - 17, iconY + 20), false, 6);
                        }
                        if (kind == SequenceParticipantShape.Control) Arrow(id + "-control", owner, SequenceArrowHead.Open, new(x, iconY + 3), new(x + 10, iconY + 3));
                        break;
                }
                Label(id + "-text", owner, SequenceElementKind.Participant, participant.Display, options.ActorFont, x - text.Width / 2, top + lane.Height - text.Height - 5, ink, "center");
            }
            regions.Add(new(owner, SequenceElementKind.Participant, rect));
        }
        void Arrow(string id, string owner, SequenceArrowHead head, DiagramPoint tip, DiagramPoint toward)
        {
            if (head == SequenceArrowHead.None) return;
            var dx = toward.X - tip.X; var dy = toward.Y - tip.Y;
            var length = Math.Sqrt(dx * dx + dy * dy);
            if (length < 1e-9) return;
            dx /= length; dy /= length;
            DiagramPoint Point(double along, double side) => new(tip.X + dx * along - dy * side, tip.Y + dy * along + dx * side);
            if (head == SequenceArrowHead.Cross)
            {
                Line(id + "-a", owner, SequenceElementKind.Message, new(tip.X - 5, tip.Y - 5), new(tip.X + 5, tip.Y + 5), false, 8);
                Line(id + "-b", owner, SequenceElementKind.Message, new(tip.X - 5, tip.Y + 5), new(tip.X + 5, tip.Y - 5), false, 8);
                return;
            }
            var half = head is SequenceArrowHead.FilledTop or SequenceArrowHead.FilledBottom or SequenceArrowHead.StickTop or SequenceArrowHead.StickBottom;
            var top = head is SequenceArrowHead.FilledTop or SequenceArrowHead.StickTop;
            var side = (dx >= 0 ? -1 : 1) * (top ? 5 : -5);
            var points = half ? new[] { tip, Point(10, side), Point(10, 0) } : new[] { Point(10, 5), tip, Point(10, -5) };
            var filled = head is SequenceArrowHead.Filled or SequenceArrowHead.FilledTop or SequenceArrowHead.FilledBottom;
            if (!filled && half) points = [tip, Point(10, side)];
            Figure(id + "-halo", owner, SequenceElementKind.Message,
                filled ? SequenceFigureKind.Polygon : SequenceFigureKind.Polyline, points, null, background, false, 8, 3.8);
            Figure(id, owner, SequenceElementKind.Message, filled ? SequenceFigureKind.Polygon : SequenceFigureKind.Polyline, points, filled ? ink : null, ink, false, 8);
        }
        ClassColor Contrast(ClassColor fill)
        {
            double Channel(byte foreground, byte back)
            {
                var value = (foreground * (fill.Alpha / 255d) + back * (1 - fill.Alpha / 255d)) / 255;
                return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
            }
            var luminance = .2126 * Channel(fill.Red, background.Red) + .7152 * Channel(fill.Green, background.Green) + .0722 * Channel(fill.Blue, background.Blue);
            return luminance > .179 ? new(0, 0, 0) : new(255, 255, 255);
        }
        SequenceTextSize Label(string id, string owner, SequenceElementKind kind, string text, SequenceFont font, double x, double top, ClassColor color, string alignment = "left")
        {
            var size = Measure(text, font);
            labels.Add(new(id, owner, kind, text, font, color, new(x, top, size.Width, size.Height), alignment));
            return size;
        }
        void Rectangle(string id, string owner, SequenceElementKind kind, DiagramRect bounds, ClassColor? fill, ClassColor? stroke, int layer) =>
            figures.Add(new(id, owner, kind, SequenceFigureKind.Rectangle, bounds, [], fill, stroke, Layer: layer));
        void Ellipse(string id, string owner, SequenceElementKind kind, DiagramRect bounds, ClassColor? fill, ClassColor? stroke, int layer) =>
            figures.Add(new(id, owner, kind, SequenceFigureKind.Ellipse, bounds, [], fill, stroke, Layer: layer));
        void Line(string id, string owner, SequenceElementKind kind, DiagramPoint from, DiagramPoint to, bool dashed, int layer) =>
            Polyline(id, owner, kind, [from, to], dashed, layer);
        void Polyline(string id, string owner, SequenceElementKind kind, IReadOnlyList<DiagramPoint> points, bool dashed, int layer)
        {
            if (id.EndsWith("-lifeline", StringComparison.Ordinal) || id.EndsWith("-line", StringComparison.Ordinal) && kind == SequenceElementKind.Message)
                Figure(id + "-halo", owner, kind, SequenceFigureKind.Polyline, points, null, background, false, layer, 3.8);
            Figure(id, owner, kind, SequenceFigureKind.Polyline, points, null, ink, dashed, layer);
        }
        void Figure(string id, string owner, SequenceElementKind kind, SequenceFigureKind figureKind,
            IReadOnlyList<DiagramPoint> points, ClassColor? fill, ClassColor? stroke, bool dashed, int layer, double strokeWidth = 1.4)
        {
            var x = points.Min(p => p.X); var top = points.Min(p => p.Y);
            figures.Add(new(id, owner, kind, figureKind,
                new(x, top, points.Max(p => p.X) - x, points.Max(p => p.Y) - top), points, fill, stroke, strokeWidth, Dashed: dashed, Layer: layer));
        }
    }

    private sealed class Lane(SequenceParticipant participant, double x, double width, double? top, double height)
    {
        public SequenceParticipant Participant { get; } = participant;
        public double X { get; } = x;
        public double Width { get; } = width;
        public double? Top { get; set; } = top;
        public double Height { get; } = height;
        public double? End { get; set; }
    }
    private sealed record Activation(string Id, double Top, int Level);
}
