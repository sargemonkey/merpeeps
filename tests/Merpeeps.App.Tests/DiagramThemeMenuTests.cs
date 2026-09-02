using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Merpeeps.Core.Model;
using Xunit;

namespace Merpeeps.App.Tests;

/// <summary>
/// The Diagram theme menu offers mermaid's themes and records the choice in the
/// file, where every other renderer will honour it.
/// </summary>
public class DiagramThemeMenuTests
{
    /// <summary>
    /// The two theme menus are separate and stay separate.
    /// </summary>
    /// <remarks>
    /// They were conflated once already, in a user's head rather than in the
    /// code: asked for "an option to change themes", the app theme menu was
    /// already there and the diagram themes were not. The menu now names both
    /// so the distinction is visible before anyone has to ask.
    /// </remarks>
    [AvaloniaFact]
    public void BothThemeMenusExistAndAreDistinct()
    {
        var window = new MainWindow();
        window.Show();

        Assert.NotNull(window.FindControl<MenuItem>("MenuThemeLight"));
        Assert.NotNull(window.FindControl<MenuItem>("MenuDiagramTheme"));

        var diagram = window.FindControl<MenuItem>("MenuDiagramTheme")!;
        var headers = diagram.ItemsSource!.Cast<MenuItem>()
            .Select(m => (string)m.Header!).ToList();

        // Every mermaid theme, plus an explicit way back to no theme at all.
        Assert.Equal(DiagramPalette.All.Length + 1, headers.Count);
        Assert.Contains(headers, h => h.Contains("None"));
        Assert.Contains(headers, h => h.Contains("Forest"));
    }

    /// <summary>Every offered theme is one mermaid actually ships.</summary>
    [Theory]
    [InlineData("default")] [InlineData("dark")] [InlineData("forest")]
    [InlineData("neutral")] [InlineData("base")]
    [InlineData("neo")] [InlineData("neo-dark")]
    [InlineData("redux")] [InlineData("redux-dark")]
    [InlineData("redux-color")] [InlineData("redux-dark-color")]
    public void TheMermaidThemesAreOffered(string name)
    {
        Assert.NotNull(DiagramPalette.Find(name));
    }

    /// <summary>
    /// A theme that only exists in Mermaid Chart's own editor is not offered.
    /// </summary>
    /// <remarks>
    /// It appears in some theme menus alongside the real ones, but the
    /// open-source build falls back to <c>default</c> when asked for it — so
    /// offering it would write a key into the file that changes nothing and
    /// leaves the canvas disagreeing with every renderer.
    /// </remarks>
    [Fact]
    public void MermaidChartsOwnThemeIsNotOffered()
    {
        Assert.Null(DiagramPalette.Find("mermaid-chart"));
        Assert.Null(DiagramPalette.Find("mc"));
    }

    /// <summary>
    /// An unrecognised theme leaves the canvas on its own colours rather than
    /// asserting a palette that is probably wrong.
    /// </summary>
    [Fact]
    public void AnUnknownThemeHasNoPalette()
    {
        Assert.Null(DiagramPalette.Find("someFutureTheme"));
        Assert.Null(DiagramPalette.Find(null));
        Assert.Null(DiagramPalette.Find(""));
    }

    /// <summary>
    /// Opening a themed document ticks the matching menu item and colours the
    /// canvas, with no click required.
    /// </summary>
    [AvaloniaFact]
    public void OpeningAThemedDocumentAdoptsItsTheme()
    {
        var window = new MainWindow();
        window.Show();
        window.LoadSourceForTest(
            "---\nconfig:\n  theme: forest\n---\nflowchart TD\n    a[One] --> b[Two]\n");

        var canvas = window.CanvasForTest;
        Assert.Equal("forest", canvas.Model!.Theme);
        Assert.Equal(DiagramPalette.Find("forest"), canvas.DiagramTheme);

        var ticked = window.FindControl<MenuItem>("MenuDiagramTheme")!
            .ItemsSource!.Cast<MenuItem>().Where(m => m.IsChecked).ToList();

        Assert.Single(ticked);
        Assert.Equal("Forest", (string)ticked[0].Header!);
    }

    /// <summary>
    /// The palette values are the ones mermaid actually renders.
    /// </summary>
    /// <remarks>
    /// Measured from the shipped mermaid build by
    /// <c>oracle/theme-colour-probe.mjs</c>. Pinned here so an upgrade that
    /// changes them shows up as a failing test rather than as a canvas that
    /// quietly disagrees with the render.
    /// </remarks>
    [Theory]
    [InlineData("default", 0xEC, 0xEC, 0xFF, 0x93, 0x70, 0xDB)]
    [InlineData("dark", 0x1F, 0x20, 0x20, 0xCC, 0xCC, 0xCC)]
    [InlineData("forest", 0xCD, 0xE4, 0x98, 0x13, 0x54, 0x0C)]
    [InlineData("neutral", 0xEE, 0xEE, 0xEE, 0x99, 0x99, 0x99)]
    [InlineData("base", 0xFF, 0xF4, 0xDD, 0xEE, 0xDE, 0xBB)]
    [InlineData("neo", 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x00)]
    [InlineData("neo-dark", 0x2A, 0x20, 0x20, 0xCC, 0xCC, 0xCC)]
    [InlineData("redux", 0xFF, 0xFF, 0xFF, 0x28, 0x25, 0x3D)]
    [InlineData("redux-dark", 0x11, 0x11, 0x13, 0xFF, 0xFF, 0xFF)]
    public void ThePaletteMatchesWhatMermaidRenders(
        string name, byte fr, byte fg, byte fb, byte sr, byte sg, byte sb)
    {
        // Compared by component, not by ToString(): Avalonia renders a colour
        // that happens to match a CSS keyword as its name, so #9370DB came back
        // as "MEDIUMPURPLE" and the assertion failed on correct data.
        var e = DiagramPalette.Find(name)!.Value;

        Assert.Equal((fr, fg, fb), (e.Fill.R, e.Fill.G, e.Fill.B));
        Assert.Equal((sr, sg, sb), (e.Stroke.R, e.Stroke.G, e.Stroke.B));
    }

    /// <summary>
    /// The theme is reachable from the toolbar, not just the menu, and the two
    /// always agree.
    /// </summary>
    [AvaloniaFact]
    public void TheToolbarDropdownMirrorsTheMenu()
    {
        var window = new MainWindow();
        window.Show();

        var box = window.FindControl<ComboBox>("DiagramThemeBox")!;
        var menu = window.FindControl<MenuItem>("MenuDiagramTheme")!;

        var fromBox = box.ItemsSource!.Cast<string>().ToList();
        var fromMenu = menu.ItemsSource!.Cast<MenuItem>()
            .Select(m => ((string)m.Header!).Replace("_", "")).ToList();

        Assert.Equal(fromMenu, fromBox);
        Assert.Equal(DiagramPalette.All.Length + 1, fromBox.Count);
    }

    /// <summary>
    /// Choosing from the toolbar records the theme and ticks the menu.
    /// </summary>
    [AvaloniaFact]
    public void ChoosingFromTheToolbarSetsTheTheme()
    {
        var window = new MainWindow();
        window.Show();
        window.LoadSourceForTest("flowchart TD\n    a[One] --> b[Two]\n");

        var box = window.FindControl<ComboBox>("DiagramThemeBox")!;
        var forest = box.ItemsSource!.Cast<string>().ToList().IndexOf("Forest");
        box.SelectedIndex = forest;

        Assert.Equal("forest", window.CanvasForTest.Model!.Theme);
        Assert.Equal(DiagramPalette.Find("forest"), window.CanvasForTest.DiagramTheme);

        var ticked = window.FindControl<MenuItem>("MenuDiagramTheme")!
            .ItemsSource!.Cast<MenuItem>().Single(m => m.IsChecked);
        Assert.Equal("Forest", (string)ticked.Header!);
    }

    /// <summary>
    /// A theme the file names but this build does not know leaves the dropdown
    /// blank rather than claiming a theme that is not in the file.
    /// </summary>
    [AvaloniaFact]
    public void AnUnknownThemeLeavesTheDropdownBlank()
    {
        var window = new MainWindow();
        window.Show();
        window.LoadSourceForTest(
            "---\nconfig:\n  theme: someFutureTheme\n---\nflowchart TD\n    a[One] --> b[Two]\n");

        Assert.Equal("someFutureTheme", window.CanvasForTest.Model!.Theme);
        Assert.Equal(-1, window.FindControl<ComboBox>("DiagramThemeBox")!.SelectedIndex);
        Assert.Null(window.CanvasForTest.DiagramTheme);
    }
}
