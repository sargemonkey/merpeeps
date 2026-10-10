using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Merpeeps.Render;
using Xunit;

namespace Merpeeps.App.Tests;

/// <summary>
/// Covers the reference-render pane as far as a headless runner can reach.
///
/// <para>The webview is a <em>native child surface</em>. It composites outside
/// Avalonia, so it never appears in a screenshot or in the accessibility tree,
/// and it cannot exist at all on a machine with no platform web engine — which
/// includes every CI runner. Pixel assertions are therefore impossible here,
/// and pretending otherwise would produce tests that pass by accident.</para>
///
/// <para>So this covers the two halves that <em>are</em> reachable: the file the
/// pane feeds the engine, and what happens when there is no engine. The third
/// half — that the page actually draws a diagram — is covered by
/// <c>oracle/preview-page-test.mjs</c>, which loads the same page in real Chrome.
/// Between them nothing is left to hope.</para>
/// </summary>
public class ReferencePaneTests
{
    // --------------------------------------------- the file handed to the engine

    /// <summary>
    /// Each shell built once for the whole class.
    ///
    /// <para>These payloads are ~3.6 MB. Building or reading one per test put
    /// four of them plus their StringBuilder growth on the large-object heap in
    /// quick succession, and the resulting GC churn cost seconds per test —
    /// measured, after two wrong guesses about the cause. Composition itself is
    /// 11 ms and the file write 4 ms, so the cost was never the work; it was
    /// doing the work repeatedly.</para>
    /// </summary>
    private static readonly Lazy<string> StockShell =
        new(() => RenderAssets.BuildShell(RenderAssets.Mode.Stock, dark: false));

    private static readonly Lazy<string> MerpeepsShell =
        new(() => RenderAssets.BuildShell(RenderAssets.Mode.WithMerpeeps, dark: false));

    /// <summary>Read only the opening bytes, never the whole file.</summary>
    private static string Head(string path, int chars = 512)
    {
        using var reader = new StreamReader(path);
        var buffer = new char[chars];
        var read = reader.ReadBlock(buffer, 0, chars);
        return new string(buffer, 0, read);
    }

    /// <summary>
    /// The reason a file exists at all: WebView2 caps <c>NavigateToString</c> at
    /// roughly 2 MB, and exceeding it killed the process rather than throwing.
    /// If the shell ever shrinks below that the workaround stops being load
    /// bearing — and if this fires the other way, the payload has grown and
    /// someone should know.
    /// </summary>
    [Fact]
    public void TheShellIsTooLargeForNavigateToString()
    {
        Assert.True(StockShell.Value.Length > 2_000_000,
            $"the shell is {StockShell.Value.Length:N0} bytes; the file-based " +
            "workaround exists because WebView2 caps NavigateToString at about 2 MB");
    }

    /// <summary>
    /// The file is written and starts with a document, for each mode.
    /// </summary>
    /// <remarks>
    /// Only the two modes, not all four mode/theme combinations. Writing a fresh
    /// 3.6 MB file costs seconds on Windows — the first write of each path is
    /// scanned by the on-access antivirus, which measurement pinned down after
    /// two wrong guesses (composition is 11 ms, the write 4 ms, reading it back
    /// 13 ms; all four combinations cost ~30 s only because each is a new file).
    /// That the four paths are distinct is covered separately and cheaply by
    /// <see cref="EachModeAndThemeGetsItsOwnFile"/>, so paying it four times
    /// buys nothing.
    /// </remarks>
    [Theory]
    [InlineData(RenderAssets.Mode.Stock)]
    [InlineData(RenderAssets.Mode.WithMerpeeps)]
    public void TheShellIsWrittenAndReadable(RenderAssets.Mode mode)
    {
        var uri = ReferenceRenderPane.ShellUri(mode, dark: false);

        Assert.True(uri.IsFile);
        Assert.True(File.Exists(uri.LocalPath));
        Assert.True(new FileInfo(uri.LocalPath).Length > 2_000_000);
        Assert.StartsWith("<!DOCTYPE html>", Head(uri.LocalPath), StringComparison.Ordinal);
    }

    /// <summary>
    /// Each mode and theme gets its own file, so switching does not rewrite
    /// megabytes and two panes cannot fight over one path.
    /// </summary>
    [Fact]
    public void EachModeAndThemeGetsItsOwnFile()
    {
        var paths = new[]
        {
            ReferenceRenderPane.ShellUri(RenderAssets.Mode.Stock, false).LocalPath,
            ReferenceRenderPane.ShellUri(RenderAssets.Mode.Stock, true).LocalPath,
            ReferenceRenderPane.ShellUri(RenderAssets.Mode.WithMerpeeps, false).LocalPath,
            ReferenceRenderPane.ShellUri(RenderAssets.Mode.WithMerpeeps, true).LocalPath,
        };

        Assert.Equal(4, paths.Distinct().Count());
    }

    [Fact]
    public void RewritingTheSameShellIsStableAndIdempotent()
    {
        var first = ReferenceRenderPane.ShellUri(RenderAssets.Mode.Stock, false);
        var stamp = new FileInfo(first.LocalPath).Length;

        var second = ReferenceRenderPane.ShellUri(RenderAssets.Mode.Stock, false);

        Assert.Equal(first.LocalPath, second.LocalPath);
        Assert.Equal(stamp, new FileInfo(second.LocalPath).Length);
    }

    [Fact]
    public void CachedShellIsNotRewrittenWhileAWebEngineMapsIt()
    {
        var uri = ReferenceRenderPane.ShellUri(RenderAssets.Mode.Stock, false);
        using var mapped = System.IO.MemoryMappedFiles.MemoryMappedFile.CreateFromFile(
            uri.LocalPath, FileMode.Open, null, 0, System.IO.MemoryMappedFiles.MemoryMappedFileAccess.Read);
        using var view = mapped.CreateViewAccessor(0, 0, System.IO.MemoryMappedFiles.MemoryMappedFileAccess.Read);
        var modified = File.GetLastWriteTimeUtc(uri.LocalPath);
        Assert.Equal(uri, ReferenceRenderPane.ShellUri(RenderAssets.Mode.Stock, false));
        Assert.Equal(modified, File.GetLastWriteTimeUtc(uri.LocalPath));
    }

    /// <summary>
    /// The Merpeeps shell must carry the published plugin; the stock one must not.
    /// Getting this backwards would make the comparison meaningless while still
    /// rendering a plausible diagram — the expensive kind of wrong.
    /// </summary>
    [Fact]
    public void TheTwoModesProduceGenuinelyDifferentPages()
    {
        Assert.DoesNotContain("registerMerpeepsLayout", StockShell.Value, StringComparison.Ordinal);
        Assert.Contains("registerMerpeepsLayout", MerpeepsShell.Value, StringComparison.Ordinal);
        Assert.True(MerpeepsShell.Value.Length > StockShell.Value.Length);
    }

    // ------------------------------------------------- when there is no engine

    /// <summary>
    /// A headless runner has no platform web engine, so constructing the pane
    /// exercises the degradation path for real rather than through a mock.
    ///
    /// <para>The requirement is only that it does not throw and does not take
    /// the editor with it. Whether an engine happens to exist on the machine
    /// running the tests is not something to assert on.</para>
    /// </summary>
    [AvaloniaFact]
    public void ThePaneNeverThrowsWhateverTheEnvironment()
    {
        var pane = new ReferenceRenderPane();

        // Available either way is legitimate; what matters is that we got here.
        Assert.NotNull(pane);

        if (!pane.Available)
        {
            Assert.False(string.IsNullOrWhiteSpace(pane.Unavailable),
                "an unavailable pane must explain itself rather than sit blank");
        }
    }

    /// <summary>
    /// Asking an unavailable pane to render is a no-op, not an exception. The
    /// window calls this on every edit, so a pane that threw would make the
    /// editor unusable on any machine lacking a web engine.
    /// </summary>
    [AvaloniaFact]
    public async Task RenderingThroughAnUnavailablePaneIsHarmless()
    {
        var pane = new ReferenceRenderPane();

        await pane.ShowAsync("flowchart LR\n    a[Alpha] --> b[Beta]\n");
        await pane.ShowAsync("");

        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void SwitchingModeIsSafeBeforeAnythingHasLoaded()
    {
        var pane = new ReferenceRenderPane();

        pane.SetMode(RenderAssets.Mode.Stock, dark: true);
        pane.SetMode(RenderAssets.Mode.WithMerpeeps, dark: false);

        Dispatcher.UIThread.RunJobs();
    }

    // ----------------------------------------------------- wiring in the window

    [AvaloniaFact]
    public void TheWindowOffersTheReferenceToggleAndStartsWithItOff()
    {
        var window = new MainWindow();
        window.Show();
        window.ApplyTemplate();
        Dispatcher.UIThread.RunJobs();

        var toggle = window.FindControl<MenuItem>("MenuShowReference");

        Assert.NotNull(toggle);
        Assert.False(toggle!.IsChecked,
            "the pane starts closed: a webview is expensive to start and most " +
            "sessions never open one");
    }

    /// <summary>
    /// Collapsed means gone, not merely zero-width. A zero-width Grid column
    /// still lays its children out and Avalonia does not clip them — the mode
    /// dropdown drew straight over the property pane next door until both the
    /// width and IsVisible were handled.
    /// </summary>
    [AvaloniaFact]
    public void TheCollapsedPaneIsHiddenNotJustNarrowed()
    {
        var window = new MainWindow();
        window.Show();
        window.ApplyTemplate();
        Dispatcher.UIThread.RunJobs();

        var split = window.FindControl<Grid>("CanvasSplit");
        var pane = window.FindControl<DockPanel>("ReferencePane");
        var splitter = window.FindControl<GridSplitter>("ReferenceSplitter");

        Assert.NotNull(split);
        Assert.NotNull(pane);
        Assert.NotNull(splitter);

        Assert.Equal(0, split!.ColumnDefinitions[2].Width.Value);
        Assert.False(pane!.IsVisible);
        Assert.False(splitter!.IsVisible);
    }

    /// <summary>
    /// The dropdown offers every renderer/layout pairing the pane can render.
    /// </summary>
    /// <remarks>
    /// It once offered ELK's algorithms too, so their arrangements could be
    /// harvested into the file. The plugin is no longer embedded, so listing them
    /// would be an offer the pane cannot honour — hence the explicit check that
    /// they are gone rather than merely the count matching.
    /// </remarks>
    [AvaloniaFact]
    public void TheModeDropdownOffersEveryChoice()
    {
        var window = new MainWindow();
        window.Show();
        window.ApplyTemplate();
        Dispatcher.UIThread.RunJobs();

        var box = window.FindControl<ComboBox>("ReferenceModeBox");

        Assert.NotNull(box);
        Assert.Equal(ReferenceRenderPane.Choices.Length, box!.ItemCount);
        Assert.Equal(0, box.SelectedIndex);

        // Merpeeps first: replaying stored coordinates is what the editor is for,
        // and stock mermaid is the comparison.
        Assert.Equal(RenderAssets.Mode.WithMerpeeps, ReferenceRenderPane.Choices[0].Mode);

        Assert.DoesNotContain(ReferenceRenderPane.Choices,
            c => c.Layout?.StartsWith("elk", StringComparison.Ordinal) == true);

        // Every layout the pane offers must be one the shell can actually build.
        foreach (var choice in ReferenceRenderPane.Choices)
            Assert.True(choice.Layout is null ||
                        RenderAssets.Layouts.Contains(choice.Layout),
                $"the pane offers '{choice.Layout}', which the renderer does not support");
    }
}
