using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Merpeeps.Core.Layout;
using Xunit;

namespace Merpeeps.App.Tests;

/// <summary>
/// The layout dropdown offers the engines, and nothing it cannot run.
/// </summary>
/// <remarks>
/// <para>This list used to be longer. ELK's four algorithms and mermaid's dagre
/// were offered alongside the native engines, computed by the reference renderer
/// and harvested back as coordinates. They were removed because selecting one
/// opened a browser pane in the middle of an edit, which reads as a bug rather
/// than a feature — and because the ELK plugin cost 1.6 MB of embedded resource
/// to support it.</para>
///
/// <para>So these now pin the opposite property to the ones they replaced: every
/// entry must be something the app can compute in-process, with no renderer and
/// no pane.</para>
/// </remarks>
public class EditorLayoutsTests
{
    [Fact]
    public void EveryNativeEngineIsOffered()
    {
        foreach (var engine in LayoutEngines.Available)
            Assert.Contains(EditorLayouts.All, c => c.Id == engine.Id);
    }

    /// <summary>
    /// Every offered layout runs in C#.
    /// </summary>
    /// <remarks>
    /// The guard against the old behaviour coming back by accident. If an entry
    /// ever appears that <see cref="LayoutEngines"/> cannot resolve, selecting it
    /// would throw inside <c>Relayout</c> — which is the failure this pins.
    /// </remarks>
    [Fact]
    public void EveryOfferedLayoutIsOneTheAppCanCompute()
    {
        foreach (var choice in EditorLayouts.All)
            Assert.Contains(LayoutEngines.Available, e => e.Id == choice.Id);
    }

    /// <summary>
    /// No layout entry is a borrowed mermaid algorithm.
    /// </summary>
    /// <remarks>
    /// Named explicitly rather than left implied, because these are the exact ids
    /// that used to be here. A regression would most likely reintroduce them by
    /// name.
    /// </remarks>
    [Theory]
    [InlineData("elk")] [InlineData("elk.mrtree")]
    [InlineData("elk.force")] [InlineData("elk.stress")] [InlineData("dagre")]
    public void BorrowedLayoutsAreNotOffered(string id)
    {
        Assert.DoesNotContain(EditorLayouts.All, c => c.Id == id);
    }

    /// <summary>
    /// A file recording a retired engine keeps its arrangement.
    /// </summary>
    /// <remarks>
    /// The one thing that had to survive the removal. A layout is stored as
    /// coordinates, not as an instruction to re-run an engine, so a document
    /// saying <c>engine: elk</c> still draws exactly as it did. -1 tells the
    /// dropdown to leave itself alone rather than snapping to a default, which
    /// would misreport what produced the arrangement on screen.
    /// </remarks>
    [Theory]
    [InlineData("elk")] [InlineData("elk.mrtree")] [InlineData("dagre")]
    [InlineData("manual")] [InlineData("something-invented-later")]
    public void AnUnknownEngineLeavesTheDropdownAlone(string id)
    {
        Assert.Equal(-1, EditorLayouts.IndexOf(id));
    }

    [Fact]
    public void ManualIsNotOfferedAsALayout()
    {
        // "manual" means a person placed the nodes. Offering it as a choice
        // would imply selecting it does something.
        Assert.DoesNotContain(EditorLayouts.All, c => c.Id == "manual");
        Assert.Equal(-1, EditorLayouts.IndexOf("manual"));
        Assert.Equal(-1, EditorLayouts.IndexOf(null));
    }

    [Fact]
    public void LabelsAreUnique()
    {
        var labels = EditorLayouts.All.Select(c => c.Label).ToList();
        Assert.Equal(labels.Count, labels.Distinct().Count());
    }

    /// <summary>The dropdown shows the whole list.</summary>
    [AvaloniaFact]
    public void TheDropdownOffersEveryLayout()
    {
        var window = new MainWindow();
        window.Show();

        var box = window.FindControl<ComboBox>("EngineBox")!;
        var items = box.ItemsSource!.Cast<string>().ToList();

        Assert.Equal(EditorLayouts.All.Count, items.Count);
        Assert.DoesNotContain(items, i => i.Contains("ELK"));
    }

    /// <summary>The Layout > Engine submenu mirrors the dropdown exactly.</summary>
    [AvaloniaFact]
    public void TheMenuMirrorsTheDropdown()
    {
        var window = new MainWindow();
        window.Show();

        var menu = window.FindControl<MenuItem>("MenuEngines")!;
        var headers = menu.ItemsSource!.Cast<MenuItem>()
            .Select(m => (string)m.Header!).ToList();
        var box = window.FindControl<ComboBox>("EngineBox")!.ItemsSource!
            .Cast<string>().ToList();

        Assert.Equal(box, headers);
    }
}
