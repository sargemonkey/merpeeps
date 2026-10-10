using Merpeeps.Core.Model;
using Merpeeps.Core.Parsing;

namespace Merpeeps.Core.Layout;

public sealed record RailroadLayoutResult(DrawingScene Scene, IReadOnlyDictionary<string, DiagramRect> ExpressionBounds,
    IReadOnlyDictionary<string, DiagramRect> RuleBounds);
public static class RailroadDiagramLayout
{
    private sealed record Metrics(double Width, double Up, double Down);
    public static RailroadLayoutResult Create(DiagramModel model, DrawingTextMeasure? measure = null, string? themeOverride = null)
    {
        var document = model.Railroad ?? throw new ArgumentException("Railroad content is missing."); RailroadValidation.Check(document);
        var b = new DataDiagramLayouts.Builder(measure, document.ThemeVariables, document.GlobalConfiguration, themeOverride ?? model.Theme);
        var fontSize = Number("fontSize", 14); var padding = Number("padding", 10); var gap = Number("horizontalSeparation", 10);
        var vertical = Number("verticalSeparation", 8); var arc = Number("arcRadius", 10); var strokeWidth = Number("strokeWidth", 2);
        var family = document.Configuration["fontFamily"]?.ToString() ?? document.GlobalConfiguration["fontFamily"]?.ToString() ?? "monospace";
        if (fontSize <= 0 || arc <= 0) throw new ArgumentException("Railroad font size and arc radius must be positive.");
        var line = Paint("lineColor", b.Ink); var metrics = new Dictionary<string, Metrics>();
        var expressions = new Dictionary<string, DiagramRect>(); var rules = new Dictionary<string, DiagramRect>();
        string Display(RailroadExpression node) => node.Kind switch
        {
            RailroadExpressionKind.AnyCharacter => ".", RailroadExpressionKind.AndPredicate => "&",
            RailroadExpressionKind.NotPredicate => "!", RailroadExpressionKind.Exception => "except", _ => node.Text,
        };
        DrawingTextSize MeasureText(string value) => b.MeasureWrapped(value, fontSize, double.MaxValue, family: family);
        Metrics Measure(RailroadExpression expression)
        {
            var children = expression.Children.Select(Measure).ToArray();
            Metrics result;
            if (children.Length == 0)
            {
                var text = MeasureText(Display(expression));
                result = new(Math.Max(24, text.Width + padding * 2), Math.Max(15, text.Height / 2 + padding), Math.Max(15, text.Height / 2 + padding));
            }
            else if (expression.Kind is RailroadExpressionKind.Choice or RailroadExpressionKind.OrderedChoice)
                result = new(children.Max(c => c.Width) + arc * 4, children[0].Up,
                    children[0].Down + children.Skip(1).Sum(c => c.Up + c.Down + vertical + arc));
            else if (expression.Kind == RailroadExpressionKind.Optional)
                result = new(children[0].Width + arc * 4, arc, children[0].Up + children[0].Down + arc * 2 + vertical);
            else if (expression.Kind == RailroadExpressionKind.Repeat)
                result = new(children[0].Width + arc * 4, children[0].Up + (expression.Minimum == 0 ? arc * 2 : 0),
                    children[0].Down + arc * 2 + fontSize + vertical);
            else
            {
                var extra = expression.Kind is RailroadExpressionKind.AndPredicate or RailroadExpressionKind.NotPredicate or RailroadExpressionKind.Exception ? MeasureText(Display(expression)).Width + padding * 2 + gap : 0;
                result = new(children.Sum(c => c.Width) + gap * (children.Length - 1) + extra, children.Max(c => c.Up), children.Max(c => c.Down));
            }
            metrics[expression.Id] = result; return result;
        }
        foreach (var rule in document.Rules) Measure(rule.Expression);
        var y = document.Title.Length == 0 ? 20d : 65d;
        if (document.Title.Length > 0) b.Text("title", "RailroadDiagram", "", document.Title, fontSize + 6, 20, 15, 900, true, family: family);
        foreach (var rule in document.Rules)
        {
            var m = metrics[rule.Expression.Id];
            b.Text(rule.Id + "-name", "RailroadRule", rule.Id, rule.Name, fontSize, 20, y, 600, true, Paint("ruleNameColor", b.Ink), family: family);
            var baseline = y + fontSize + 15 + m.Up; var x = 50d;
            rules[rule.Id] = new(15, y, m.Width + 85, m.Up + m.Down + fontSize + 25);
            b.Hit("RailroadRule", rule.Id, rules[rule.Id]);
            Draw(rule.Expression, rule.Id, x, baseline);
            Rail(rule.Id + "-start", rule.Id, [new(25, baseline), new(x, baseline)]);
            Rail(rule.Id + "-end", rule.Id, [new(x + m.Width, baseline), new(x + m.Width + 25, baseline)]);
            if (document.Configuration["showMarkers"]?.GetValue<bool>() != false)
            {
                var radius = Number("markerRadius", 5);
                b.Circle(rule.Id + "-start-marker", "RailroadRule", rule.Id, new(25, baseline), radius, Paint("markerFill", line), line);
                b.Circle(rule.Id + "-end-marker", "RailroadRule", rule.Id, new(x + m.Width + 25, baseline), radius, Paint("markerFill", line), line);
            }
            y += m.Up + m.Down + fontSize + 50;
        }
        var scene = b.Finish(); var offset = b.OriginOffset;
        return new(scene with { Figures = scene.Figures.Select(f => f with { StrokeWidth = strokeWidth }).ToArray() },
            expressions.ToDictionary(p => p.Key, p => Shift(p.Value)), rules.ToDictionary(p => p.Key, p => Shift(p.Value)));
        DiagramRect Shift(DiagramRect r) => r with { X = r.X + offset.X, Y = r.Y + offset.Y };
        void Draw(RailroadExpression node, string rule, double x, double baseline)
        {
            var m = metrics[node.Id]; var box = new DiagramRect(x, baseline - m.Up, m.Width, m.Up + m.Down);
            expressions[node.Id] = box; b.Hit("RailroadExpression", node.Id, box, rule);
            if (node.Children.Count == 0)
            {
                var terminal = node.Kind is RailroadExpressionKind.Terminal or RailroadExpressionKind.NumericLiteral;
                var special = node.Kind is RailroadExpressionKind.Special or RailroadExpressionKind.AnyCharacter;
                var fill = Paint(terminal ? "terminalFill" : special ? "specialFill" : "nonTerminalFill", terminal ? b.Tint(b.Scale(0)) : b.Background);
                var stroke = Paint(terminal ? "terminalStroke" : special ? "specialStroke" : "nonTerminalStroke", line);
                if (terminal) b.Path(node.Id, "RailroadExpression", node.Id, Rounded(box, Math.Min(10, box.Height / 2)), fill, stroke);
                else b.Box(node.Id, "RailroadExpression", node.Id, box, fill, stroke);
                var text = Display(node); var t = MeasureText(text);
                b.Text(node.Id + "-label", "RailroadExpression", node.Id, text, fontSize, x + (m.Width - t.Width) / 2, baseline - t.Height / 2, double.MaxValue,
                    color: Paint(terminal ? "terminalTextColor" : special ? "commentTextColor" : "nonTerminalTextColor", b.Ink), family: family);
                return;
            }
            if (node.Kind is RailroadExpressionKind.Choice or RailroadExpressionKind.OrderedChoice)
            {
                var currentY = baseline;
                for (var i = 0; i < node.Children.Count; i++)
                {
                    var child = node.Children[i]; var childSize = metrics[child.Id];
                    if (i > 0) currentY += metrics[node.Children[i - 1].Id].Down + vertical + arc + childSize.Up;
                    var left = x + arc * 2; Draw(child, rule, left, currentY);
                    if (i == 0)
                    {
                        Rail(node.Id + "-in0", node.Id, [new(x, baseline), new(left, baseline)]);
                        Rail(node.Id + "-out0", node.Id, [new(left + childSize.Width, baseline), new(x + m.Width, baseline)]);
                    }
                    else
                    {
                        Curve(node.Id + "-in" + i, node.Id, new(x, baseline), new(left, currentY), true);
                        Rail(node.Id + "-after" + i, node.Id, [new(left + childSize.Width, currentY), new(x + m.Width - arc * 2, currentY)]);
                        Curve(node.Id + "-out" + i, node.Id, new(x + m.Width - arc * 2, currentY), new(x + m.Width, baseline), false);
                    }
                }
                return;
            }
            if (node.Kind == RailroadExpressionKind.Optional)
            {
                var child = node.Children[0]; var inner = metrics[child.Id]; var childY = baseline + arc + vertical + inner.Up;
                Rail(node.Id + "-skip", node.Id, [new(x, baseline), new(x + m.Width, baseline)]);
                Curve(node.Id + "-in", node.Id, new(x, baseline), new(x + arc * 2, childY), true);
                Draw(child, rule, x + arc * 2, childY);
                Curve(node.Id + "-out", node.Id, new(x + arc * 2 + inner.Width, childY), new(x + m.Width, baseline), false);
                return;
            }
            if (node.Kind == RailroadExpressionKind.Repeat)
            {
                var child = node.Children[0]; var inner = metrics[child.Id]; var left = x + arc * 2; var right = left + inner.Width;
                Rail(node.Id + "-in", node.Id, [new(x, baseline), new(left, baseline)]);
                Draw(child, rule, left, baseline);
                Rail(node.Id + "-out", node.Id, [new(right, baseline), new(x + m.Width, baseline)]);
                var lower = baseline + inner.Down + arc + vertical;
                b.Path(node.Id + "-loop", "RailroadExpression", node.Id, [
                    new(DrawingPathKind.Move, new(right, baseline)),
                    new(DrawingPathKind.Cubic, new(right, lower), new(right + arc * 2, baseline), new(right + arc * 2, lower)),
                    new(DrawingPathKind.Line, new(left, lower)),
                    new(DrawingPathKind.Cubic, new(left, baseline), new(left - arc * 2, lower), new(left - arc * 2, baseline))], null, line);
                var count = node.Maximum is null ? node.Minimum == 0 ? "zero or more" : "one or more" :
                    node.Minimum == node.Maximum ? node.Minimum + " times" : node.Minimum + ".." + node.Maximum;
                b.Text(node.Id + "-count", "RailroadExpression", node.Id, count, fontSize * .8, left, lower + 3, inner.Width, color: line, family: family);
                if (node.Minimum == 0)
                {
                    var upper = baseline - inner.Up - arc;
                    b.Path(node.Id + "-bypass", "RailroadExpression", node.Id, [
                        new(DrawingPathKind.Move, new(x, baseline)),
                        new(DrawingPathKind.Cubic, new(left, upper), new(x + arc, baseline), new(x + arc, upper)),
                        new(DrawingPathKind.Line, new(right, upper)),
                        new(DrawingPathKind.Cubic, new(x + m.Width, baseline), new(right + arc, upper), new(right + arc, baseline))], null, line);
                }
                return;
            }
            var cursor = x;
            for (var i = 0; i < node.Children.Count; i++)
            {
                if (node.Kind is RailroadExpressionKind.AndPredicate or RailroadExpressionKind.NotPredicate || node.Kind == RailroadExpressionKind.Exception && i == 1)
                {
                    var text = Display(node); var t = MeasureText(text); var textWidth = t.Width + padding * 2;
                    b.Box(node.Id + "-operator", "RailroadExpression", node.Id, new(cursor, baseline - t.Height / 2 - padding, textWidth, t.Height + padding * 2), Paint("specialFill", b.Tint(b.Scale(2))), line);
                    b.Text(node.Id + "-operator-label", "RailroadExpression", node.Id, text, fontSize, cursor + padding, baseline - t.Height / 2, double.MaxValue, family: family);
                    cursor += textWidth; Rail(node.Id + "-operator-rail", node.Id, [new(cursor, baseline), new(cursor + gap, baseline)]); cursor += gap;
                }
                var child = node.Children[i]; Draw(child, rule, cursor, baseline); cursor += metrics[child.Id].Width;
                if (i + 1 < node.Children.Count) { Rail(node.Id + "-join" + i, node.Id, [new(cursor, baseline), new(cursor + gap, baseline)]); cursor += gap; }
            }
        }
        void Rail(string id, string owner, IReadOnlyList<DiagramPoint> points) => b.Line(id, "RailroadExpression", owner, points, line, strokeWidth);
        void Curve(string id, string owner, DiagramPoint start, DiagramPoint end, bool entry)
        {
            var middleX = (start.X + end.X) / 2;
            b.Path(id, "RailroadExpression", owner, [new(DrawingPathKind.Move, start),
                new(DrawingPathKind.Cubic, end, new(middleX, start.Y), new(middleX, end.Y))], null, line);
        }
        double Number(string key, double fallback) => document.Configuration[key] is { } value ? ChartConfiguration.ParseNumber(value.ToString()) : fallback;
        ClassColor Paint(string key, ClassColor fallback) => document.Configuration[key] is { } value ?
            SequenceColors.TryParse(value.ToString(), out var color) ? color : throw new ArgumentException("Invalid Railroad paint: " + key) : fallback;
    }
    private static IReadOnlyList<DrawingPathPart> Rounded(DiagramRect r, double radius)
    {
        const double k = .5522847498; var x = r.X; var y = r.Y; var right = x + r.Width; var bottom = y + r.Height; var a = radius;
        return [new(DrawingPathKind.Move, new(x + a, y)), new(DrawingPathKind.Line, new(right - a, y)),
            new(DrawingPathKind.Cubic, new(right, y + a), new(right - a + k * a, y), new(right, y + a - k * a)),
            new(DrawingPathKind.Line, new(right, bottom - a)), new(DrawingPathKind.Cubic, new(right - a, bottom), new(right, bottom - a + k * a), new(right - a + k * a, bottom)),
            new(DrawingPathKind.Line, new(x + a, bottom)), new(DrawingPathKind.Cubic, new(x, bottom - a), new(x + a - k * a, bottom), new(x, bottom - a + k * a)),
            new(DrawingPathKind.Line, new(x, y + a)), new(DrawingPathKind.Cubic, new(x + a, y), new(x, y + a - k * a), new(x + a - k * a, y)), new(DrawingPathKind.Close, default)];
    }
}
