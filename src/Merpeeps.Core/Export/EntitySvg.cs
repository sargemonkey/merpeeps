using System.Text;
using Merpeeps.Core.Model;

namespace Merpeeps.Core.Export;

public static partial class SvgExporter
{
    private static void WriteEntityNode(StringBuilder output, DiagramNode node, Palette palette)
    {
        var style = EntityDiagramStyle.Resolve(node);
        var fill = style.Fill?.Css ?? palette.NodeFill;
        var stroke = style.Stroke?.Css ?? palette.NodeStroke;
        var ink = style.TextColor?.Css ?? Ink(fill);
        var content = EntityDiagramText.Content(node);
        var header = style.FontSize * 1.4 + 16;
        var rowHeight = style.FontSize * 1.4 + 8;
        output.Append($"<g data-entity=\"{Esc(node.Id)}\" font-family=\"{Esc(style.FontFamily)}\">\n");
        output.Append($"<rect x=\"{N(node.X)}\" y=\"{N(node.Y)}\" width=\"{N(node.Width)}\" height=\"{N(node.Height)}\" fill=\"{Esc(fill)}\" stroke=\"{Esc(stroke)}\" stroke-width=\"{N(style.StrokeWidth)}\"");
        if (style.DashPattern.Count > 0) output.Append(" stroke-dasharray=\"").Append(string.Join(",", style.DashPattern.Select(N))).Append('"');
        output.Append("/>\n");
        output.Append($"<text x=\"{N(node.X + node.Width / 2)}\" y=\"{N(node.Y + style.FontSize + 8)}\" text-anchor=\"middle\" font-size=\"{N(style.FontSize)}\" fill=\"{Esc(ink)}\">");
        foreach (var run in content.NameRuns)
            output.Append($"<tspan font-weight=\"{(run.Bold ? "bold" : "normal")}\" font-style=\"{(run.Italic ? "italic" : "normal")}\">{Esc(run.Text)}</tspan>");
        output.Append("</text>\n");
        if (content.Attributes.Count > 0)
            output.Append($"<line x1=\"{N(node.X)}\" y1=\"{N(node.Y + header)}\" x2=\"{N(node.X + node.Width)}\" y2=\"{N(node.Y + header)}\" stroke=\"{Esc(stroke)}\"/>\n");
        var columns = EntityDiagramGeometry.Columns(node, style.FontSize);
        for (var index = 0; index < content.Attributes.Count; index++)
        {
            var attribute = content.Attributes[index];
            var top = node.Y + header + index * rowHeight;
            output.Append($"<g data-attribute=\"{Esc(attribute.Id)}\">");
            var values = new[] { attribute.Type, attribute.Name, attribute.Keys, attribute.Comment ?? "" };
            var x = node.X;
            for (var col = 0; col < values.Length; col++)
            {
                ClassText(output, values[col], x + 7, top + 4, style.FontSize, ink, "start");
                x += columns[col];
                if (col < values.Length - 1)
                    output.Append($"<line x1=\"{N(x)}\" y1=\"{N(top)}\" x2=\"{N(x)}\" y2=\"{N(top + rowHeight)}\" stroke=\"{Esc(stroke)}\"/>\n");
            }
            output.Append("</g>");
            if (index < content.Attributes.Count - 1)
                output.Append($"<line x1=\"{N(node.X)}\" y1=\"{N(top + rowHeight)}\" x2=\"{N(node.X + node.Width)}\" y2=\"{N(top + rowHeight)}\" stroke=\"{Esc(stroke)}\"/>\n");
        }
        output.Append("</g>\n");
    }

    private static void WriteEntityGroups(StringBuilder output, DiagramModel model, Palette palette)
    {
        foreach (var group in model.Subgraphs)
        {
            var r = EntityDiagramGeometry.GroupBounds(model, group);
            output.Append($"<g data-er-subgraph=\"{Esc(group.Id)}\"><rect x=\"{N(r.X)}\" y=\"{N(r.Y)}\" width=\"{N(r.Width)}\" height=\"{N(r.Height)}\" fill=\"none\" stroke=\"{palette.NodeStroke}\" stroke-dasharray=\"4,2\"/>\n");
            ClassText(output, group.Title ?? group.Id, r.X + 10, r.Y + 8, 13, palette.MutedText, "start");
            output.Append("</g>\n");
        }
    }

    private static void WriteEntityEdge(StringBuilder output, DiagramModel model, DiagramEdge edge, Palette palette)
    {
        var relation = edge.EntityRelationship!;
        var route = EntityDiagramGeometry.Route(model, edge);
        output.Append($"<g data-er-relationship=\"{Esc(edge.Key)}\"><polyline points=\"")
            .Append(string.Join(" ", route.Select(p => $"{N(p.X)},{N(p.Y)}")))
            .Append($"\" fill=\"none\" stroke=\"{palette.Edge}\"");
        if (!relation.Identifying) output.Append(" stroke-dasharray=\"5,4\"");
        output.Append("/>\n");
        foreach (var marker in new[]
        {
            EntityDiagramGeometry.Marker(relation.FromCardinality, route[0], route[1]),
            EntityDiagramGeometry.Marker(relation.ToCardinality, route[^1], route[^2]),
        })
        {
            foreach (var line in marker.Lines)
                output.Append($"<line x1=\"{N(line.Start.X)}\" y1=\"{N(line.Start.Y)}\" x2=\"{N(line.End.X)}\" y2=\"{N(line.End.Y)}\" stroke=\"{palette.Edge}\"/>\n");
            foreach (var circle in marker.Circles)
                output.Append($"<circle cx=\"{N(circle.Center.X)}\" cy=\"{N(circle.Center.Y)}\" r=\"{N(circle.Radius)}\" fill=\"{palette.Background}\" stroke=\"{palette.Edge}\"/>\n");
        }
        var mid = route[route.Count / 2];
        if (route.Count == 2) mid = new((route[0].X + route[1].X) / 2, (route[0].Y + route[1].Y) / 2);
        if (!string.IsNullOrEmpty(edge.Label)) ClassText(output, edge.Label, mid.X, mid.Y - 20, 12, palette.MutedText, "middle");
        output.Append("</g>\n");
    }
}
