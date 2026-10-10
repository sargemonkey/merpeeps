using System.Text;
using Merpeeps.Core.Model;

namespace Merpeeps.Core.Export;

public static partial class SvgExporter
{
    private static void WriteClassNode(StringBuilder sb, DiagramModel model, DiagramNode node, Palette palette)
    {
        var definition = node.Class!;
        var style = ClassDiagramStyle.Resolve(node);
        var fill = style.Fill?.Css ?? palette.NodeFill;
        var stroke = style.Stroke?.Css ?? palette.NodeStroke;
        var text = style.TextColor?.Css ?? Ink(fill);
        var dash = style.DashPattern.Count == 0 ? "" : " stroke-dasharray=\"" + string.Join(",", style.DashPattern.Select(N)) + "\"";
        sb.Append($"<g id=\"{Esc(node.Id)}\" data-class-kind=\"{definition.Kind}\" font-family=\"{Esc(style.FontFamily)}\">\n");
        if (definition.Kind == ClassNodeKind.Lollipop)
        {
            var center = ClassDiagramGeometry.Center(node);
            sb.Append($"<circle cx=\"{N(center.X)}\" cy=\"{N(center.Y)}\" r=\"8\" fill=\"{Esc(fill)}\" stroke=\"{Esc(stroke)}\" stroke-width=\"{N(style.StrokeWidth)}\"/>\n");
            ClassText(sb, node.Display, node.X + node.Width / 2, node.Y + 30, style.FontSize, text, "middle");
        }
        else if (definition.Kind == ClassNodeKind.Note)
        {
            sb.Append($"<path d=\"M{N(node.X)},{N(node.Y)} H{N(node.X + node.Width - 12)} L{N(node.X + node.Width)},{N(node.Y + 12)} V{N(node.Y + node.Height)} H{N(node.X)} Z\" fill=\"{Esc(fill)}\" stroke=\"{Esc(stroke)}\"/>\n");
            for (var i = 0; i < node.Display.Split('\n').Length; i++)
                ClassText(sb, node.Display.Split('\n')[i], node.X + 10, node.Y + 10 + i * style.FontSize * 1.4, style.FontSize, text, "start");
        }
        else
        {
            sb.Append($"<rect x=\"{N(node.X)}\" y=\"{N(node.Y)}\" width=\"{N(node.Width)}\" height=\"{N(node.Height)}\" fill=\"{Esc(fill)}\" stroke=\"{Esc(stroke)}\" stroke-width=\"{N(style.StrokeWidth)}\"{dash}/>\n");
            var content = ClassDiagramText.Content(node, model.ClassOptions!);
            var lineHeight = style.FontSize * 1.4;
            var regions = ClassDiagramGeometry.CardRegions(node, model.ClassOptions!, lineHeight);
            void Rows(IEnumerable<ClassTextRow> rows, DiagramRect region, bool centered)
            {
                var i = 0;
                foreach (var row in rows)
                {
                    ClassText(sb, row.Text, centered ? region.X + region.Width / 2 : region.X + 10,
                        region.Y + 8 + i++ * lineHeight, style.FontSize, text, centered ? "middle" : "start",
                        row.Bold || style.Bold, row.Italic || style.Italic, row.Underline);
                }
            }
            Rows(content.Header, regions.Header, true);
            if (content.ShowMemberCompartments)
            {
                foreach (var y in new[] { regions.Attributes.Y, regions.Operations.Y })
                    sb.Append($"<line x1=\"{N(node.X)}\" y1=\"{N(y)}\" x2=\"{N(node.X + node.Width)}\" y2=\"{N(y)}\" stroke=\"{Esc(stroke)}\"/>\n");
                Rows(content.Attributes, regions.Attributes, false);
                Rows(content.Operations, regions.Operations, false);
            }
        }
        sb.Append("</g>\n");
    }

    private static void ClassText(StringBuilder sb, string label, double x, double y, double size,
        string color, string anchor, bool bold = false, bool italic = false, bool underline = false)
    {
        sb.Append($"<text x=\"{N(x)}\" y=\"{N(y + size)}\" font-size=\"{N(size)}\" fill=\"{Esc(color)}\" text-anchor=\"{anchor}\"");
        if (bold) sb.Append(" font-weight=\"bold\"");
        if (italic) sb.Append(" font-style=\"italic\"");
        if (underline) sb.Append(" text-decoration=\"underline\"");
        sb.Append('>').Append(Esc(label)).Append("</text>\n");
    }

    private static void WriteClassEdge(StringBuilder sb, DiagramModel model, DiagramEdge edge, Palette palette)
    {
        var relation = edge.ClassRelationship!;
        var from = model.Node(edge.From)!;
        var to = model.Node(edge.To)!;
        var start = ClassDiagramGeometry.Anchor(from, ClassDiagramGeometry.Center(to));
        var end = ClassDiagramGeometry.Anchor(to, ClassDiagramGeometry.Center(from));
        sb.Append($"<g data-class-relationship=\"{Esc(edge.Key)}\"><line x1=\"{N(start.X)}\" y1=\"{N(start.Y)}\" x2=\"{N(end.X)}\" y2=\"{N(end.Y)}\" stroke=\"{palette.Edge}\"");
        if (relation.Dashed) sb.Append(" stroke-dasharray=\"5,4\"");
        sb.Append("/>\n");
        foreach (var (decoration, tip, toward) in new[] { (relation.FromEnd, start, end), (relation.ToEnd, end, start) })
        {
            var marker = ClassDiagramGeometry.Marker(decoration, tip, toward);
            if (marker.Points.Count == 0) continue;
            sb.Append(marker.Closed ? "<polygon" : "<polyline");
            sb.Append(" points=\"").Append(string.Join(" ", marker.Points.Select(p => $"{N(p.X)},{N(p.Y)}"))).Append('"');
            sb.Append($" fill=\"{(marker.Filled ? palette.Edge : marker.Closed ? palette.Background : "none")}\" stroke=\"{palette.Edge}\"/>\n");
        }
        if (relation.FromMultiplicity is { } fm) ClassText(sb, fm, start.X * .85 + end.X * .15, start.Y * .85 + end.Y * .15 - 20, 12, palette.MutedText, "middle");
        if (relation.ToMultiplicity is { } tm) ClassText(sb, tm, start.X * .15 + end.X * .85, start.Y * .15 + end.Y * .85 - 20, 12, palette.MutedText, "middle");
        if (edge.Label is { } label) ClassText(sb, label, (start.X + end.X) / 2, (start.Y + end.Y) / 2 - 18, 12, palette.MutedText, "middle");
        sb.Append("</g>\n");
    }

    private static void WriteClassNamespaces(StringBuilder sb, DiagramModel model, Palette palette)
    {
        foreach (var group in model.Subgraphs)
        {
            if (model.ClassOptions?.HierarchicalNamespaces == false && !group.IsExplicit) continue;
            var bounds = GraphGroupGeometry.Bounds(model, group);
            sb.Append($"<rect data-namespace=\"{Esc(group.Id)}\" x=\"{N(bounds.X)}\" y=\"{N(bounds.Y)}\" width=\"{N(bounds.Width)}\" height=\"{N(bounds.Height)}\" fill=\"none\" stroke=\"{palette.NodeStroke}\" stroke-dasharray=\"4,2\"/>\n");
            ClassText(sb, group.Title ?? group.Id, bounds.X + 8, bounds.Y + 6, 12, palette.MutedText, "start");
        }
        foreach (var note in model.Nodes.Where(n => n.Class?.Kind == ClassNodeKind.Note && n.Class.NoteFor is not null))
        {
            var target = model.Node(note.Class!.NoteFor!);
            if (target is null) continue;
            var start = ClassDiagramGeometry.Anchor(note, ClassDiagramGeometry.Center(target));
            var end = ClassDiagramGeometry.Anchor(target, ClassDiagramGeometry.Center(note));
            sb.Append($"<line x1=\"{N(start.X)}\" y1=\"{N(start.Y)}\" x2=\"{N(end.X)}\" y2=\"{N(end.Y)}\" stroke=\"{palette.Edge}\" stroke-dasharray=\"3,3\"/>\n");
        }
    }
}
