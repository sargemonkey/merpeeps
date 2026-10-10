using System.Text;
using Merpeeps.Core.Layout;
using Merpeeps.Core.Model;

namespace Merpeeps.Core.Export;

public static partial class SvgExporter
{
    private static string SequenceSvg(DiagramModel model, string? theme)
    {
        var layout = SequenceDiagramLayout.Create(model, themeOverride: theme);
        var output = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        output.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{N(layout.Width)}\" height=\"{N(layout.Height)}\" viewBox=\"0 0 {N(layout.Width)} {N(layout.Height)}\" data-text-metrics=\"approximate\">\n");
        foreach (var figure in layout.Figures)
        {
            var stroke = figure.Stroke?.Css ?? "none";
            var fill = figure.Fill?.Css ?? "none";
            var attributes = $"data-sequence-owner=\"{Esc(figure.OwnerId)}\" data-sequence-kind=\"{figure.ElementKind}\" fill=\"{Esc(fill)}\" stroke=\"{Esc(stroke)}\" stroke-width=\"{N(figure.StrokeWidth)}\"";
            if (figure.Dashed) attributes += " stroke-dasharray=\"4,4\"";
            var bounds = figure.Bounds;
            switch (figure.Kind)
            {
                case SequenceFigureKind.Rectangle:
                    output.Append($"<rect x=\"{N(bounds.X)}\" y=\"{N(bounds.Y)}\" width=\"{N(bounds.Width)}\" height=\"{N(bounds.Height)}\" {attributes}/>\n");
                    break;
                case SequenceFigureKind.Ellipse:
                    output.Append($"<ellipse cx=\"{N(bounds.X + bounds.Width / 2)}\" cy=\"{N(bounds.Y + bounds.Height / 2)}\" rx=\"{N(bounds.Width / 2)}\" ry=\"{N(bounds.Height / 2)}\" {attributes}/>\n");
                    break;
                default:
                    output.Append(figure.Kind == SequenceFigureKind.Polygon ? "<polygon " : "<polyline ");
                    output.Append("points=\"").Append(string.Join(" ", figure.Points.Select(p => $"{N(p.X)},{N(p.Y)}")))
                        .Append("\" ").Append(attributes).Append("/>\n");
                    break;
            }
        }
        foreach (var label in layout.Labels)
        {
            var x = label.Alignment switch
            {
                "center" => label.Bounds.X + label.Bounds.Width / 2,
                "right" => label.Bounds.X + label.Bounds.Width,
                _ => label.Bounds.X,
            };
            var anchor = label.Alignment switch { "center" => "middle", "right" => "end", _ => "start" };
            output.Append($"<text data-sequence-owner=\"{Esc(label.OwnerId)}\" data-sequence-kind=\"{label.ElementKind}\" font-family=\"{Esc(label.Font.Family)}\" font-size=\"{N(label.Font.Size)}\" font-weight=\"{label.Font.Weight}\" fill=\"{Esc(label.Color.Css)}\" text-anchor=\"{anchor}\">");
            var lines = label.Text.Split('\n');
            for (var index = 0; index < lines.Length; index++)
                output.Append($"<tspan x=\"{N(x)}\" y=\"{N(label.Bounds.Y + label.Font.Size + index * label.Bounds.Height / lines.Length)}\">{Esc(lines[index])}</tspan>");
            output.Append("</text>\n");
        }
        return output.Append("</svg>\n").ToString();
    }
}
