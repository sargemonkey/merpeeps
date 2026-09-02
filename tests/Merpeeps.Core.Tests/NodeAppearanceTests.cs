using Merpeeps.Core.Model;
using Xunit;

namespace Merpeeps.Core.Tests;

/// <summary>
/// "Show Merpeeps styling" is an all-or-nothing switch, and the ways it can go
/// wrong are invisible in a screenshot: one property forgotten leaves a view
/// that matches neither what mermaid draws nor what Merpeeps draws, and it looks
/// plausible either way.
/// </summary>
public class NodeAppearanceTests
{
    private static DiagramNode Styled(
        NodeShape shape = NodeShape.Cylinder,
        string? icon = "database",
        string? fill = "#DAE8FC",
        string? stroke = "#6C8EBF",
        string? textColor = "#112233") => new()
    {
        Id = "n",
        Label = "Node",
        Shape = shape,
        Fill = fill,
        Stroke = stroke,
        StrokeWidth = 3,
        Dashed = true,
        TextColor = textColor,
        Icon = icon,
    };

    // ------------------------------------------------------- styling hidden

    /// <summary>
    /// The reported bug: icons kept showing with styling off. Asserted as a
    /// whole rather than icon-by-icon, so forgetting a future property fails
    /// here instead of shipping.
    /// </summary>
    [Fact]
    public void PlainModeSuppressesEveryPieceOfStyling()
    {
        var look = NodeAppearance.For(Styled(), showMerpeepsStyling: false);

        Assert.Null(look.Fill);
        Assert.Null(look.Stroke);
        Assert.Null(look.StrokeWidth);
        Assert.False(look.Dashed);
        Assert.Null(look.TextColor);
        Assert.Null(look.Icon);
        Assert.False(look.HasIcon);
        Assert.True(look.IsPlain, "something is still styled in plain mode");
    }

    /// <summary>
    /// Shape is mermaid's own syntax, not Merpeeps styling. A cylinder must stay
    /// a cylinder in plain mode, because that is what stock mermaid draws.
    /// </summary>
    [Theory]
    [InlineData(NodeShape.Cylinder)]
    [InlineData(NodeShape.Rhombus)]
    [InlineData(NodeShape.Hexagon)]
    [InlineData(NodeShape.Stadium)]
    [InlineData(NodeShape.Subroutine)]
    [InlineData(NodeShape.DoubleCircle)]
    public void PlainModeKeepsTheShape(NodeShape shape)
    {
        var node = Styled(shape: shape);
        Assert.Equal(shape, NodeAppearance.For(node, false).Shape);
    }

    [Fact]
    public void PlainModeIsIdenticalForStyledAndUnstyledNodes()
    {
        var bare = new DiagramNode { Id = "b", Label = "Node", Shape = NodeShape.Cylinder };

        Assert.Equal(
            NodeAppearance.For(bare, false),
            NodeAppearance.For(Styled(), false));
    }

    // -------------------------------------------------------- styling shown

    [Fact]
    public void StyledModeKeepsEveryPieceOfStyling()
    {
        var look = NodeAppearance.For(Styled(), showMerpeepsStyling: true);

        Assert.Equal("#DAE8FC", look.Fill);
        Assert.Equal("#6C8EBF", look.Stroke);
        Assert.Equal(3, look.StrokeWidth);
        Assert.True(look.Dashed);
        Assert.Equal("#112233", look.TextColor);
        Assert.Equal("database", look.Icon);
        Assert.True(look.HasIcon);
        Assert.False(look.IsPlain);
    }

    /// <summary>
    /// An unknown icon name draws nothing, so reserving space for it would
    /// leave a permanent gap beside the label with no explanation.
    /// </summary>
    [Theory]
    [InlineData("not-an-icon")]
    [InlineData("")]
    [InlineData(null)]
    public void AnUndrawableIconIsTreatedAsAbsent(string? icon)
    {
        var look = NodeAppearance.For(Styled(icon: icon), true);
        Assert.Null(look.Icon);
        Assert.False(look.HasIcon);
    }

    [Theory]
    [InlineData("server")]
    [InlineData("database")]
    [InlineData("queue")]
    [InlineData("shield")]
    public void KnownIconsSurvive(string icon)
    {
        var look = NodeAppearance.For(Styled(icon: icon), true);
        Assert.Equal(icon, look.Icon);
    }

    /// <summary>Blank strings are absence, not a colour called "".</summary>
    [Fact]
    public void EmptyStyleValuesAreTreatedAsUnset()
    {
        var node = Styled(fill: "", stroke: "   ", textColor: "");
        var look = NodeAppearance.For(node, true);

        Assert.Null(look.Fill);
        Assert.Null(look.Stroke);
        Assert.Null(look.TextColor);
    }

    [Fact]
    public void AnUnstyledNodeIsPlainInBothModes()
    {
        var bare = new DiagramNode { Id = "b", Label = "Node" };
        Assert.True(NodeAppearance.For(bare, true).IsPlain);
        Assert.True(NodeAppearance.For(bare, false).IsPlain);
    }

    // ----------------------------------------------------- no side effects

    /// <summary>
    /// Looking at a diagram must not edit it. If the preview mutated the node,
    /// toggling the view would mark the document dirty and could be saved.
    /// </summary>
    [Fact]
    public void DecidingAppearanceNeverMutatesTheNode()
    {
        var node = Styled();
        var before = (node.Fill, node.Stroke, node.StrokeWidth, node.Dashed,
                      node.TextColor, node.Icon, node.Shape, node.Width, node.Height);

        NodeAppearance.For(node, false);
        NodeAppearance.For(node, true);

        Assert.Equal(before, (node.Fill, node.Stroke, node.StrokeWidth, node.Dashed,
                              node.TextColor, node.Icon, node.Shape, node.Width, node.Height));
    }

    /// <summary>
    /// Whether a node reserves icon space is a property of the document, so it
    /// must not depend on the current view. Otherwise looking at the plain
    /// render and saving would silently resize every icon node.
    /// </summary>
    [Fact]
    public void IconSpaceIsDecidedByTheDocumentNotTheView()
    {
        var node = Styled();

        // The document says there is an icon regardless of what is displayed.
        Assert.True(IconVocabulary.IsKnown(node.Icon));
        Assert.True(IconVocabulary.IsKnown(node.Icon),
            "sizing must consult the node, not the preview state");
    }
}
