using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Merpeeps.Core.Model;
using Xunit;

namespace Merpeeps.App.Tests;

/// <summary>
/// Drives the real window headlessly: real XAML, real handlers, no display.
///
/// <para>These exist because the UI bugs that actually shipped — icons still
/// drawn with styling off, unreadable labels in dark mode — were all invisible
/// to the rest of the suite and only caught by a person looking at a
/// screenshot. Looking is not a test.</para>
/// </summary>
public class MainWindowTests
{
    private const string Styled = """
        ---
        x-merpeeps:
          spec: 1
          layout:
            nodes:
              a: {x: 0, y: 0}
              b: {x: 240, y: 0}
          style:
            nodes:
              a: {fill: '#DAE8FC', stroke: '#6C8EBF', icon: database}
              b: {fill: '#D5E8D4', stroke: '#82B366', icon: server, dashed: true}
        ---
        flowchart LR
            a[Alpha] --> b[Beta]
        """;

    /// <summary>Open a window on <paramref name="source"/> and settle layout.</summary>
    private static (MainWindow Window, EditorCanvas Canvas) Open(string source)
    {
        var window = new MainWindow();
        window.Show();

        window.LoadSourceForTest(source);
        window.ApplyTemplate();
        Settle();

        return (window, window.CanvasForTest);
    }

    /// <summary>
    /// Let queued UI work run. Handlers post to the dispatcher, so without
    /// this an assertion can read state from before the click was processed.
    /// </summary>
    private static void Settle() => Dispatcher.UIThread.RunJobs();

    private static T Find<T>(MainWindow w, string name) where T : Control =>
        w.FindControl<T>(name)
        ?? throw new InvalidOperationException($"no {typeof(T).Name} named '{name}'");

    // ------------------------------------------------------------- the shell

    [AvaloniaFact]
    public void TheWindowBuildsWithEveryPaneAndMenu()
    {
        var (window, _) = Open(Styled);

        Assert.NotNull(Find<Menu>(window, "MainMenu"));
        Assert.NotNull(Find<DockPanel>(window, "NodePane"));
        Assert.NotNull(Find<ScrollViewer>(window, "PropertyPane"));
        Assert.NotNull(Find<DockPanel>(window, "SourcePane"));
        Assert.NotNull(Find<ComboBox>(window, "EngineBox"));
        Assert.NotNull(Find<TextBlock>(window, "PaletteTitle"));
    }

    /// <summary>
    /// Every menu item the code wires up must exist in the XAML. A rename in
    /// one and not the other used to crash on startup with a bare
    /// NullReferenceException.
    /// </summary>
    [AvaloniaTheory]
    [InlineData("MenuNew")] [InlineData("MenuOpen")] [InlineData("MenuSave")]
    [InlineData("MenuSaveAs")] [InlineData("MenuExport")] [InlineData("MenuSample")]
    [InlineData("MenuExit")] [InlineData("MenuUndo")] [InlineData("MenuRedo")]
    [InlineData("MenuSelectAll")] [InlineData("MenuDelete")] [InlineData("MenuRename")]
    [InlineData("MenuZoomIn")] [InlineData("MenuZoomOut")] [InlineData("MenuZoom100")]
    [InlineData("MenuFit")] [InlineData("MenuShowStyling")] [InlineData("MenuShowNodes")]
    [InlineData("MenuShowSource")] [InlineData("MenuThemeLight")] [InlineData("MenuThemeDark")]
    [InlineData("MenuThemeSystem")] [InlineData("MenuRelayout")] [InlineData("MenuEngines")]
    [InlineData("MenuExportSvg")] [InlineData("MenuExportBaked")]
    [InlineData("MenuDiagramTheme")]
    [InlineData("MenuShortcuts")] [InlineData("MenuAbout")]
    public void EveryWiredMenuItemExists(string name)
    {
        var (window, _) = Open(Styled);
        Assert.NotNull(Find<MenuItem>(window, name));
    }

    // -------------------------------------------------- the styling preview

    /// <summary>
    /// The reported bug. Asserted through the real toggle rather than by
    /// calling the canvas directly, so a broken wiring fails too.
    /// </summary>
    [AvaloniaFact]
    public void TurningStylingOffHidesIconsAndColours()
    {
        var (window, canvas) = Open(Styled);
        var model = canvas.Model!;

        Assert.True(canvas.ShowMerpeepsStyling, "styling should start on");
        Assert.All(model.Nodes, n =>
            Assert.True(NodeAppearance.For(n, canvas.ShowMerpeepsStyling).HasIcon));

        Find<ToggleButton>(window, "StylingToggle").IsChecked = false;
        Settle();

        Assert.False(canvas.ShowMerpeepsStyling);
        Assert.All(model.Nodes, n =>
        {
            var look = NodeAppearance.For(n, canvas.ShowMerpeepsStyling);
            Assert.False(look.HasIcon, $"{n.Id} still shows an icon");
            Assert.Null(look.Fill);
            Assert.Null(look.Stroke);
            Assert.False(look.Dashed);
            Assert.True(look.IsPlain);
        });
    }

    [AvaloniaFact]
    public void TheToolbarToggleAndMenuItemStayInStep()
    {
        var (window, canvas) = Open(Styled);
        var toggle = Find<ToggleButton>(window, "StylingToggle");
        var menu = Find<MenuItem>(window, "MenuShowStyling");

        toggle.IsChecked = false;
        Settle();

        Assert.False(canvas.ShowMerpeepsStyling);
        Assert.False(menu.IsChecked);

        toggle.IsChecked = true;
        Settle();

        Assert.True(canvas.ShowMerpeepsStyling);
        Assert.True(menu.IsChecked);
    }

    /// <summary>Looking at the plain view must not edit the document.</summary>
    [AvaloniaFact]
    public void TheStylingToggleNeverChangesTheDocument()
    {
        var (window, canvas) = Open(Styled);
        var model = canvas.Model!;

        var before = model.Nodes
            .Select(n => (n.Id, n.Fill, n.Stroke, n.Icon, n.Dashed, n.Width, n.Height))
            .ToList();

        var toggle = Find<ToggleButton>(window, "StylingToggle");
        toggle.IsChecked = false;
        Settle();
        toggle.IsChecked = true;
        Settle();

        Assert.Equal(before, model.Nodes
            .Select(n => (n.Id, n.Fill, n.Stroke, n.Icon, n.Dashed, n.Width, n.Height))
            .ToList());
    }

    // -------------------------------------------------------- shape palette

    [AvaloniaFact]
    public void ThePaletteDoesNotOfferStencilCategoriesAsDiagramTypes()
    {
        var (window, _) = Open(Styled);
        Assert.Null(window.FindControl<ComboBox>("CollectionBox"));
        Assert.Equal("Flowchart shapes", Find<TextBlock>(window, "PaletteTitle").Text);
        Assert.Equal(NodePalette.Flowchart.Items, Find<ListBox>(window, "PaletteList").Items.Cast<PaletteItem>());
    }

    [AvaloniaFact]
    public void LoadingADifferentFamilyChangesTheAvailableTools()
    {
        var (window, _) = Open(Styled);
        var list = Find<ListBox>(window, "PaletteList");
        window.LoadSourceForTest("stateDiagram-v2\nIdle --> Running\n");
        Settle();
        Assert.Equal("State diagram tools", Find<TextBlock>(window, "PaletteTitle").Text);
        Assert.Equal(new[] { "Start", "State", "End" }, list.Items.Cast<PaletteItem>().Select(i => i.Name));
        window.LoadSourceForTest("classDiagram\nclass Account\n");
        Settle();
        Assert.True(Find<DockPanel>(window, "NodePane").IsVisible);
        Assert.True(Find<ScrollViewer>(window, "FamilyPaletteScroller").IsVisible);
        Assert.Empty(list.Items);
        window.LoadSourceForTest(Styled);
        Assert.Equal(NodePalette.Flowchart.Items, list.Items.Cast<PaletteItem>());
    }

    /// <summary>
    /// A palette entry must arrive fully configured — that is the difference
    /// between a stencil and a shape menu.
    /// </summary>
    [AvaloniaFact]
    public void AddingFromThePaletteCreatesAConfiguredNode()
    {
        var (window, canvas) = Open(Styled);
        var before = canvas.Model!.Nodes.Count;

        var item = NodePalette.Architecture.Items.First(i => i.Name == "Database");
        canvas.AddFromPalette(item);
        Settle();

        Assert.Equal(before + 1, canvas.Model.Nodes.Count);

        var added = canvas.Model.Nodes[^1];
        Assert.Equal(item.Shape, added.Shape);
        Assert.Equal(item.Icon, added.Icon);
        Assert.Equal(item.Fill, added.Fill);
        Assert.Equal(item.Stroke, added.Stroke);
        Assert.True(added.HasExplicitPosition, "a placed node should keep its spot");
    }

    // --------------------------------------------------------------- themes

    [AvaloniaFact]
    public void SwitchingToDarkRepaintsTheCanvas()
    {
        var (window, canvas) = Open(Styled);
        Assert.Same(CanvasTheme.Light, canvas.CanvasColours);

        Find<MenuItem>(window, "MenuThemeDark").RaiseClick();
        Settle();

        Assert.Same(CanvasTheme.Dark, canvas.CanvasColours);

        Find<MenuItem>(window, "MenuThemeLight").RaiseClick();
        Settle();

        Assert.Same(CanvasTheme.Light, canvas.CanvasColours);
    }

    /// <summary>
    /// Every default in a theme must be legible on that theme's own canvas.
    /// The dark palette shipped with near-white labels drawn on the user's
    /// light pastel fills, which nothing but a screenshot would have caught.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThemeDefaultsAreLegible(bool dark)
    {
        var theme = dark ? CanvasTheme.Dark : CanvasTheme.Light;

        var background = ((ISolidColorBrush)theme.Background).Color;
        var label = ((ISolidColorBrush)theme.Label).Color;
        var fill = ((ISolidColorBrush)theme.NodeFill).Color;

        Assert.True(Contrast(label, fill) >= 4.5,
            $"{(dark ? "dark" : "light")}: label on node fill is {Contrast(label, fill):F1}:1");
        Assert.True(Contrast(theme.DefaultNodeText.ToColor(), fill) >= 4.5,
            "default node text is not legible on the default fill");
        Assert.True(Contrast(label, background) >= 4.5,
            "label is not legible on the canvas background");
    }

    private static double Contrast(Avalonia.Media.Color a, Avalonia.Media.Color b)
    {
        static double L(Avalonia.Media.Color c)
        {
            static double Ch(byte v)
            {
                var s = v / 255.0;
                return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Ch(c.R) + 0.7152 * Ch(c.G) + 0.0722 * Ch(c.B);
        }

        var (x, y) = (L(a), L(b));
        var (hi, lo) = x > y ? (x, y) : (y, x);
        return (hi + 0.05) / (lo + 0.05);
    }

    // ----------------------------------------------------------- pane state

    [AvaloniaFact]
    public void HidingAPaneCollapsesItsColumn()
    {
        var (window, _) = Open(Styled);
        var pane = Find<DockPanel>(window, "SourcePane");
        var menu = Find<MenuItem>(window, "MenuShowSource");

        Assert.False(pane.IsVisible);
        menu.IsChecked = true;
        menu.RaiseClick();
        Settle();
        Assert.True(pane.IsVisible);

        menu.IsChecked = false;
        menu.RaiseClick();
        Settle();

        Assert.False(pane.IsVisible);
        Assert.Equal(0, ((Grid)pane.Parent!).ColumnDefinitions[6].Width.Value);
    }

    // ------------------------------------------------------- layout engines

    [AvaloniaFact]
    public void ChoosingAnEngineRelaysOutAndIsRecorded()
    {
        var (window, canvas) = Open(Styled);
        var box = Find<ComboBox>(window, "EngineBox");

        var index = Merpeeps.Core.Layout.LayoutEngines.Available
            .Select((e, i) => (e, i)).First(t => t.e.Id == "grid").i;

        box.SelectedIndex = index;
        Settle();

        Assert.Equal("grid", canvas.Model!.LayoutEngine);
    }

    /// <summary>
    /// The dropdown offers exactly the engines the app can run.
    /// </summary>
    /// <remarks>
    /// The assertion has been inverted twice, which is worth recording. It first
    /// counted native engines; it was changed to demand *more* than that after a
    /// user asked where ELK was; and it is now back to an exact match, because the
    /// borrowed layouts were removed for opening a browser pane mid-edit. Each
    /// version was right about the build it shipped with.
    /// </remarks>
    [AvaloniaFact]
    public void TheEngineDropdownOffersEveryLayout()
    {
        var (window, _) = Open(Styled);
        var box = Find<ComboBox>(window, "EngineBox");

        Assert.Equal(EditorLayouts.All.Count, box.ItemCount);
        Assert.Equal(Merpeeps.Core.Layout.LayoutEngines.Available.Count, box.ItemCount);
    }
}

internal static class TestExtensions
{
    /// <summary>Fire a menu item's Click the way the framework would.</summary>
    internal static void RaiseClick(this MenuItem item) =>
        item.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));

    internal static Avalonia.Media.Color ToColor(this Avalonia.Media.Color c) => c;
}
