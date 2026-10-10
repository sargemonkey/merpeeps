using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Merpeeps.App;

/// <summary>Illustrative type previews only; these never supply content to a new document.</summary>
internal sealed class DiagramTypePreview(string familyId) : Control
{
    private static readonly IBrush Paper = new SolidColorBrush(Color.Parse("#F5F7FA"));
    private static readonly IBrush Ink = new SolidColorBrush(Color.Parse("#243B53"));
    private static readonly IBrush Blue = new SolidColorBrush(Color.Parse("#DBEAFE"));
    private static readonly IPen Line = new Pen(Ink, 1.5);
    private static readonly Typeface Face = new("Segoe UI");

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Paper, new Rect(Bounds.Size), 6);
        var scale = Math.Min(Bounds.Width / 280, Bounds.Height / 160);
        using var transform = context.PushTransform(Matrix.CreateScale(scale, scale)
            * Matrix.CreateTranslation((Bounds.Width - 280 * scale) / 2, (Bounds.Height - 160 * scale) / 2));
        switch (familyId)
        {
            case "flowchart":
                Box(new(94, 13, 92, 29), "Start", 14);
                Arrow(new(140, 42), new(140, 57));
                var diamond = Polygon(new(140, 57), new(179, 81), new(140, 105), new(101, 81));
                context.DrawGeometry(Brushes.White, Line, diamond);
                Text("Check", 140, 73);
                Arrow(new(101, 81), new(56, 81));
                Arrow(new(179, 81), new(224, 81));
                Box(new(15, 111, 82, 31), "Revise");
                Box(new(183, 111, 82, 31), "Continue");
                context.DrawLine(Line, new(56, 81), new(56, 111));
                context.DrawLine(Line, new(224, 81), new(224, 111));
                break;
            case "state":
                context.DrawEllipse(Ink, null, new Point(24, 82), 7, 7);
                Arrow(new(32, 82), new(53, 82));
                Box(new(53, 60, 71, 44), "Idle", 10);
                Arrow(new(124, 82), new(159, 82));
                Text("begin", 142, 53, 10);
                Box(new(159, 60, 90, 44), "Running", 10);
                context.DrawLine(Line, new(204, 104), new(204, 132));
                context.DrawLine(Line, new(204, 132), new(89, 132));
                Arrow(new(89, 132), new(89, 104));
                Text("finish", 148, 137, 10);
                break;
            case "class":
                Card(new(13, 34, 113, 94), "Account", "+ owner", "+ save()");
                Card(new(173, 34, 95, 94), "Order", "+ total", "+ submit()");
                Arrow(new(126, 83), new(173, 83));
                Text("1", 135, 65, 10);
                Text("*", 163, 65, 10);
                break;
            case "er":
                Card(new(13, 34, 108, 94), "Customer", "id  PK", "email  UK");
                Card(new(177, 34, 91, 94), "Order", "id  PK", "owner  FK");
                context.DrawLine(Line, new(121, 82), new(177, 82));
                context.DrawLine(Line, new(128, 75), new(128, 89));
                context.DrawLine(Line, new(135, 75), new(135, 89));
                context.DrawEllipse(Paper, Line, new Point(153, 82), 4, 4);
                context.DrawLine(Line, new(162, 82), new(176, 74));
                context.DrawLine(Line, new(162, 82), new(176, 90));
                break;
            case "sequence":
                Box(new(20, 12, 82, 28), "Client");
                Box(new(178, 12, 82, 28), "Service");
                var lifeline = new Pen(Ink, 1, new DashStyle([3, 3], 0));
                context.DrawLine(lifeline, new(61, 40), new(61, 149));
                context.DrawLine(lifeline, new(219, 40), new(219, 149));
                Arrow(new(61, 70), new(219, 70));
                Text("Request", 140, 52, 10);
                context.DrawRectangle(Blue, Line, new(214, 70, 10, 48));
                Arrow(new(214, 118), new(61, 118));
                Text("Response", 140, 99, 10);
                break;
            case "pie":
                var colors = new[] { "#93B5DF", "#A6D5C4", "#EFC78D" };
                var angle = -Math.PI / 2;
                for (var i = 0; i < 3; i++)
                {
                    var sweep = new[] { Math.PI, Math.PI * .6, Math.PI * .4 }[i];
                    var parts = Core.Layout.ChartDiagramLayout.Sector(new(100, 80), 60, 20, angle, sweep);
                    var geometry = new StreamGeometry();
                    using (var path = geometry.Open())
                        foreach (var part in parts)
                            switch (part.Kind)
                            {
                                case Core.Layout.DrawingPathKind.Move: path.BeginFigure(new(part.End.X, part.End.Y), true); break;
                                case Core.Layout.DrawingPathKind.Line: path.LineTo(new(part.End.X, part.End.Y)); break;
                                case Core.Layout.DrawingPathKind.Cubic: path.CubicBezierTo(new(part.Control1.X, part.Control1.Y), new(part.Control2.X, part.Control2.Y), new(part.End.X, part.End.Y)); break;
                                case Core.Layout.DrawingPathKind.Close: path.EndFigure(true); break;
                            }
                    context.DrawGeometry(new SolidColorBrush(Color.Parse(colors[i])), new Pen(Brushes.White, 1), geometry);
                    context.DrawRectangle(new SolidColorBrush(Color.Parse(colors[i])), null, new Rect(185, 43 + i * 29, 12, 12));
                    Text(new[] { "A", "B", "C" }[i], 216, 41 + i * 29);
                    angle += sweep;
                }
                break;
            case "quadrant":
                context.DrawRectangle(Blue, Line, new Rect(43, 20, 194, 120));
                context.DrawLine(Line, new(140, 20), new(140, 140));
                context.DrawLine(Line, new(43, 80), new(237, 80));
                foreach (var point in new[] { new Point(92, 53), new Point(187, 48), new Point(164, 111) })
                    context.DrawEllipse(Ink, null, point, 4, 4);
                break;
            case "xy":
                context.DrawLine(Line, new(32, 15), new(32, 140));
                context.DrawLine(Line, new(32, 140), new(255, 140));
                var heights = new[] { 40d, 66, 54, 98 };
                for (var i = 0; i < 4; i++) context.DrawRectangle(Blue, Line, new Rect(51 + i * 47, 140 - heights[i], 27, heights[i]));
                var linePoints = new[] { new Point(65, 92), new Point(112, 70), new Point(159, 80), new Point(206, 29) };
                for (var i = 1; i < linePoints.Length; i++) context.DrawLine(new Pen(Ink, 2), linePoints[i - 1], linePoints[i]);
                break;
            case "radar":
                foreach (var radius in new[] { 25d, 45, 64 }) context.DrawEllipse(null, new Pen(Ink, .6), new Point(140, 80), radius, radius);
                var outer = Enumerable.Range(0, 5).Select(i => new Point(140 + 64 * Math.Cos(i * 2 * Math.PI / 5 - Math.PI / 2), 80 + 64 * Math.Sin(i * 2 * Math.PI / 5 - Math.PI / 2))).ToArray();
                foreach (var point in outer) context.DrawLine(new Pen(Ink, .6), new(140, 80), point);
                var polygon = outer.Select((p, i) => new Point(140 + (p.X - 140) * new[] { .9, .6, .8, .45, .7 }[i], 80 + (p.Y - 80) * new[] { .9, .6, .8, .45, .7 }[i])).ToArray();
                context.DrawGeometry(new SolidColorBrush(Color.Parse("#7793B5DF")), Line, Polygon(polygon));
                break;
            case "timeline":
                context.DrawLine(Line, new(20, 68), new(260, 68));
                foreach (var (x, label) in new[] { (30d, "Past"), (115d, "Now"), (200d, "Next") })
                {
                    Box(new(x, 27, 58, 30), label);
                    context.DrawLine(Line, new(x + 29, 57), new(x + 29, 107));
                    Box(new(x, 105, 58, 29), "Event");
                }
                break;
            case "journey":
                for (var i = 0; i < 3; i++)
                {
                    var x = 29 + i * 86;
                    Box(new(x, 20, 66, 28), new[] { "Start", "Try", "Finish" }[i]);
                    var y = new[] { 80d, 126, 70 }[i];
                    context.DrawLine(Line, new(x + 33, 48), new(x + 33, y - 13));
                    context.DrawEllipse(Paper, Line, new Point(x + 33, y), 13, 13);
                    Text(new[] { "5", "2", "5" }[i], x + 33, y - 7, 11);
                }
                break;
            case "mindmap":
                Box(new(108, 58, 66, 40), "Idea");
                foreach (var r in new[] { new Rect(18, 16, 63, 30), new Rect(20, 120, 65, 28), new Rect(203, 35, 59, 29), new Rect(207, 112, 58, 29) })
                {
                    context.DrawLine(Line, new(140, 80), r.Center);
                    Box(r, "Topic");
                }
                break;
            case "treeview":
                Text("project/", 94, 23, 12);
                context.DrawLine(Line, new(68, 42), new(68, 133));
                foreach (var (y, label) in new[] { (58d, "src/"), (92d, "config"), (125d, "readme") })
                {
                    context.DrawLine(Line, new(68, y), new(94, y));
                    Text(label, 132, y - 7, 12);
                }
                break;
            case "packet":
                Box(new(24, 32, 115, 35), "Source");
                Box(new(139, 32, 115, 35), "Destination");
                Box(new(24, 67, 230, 34), "Sequence");
                Box(new(24, 101, 230, 34), "Payload");
                Text("0", 30, 17, 10); Text("15", 132, 17, 10); Text("31", 247, 17, 10);
                break;
            case "kanban":
                for (var i = 0; i < 3; i++)
                {
                    Box(new(16 + i * 86, 17, 76, 27), new[] { "Todo", "Doing", "Done" }[i]);
                    Box(new(20 + i * 86, 57, 68, 30), "Task");
                    if (i != 1) Box(new(20 + i * 86, 101, 68, 30), "Task");
                }
                break;
            case "requirement":
                Box(new(16, 24, 112, 49), "Requirement");
                Box(new(155, 102, 110, 38), "Element");
                Arrow(new(210, 102), new(88, 73));
                Text("satisfies", 176, 76, 10);
                break;
            case "git":
                context.DrawLine(Line, new(25, 55), new(252, 55));
                context.DrawLine(Line, new(88, 109), new(198, 109));
                context.DrawLine(Line, new(57, 55), new(88, 109));
                context.DrawLine(Line, new(198, 109), new(230, 55));
                foreach (var point in new[] { new Point(30, 55), new Point(57, 55), new Point(135, 55), new Point(230, 55), new Point(88, 109), new Point(155, 109), new Point(198, 109) })
                    context.DrawEllipse(Blue, Line, point, 6, 6);
                break;
            case "sankey":
                context.DrawLine(new Pen(Blue, 30), new(42, 76), new(228, 36));
                context.DrawLine(new Pen(Blue, 18), new(42, 91), new(228, 124));
                context.DrawRectangle(Ink, null, new Rect(30, 50, 12, 55));
                context.DrawRectangle(Ink, null, new Rect(228, 20, 12, 33));
                context.DrawRectangle(Ink, null, new Rect(228, 114, 12, 20));
                break;
            case "treemap":
                context.DrawRectangle(Blue, Line, new Rect(24, 22, 145, 118));
                context.DrawRectangle(Paper, Line, new Rect(169, 22, 88, 58));
                context.DrawRectangle(Blue, Line, new Rect(169, 80, 88, 60));
                Text("60", 96, 70); Text("20", 212, 39); Text("20", 212, 98);
                break;
            case "block":
                Box(new(24, 22, 68, 40), "A");
                Box(new(102, 22, 150, 40), "Wide");
                Box(new(24, 77, 150, 64), "Composite");
                Box(new(184, 77, 68, 64), "D");
                break;
            case "architecture":
                context.DrawRectangle(null, Line, new Rect(14, 16, 250, 128));
                Box(new(36, 48, 63, 40), "DB");
                Box(new(182, 88, 62, 40), "API");
                context.DrawLine(Line, new(99, 68), new(145, 68));
                context.DrawLine(Line, new(145, 68), new(145, 108));
                Arrow(new(145, 108), new(182, 108));
                break;
            case "c4":
                context.DrawEllipse(Blue, Line, new Point(55, 37), 14, 14);
                Box(new(24, 53, 62, 54), "User");
                Box(new(158, 40, 96, 74), "System");
                Arrow(new(86, 80), new(158, 80));
                break;
            case "gantt":
                context.DrawLine(Line, new(40, 18), new(40, 142));
                context.DrawLine(Line, new(40, 142), new(250, 142));
                context.DrawRectangle(Blue, null, new Rect(50, 27, 70, 20));
                context.DrawRectangle(Blue, null, new Rect(111, 66, 86, 20));
                context.DrawRectangle(Blue, null, new Rect(179, 105, 66, 20));
                break;
            case "wardley":
                context.DrawLine(Line, new(30, 20), new(30, 138));
                context.DrawLine(Line, new(30, 138), new(250, 138));
                context.DrawLine(Line, new(98, 40), new(155, 80));
                context.DrawLine(Line, new(155, 80), new(205, 119));
                foreach (var p in new[] { new Point(98, 40), new Point(155, 80), new Point(205, 119) }) context.DrawEllipse(Paper, Line, p, 6, 6);
                Arrow(new(155, 80), new(233, 80));
                break;
            case "ishikawa":
                Arrow(new(18, 85), new(207, 85));
                foreach (var x in new[] { 45, 110, 170 })
                {
                    context.DrawLine(Line, new(x, 85), new(x - 25, 30));
                    context.DrawLine(Line, new(x, 85), new(x - 25, 140));
                }
                Box(new(204, 61, 60, 47), "Effect");
                break;
            case "cynefin":
                context.DrawRectangle(Blue, null, new Rect(25, 15, 112, 65));
                context.DrawRectangle(Paper, null, new Rect(137, 15, 112, 65));
                context.DrawRectangle(Paper, null, new Rect(25, 80, 112, 65));
                context.DrawRectangle(Blue, null, new Rect(137, 80, 112, 65));
                context.DrawLine(Line, new(137, 15), new(137, 145));
                context.DrawLine(Line, new(25, 80), new(249, 80));
                context.DrawEllipse(Paper, Line, new(137, 80), 29, 21);
                break;
            case "venn":
                context.DrawEllipse(Blue, Line, new(105, 73), 49, 49);
                context.DrawEllipse(null, Line, new(172, 73), 49, 49);
                context.DrawEllipse(null, Line, new(139, 105), 49, 49);
                break;
            case "railroad":
                context.DrawLine(Line, new(18, 80), new(45, 80));
                Box(new(45, 58, 70, 42), "literal");
                context.DrawLine(Line, new(115, 80), new(143, 80));
                Box(new(143, 58, 75, 42), "rule");
                context.DrawLine(Line, new(143, 80), new(143, 130));
                context.DrawLine(Line, new(143, 130), new(218, 130));
                context.DrawLine(Line, new(218, 130), new(218, 80));
                Arrow(new(218, 80), new(259, 80));
                break;
            case "swimlane":
                context.DrawRectangle(null, Line, new Rect(12, 20, 254, 55));
                context.DrawRectangle(null, Line, new Rect(12, 75, 254, 55));
                Box(new(32, 34, 66, 29), "Task");
                Box(new(166, 89, 76, 29), "Review");
                Arrow(new(98, 49), new(166, 103));
                break;
            case "usecase":
                context.DrawEllipse(Paper, Line, new(40, 41), 12, 12);
                context.DrawLine(Line, new(40, 53), new(40, 99));
                context.DrawLine(Line, new(17, 69), new(63, 69));
                context.DrawLine(Line, new(40, 99), new(20, 127));
                context.DrawLine(Line, new(40, 99), new(60, 127));
                context.DrawRectangle(null, Line, new Rect(122, 24, 140, 106));
                context.DrawEllipse(Blue, Line, new(191, 77), 53, 29);
                Arrow(new(66, 77), new(137, 77));
                break;
            case "agentflow":
                context.DrawRectangle(null, Line, new Rect(20, 18, 230, 118));
                Box(new(37, 38, 72, 36), "Task"); Box(new(147, 38, 72, 36), "Tool");
                Box(new(92, 92, 82, 30), "Action");
                Arrow(new(109, 56), new(147, 56)); Arrow(new(182, 74), new(150, 92));
                break;
            case "eventmodeling":
                context.DrawLine(Line, new(15, 52), new(260, 52));
                context.DrawLine(Line, new(15, 97), new(260, 97));
                Box(new(20, 17, 68, 30), "UI");
                Box(new(102, 60, 68, 30), "Cmd");
                Box(new(186, 105, 68, 30), "Event");
                Arrow(new(88, 35), new(102, 75)); Arrow(new(170, 75), new(186, 120));
                break;
            case "zenuml":
                Box(new(22, 14, 60, 26), "A"); Box(new(174, 14, 60, 26), "B");
                context.DrawLine(Line, new(52, 40), new(52, 142)); context.DrawLine(Line, new(204, 40), new(204, 142));
                Arrow(new(52, 65), new(204, 65)); context.DrawRectangle(Blue, Line, new Rect(199, 65, 10, 49));
                Arrow(new(204, 118), new(52, 118));
                break;
        }

        void Box(Rect bounds, string label, double radius = 3)
        {
            context.DrawRectangle(Blue, Line, bounds, radius, radius);
            Text(label, bounds.Center.X, bounds.Center.Y - 8);
        }
        void Card(Rect bounds, string name, string attribute, string operation)
        {
            context.DrawRectangle(Brushes.White, Line, bounds);
            context.FillRectangle(Blue, new Rect(bounds.X + 1, bounds.Y + 1, bounds.Width - 2, 28));
            context.DrawLine(Line, new(bounds.X, bounds.Y + 29), new(bounds.Right, bounds.Y + 29));
            context.DrawLine(Line, new(bounds.X, bounds.Y + 60), new(bounds.Right, bounds.Y + 60));
            Text(name, bounds.Center.X, bounds.Y + 8, 12);
            Text(attribute, bounds.Center.X, bounds.Y + 38, 11);
            Text(operation, bounds.Center.X, bounds.Y + 70, 11);
        }
        void Text(string label, double x, double y, double fontSize = 12)
        {
            var text = new FormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, fontSize, Ink);
            context.DrawText(text, new(x - text.Width / 2, y));
        }
        void Arrow(Point from, Point to)
        {
            context.DrawLine(Line, from, to);
            var direction = to - from;
            var length = Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
            var dx = direction.X / length;
            var dy = direction.Y / length;
            context.DrawGeometry(Ink, null, Polygon(to,
                new(to.X - dx * 6 - dy * 3, to.Y - dy * 6 + dx * 3),
                new(to.X - dx * 6 + dy * 3, to.Y - dy * 6 - dx * 3)));
        }
    }

    private static StreamGeometry Polygon(params Point[] points)
    {
        var geometry = new StreamGeometry();
        using var path = geometry.Open();
        path.BeginFigure(points[0], true);
        foreach (var point in points.Skip(1)) path.LineTo(point);
        path.EndFigure(true);
        return geometry;
    }
}
