using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Merpeeps.Core.Export;
using Xunit;

namespace Merpeeps.App.Tests;

/// <summary>
/// Baking from the editor: plain mermaid that keeps the colours.
/// </summary>
/// <remarks>
/// <para>This existed as <c>merpeeps bake</c> for a long time before it existed in
/// the window, which made the one export most people want reachable only by
/// people who open a terminal. The two neighbouring menu items are both worse
/// answers to the same question: "Export plain mermaid" throws the colours away
/// with the metadata, and SVG keeps everything but stops being a diagram.</para>
///
/// <para><see cref="MermaidBake"/> itself is covered in the Core suite. What is
/// covered here is the part written for the window — the text the user is shown,
/// which is the whole point of surfacing a lossy operation in a GUI, and which
/// cannot be checked by reading the code that produces it.</para>
/// </remarks>
public class BakeExportTests
{
    private static MermaidBake.Result Result(
        int styled = 2,
        string[]? icons = null,
        string[]? positions = null,
        string? unsupported = null) =>
        new(
            Output: "flowchart TD\n    a[One]\n",
            StyledNodes: Enumerable.Range(0, styled).Select(i => $"n{i}").ToArray(),
            DroppedIcons: icons ?? [],
            DroppedPositions: positions ?? [],
            UnsupportedKind: unsupported);

    /// <summary>
    /// A bake that lost nothing says nothing.
    /// </summary>
    /// <remarks>
    /// The status bar still reports the export. A modal on every bake would be
    /// noise, and noise is skipped — including on the run where something really
    /// was dropped.
    /// </remarks>
    [Fact]
    public void ALosslessBakeRaisesNoDialog()
    {
        Assert.Null(MainWindow.BakeNotice(Result(), "diagram-baked.mmd"));
    }

    /// <summary>
    /// Dropped icons are named, not counted.
    /// </summary>
    /// <remarks>
    /// "3 icons were dropped" tells you a number; naming them tells you which
    /// nodes to go and look at. The point of the message is that it can be acted
    /// on.
    /// </remarks>
    [Fact]
    public void DroppedIconsAreNamed()
    {
        var notice = MainWindow.BakeNotice(
            Result(icons: ["web", "db"]), "diagram-baked.mmd")!;

        Assert.Contains("web", notice);
        Assert.Contains("db", notice);
        Assert.Contains("Icons on", notice);
    }

    /// <summary>
    /// A long list of ids is truncated rather than dumped.
    /// </summary>
    /// <remarks>
    /// Forty ids read as "lots" and no more usefully than a number would, while
    /// burying the few worth seeing. The count still has to survive the
    /// truncation, or the message understates the loss.
    /// </remarks>
    [Fact]
    public void ALongListOfIconsIsSummarised()
    {
        var many = Enumerable.Range(1, 12).Select(i => $"node{i}").ToArray();
        var notice = MainWindow.BakeNotice(Result(icons: many), "d.mmd")!;

        Assert.Contains("node1", notice);
        Assert.Contains("and 8 more", notice);
        Assert.DoesNotContain("node12", notice);
    }

    /// <summary>
    /// Dropped positions are reported even when nothing else was.
    /// </summary>
    /// <remarks>
    /// The most likely loss by far — every arranged diagram has positions — and
    /// the one a user is most likely to be surprised by, because the whole reason
    /// they used this editor was to place the boxes.
    /// </remarks>
    [Fact]
    public void DroppedPositionsAreReportedOnTheirOwn()
    {
        var notice = MainWindow.BakeNotice(
            Result(positions: ["a", "b", "c"]), "d.mmd")!;

        Assert.Contains("3 stored position(s)", notice);
        Assert.DoesNotContain("Icons on", notice);
    }

    /// <summary>
    /// The message says the original file was not touched.
    /// </summary>
    /// <remarks>
    /// Without it, "positions dropped" reads as "your work is gone" rather than
    /// "the copy does not carry them" — the difference between a warning and a
    /// scare.
    /// </remarks>
    [Fact]
    public void TheNoticeSaysTheOriginalIsSafe()
    {
        var notice = MainWindow.BakeNotice(Result(positions: ["a"]), "d.mmd")!;
        Assert.Contains("untouched", notice);
    }

    /// <summary>
    /// The file that was written is named.
    /// </summary>
    [Fact]
    public void TheNoticeNamesTheFile()
    {
        var notice = MainWindow.BakeNotice(
            Result(positions: ["a"]), "orders-baked.mmd")!;

        Assert.Contains("orders-baked.mmd", notice);
    }

    /// <summary>
    /// The exported file is plain mermaid carrying the colours.
    /// </summary>
    /// <remarks>
    /// The end of the path that actually touches disk. Everything above it is
    /// covered elsewhere; this is the only check that the thing written is the
    /// thing intended.
    /// </remarks>
    [AvaloniaFact]
    public async Task TheExportedFileCarriesTheStyling()
    {
        var window = new MainWindow();
        window.Show();
        window.LoadSourceForTest("""
            ---
            x-merpeeps:
              spec: 1
              style:
                nodes:
                  a: {fill: '#FF0000', stroke: '#880000'}
            ---
            flowchart TD
                a[One] --> b[Two]
            """);
        window.ApplyTemplate();

        var path = Path.Combine(Path.GetTempPath(), $"merpeeps-bake-{Guid.NewGuid():N}.mmd");
        try
        {
            await window.WriteBaked(path);
            var written = await File.ReadAllTextAsync(path);

            Assert.Contains("style a fill:#FF0000", written);
            Assert.Contains("a[One] --> b[Two]", written);

            // The Merpeeps block must not survive: the whole point is a file that
            // needs no plugin, and leaving the metadata in would ship both halves
            // of a decision that was made to drop one.
            Assert.DoesNotContain("x-merpeeps", written);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    /// <summary>
    /// An unsaved edit is carried into the export.
    /// </summary>
    /// <remarks>
    /// The export reads the live document rather than the file on disk. Baking a
    /// stale copy of what someone is looking at would be a quiet way to hand them
    /// the wrong file — quiet because the output is valid mermaid either way.
    /// </remarks>
    [AvaloniaFact]
    public async Task AnUnsavedEditIsBakedToo()
    {
        var window = new MainWindow();
        window.Show();
        window.LoadSourceForTest("flowchart TD\n    a[One] --> b[Two]\n");
        window.ApplyTemplate();

        window.CanvasForTest.SelectByIds(["a"]);
        window.CanvasForTest.ApplyStyle(
            new Merpeeps.Core.Editing.StyleNodesCommand.Style(Fill: "#00FF00"));

        var path = Path.Combine(Path.GetTempPath(), $"merpeeps-bake-{Guid.NewGuid():N}.mmd");
        try
        {
            await window.WriteBaked(path);
            Assert.Contains("style a fill:#00FF00", await File.ReadAllTextAsync(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    /// <summary>
    /// A diagram type that cannot be baked says so, and writes nothing.
    /// </summary>
    /// <remarks>
    /// <para>Runs through the real menu item, so the wiring is under test rather
    /// than assumed. <c>On()</c> resolves the item with <c>Need&lt;&gt;</c>, which
    /// throws, so a missing item would take the whole window down at startup —
    /// but a handler attached to the wrong method would not, and that is what
    /// this catches.</para>
    ///
    /// <para>The check happens before the file picker deliberately. Mermaid's
    /// sequence grammar rejects <c>style</c>, so there is nothing to bake and the
    /// output would be an ordinary strip. Asking someone to choose a filename for
    /// that wastes their time, and writing it silently would answer a question
    /// they did not ask.</para>
    /// </remarks>
    [AvaloniaFact]
    public void ADiagramThatCannotBeBakedExplainsItself()
    {
        var window = new MainWindow();
        window.Show();
        window.LoadSourceForTest("""
            sequenceDiagram
                Alice->>Bob: Hello
            """);
        window.ApplyTemplate();

        Assert.Empty(window.OwnedWindows);

        window.FindControl<MenuItem>("MenuExportBaked")!.RaiseClick();

        var dialog = Assert.Single(window.OwnedWindows);
        Assert.Equal("Export with styling baked in", dialog.Title);
    }

    /// <summary>
    /// A bakeable diagram goes straight on to the file picker.
    /// </summary>
    /// <remarks>
    /// The control for the test above. Without it, that one would pass just as
    /// happily on a handler that popped the same dialog for every diagram — which
    /// would be a worse bug than the one it is guarding, because it would block
    /// the export entirely.
    /// </remarks>
    [AvaloniaFact]
    public void ABakeableDiagramRaisesNoDialog()
    {
        var window = new MainWindow();
        window.Show();
        window.LoadSourceForTest("flowchart TD\n    a[One] --> b[Two]\n");
        window.ApplyTemplate();

        window.FindControl<MenuItem>("MenuExportBaked")!.RaiseClick();

        // Headless has no storage provider, so the picker resolves to nothing and
        // the handler returns. What matters is that it got that far.
        Assert.Empty(window.OwnedWindows);
    }

    /// <summary>
    /// The menu item exists and is reachable.
    /// </summary>
    /// <remarks>
    /// The failure this whole change is about: the capability existed and no
    /// route to it did.
    /// </remarks>
    [AvaloniaFact]
    public void TheExportIsInTheFileMenu()
    {
        var window = new MainWindow();
        window.Show();
        window.LoadSourceForTest("flowchart TD\n    a[One] --> b[Two]\n");
        window.ApplyTemplate();

        var item = window.FindControl<MenuItem>("MenuExportBaked");

        Assert.NotNull(item);
        Assert.Equal("Export with styling _baked in…", item!.Header);
    }

    /// <summary>
    /// The three exports are distinguishable from their tooltips alone.
    /// </summary>
    /// <remarks>
    /// They produce very different files and one of them silently discards the
    /// colours, so a person choosing between them from the menu has to be able to
    /// tell which is which without trying all three. The guide had this wrong
    /// while it was being written — describing "Export plain mermaid" as the one
    /// that keeps the styling, which is exactly backwards.
    /// </remarks>
    [AvaloniaFact]
    public void TheThreeExportsExplainHowTheyDiffer()
    {
        var window = new MainWindow();
        window.Show();
        window.ApplyTemplate();

        string Tip(string name) =>
            ToolTip.GetTip(window.FindControl<MenuItem>(name)!) as string ?? "";

        // Plain has to say the colours go too, or it reads like the safe choice.
        Assert.Contains("colours", Tip("MenuExport"));
        // Baked has to say the colours stay, and what it cannot carry.
        Assert.Contains("colours", Tip("MenuExportBaked"));
        Assert.Contains("dropped", Tip("MenuExportBaked"));

        var tips = new[] { "MenuExport", "MenuExportBaked", "MenuExportSvg" }
            .Select(Tip).ToList();
        Assert.Equal(3, tips.Distinct().Count());
        Assert.DoesNotContain("", tips);
    }
}
