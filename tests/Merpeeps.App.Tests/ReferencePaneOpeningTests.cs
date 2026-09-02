using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Xunit;

namespace Merpeeps.App.Tests;

/// <summary>
/// Choosing a layout never opens the reference pane.
/// </summary>
/// <remarks>
/// <para>This class used to assert the opposite, and the history is the reason it
/// still exists. Some layouts — ELK's four algorithms and mermaid's dagre — were
/// computed by the reference renderer rather than in C#, so selecting one had to
/// open the pane: a web view in a collapsed column never lays out, every node
/// measures zero, and the harvest comes back as a pile of coincident points.
/// Opening it was the fix for that, and it worked.</para>
///
/// <para>It was still the wrong behaviour. A dropdown that reaches out and
/// rearranges the window is surprising, and the report it produced was "why is an
/// upstream renderer popping up?" — someone reading a feature as a bug, which for
/// interface purposes is the same thing. The borrowed layouts were removed
/// rather than made quieter.</para>
///
/// <para>So these pin the inverse property: whatever is in the layout dropdown,
/// picking it rearranges the diagram and nothing else.</para>
/// </remarks>
public class ReferencePaneOpeningTests
{
    private const string Source = "flowchart TD\n    a[One] --> b[Two]\n";

    private static MainWindow Open()
    {
        var window = new MainWindow();
        window.Show();
        window.LoadSourceForTest(Source);
        window.ApplyTemplate();
        return window;
    }

    private static bool PaneIsOpen(MainWindow w) =>
        w.FindControl<DockPanel>("ReferencePane")!.IsVisible;

    /// <summary>
    /// The pane starts collapsed: hidden *and* zero-width.
    /// </summary>
    /// <remarks>
    /// Both are needed. A zero-width Grid column still lays its children out and
    /// Avalonia does not clip them, so the mode dropdown used to draw over the
    /// property pane next door.
    /// </remarks>
    [AvaloniaFact]
    public void ThePaneStartsFullyCollapsed()
    {
        var window = Open();
        var split = window.FindControl<Grid>("CanvasSplit")!;

        Assert.False(PaneIsOpen(window));
        Assert.Equal(0, split.ColumnDefinitions[2].Width.Value);
    }

    /// <summary>
    /// No layout in the dropdown opens the pane.
    /// </summary>
    /// <remarks>
    /// Every entry, not a sample. The old behaviour was attached to a property of
    /// the choice rather than to a particular one, so a partial check could pass
    /// while a single reintroduced entry still hijacked the window.
    /// </remarks>
    [AvaloniaFact]
    public void NoLayoutChoiceOpensThePane()
    {
        var window = Open();
        var box = window.FindControl<ComboBox>("EngineBox")!;

        for (var i = 0; i < EditorLayouts.All.Count; i++)
        {
            box.SelectedIndex = i;

            Assert.False(PaneIsOpen(window),
                $"'{EditorLayouts.All[i].Label}' opened the reference pane");
            Assert.False(window.FindControl<MenuItem>("MenuShowReference")!.IsChecked,
                $"'{EditorLayouts.All[i].Label}' ticked the pane's menu item");
        }

        var split = window.FindControl<Grid>("CanvasSplit")!;
        Assert.Equal(0, split.ColumnDefinitions[2].Width.Value);
        Assert.False(window.FindControl<GridSplitter>("ReferenceSplitter")!.IsVisible);
    }

    /// <summary>
    /// Every layout actually rearranges the diagram.
    /// </summary>
    /// <remarks>
    /// <para>The other half, and the reason the check above is not satisfied by a
    /// dropdown that does nothing at all. Two nodes cannot land on top of each
    /// other under any sane engine, so a shared position means the layout did not
    /// run.</para>
    ///
    /// <para>The relayout is triggered explicitly rather than trusted to fall out
    /// of the assignment. Setting <c>SelectedIndex</c> to the value it already
    /// holds raises no event, so the entry that happens to be selected on startup
    /// would be asserted about without ever having run — which is how the first
    /// version of this passed the pane check and failed here.</para>
    /// </remarks>
    [AvaloniaFact]
    public void EveryLayoutArrangesTheDiagram()
    {
        var window = Open();
        var box = window.FindControl<ComboBox>("EngineBox")!;
        var relayout = window.FindControl<MenuItem>("MenuRelayout")!;

        for (var i = 0; i < EditorLayouts.All.Count; i++)
        {
            box.SelectedIndex = i;
            relayout.RaiseClick();

            var nodes = window.CanvasForTest.Model!.Nodes;
            var places = nodes.Select(n => (n.X, n.Y)).ToList();

            Assert.Equal(places.Count, places.Distinct().Count());
            Assert.Equal(EditorLayouts.All[i].Id, window.CanvasForTest.Model!.LayoutEngine);
        }
    }

    /// <summary>
    /// Picking a layout never opens a window.
    /// </summary>
    /// <remarks>
    /// Headless tests have no web engine, which is what made the old path fail —
    /// and it failed by opening a modal error window per attempt, stacked over the
    /// canvas. Nothing in the layout path should be able to do that now.
    /// </remarks>
    [AvaloniaFact]
    public void PickingALayoutOpensNoWindows()
    {
        var window = Open();

        for (var i = 0; i < EditorLayouts.All.Count; i++)
        {
            window.FindControl<ComboBox>("EngineBox")!.SelectedIndex = i;
            Assert.Empty(window.OwnedWindows);
        }
    }
}
