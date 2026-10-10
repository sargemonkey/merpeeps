using System.Text.Json.Nodes;
using Merpeeps.Core.Model;

namespace Merpeeps.Core.Layout;

public static class RequirementDiagramLayout
{
    public static DrawingScene Create(DiagramModel model, DrawingTextMeasure? measure = null, string? themeOverride = null)
    {
        var requirements = model.Requirements ?? throw new ArgumentException("Requirement content is missing.");
        Parsing.RequirementDiagramAdapter.Validate(requirements);
        var b = new DataDiagramLayouts.Builder(measure, requirements.ThemeVariables, requirements.GlobalConfiguration, themeOverride ?? model.Theme);
        var layout = new DiagramModel { Direction = requirements.Direction };
        var rows = new Dictionary<string, string[]>();
        var appearances = new Dictionary<string, ClassResolvedStyle>();
        foreach (var item in requirements.Items)
        {
            var content = item.Kind == RequirementKind.Element
                ? new[] { "<<element>>", item.Name, "Type: " + item.ElementType, "Doc: " + item.DocumentReference }
                : new[] { "<<" + item.Kind + ">>", item.Name, "Id: " + item.DisplayId, "Text: " + item.Text,
                    "Risk: " + item.Risk, "Verify: " + item.Verification };
            rows[item.Id] = content;
            var node = new DiagramNode { Id = item.Id };
            var appearance = RequirementAppearance.Resolve(requirements, item);
            appearances[item.Id] = appearance;
            var size = content.Select((text, i) => b.MeasureWrapped(text, appearance.FontSize, 220, i == 1 || appearance.Bold, appearance.Italic, true, appearance.FontFamily)).ToArray();
            node.Width = Math.Max(260, size.Max(s => s.Width) + 30);
            node.Height = size.Sum(s => s.Height) + 42;
            layout.Nodes.Add(node);
        }
        layout.Edges.AddRange(requirements.Relations.Select(r => new DiagramEdge { From = r.From, To = r.To }));
        LayoutEngines.Apply(layout, "layered");
        foreach (var relation in requirements.Relations)
        {
            var a = layout.Node(relation.From)!; var z = layout.Node(relation.To)!;
            DiagramPoint start; DiagramPoint end;
            if (requirements.Direction is Direction.LR or Direction.RL)
            {
                var right = z.X > a.X;
                start = new(right ? a.X + a.Width : a.X, a.Y + a.Height / 2);
                end = new(right ? z.X : z.X + z.Width, z.Y + z.Height / 2);
            }
            else
            {
                var down = z.Y > a.Y;
                start = new(a.X + a.Width / 2, down ? a.Y + a.Height : a.Y);
                end = new(z.X + z.Width / 2, down ? z.Y : z.Y + z.Height);
            }
            var dx = start.X - end.X; var dy = start.Y - end.Y; var length = Math.Sqrt(dx * dx + dy * dy);
            if (relation.Kind == RequirementRelationKind.Contains)
            {
                b.Line(relation.Id, "Relationship", relation.Id, [start, end], b.Ink, 1.5);
                b.Circle(relation.Id + "-contains", "Relationship", relation.Id, start, 6, b.Background, b.Ink);
                b.Line(relation.Id + "-plus-h", "Relationship", relation.Id, [new(start.X - 4, start.Y), new(start.X + 4, start.Y)], b.Ink, 1);
                b.Line(relation.Id + "-plus-v", "Relationship", relation.Id, [new(start.X, start.Y - 4), new(start.X, start.Y + 4)], b.Ink, 1);
            }
            else if (length > 0)
            {
                for (var distance = 0d; distance < length; distance += 17)
                {
                    var stop = Math.Min(length, distance + 10);
                    b.Line(relation.Id + "-dash-" + distance, "Relationship", relation.Id,
                        [new(start.X - dx / length * distance, start.Y - dy / length * distance), new(start.X - dx / length * stop, start.Y - dy / length * stop)], b.Ink, 1.5);
                }
                b.Line(relation.Id + "-head", "Relationship", relation.Id,
                    [new(end.X + dx / length * 10 - dy / length * 5, end.Y + dy / length * 10 + dx / length * 5), end,
                    new(end.X + dx / length * 10 + dy / length * 5, end.Y + dy / length * 10 - dx / length * 5)], b.Ink, 1.5);
            }
            var text = "<<" + relation.Kind.ToString().ToLowerInvariant() + ">>";
            b.Text(relation.Id + "-label", "Relationship", relation.Id, text, 12, (start.X + end.X) / 2 + 6, (start.Y + end.Y) / 2 - 16, 160);
            b.Hit("Relationship", relation.Id, new(Math.Min(start.X, end.X) - 6, Math.Min(start.Y, end.Y) - 6, Math.Abs(start.X - end.X) + 12, Math.Abs(start.Y - end.Y) + 12));
        }
        foreach (var item in requirements.Items)
        {
            var node = layout.Node(item.Id)!; var style = appearances[item.Id];
            var fill = style.Fill ?? b.Tint(b.Scale(0));
            var stroke = style.Stroke ?? b.Ink;
            b.Box(item.Id, "Requirement", item.Id, new(node.X, node.Y, node.Width, node.Height), fill, stroke, style.StrokeWidth, style.DashPattern.Count > 0);
            var y = node.Y + 10;
            foreach (var (text, index) in rows[item.Id].Select((text, i) => (text, i)))
            {
                b.Text(item.Id + "-" + index, "Requirement", item.Id, text, style.FontSize, node.X + 15, y, node.Width - 30, index == 1 || style.Bold, style.TextColor ?? b.Ink, style.Italic, true, style.FontFamily);
                y += b.MeasureWrapped(text, style.FontSize, node.Width - 30, index == 1 || style.Bold, style.Italic, true, style.FontFamily).Height + 4;
                if (index == 1) b.Line(item.Id + "-divider", "Requirement", item.Id, [new(node.X, y), new(node.X + node.Width, y)], stroke, 1);
            }
            b.Hit("Requirement", item.Id, new(node.X, node.Y, node.Width, node.Height));
        }
        return b.Finish();
    }
}
