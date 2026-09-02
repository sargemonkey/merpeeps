using Avalonia.Media;

namespace Merpeeps.App;

/// <summary>
/// The colours the canvas draws with, as a swappable set.
///
/// <para>Two things need to change together for dark mode to look right: the
/// chrome (background, gridlines, labels) and the *defaults* used for nodes
/// that carry no explicit styling. A node the user has coloured keeps its
/// colour in either theme — that is their data, not our decoration.</para>
/// </summary>
public sealed class CanvasTheme
{
    public required IBrush Background { get; init; }
    public required IBrush NodeFill { get; init; }
    public required IBrush NodeFillSelected { get; init; }
    public required IPen NodeStroke { get; init; }
    public required IPen NodeStrokeSelected { get; init; }
    public required IPen TargetStroke { get; init; }
    public required IPen Edge { get; init; }
    public required IPen EdgeDotted { get; init; }
    public required IPen EdgeThick { get; init; }

    /// <summary>Wide translucent stroke drawn under a selected link.</summary>
    /// <remarks>
    /// A halo rather than a recolour: the line's own colour and dash pattern are
    /// what say whether it is dotted, thick or plain, and hiding that at the
    /// moment someone is about to change it would be perverse.
    /// </remarks>
    public required IPen EdgeSelectedHalo { get; init; }
    public required IPen Ghost { get; init; }
    public required IBrush Label { get; init; }
    public required IBrush MutedLabel { get; init; }
    public required IBrush EdgeLabelBackground { get; init; }
    public required IBrush SubgraphFill { get; init; }
    public required IPen SubgraphStroke { get; init; }
    public required IBrush MarqueeFill { get; init; }
    public required IPen MarqueePen { get; init; }
    public required IBrush HandleFill { get; init; }
    public required IBrush EditBackground { get; init; }
    public required IPen EditPen { get; init; }
    public required IBrush SelectionFill { get; init; }

    /// <summary>Default fill for a node with no explicit colour.</summary>
    public required Color DefaultNodeFill { get; init; }

    /// <summary>Default outline for a node with no explicit colour.</summary>
    public required Color DefaultNodeStroke { get; init; }

    /// <summary>Default text colour for a node with no explicit colour.</summary>
    public required Color DefaultNodeText { get; init; }

    private static IPen Pen(string colour, double thickness, double[]? dash = null) =>
        new Pen(new SolidColorBrush(Color.Parse(colour)), thickness)
        {
            DashStyle = dash is null ? null : new DashStyle(dash, 0),
        };

    private static IBrush Brush(string colour) =>
        new SolidColorBrush(Color.Parse(colour));

    public static readonly CanvasTheme Light = new()
    {
        Background = Brush("#FFFFFF"),
        NodeFill = Brush("#DAE8FC"),
        NodeFillSelected = Brush("#FFE6CC"),
        NodeStroke = Pen("#6C8EBF", 1.6),
        NodeStrokeSelected = Pen("#D79B00", 2.4),
        TargetStroke = Pen("#2E9E48", 2.6),
        Edge = Pen("#7A7A7A", 1.4),
        EdgeDotted = Pen("#7A7A7A", 1.4, [4, 3]),
        EdgeThick = Pen("#4A4A4A", 3),
        EdgeSelectedHalo = Pen("#D79B0088", 7),
        Ghost = Pen("#2E9E48", 1.8, [5, 4]),
        Label = Brush("#1A1A1A"),
        MutedLabel = Brush("#777777"),
        EdgeLabelBackground = Brush("#FFFFFFE0"),
        SubgraphFill = Brush("#F5F5F5"),
        SubgraphStroke = Pen("#BBBBBB", 1, [6, 4]),
        MarqueeFill = Brush("#3B82F626"),
        MarqueePen = Pen("#3B82F6", 1),
        HandleFill = Brush("#2E9E48"),
        EditBackground = Brushes.White,
        EditPen = Pen("#2563EB", 2),
        SelectionFill = Brush("#B4D5FE"),
        DefaultNodeFill = Color.Parse("#DAE8FC"),
        DefaultNodeStroke = Color.Parse("#6C8EBF"),
        DefaultNodeText = Color.Parse("#1A1A1A"),
    };

    /// <summary>
    /// Dark canvas. Deliberately not a plain inversion: the node fill is a
    /// desaturated slate rather than a dark blue, because inverted pastels
    /// turn muddy, and edges are lightened rather than darkened so they stay
    /// visible against the background.
    /// </summary>
    public static readonly CanvasTheme Dark = new()
    {
        Background = Brush("#1E1E1E"),
        NodeFill = Brush("#2D3748"),
        NodeFillSelected = Brush("#4A3B23"),
        NodeStroke = Pen("#7BA0CC", 1.6),
        NodeStrokeSelected = Pen("#E0A93B", 2.4),
        TargetStroke = Pen("#4ADE80", 2.6),
        Edge = Pen("#9A9A9A", 1.4),
        EdgeDotted = Pen("#9A9A9A", 1.4, [4, 3]),
        EdgeThick = Pen("#C4C4C4", 3),
        EdgeSelectedHalo = Pen("#E0A93B88", 7),
        Ghost = Pen("#4ADE80", 1.8, [5, 4]),
        Label = Brush("#E8E8E8"),
        MutedLabel = Brush("#999999"),
        EdgeLabelBackground = Brush("#1E1E1EE0"),
        SubgraphFill = Brush("#252525"),
        SubgraphStroke = Pen("#4A4A4A", 1, [6, 4]),
        MarqueeFill = Brush("#60A5FA33"),
        MarqueePen = Pen("#60A5FA", 1),
        HandleFill = Brush("#4ADE80"),
        EditBackground = Brush("#2A2A2A"),
        EditPen = Pen("#60A5FA", 2),
        SelectionFill = Brush("#2C5282"),
        DefaultNodeFill = Color.Parse("#2D3748"),
        DefaultNodeStroke = Color.Parse("#7BA0CC"),
        DefaultNodeText = Color.Parse("#E8E8E8"),
    };
}
