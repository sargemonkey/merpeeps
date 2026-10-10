using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Merpeeps.Core.Model;

namespace Merpeeps.Core.Export;

/// <summary>
/// Render a diagram to a standalone SVG file.
/// </summary>
/// <remarks>
/// <para>This draws what the <em>editor</em> shows, not what mermaid would: the
/// stored positions, the Merpeeps styling, and the icons. That is deliberate. A
/// user exporting a picture wants the diagram they have been looking at, and
/// the styling and artwork are exactly the things mermaid cannot express — the
/// reason the Merpeeps layer exists. Anyone who wants mermaid's own rendering
/// already has it in the reference pane, and <c>merpeeps bake</c> for a file that
/// renders that way unaided.</para>
///
/// <para>It is written in C# with no browser, so it works in the CLI, in CI,
/// and on a machine with no web view. That is the property that made a native
/// exporter worth the effort over reading the SVG back out of the
/// renderer.</para>
///
/// <para>Text is emitted as real <c>&lt;text&gt;</c> elements rather than
/// outlines, so labels stay selectable and searchable. The cost is that
/// wrapping is approximated — the exporter has no font metrics — so a label is
/// laid out on the same simple rule the canvas uses.</para>
/// </remarks>
public static partial class SvgExporter
{
    private const double Pad = 24;
    private const double FontSize = 14;

    /// <summary>Average glyph width as a fraction of the font size.</summary>
    /// <remarks>
    /// Measured against the canvas's own default face at 14px over mixed-case
    /// text. Only used to centre labels, where being a pixel out is invisible;
    /// nothing about the layout depends on it.
    /// </remarks>
    private const double GlyphRatio = 0.55;

    /// <summary>Render <paramref name="model"/> as a complete SVG document.</summary>
    /// <param name="model">The diagram to draw.</param>
    /// <param name="theme">
    /// A mermaid theme name for the colours of unstyled nodes, or null for the
    /// exporter's own neutral defaults.
    /// </param>
    /// <param name="evaluationContext">Required for Gantt: the host-captured reference instant and timezone.</param>
    public static string ToSvg(DiagramModel model, string? theme = null, GanttEvaluationContext? evaluationContext = null)
    {
        Parsing.DiagramAdapters.ForModel(model);
        if (model.Sequence is not null) return SequenceSvg(model, theme);
        if (model.Chart is not null) return DrawingSceneSvg.Write(Layout.ChartDiagramLayout.Create(model, themeOverride: theme).Scene);
        if (model.Outline is not null || model.Temporal is not null)
            return DrawingSceneSvg.Write(Layout.DataDiagramLayouts.Create(model, themeOverride: theme).Scene);
        if (model.Packet is not null || model.Kanban is not null)
            return DrawingSceneSvg.Write(Layout.BoardPacketLayout.Create(model, themeOverride: theme));
        if (model.Requirements is not null)
            return DrawingSceneSvg.Write(Layout.RequirementDiagramLayout.Create(model, themeOverride: theme));
        if (model.GitDiagram is not null)
            return DrawingSceneSvg.Write(Layout.GitDiagramLayout.Create(model, themeOverride: theme));
        if (model.Sankey is not null || model.Treemap is not null)
            return DrawingSceneSvg.Write(Layout.WeightedDiagramLayout.Create(model, themeOverride: theme).Scene);
        if (model.Blocks is not null) return DrawingSceneSvg.Write(Layout.BlockDiagramLayout.Create(model, themeOverride: theme).Scene);
        if (model.Architecture is not null) return DrawingSceneSvg.Write(Layout.ArchitectureDiagramLayout.Create(model, themeOverride: theme).Scene);
        if (model.C4 is not null) return DrawingSceneSvg.Write(Layout.C4DiagramLayout.Create(model, themeOverride: theme).Scene);
        if (model.Gantt is not null) return DrawingSceneSvg.Write(Layout.GanttDiagramLayout.Create(model,
            evaluationContext ?? throw new ArgumentException("Gantt export requires an explicit reference clock and timezone."), themeOverride: theme).Scene);
        if (model.Wardley is not null) return DrawingSceneSvg.Write(Layout.StrategyDiagramLayouts.Wardley(model, themeOverride: theme).Scene);
        if (model.Ishikawa is not null) return DrawingSceneSvg.Write(Layout.StrategyDiagramLayouts.Ishikawa(model, themeOverride: theme).Scene);
        if (model.Cynefin is not null) return DrawingSceneSvg.Write(Layout.StrategyDiagramLayouts.Cynefin(model, themeOverride: theme).Scene);
        if (model.Venn is not null) return DrawingSceneSvg.Write(Layout.VennDiagramLayout.Create(model, themeOverride: theme).Scene);
        if (model.Railroad is not null) return DrawingSceneSvg.Write(Layout.RailroadDiagramLayout.Create(model, themeOverride: theme).Scene);
        if (model.Swimlane is not null) return DrawingSceneSvg.Write(Layout.SwimlaneDiagramLayout.Create(model, themeOverride: theme).Scene);
        if (model.UseCase is not null) return DrawingSceneSvg.Write(Layout.UseCaseDiagramLayout.Create(model, themeOverride: theme).Scene);
        if (model.AgentFlow is not null) return DrawingSceneSvg.Write(Layout.AgentFlowDiagramLayout.Create(model, themeOverride: theme).Scene);
        if (model.EventModeling is not null) return DrawingSceneSvg.Write(Layout.EventModelingLayout.Create(model, themeOverride: theme).Scene);
        if (model.Zen is not null) return DrawingSceneSvg.Write(Layout.ZenDiagramLayout.Create(model, themeOverride: theme).Scene);
        if (model.ClassOptions is not null)
        {
            var diagnostics = model.Nodes.SelectMany(n => ClassDiagramStyle.Resolve(n).Diagnostics).Distinct().ToArray();
            if (diagnostics.Length != 0)
                throw new NotSupportedException("Native class SVG cannot represent: " + string.Join(" ", diagnostics));
        }
        if (model.EntityOptions is not null)
        {
            var diagnostics = model.Nodes.SelectMany(n => EntityDiagramStyle.Resolve(n).Diagnostics).Distinct().ToArray();
            if (diagnostics.Length != 0) throw new NotSupportedException("Native ER SVG cannot represent: " + string.Join(" ", diagnostics));
        }
        var palette = Palette.For(theme ?? model.Theme);

        // The extent of everything drawn, so the viewBox fits the diagram rather
        // than whatever the window happened to be.
        var minX = model.Nodes.Count > 0 ? model.Nodes.Min(n => n.X) : 0;
        var minY = model.Nodes.Count > 0 ? model.Nodes.Min(n => n.Y) : 0;
        var maxX = model.Nodes.Count > 0 ? model.Nodes.Max(n => n.X + n.Width) : 100;
        var maxY = model.Nodes.Count > 0 ? model.Nodes.Max(n => n.Y + n.Height) : 100;
        if (model.Subgraphs.Count > 0)
        {
            var rectangles = model.Subgraphs.Where(g => GraphGroupGeometry.IsVisible(model, g)).Select(g => GraphGroupGeometry.Bounds(model, g));
            foreach (var rectangle in rectangles)
            {
                minX = Math.Min(minX, rectangle.X); minY = Math.Min(minY, rectangle.Y);
                maxX = Math.Max(maxX, rectangle.X + rectangle.Width); maxY = Math.Max(maxY, rectangle.Y + rectangle.Height);
            }
        }
        if (model.EntityOptions is not null)
        {
            foreach (var point in model.Edges.SelectMany(e => EntityDiagramGeometry.Route(model, e)))
            {
                minX = Math.Min(minX, point.X); minY = Math.Min(minY, point.Y);
                maxX = Math.Max(maxX, point.X); maxY = Math.Max(maxY, point.Y);
            }
        }
        else if (model.ClassOptions is null)
            foreach (var point in model.Edges.SelectMany(e => GraphEdgeGeometry.Route(model, e)))
            {
                minX = Math.Min(minX, point.X); minY = Math.Min(minY, point.Y);
                maxX = Math.Max(maxX, point.X); maxY = Math.Max(maxY, point.Y);
            }

        var padding = model.ClassOptions is null ? Pad : Pad + 36 * Math.Max(1, model.Subgraphs.Count);
        if (model.RenderedGeometry?.Caption is { } caption)
            foreach (var line in caption.Lines)
            {
                minX = Math.Min(minX, line.Bounds.X); minY = Math.Min(minY, line.Bounds.Y);
                maxX = Math.Max(maxX, line.Bounds.X + line.Bounds.Width); maxY = Math.Max(maxY, line.Bounds.Y + line.Bounds.Height);
            }
        var w = maxX - minX + padding * 2;
        var h = maxY - minY + padding * 2;
        var dx = padding - minX;
        var dy = padding - minY;

        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{N(w)}\" height=\"{N(h)}\" ");
        sb.Append($"viewBox=\"0 0 {N(w)} {N(h)}\" font-family=\"Segoe UI, system-ui, sans-serif\">\n");

        // One marker per edge colour would be ideal, but every edge here uses
        // the same ink, so one shared arrowhead keeps the file small.
        sb.Append("  <defs>\n");
        sb.Append("    <marker id=\"arrow\" viewBox=\"0 0 10 10\" refX=\"9\" refY=\"5\" ");
        sb.Append("markerWidth=\"7\" markerHeight=\"7\" orient=\"auto-start-reverse\">\n");
        sb.Append($"      <path d=\"M 0 0 L 10 5 L 0 10 z\" fill=\"{palette.Edge}\"/>\n");
        sb.Append("    </marker>\n");
        sb.Append("  </defs>\n");

        sb.Append($"  <rect width=\"100%\" height=\"100%\" fill=\"{palette.Background}\"/>\n");
        sb.Append($"  <g transform=\"translate({N(dx)},{N(dy)})\">\n");
        if (model.RenderedGeometry?.Caption is { } captionLabel) WriteRenderedText(sb, captionLabel, palette.MutedText);

        // Edges first, so a node covers the stub of a line that reaches it.
        if (model.ClassOptions is not null)
        {
            WriteClassNamespaces(sb, model, palette);
            foreach (var edge in model.Edges) WriteClassEdge(sb, model, edge, palette);
            foreach (var node in model.Nodes) WriteClassNode(sb, model, node, palette);
        }
        else if (model.EntityOptions is not null)
        {
            WriteEntityGroups(sb, model, palette);
            foreach (var edge in model.Edges) WriteEntityEdge(sb, model, edge, palette);
            foreach (var node in model.Nodes) WriteEntityNode(sb, node, palette);
        }
        else
        {
            foreach (var group in model.Subgraphs)
            {
                var bounds = GraphGroupGeometry.Bounds(model, group);
                var imported = model.RenderedGeometry?.Groups.GetValueOrDefault(group.Id);
                sb.Append($"<g data-subgraph=\"{Esc(group.Id)}\"><rect x=\"{N(bounds.X)}\" y=\"{N(bounds.Y)}\" width=\"{N(bounds.Width)}\" height=\"{N(bounds.Height)}\" fill=\"{Esc(imported?.Fill ?? "none")}\" stroke=\"{Esc(imported?.Stroke ?? palette.NodeStroke)}\" stroke-width=\"{N(imported?.StrokeWidth ?? 1)}\"/>\n");
                if (model.RenderedGeometry?.GroupLabel(model, group) is { } label) WriteRenderedText(sb, label, palette.MutedText);
                else WriteLabel(sb, group.Title ?? group.Id, bounds.X + 10, bounds.Y + 16, palette.MutedText, "start");
                sb.Append("</g>\n");
            }
            foreach (var edge in model.Edges) WriteEdge(sb, model, edge, palette);
            foreach (var node in model.Nodes) WriteNode(sb, node, palette, model.RenderedGeometry?.NodeLabel(model, node));
        }

        sb.Append("  </g>\n");
        sb.Append("</svg>\n");
        return sb.ToString();
    }

    // ------------------------------------------------------------------ nodes

    private static void WriteNode(StringBuilder sb, DiagramNode n, Palette p, RenderedText? renderedLabel = null)
    {
        var fill = n.Fill ?? p.NodeFill;
        var stroke = n.Stroke ?? p.NodeStroke;
        var width = n.StrokeWidth ?? 1.6;
        var dash = n.Dashed ? " stroke-dasharray=\"5,3\"" : "";

        sb.Append($"    <g id=\"{Esc(n.Id)}\">\n      ");
        sb.Append(ShapeElement(n, fill, stroke, width, dash));
        sb.Append('\n');

        var hasIcon = IconGeometry.For(n.Icon).Count > 0;
        var text = n.TextColor ?? Ink(fill);

        if (renderedLabel is not null) WriteRenderedText(sb, renderedLabel, text);
        else if (hasIcon)
        {
            // Glyph on the left, label beside it — the arrangement the canvas
            // uses, so an exported diagram matches the one on screen.
            const double Glyph = 26;
            var box = (X: n.X + 8, Y: n.Y + (n.Height - Glyph) / 2, S: Glyph);
            WriteIcon(sb, n.Icon!, box.X, box.Y, box.S, text);
            WriteLabel(sb, n.Label ?? n.Id, box.X + Glyph + 8, n.Y + n.Height / 2, text, "start");
        }
        else
        {
            WriteLabel(sb, n.Label ?? n.Id, n.X + n.Width / 2, n.Y + n.Height / 2, text, "middle");
        }

        sb.Append("    </g>\n");
    }

    private static string ShapeElement(
        DiagramNode n, string fill, string stroke, double width, string dash)
    {
        var style = $"fill=\"{Esc(fill)}\" stroke=\"{Esc(stroke)}\" stroke-width=\"{N(width)}\"{dash}";
        double x = n.X, y = n.Y, w = n.Width, h = n.Height;

        return n.Shape switch
        {
            NodeShape.RoundedRect =>
                $"<rect x=\"{N(x)}\" y=\"{N(y)}\" width=\"{N(w)}\" height=\"{N(h)}\" rx=\"12\" ry=\"12\" {style}/>",

            NodeShape.Stadium =>
                $"<rect x=\"{N(x)}\" y=\"{N(y)}\" width=\"{N(w)}\" height=\"{N(h)}\" "
                + $"rx=\"{N(h / 2)}\" ry=\"{N(h / 2)}\" {style}/>",

            NodeShape.Circle =>
                $"<ellipse cx=\"{N(x + w / 2)}\" cy=\"{N(y + h / 2)}\" "
                + $"rx=\"{N(w / 2)}\" ry=\"{N(h / 2)}\" {style}/>",

            NodeShape.DoubleCircle =>
                $"<ellipse cx=\"{N(x + w / 2)}\" cy=\"{N(y + h / 2)}\" "
                + $"rx=\"{N(w / 2)}\" ry=\"{N(h / 2)}\" {style}/>"
                + $"<ellipse cx=\"{N(x + w / 2)}\" cy=\"{N(y + h / 2)}\" "
                + $"rx=\"{N(w / 2 - 5)}\" ry=\"{N(h / 2 - 5)}\" fill=\"none\" "
                + $"stroke=\"{Esc(stroke)}\" stroke-width=\"{N(width)}\"/>",

            NodeShape.Rhombus =>
                Poly([(x + w / 2, y), (x + w, y + h / 2), (x + w / 2, y + h), (x, y + h / 2)], style),

            NodeShape.Hexagon =>
                Poly([(x + w * 0.2, y), (x + w * 0.8, y), (x + w, y + h / 2),
                      (x + w * 0.8, y + h), (x + w * 0.2, y + h), (x, y + h / 2)], style),

            NodeShape.Parallelogram =>
                Poly([(x + w * 0.18, y), (x + w, y), (x + w * 0.82, y + h), (x, y + h)], style),

            NodeShape.ParallelogramAlt =>
                Poly([(x, y), (x + w * 0.82, y), (x + w, y + h), (x + w * 0.18, y + h)], style),

            NodeShape.Trapezoid =>
                Poly([(x + w * 0.2, y), (x + w * 0.8, y), (x + w, y + h), (x, y + h)], style),

            NodeShape.TrapezoidAlt =>
                Poly([(x, y), (x + w, y), (x + w * 0.8, y + h), (x + w * 0.2, y + h)], style),

            NodeShape.Asymmetric =>
                Poly([(x, y), (x + w * 0.85, y), (x + w, y + h / 2),
                      (x + w * 0.85, y + h), (x, y + h)], style),

            NodeShape.Cylinder => Cylinder(x, y, w, h, style),

            NodeShape.Subroutine =>
                $"<rect x=\"{N(x)}\" y=\"{N(y)}\" width=\"{N(w)}\" height=\"{N(h)}\" {style}/>"
                + $"<line x1=\"{N(x + 8)}\" y1=\"{N(y)}\" x2=\"{N(x + 8)}\" y2=\"{N(y + h)}\" "
                + $"stroke=\"{Esc(stroke)}\" stroke-width=\"{N(width)}\"/>"
                + $"<line x1=\"{N(x + w - 8)}\" y1=\"{N(y)}\" x2=\"{N(x + w - 8)}\" y2=\"{N(y + h)}\" "
                + $"stroke=\"{Esc(stroke)}\" stroke-width=\"{N(width)}\"/>",

            _ => $"<rect x=\"{N(x)}\" y=\"{N(y)}\" width=\"{N(w)}\" height=\"{N(h)}\" rx=\"3\" ry=\"3\" {style}/>",
        };
    }

    private static string Cylinder(double x, double y, double w, double h, string style)
    {
        var ry = Math.Min(h * 0.18, 14);
        // Body drawn as a path so the top ellipse can sit over it cleanly.
        return $"<path d=\"M {N(x)} {N(y + ry)} A {N(w / 2)} {N(ry)} 0 0 1 {N(x + w)} {N(y + ry)} "
             + $"L {N(x + w)} {N(y + h - ry)} A {N(w / 2)} {N(ry)} 0 0 1 {N(x)} {N(y + h - ry)} Z\" {style}/>"
             + $"<ellipse cx=\"{N(x + w / 2)}\" cy=\"{N(y + ry)}\" rx=\"{N(w / 2)}\" ry=\"{N(ry)}\" {style}/>";
    }

    private static string Poly(IReadOnlyList<(double X, double Y)> pts, string style) =>
        $"<polygon points=\"{string.Join(" ", pts.Select(p => $"{N(p.X)},{N(p.Y)}"))}\" {style}/>";

    // ------------------------------------------------------------------ icons

    private static void WriteIcon(
        StringBuilder sb, string name, double ox, double oy, double s, string colour)
    {
        var shapes = IconGeometry.For(name);
        if (shapes.Count == 0) return;

        string X(double v) => N(ox + v * s);
        string Y(double v) => N(oy + v * s);

        sb.Append($"      <g fill=\"none\" stroke=\"{Esc(colour)}\" stroke-width=\"1.6\">\n");
        foreach (var shape in shapes)
        {
            sb.Append("        ");
            sb.Append(shape switch
            {
                IconShape.Box b =>
                    $"<rect x=\"{X(b.X)}\" y=\"{Y(b.Y)}\" width=\"{N(b.W * s)}\" "
                    + $"height=\"{N(b.H * s)}\" rx=\"{N(b.Radius * s)}\"/>",

                IconShape.Ellipse e when e.Filled =>
                    $"<ellipse cx=\"{X(e.CX)}\" cy=\"{Y(e.CY)}\" rx=\"{N(e.RX * s)}\" "
                    + $"ry=\"{N(e.RY * s)}\" fill=\"{Esc(colour)}\" stroke=\"none\"/>",

                IconShape.Ellipse e =>
                    $"<ellipse cx=\"{X(e.CX)}\" cy=\"{Y(e.CY)}\" rx=\"{N(e.RX * s)}\" ry=\"{N(e.RY * s)}\"/>",

                IconShape.Line l =>
                    $"<line x1=\"{X(l.X1)}\" y1=\"{Y(l.Y1)}\" x2=\"{X(l.X2)}\" y2=\"{Y(l.Y2)}\"/>",

                IconShape.Polyline p =>
                    $"<polyline points=\"{string.Join(" ", p.Points.Select(pt => $"{X(pt.X)},{Y(pt.Y)}"))}\"/>",

                IconShape.Polygon g =>
                    $"<polygon points=\"{string.Join(" ", g.Points.Select(pt => $"{X(pt.X)},{Y(pt.Y)}"))}\"/>",

                _ => "",
            });
            sb.Append('\n');
        }
        sb.Append("      </g>\n");
    }

    // ------------------------------------------------------------------ edges

    private static void WriteEdge(StringBuilder sb, DiagramModel m, DiagramEdge e, Palette p)
    {
        var a = m.Node(e.From);
        var b = m.Node(e.To);
        if (a is null || b is null) return;
        if (e.Kind == EdgeKind.Invisible) return;

        var route = GraphEdgeGeometry.Route(m, e);
        if (route.Count < 2) return;
        var ax = route[0].X; var ay = route[0].Y;
        var bx = route[^1].X; var by = route[^1].Y;

        var dash = e.Kind is EdgeKind.Dotted or EdgeKind.DottedArrow
            ? " stroke-dasharray=\"5,4\"" : "";
        var width = e.Kind is EdgeKind.Thick or EdgeKind.ThickArrow ? 3 : 1.4;
        var head = e.Kind is EdgeKind.Open or EdgeKind.Dotted
            ? "" : " marker-end=\"url(#arrow)\"";

        if (route.Count == 2) sb.Append($"    <line x1=\"{N(ax)}\" y1=\"{N(ay)}\" x2=\"{N(bx)}\" y2=\"{N(by)}\" ");
        else sb.Append($"    <polyline points=\"{string.Join(" ", route.Select(point => $"{N(point.X)},{N(point.Y)}"))}\" fill=\"none\" ");
        sb.Append($"stroke=\"{p.Edge}\" stroke-width=\"{N(width)}\"{dash}{head}/>\n");

        if (m.RenderedGeometry?.EdgeLabel(m, e) is { } label) { WriteRenderedText(sb, label, p.MutedText); return; }
        if (!string.IsNullOrEmpty(e.Label))
        {
            const double LabelSize = 12;
            var center = GraphEdgeGeometry.LabelPoint(route);
            var mx = center.X;
            var my = center.Y;

            // Sized from the label's own font size, not the node one. Using the
            // larger figure made the backing box noticeably wider than its text
            // and it covered the edge of the node it pointed at.
            var w = e.Label!.Length * LabelSize * GlyphRatio + 8;

            // A backing rectangle, because a label sitting directly on the line
            // is unreadable — the same trick the canvas uses.
            sb.Append($"    <rect x=\"{N(mx - w / 2)}\" y=\"{N(my - 9)}\" width=\"{N(w)}\" ");
            sb.Append($"height=\"18\" fill=\"{p.Background}\" opacity=\"0.88\"/>\n");
            sb.Append($"    <text x=\"{N(mx)}\" y=\"{N(my + 4)}\" text-anchor=\"middle\" ");
            sb.Append($"font-size=\"{N(LabelSize)}\" fill=\"{p.MutedText}\">{Esc(e.Label)}</text>\n");
        }
    }

    private static (double X, double Y) Centre(DiagramNode n) =>
        (n.X + n.Width / 2, n.Y + n.Height / 2);

    /// <summary>
    /// Where a line from <paramref name="towards"/> meets the node's outline.
    /// </summary>
    /// <remarks>
    /// Without this an arrowhead lands on the node's centre and disappears
    /// underneath it, because nodes are drawn after edges.
    /// </remarks>
    private static (double X, double Y) Border(DiagramNode n, (double X, double Y) towards)
    {
        var (cx, cy) = Centre(n);
        var dx = towards.X - cx;
        var dy = towards.Y - cy;
        if (Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9) return (cx, cy);

        var hw = n.Width / 2;
        var hh = n.Height / 2;

        // Scale the direction until it touches whichever edge it reaches first.
        var scale = Math.Min(
            Math.Abs(dx) < 1e-9 ? double.MaxValue : hw / Math.Abs(dx),
            Math.Abs(dy) < 1e-9 ? double.MaxValue : hh / Math.Abs(dy));

        return (cx + dx * scale, cy + dy * scale);
    }

    // ------------------------------------------------------------------- text

    private static void WriteRenderedText(StringBuilder sb, RenderedText text, string ink)
    {
        if (text.Background is { } background && text.Lines.Count > 0)
        {
            var left = text.Lines.Min(l => l.Bounds.X); var top = text.Lines.Min(l => l.Bounds.Y);
            var right = text.Lines.Max(l => l.Bounds.X + l.Bounds.Width); var bottom = text.Lines.Max(l => l.Bounds.Y + l.Bounds.Height);
            sb.Append($"<rect x=\"{N(left - 3)}\" y=\"{N(top - 2)}\" width=\"{N(right - left + 6)}\" height=\"{N(bottom - top + 4)}\" fill=\"{Esc(background)}\"/>\n");
        }
        foreach (var line in text.Lines)
            sb.Append($"<text x=\"{N(line.Bounds.X + line.Bounds.Width / 2)}\" y=\"{N(line.Bounds.Y)}\" text-anchor=\"middle\" dominant-baseline=\"text-before-edge\" font-family=\"{Esc(text.FontFamily)}\" font-size=\"{N(text.FontSize)}\" font-weight=\"{text.FontWeight}\" fill=\"{Esc(ink)}\">{Esc(line.Text)}</text>\n");
    }

    private static void WriteLabel(
        StringBuilder sb, string text, double x, double y, string colour, string anchor)
    {
        sb.Append($"      <text x=\"{N(x)}\" y=\"{N(y + FontSize * 0.35)}\" ");
        sb.Append($"text-anchor=\"{anchor}\" font-size=\"{N(FontSize)}\" ");
        sb.Append($"fill=\"{Esc(colour)}\">{Esc(text)}</text>\n");
    }

    /// <summary>Black or white text, whichever contrasts with the fill.</summary>
    /// <remarks>
    /// Mirrors the canvas so an exported node reads the same way. 0.179 is the
    /// luminance at which black and white have equal contrast ratio.
    /// </remarks>
    private static string Ink(string fill)
    {
        if (!TryHex(fill, out var r, out var g, out var b)) return "#1A1A1A";

        static double Channel(int v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        var luminance = 0.2126 * Channel(r) + 0.7152 * Channel(g) + 0.0722 * Channel(b);
        return luminance > 0.179 ? "#1A1A1A" : "#F2F2F2";
    }

    private static bool TryHex(string s, out int r, out int g, out int b)
    {
        r = g = b = 0;
        s = s.TrimStart('#');
        if (s.Length == 3)
            s = string.Concat(s.Select(c => new string(c, 2)));
        if (s.Length is not (6 or 8)) return false;

        return int.TryParse(s[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out r)
            && int.TryParse(s[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out g)
            && int.TryParse(s[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b);
    }

    // -------------------------------------------------------------- utilities

    /// <summary>
    /// A number, always with a dot for the decimal point.
    /// </summary>
    /// <remarks>
    /// Invariant culture is not optional here. On a machine with a comma decimal
    /// separator, <c>ToString()</c> would emit <c>x="12,5"</c> and every
    /// exported file would be silently invalid SVG — broken only for some users,
    /// which is the worst kind of broken.
    /// </remarks>
    private static string N(double v) =>
        Math.Round(v, 2).ToString("0.##", CultureInfo.InvariantCulture);

    private static string Esc(string s) => s
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;");

    /// <summary>Colours for the parts of a diagram nobody styled.</summary>
    private readonly record struct Palette(
        string Background, string NodeFill, string NodeStroke, string Edge, string MutedText)
    {
        public static Palette For(string? theme) => theme?.ToLowerInvariant() switch
        {
            // Matching the values measured from the mermaid build this project
            // ships — see oracle/theme-colour-probe.mjs.
            "dark" => new("#1F2020", "#1F2020", "#CCCCCC", "#CCCCCC", "#AAAAAA"),
            "forest" => new("#FFFFFF", "#CDE498", "#13540C", "#333333", "#555555"),
            "neutral" => new("#FFFFFF", "#EEEEEE", "#999999", "#666666", "#666666"),
            "base" => new("#FFFFFF", "#FFF4DD", "#EEDEBB", "#333333", "#555555"),
            "default" => new("#FFFFFF", "#ECECFF", "#9370DB", "#333333", "#555555"),
            "neo" => new("#FFFFFF", "#FFFFFF", "#000000", "#333333", "#555555"),
            "neo-dark" => new("#1B1616", "#2A2020", "#CCCCCC", "#E0DFDF", "#AAAAAA"),
            "redux" or "redux-color" => new("#FFFFFF", "#FFFFFF", "#28253D", "#28253D", "#555555"),
            "redux-dark" or "redux-dark-color" =>
                new("#0B0B0C", "#111113", "#FFFFFF", "#E0DFDF", "#AAAAAA"),
            _ => new("#FFFFFF", "#DAE8FC", "#6C8EBF", "#7A7A7A", "#777777"),
        };
    }
}
