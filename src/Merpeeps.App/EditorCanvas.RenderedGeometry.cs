using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Merpeeps.Core.Model;
using Merpeeps.Core.Parsing;

namespace Merpeeps.App;

public sealed partial class EditorCanvas
{
    private static void DrawRenderedText(DrawingContext context, RenderedText label, IBrush ink)
    {
        if (label.Background is { } background && label.Lines.Count > 0)
        {
            var left = label.Lines.Min(l => l.Bounds.X); var top = label.Lines.Min(l => l.Bounds.Y);
            var right = label.Lines.Max(l => l.Bounds.X + l.Bounds.Width); var bottom = label.Lines.Max(l => l.Bounds.Y + l.Bounds.Height);
            var fill = SequenceColors.TryParse(background, out var color) ? Brush(color) : new SolidColorBrush(ParseColor(background, Colors.Transparent));
            context.DrawRectangle(fill, null, new(left - 3, top - 2, right - left + 6, bottom - top + 4));
        }
        var family = label.FontFamily.Split(',')[0].Trim().Trim('\'', '"');
        var face = new Typeface(family is "system-ui" or "sans-serif" ? FontFamily.Default : new FontFamily(family),
            FontStyle.Normal, (FontWeight)label.FontWeight);
        foreach (var line in label.Lines)
        {
            var text = new FormattedText(line.Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, label.FontSize, ink);
            context.DrawText(text, new(line.Bounds.X + (line.Bounds.Width - text.Width) / 2,
                line.Bounds.Y + (line.Bounds.Height - text.Height) / 2));
        }
    }

    private void DrawGraphRoute(DrawingContext context, IReadOnlyList<DiagramPoint> route, IPen pen)
    {
        if (route.Count < 2) return;
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            path.BeginFigure(new(route[0].X, route[0].Y), false);
            for (var i = 1; i < route.Count; i++) path.LineTo(new(route[i].X, route[i].Y));
            path.EndFigure(false);
        }
        context.DrawGeometry(null, pen, geometry);
    }
}
