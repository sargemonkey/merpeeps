using Merpeeps.Core.Model;

namespace Merpeeps.Core.Layout;

public sealed record ZenLayoutResult(DrawingScene Scene, IReadOnlyDictionary<string, DiagramRect> Participants,
    IReadOnlyDictionary<string, DiagramRect> Statements, IReadOnlyDictionary<string, IReadOnlyList<DiagramPoint>> Routes);
public static class ZenDiagramLayout
{
    public static ZenLayoutResult Create(DiagramModel model, DrawingTextMeasure? measure = null, string? themeOverride = null)
    {
        var d = model.Zen ?? throw new ArgumentException("ZenUML content is missing."); ZenSemantics.Check(d);
        var b = new DataDiagramLayouts.Builder(measure, d.ThemeVariables, d.GlobalConfiguration, themeOverride ?? model.Theme);
        var centers = new Dictionary<string, double>(); var participants = new Dictionary<string, DiagramRect>(); var statements = new Dictionary<string, DiagramRect>();
        var routes = new Dictionary<string, IReadOnlyList<DiagramPoint>>(); var creationY = new Dictionary<string, double>();
        var measured = d.Participants.ToDictionary(p => p.Id, p => b.MeasureWrapped(p.Alias ?? p.SourceId, 14, 180, true));
        var x = 90d;
        foreach (var participant in d.Participants)
        {
            var width = Math.Max(110, measured[participant.Id].Width + 24); centers[participant.Id] = x + width / 2;
            participants[participant.Id] = new(x, 45, width, Math.Max(46, measured[participant.Id].Height + 20)); x += width + 85;
        }
        var y = 140d; var order = new List<(ZenStatement Statement, double Start, double End)>();
        MeasureStatements(d.Statements);
        var bottom = Math.Max(200, y + 30);
        if (d.Title.Length > 0) b.Text("title", "ZenDiagram", "", d.Title, 20, 30, 10, Math.Max(400, x), true);
        foreach (var participant in d.Participants)
        {
            var rect = participants[participant.Id]; var top = creationY.GetValueOrDefault(participant.Id, rect.Y + rect.Height);
            if (creationY.ContainsKey(participant.Id)) rect = rect with { Y = top - rect.Height / 2 };
            participants[participant.Id] = rect;
            b.Line(participant.Id + "-lifeline", "ZenParticipant", participant.Id, [new(centers[participant.Id], top), new(centers[participant.Id], bottom)], b.Ink with { Alpha = 120 }, 1);
            if (participant.Kind == ZenParticipantKind.Actor)
            {
                var cx = centers[participant.Id];
                b.Circle(participant.Id + "-head", "ZenParticipant", participant.Id, new(cx, rect.Y + 9), 9, b.Background, b.Ink);
                b.Line(participant.Id + "-body", "ZenParticipant", participant.Id, [new(cx, rect.Y + 18), new(cx, rect.Y + 34), new(cx - 14, rect.Y + 45)], b.Ink, 2);
                b.Line(participant.Id + "-leg", "ZenParticipant", participant.Id, [new(cx, rect.Y + 34), new(cx + 14, rect.Y + 45)], b.Ink, 2);
                b.Line(participant.Id + "-arms", "ZenParticipant", participant.Id, [new(cx - 18, rect.Y + 25), new(cx + 18, rect.Y + 25)], b.Ink, 2);
                b.Text(participant.Id + "-label", "ZenParticipant", participant.Id, participant.Alias ?? participant.SourceId, 14, rect.X, rect.Y + 47, rect.Width, true, alignment: "center");
            }
            else
            {
                var shape = participant.Kind == ZenParticipantKind.Database ? NodeShape.Cylinder : NodeShape.Rectangle;
                BlockDiagramLayout.Shape(b, new(participant.Id, BlockItemKind.Shape, []) { Shape = shape }, rect, b.Tint(b.Scale(0)), b.Ink, 1.5);
                b.Text(participant.Id + "-label", "ZenParticipant", participant.Id, participant.Alias ?? participant.SourceId, 14, rect.X + 12, rect.Y + 12, rect.Width - 24, true, alignment: "center");
            }
            b.Hit("ZenParticipant", participant.Id, rect);
        }
        foreach (var (statement, startY, endY) in order.Where(p => p.Statement.Kind is ZenStatementKind.Call or ZenStatementKind.Create && p.Statement.Children.Count > 0))
            if (statement.To is { } target) b.Box(statement.Id + "-activation", "ZenStatement", statement.Id,
                new(centers[target] - 5, startY, 10, Math.Max(10, endY - startY)), b.Tint(b.Scale(1)), b.Ink);
        foreach (var (statement, startY, endY) in order.OrderBy(o => o.Start).ThenByDescending(o => o.End))
        {
            var owner = statement.Id;
            if (statement.Kind == ZenStatementKind.Comment)
            {
                var text = b.MeasureWrapped(statement.Text, 13, Math.Max(300, x - 70), markdown: true);
                var rect = new DiagramRect(25, startY - 15, Math.Max(100, text.Width + 20), text.Height + 20);
                b.Box(owner, "ZenStatement", owner, rect, new(255, 247, 209), b.Ink);
                b.Text(owner + "-text", "ZenStatement", owner, statement.Text, 13, rect.X + 10, rect.Y + 10, rect.Width - 20, markdown: true);
                statements[owner] = rect; b.Hit("ZenStatement", owner, rect); continue;
            }
            if (statement.Kind is ZenStatementKind.Loop or ZenStatementKind.Conditional or ZenStatementKind.Optional or ZenStatementKind.Parallel or ZenStatementKind.Try)
            {
                var rect = new DiagramRect(30, startY - 22, Math.Max(250, x - 60), endY - startY + 38);
                b.Box(owner, "ZenStatement", owner, rect, null, b.Ink, 1.5);
                b.Text(owner + "-title", "ZenStatement", owner, (statement.Keyword.Length == 0 ? statement.Kind.ToString() : statement.Keyword) +
                    (statement.Text.Length == 0 ? "" : " [" + statement.Text + "]"), 12, rect.X + 8, rect.Y + 4, rect.Width - 16, true);
                statements[owner] = rect; b.Hit("ZenStatement", owner, rect); continue;
            }
            var from = statement.From is null ? 30 : centers[statement.From]; var to = statement.To is null ? 30 : centers[statement.To];
            var label = statement.Kind switch
            {
                ZenStatementKind.Create => "new " + d.Participants.Single(p => p.Id == statement.To).SourceId + "(" + statement.Arguments + ")",
                ZenStatementKind.Call => (statement.Assignment is null ? "" : statement.Assignment + " = ") + statement.Text + (statement.Arguments is null ? "" : "(" + statement.Arguments + ")"),
                _ => statement.Text,
            };
            var points = Math.Abs(from - to) < 1 ? new[] { new DiagramPoint(from, startY), new(from + 42, startY), new(from + 42, startY + 24), new(from, startY + 24) } : new[] { new DiagramPoint(from, startY), new(to, startY) };
            routes[owner] = points; b.Line(owner, "ZenStatement", owner, points, b.Ink, 1.5);
            var last = points[^1]; var prior = points[^2]; var deltaX = prior.X - last.X; var deltaY = prior.Y - last.Y; var length = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
            if (length > 0) b.Polygon(owner + "-head", "ZenStatement", owner, [last, new(last.X + deltaX / length * 10 - deltaY / length * 4, last.Y + deltaY / length * 10 + deltaX / length * 4),
                new(last.X + deltaX / length * 10 + deltaY / length * 4, last.Y + deltaY / length * 10 - deltaX / length * 4)], statement.Kind == ZenStatementKind.AsyncMessage ? b.Background : b.Ink, b.Ink);
            b.Text(owner + "-label", "ZenStatement", owner, label, 13, Math.Min(from, to) + 8, startY - 24, Math.Max(120, Math.Abs(to - from) - 16));
            var bounds = new DiagramRect(Math.Min(from, to) - 6, startY - 26, Math.Max(60, Math.Abs(from - to) + 12), points.Length == 4 ? 58 : 34);
            statements[owner] = bounds; b.Hit("ZenStatement", owner, bounds);
            if (statement.Assignment is not null && statement.Kind == ZenStatementKind.Call)
            {
                b.Line(owner + "-assignment-reply", "ZenStatement", owner, [new(to, endY + 12), new(from, endY + 12)], b.Ink, 1);
                b.Text(owner + "-assignment-label", "ZenStatement", owner, statement.Assignment, 12, Math.Min(from, to) + 8, endY + 14, 180);
            }
        }
        var scene = b.Finish(); var offset = b.OriginOffset;
        var figures = scene.Figures.Select(f => f.Owner.Role == "Block" ? f with { Owner = new("ZenParticipant", f.Owner.Id) }
            : f.Id.EndsWith("-lifeline", StringComparison.Ordinal) || f.Id.EndsWith("-assignment-reply", StringComparison.Ordinal) ||
                order.Any(o => o.Statement.Id == f.Id && o.Statement.Kind is ZenStatementKind.Return or ZenStatementKind.ReplyMessage) ? f with { Dashed = true } : f).ToArray();
        return new(scene with { Figures = figures }, Shift(participants), Shift(statements),
            routes.ToDictionary(p => p.Key, p => (IReadOnlyList<DiagramPoint>)p.Value.Select(q => new DiagramPoint(q.X + offset.X, q.Y + offset.Y)).ToArray()));
        Dictionary<string, DiagramRect> Shift(Dictionary<string, DiagramRect> values) => values.ToDictionary(p => p.Key, p => p.Value with { X = p.Value.X + offset.X, Y = p.Value.Y + offset.Y });
        void MeasureStatements(IEnumerable<ZenStatement> source)
        {
            foreach (var statement in source)
            {
                var start = y;
                if (statement.Kind == ZenStatementKind.Comment) y += b.MeasureWrapped(statement.Text, 13, Math.Max(300, x - 70), markdown: true).Height + 35;
                else
                {
                    y += statement.Kind is ZenStatementKind.Loop or ZenStatementKind.Conditional or ZenStatementKind.Optional or ZenStatementKind.Parallel or ZenStatementKind.Try ? 32 : 55;
                    if (statement.Kind == ZenStatementKind.Create && statement.To is { } target) creationY.TryAdd(target, start);
                    MeasureStatements(statement.Children);
                    foreach (var branch in statement.Branches)
                    {
                        var branchY = y; y += 28;
                        b.Line(branch.Id + "-divider", "ZenBranch", branch.Id, [new(35, branchY - 8), new(Math.Max(285, x - 35), branchY - 8)], b.Ink, 1);
                        b.Text(branch.Id + "-label", "ZenBranch", branch.Id, branch.Keyword + (branch.Condition.Length == 0 ? "" : " [" + branch.Condition + "]"), 12, 40, branchY - 5, 280, true);
                        MeasureStatements(branch.Statements);
                    }
                    if (statement.Children.Count > 0 || statement.Branches.Count > 0) y += 20;
                    if (statement.Assignment is not null) y += 32;
                }
                order.Add((statement, start, y));
            }
        }
    }
}
