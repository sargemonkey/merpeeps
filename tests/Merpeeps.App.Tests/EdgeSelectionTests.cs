using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Merpeeps.Core.Model;
using Xunit;

namespace Merpeeps.App.Tests;

/// <summary>
/// Selecting and editing a link from the editor.
/// </summary>
/// <remarks>
/// Core could delete a link before any of this existed, and the MCP server could
/// reach it — but the canvas could only ever create one. So an agent could undo a
/// mistaken connection and the person sitting in front of the editor could not,
/// unless they hit Ctrl+Z immediately. These cover the gap.
/// </remarks>
public class EdgeSelectionTests
{
    private const string Source =
        "flowchart TD\n    a[One] -->|old| b[Two]\n    b --> c[Three]\n";

    private static (MainWindow Window, EditorCanvas Canvas) Open()
    {
        var window = new MainWindow();
        window.Show();
        window.LoadSourceForTest(Source);
        window.ApplyTemplate();
        return (window, window.CanvasForTest);
    }

    private static DiagramEdge Edge(EditorCanvas c, string from, string to) =>
        c.Model!.Edges.First(e => e.From == from && e.To == to);

    /// <summary>
    /// The property pane swaps to link properties, and back again.
    /// </summary>
    /// <remarks>
    /// Shown instead of the node properties, not alongside. A pane offering an
    /// icon picker for a link would be offering something that cannot work.
    /// </remarks>
    [AvaloniaFact]
    public void SelectingALinkSwapsThePropertyPane()
    {
        var (window, canvas) = Open();

        var edgePanel = window.FindControl<StackPanel>("EdgeProperties")!;
        var nodePanel = window.FindControl<StackPanel>("NodeProperties")!;

        Assert.False(edgePanel.IsVisible);
        Assert.False(nodePanel.IsVisible);
        canvas.SelectByIds(["a"]);
        Assert.True(nodePanel.IsVisible);

        canvas.SelectEdge(Edge(canvas, "a", "b"));

        Assert.True(edgePanel.IsVisible);
        Assert.False(nodePanel.IsVisible);
        Assert.Equal("a → b", window.FindControl<TextBlock>("SelInfo")!.Text);
        Assert.Equal("old", window.FindControl<TextBox>("EdgeLabelBox")!.Text);

        canvas.SelectEdge(null);
        Assert.False(edgePanel.IsVisible);
        Assert.False(nodePanel.IsVisible);
        canvas.SelectByIds(["b"]);
        Assert.True(nodePanel.IsVisible);
    }

    /// <summary>Selecting a node clears the link selection, and the reverse.</summary>
    /// <remarks>
    /// They are alternatives. Holding both would leave the pane with nothing
    /// coherent to show and Delete with no single meaning.
    /// </remarks>
    [AvaloniaFact]
    public void NodeAndLinkSelectionAreExclusive()
    {
        var (_, canvas) = Open();

        canvas.SelectEdge(Edge(canvas, "a", "b"));
        Assert.NotNull(canvas.SelectedEdge);

        canvas.SelectByIds(new[] { "a" });
        Assert.Null(canvas.SelectedEdge);
        Assert.Single(canvas.Selection);

        canvas.SelectEdge(Edge(canvas, "b", "c"));
        Assert.Empty(canvas.Selection);
        Assert.NotNull(canvas.SelectedEdge);
    }

    [AvaloniaFact]
    public void TheArrowStyleDropdownChangesTheLink()
    {
        var (window, canvas) = Open();
        canvas.SelectEdge(Edge(canvas, "a", "b"));

        var box = window.FindControl<ComboBox>("EdgeKindBox")!;
        var index = MainWindow.EdgeKindChoices
            .Select((c, i) => (c, i)).First(t => t.c.Kind == EdgeKind.ThickArrow).i;

        box.SelectedIndex = index;

        Assert.Equal(EdgeKind.ThickArrow, Edge(canvas, "a", "b").Kind);
        Assert.Contains("a[One] ==>", window.FindControl<TextBox>("SourceBox")!.Text);
    }

    /// <summary>The dropdown offers every arrow style mermaid has.</summary>
    /// <remarks>
    /// <c>Invisible</c> included: it is real syntax used to force layout without
    /// drawing a line, and omitting it would mean a diagram containing one could
    /// not be edited without losing it.
    /// </remarks>
    [Fact]
    public void EveryArrowStyleIsOffered()
    {
        foreach (EdgeKind kind in System.Enum.GetValues<EdgeKind>())
            Assert.Contains(MainWindow.EdgeKindChoices, c => c.Kind == kind);
    }

    [AvaloniaFact]
    public void DeleteRemovesTheSelectedLinkAndLeavesTheNodes()
    {
        var (_, canvas) = Open();
        canvas.SelectEdge(Edge(canvas, "a", "b"));

        canvas.DeleteSelection();

        Assert.Single(canvas.Model!.Edges);
        Assert.Equal(3, canvas.Model.Nodes.Count);
        Assert.Null(canvas.SelectedEdge);

        canvas.Undo();
        Assert.Equal(2, canvas.Model.Edges.Count);
    }

    /// <summary>
    /// Typing a label and clicking elsewhere still commits the edit.
    /// </summary>
    /// <remarks>
    /// <para>The bug this pins. The commit used to read the canvas's current
    /// selection when focus was lost — but focus is usually lost <em>because</em>
    /// the user clicked something else, which had already cleared the selection.
    /// The typed text then vanished with nothing to say why.</para>
    ///
    /// <para>The link is captured when the box takes focus instead, so the edit
    /// lands on the link it was started on.</para>
    /// </remarks>
    [AvaloniaFact]
    public void ALabelCommitsEvenWhenTheSelectionMovesOnFirst()
    {
        var (window, canvas) = Open();
        var edge = Edge(canvas, "a", "b");
        canvas.SelectEdge(edge);

        var box = window.FindControl<TextBox>("EdgeLabelBox")!;
        box.Focus();
        box.Text = "renamed";

        // Exactly the sequence a click on another node produces: the selection
        // changes, and only then does the box lose focus.
        canvas.SelectByIds(new[] { "c" });
        box.RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent));

        Assert.Equal("renamed", edge.Label);
        Assert.Contains("|renamed|", window.FindControl<TextBox>("SourceBox")!.Text);
    }

    [AvaloniaFact]
    public void ClearingTheLabelBoxRemovesTheLabel()
    {
        var (window, canvas) = Open();
        var edge = Edge(canvas, "a", "b");
        canvas.SelectEdge(edge);

        var box = window.FindControl<TextBox>("EdgeLabelBox")!;
        box.Focus();
        box.Text = "";
        box.RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent));

        Assert.Null(edge.Label);
        Assert.DoesNotContain("|", window.FindControl<TextBox>("SourceBox")!.Text);
    }

    /// <summary>
    /// Committing an unchanged label records no undo step.
    /// </summary>
    /// <remarks>
    /// Focus passes through the box whenever a link is selected, so a commit on
    /// every LostFocus would fill the undo stack with edits that changed
    /// nothing.
    /// </remarks>
    [AvaloniaFact]
    public void AnUnchangedLabelIsNotAnEdit()
    {
        var (window, canvas) = Open();
        canvas.SelectEdge(Edge(canvas, "a", "b"));

        var box = window.FindControl<TextBox>("EdgeLabelBox")!;
        box.Focus();
        box.RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent));

        Assert.False(canvas.History!.CanUndo);
    }

    /// <summary>The Delete link button does what the Delete key does.</summary>
    [AvaloniaFact]
    public void TheDeleteButtonMatchesTheDeleteKey()
    {
        var (window, canvas) = Open();
        canvas.SelectEdge(Edge(canvas, "a", "b"));

        window.FindControl<Button>("DeleteEdgeBtn")!
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Single(canvas.Model!.Edges);
    }
}
