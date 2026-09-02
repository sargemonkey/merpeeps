using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Merpeeps.Core.Model;
using Xunit;

namespace Merpeeps.App.Tests;

/// <summary>
/// How a mermaid theme and Merpeeps's own styling combine.
/// </summary>
/// <remarks>
/// <para>Merpeeps styling sits above the theme. That is not a preference — it is
/// what mermaid does, measured against the vendored bundle: under
/// <c>forest</c> an unstyled node renders #CDE498, and the same node carrying
/// <c>style a fill:#FF0000</c> renders red.</para>
///
/// <para>It is also load-bearing. <c>merpeeps bake</c> rewrites Merpeeps styling as
/// <c>style</c> statements, so a canvas that let the theme win would disagree
/// with the file it produces — and the canvas exists to predict the render.</para>
///
/// <para>The cost is a confusing moment: pick a theme on a fully styled diagram
/// and nothing appears to happen. These pin both the behaviour and the
/// explanation that makes it legible.</para>
/// </remarks>
public class ThemeAndStylingTests
{
    /// <summary>Two styled nodes and one bare one, so precedence is visible.</summary>
    private const string Mixed = """
        ---
        config:
          theme: forest
        x-merpeeps:
          spec: 1
          style:
            nodes:
              a: {fill: '#FF0000', stroke: '#880000'}
              b: {fill: '#00FF00'}
        ---
        flowchart TD
            a[One] --> b[Two]
            b --> c[Three]
        """;

    private static (MainWindow Window, EditorCanvas Canvas) Open(string source)
    {
        var window = new MainWindow();
        window.Show();
        window.LoadSourceForTest(source);
        window.ApplyTemplate();
        return (window, window.CanvasForTest);
    }

    private static string Tip(MainWindow w) =>
        ToolTip.GetTip(w.FindControl<ToggleButton>("StylingToggle")!) as string ?? "";

    /// <summary>
    /// A styled node keeps its colour; an unstyled one takes the theme's.
    /// </summary>
    [AvaloniaFact]
    public void MerpeepsColoursOverrideTheThemeAndTheRestFollowIt()
    {
        var (_, canvas) = Open(Mixed);

        Assert.Equal("#FF0000", canvas.Model!.Node("a")!.Fill);
        Assert.Equal("#00FF00", canvas.Model.Node("b")!.Fill);
        Assert.Null(canvas.Model.Node("c")!.Fill);

        // The theme is what the unstyled node will be drawn with.
        Assert.Equal(DiagramPalette.Find("forest"), canvas.DiagramTheme);
    }

    /// <summary>
    /// Turning the styling toggle off hands every node to the theme.
    /// </summary>
    /// <remarks>
    /// The toggle is the global switch, and it hides rather than deletes: the
    /// colours are still in the file and come straight back.
    /// </remarks>
    [AvaloniaFact]
    public void TurningStylingOffLetsTheThemeTakeOver()
    {
        var (window, canvas) = Open(Mixed);
        var toggle = window.FindControl<ToggleButton>("StylingToggle")!;

        Assert.True(canvas.ShowMerpeepsStyling);

        toggle.IsChecked = false;
        Assert.False(canvas.ShowMerpeepsStyling);

        // Hidden, not removed.
        Assert.Equal("#FF0000", canvas.Model!.Node("a")!.Fill);
        Assert.Contains("#FF0000", window.FindControl<TextBox>("SourceBox")!.Text);

        toggle.IsChecked = true;
        Assert.True(canvas.ShowMerpeepsStyling);
    }

    /// <summary>
    /// With no theme chosen, the toggle says only what it has always said.
    /// </summary>
    [AvaloniaFact]
    public void WithNoThemeTheTooltipStaysPlain()
    {
        var (window, _) = Open("flowchart TD\n    a[One] --> b[Two]\n");

        Assert.Contains("stock mermaid", Tip(window));
        Assert.DoesNotContain("overrides", Tip(window));
    }

    /// <summary>
    /// With a theme and styled nodes, the toggle explains the conflict and
    /// names how many nodes are involved.
    /// </summary>
    /// <remarks>
    /// The count is the point. "Some nodes override the theme" invites exactly
    /// the question the message exists to answer.
    /// </remarks>
    [AvaloniaFact]
    public void TheTooltipExplainsWhyTheThemeLooksIgnored()
    {
        var (window, _) = Open(Mixed);
        var tip = Tip(window);

        Assert.Contains("forest", tip);
        Assert.Contains("overrides", tip);
        Assert.Contains("2 of 3", tip);
        Assert.Contains("Turn this off", tip);
    }

    /// <summary>The message flips when the toggle does.</summary>
    /// <remarks>
    /// Both singular and plural are asserted because the two sentences need
    /// different grammar, and sharing one fragment produced "restore the 9 nodes
    /// have their own colour" — visible on screen, invisible in the code.
    /// </remarks>
    [AvaloniaFact]
    public void TheTooltipFollowsTheToggle()
    {
        var (window, _) = Open(Mixed);

        window.FindControl<ToggleButton>("StylingToggle")!.IsChecked = false;
        var off = Tip(window);

        Assert.Contains("OFF", off);
        Assert.Contains("colours every node", off);
        Assert.Contains("restore the 2 nodes' own colours", off);
    }

    /// <summary>One styled node reads as one, not as "1 nodes".</summary>
    [AvaloniaFact]
    public void TheMessageIsGrammaticalForASingleStyledNode()
    {
        var (window, _) = Open("""
            ---
            config:
              theme: dark
            x-merpeeps:
              spec: 1
              style:
                nodes:
                  a: {fill: '#FF0000'}
            ---
            flowchart TD
                a[One] --> b[Two]
            """);

        Assert.Contains("1 of 2 node has its own colour", Tip(window));

        window.FindControl<ToggleButton>("StylingToggle")!.IsChecked = false;
        Assert.Contains("restore the 1 node's own colour", Tip(window));
    }

    /// <summary>
    /// A theme on a diagram with no Merpeeps colours says so, rather than warning
    /// about an override that does not exist.
    /// </summary>
    [AvaloniaFact]
    public void WithNoMerpeepsColoursTheTooltipSaysTheThemeIsFullyInEffect()
    {
        var (window, _) = Open(
            "---\nconfig:\n  theme: dark\n---\nflowchart TD\n    a[One] --> b[Two]\n");

        var tip = Tip(window);
        Assert.Contains("dark", tip);
        Assert.Contains("every node", tip);
        Assert.DoesNotContain("overrides", tip);
    }

    /// <summary>
    /// The message tracks the theme being chosen, not just the file being opened.
    /// </summary>
    [AvaloniaFact]
    public void ChoosingAThemeUpdatesTheTooltip()
    {
        var (window, _) = Open("""
            ---
            x-merpeeps:
              spec: 1
              style:
                nodes:
                  a: {fill: '#FF0000'}
            ---
            flowchart TD
                a[One] --> b[Two]
            """);

        Assert.DoesNotContain("overrides", Tip(window));

        var box = window.FindControl<ComboBox>("DiagramThemeBox")!;
        box.SelectedIndex = box.ItemsSource!.Cast<string>().ToList().IndexOf("Forest");

        var tip = Tip(window);
        Assert.Contains("forest", tip);
        Assert.Contains("1 of 2", tip);

        // Singular, because one node is one node.
        Assert.Contains("node has its own", tip);
    }

    /// <summary>
    /// Colouring a node after choosing a theme updates the count.
    /// </summary>
    /// <remarks>
    /// The obvious order of operations — set a theme, then start colouring
    /// individual nodes — and the one where a stale count would be most
    /// misleading.
    /// </remarks>
    [AvaloniaFact]
    public void StylingANodeAfterwardsUpdatesTheCount()
    {
        var (window, canvas) = Open(
            "---\nconfig:\n  theme: forest\n---\nflowchart TD\n    a[One] --> b[Two]\n");

        Assert.DoesNotContain("overrides", Tip(window));

        canvas.SelectByIds(new[] { "a" });
        canvas.ApplyStyle(new Merpeeps.Core.Editing.StyleNodesCommand.Style(Fill: "#FF0000"));

        var tip = Tip(window);
        Assert.Contains("overrides", tip);
        Assert.Contains("1 of 2", tip);
    }

    /// <summary>
    /// Picking a theme shows the explanation without waiting to be hovered.
    /// </summary>
    /// <remarks>
    /// <para>The check the first version of this feature was missing. It set the
    /// tooltip text and asserted the text was right — which it was, and which
    /// meant nothing, because a tooltip only appears on hover. It reached
    /// exactly the people who already suspected something was wrong, and never
    /// the person who picked a theme, saw nothing happen, and concluded the
    /// feature was broken.</para>
    ///
    /// <para>So this asserts <c>IsOpen</c>, not the text. Getting the words
    /// right is worth nothing if nobody is shown them.</para>
    /// </remarks>
    [AvaloniaFact]
    public void PickingAThemeShowsTheExplanationWithoutHovering()
    {
        var (window, _) = Open("""
            ---
            x-merpeeps:
              spec: 1
              style:
                nodes:
                  a: {fill: '#FF0000'}
            ---
            flowchart TD
                a[One] --> b[Two]
            """);

        var toggle = window.FindControl<ToggleButton>("StylingToggle")!;
        Assert.False(ToolTip.GetIsOpen(toggle));

        var box = window.FindControl<ComboBox>("DiagramThemeBox")!;
        box.SelectedIndex = box.ItemsSource!.Cast<string>().ToList().IndexOf("Forest");

        Assert.True(ToolTip.GetIsOpen(toggle),
            "the explanation was written but never shown");

        // Opening a tooltip from code has no pointer to anchor to, so the default
        // PlacementMode.Pointer drops it in the corner of the screen — measured at
        // roughly (0,20) while the toggle sat at (875,145). Visible, but detached
        // from the thing it explains, which is barely better than not showing it.
        Assert.Equal(PlacementMode.BottomEdgeAlignedRight, ToolTip.GetPlacement(toggle));
    }

    /// <summary>
    /// Nothing pops up when there is nothing to explain.
    /// </summary>
    /// <remarks>
    /// A message that appeared on every theme change would be noise within a
    /// minute, and noise is ignored — including on the one occasion it matters.
    /// </remarks>
    [AvaloniaFact]
    public void NothingPopsUpWhenThereIsNoConflict()
    {
        var (window, _) = Open("flowchart TD\n    a[One] --> b[Two]\n");
        var toggle = window.FindControl<ToggleButton>("StylingToggle")!;

        var box = window.FindControl<ComboBox>("DiagramThemeBox")!;
        box.SelectedIndex = box.ItemsSource!.Cast<string>().ToList().IndexOf("Forest");

        Assert.False(ToolTip.GetIsOpen(toggle),
            "no node carries a Merpeeps colour, so there is nothing to warn about");
    }

    /// <summary>
    /// Opening a file that already names a theme says nothing.
    /// </summary>
    /// <remarks>
    /// It is not a surprise that needs explaining — the theme was already the
    /// author's choice. Only changing it is.
    /// </remarks>
    [AvaloniaFact]
    public void OpeningAThemedFileDoesNotPopUp()
    {
        var (window, _) = Open(Mixed);

        Assert.False(ToolTip.GetIsOpen(window.FindControl<ToggleButton>("StylingToggle")!));
    }

    /// <summary>
    /// With styling already off there is no conflict, so nothing pops up.
    /// </summary>
    [AvaloniaFact]
    public void NothingPopsUpWhenStylingIsAlreadyOff()
    {
        var (window, _) = Open("""
            ---
            x-merpeeps:
              spec: 1
              style:
                nodes:
                  a: {fill: '#FF0000'}
            ---
            flowchart TD
                a[One] --> b[Two]
            """);

        var toggle = window.FindControl<ToggleButton>("StylingToggle")!;
        toggle.IsChecked = false;

        var box = window.FindControl<ComboBox>("DiagramThemeBox")!;
        box.SelectedIndex = box.ItemsSource!.Cast<string>().ToList().IndexOf("Forest");

        Assert.False(ToolTip.GetIsOpen(toggle));
    }
}
