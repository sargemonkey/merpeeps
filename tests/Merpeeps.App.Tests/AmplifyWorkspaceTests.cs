using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Merpeeps.Core;
using Merpeeps.Core.Model;
using Xunit;

namespace Merpeeps.App.Tests;

public class AmplifyWorkspaceTests
{
    [AvaloniaTheory]
    [InlineData(false, 900)]
    [InlineData(true, 900)]
    [InlineData(false, 1560)]
    [InlineData(true, 1560)]
    public void NativeThemeAndCompactControlsKeepTheCanvasPrimary(bool dark, int width)
    {
        using var f = new Fixture("erDiagram\nCUSTOMER {\n int id PK\n string email UK\n}\nCUSTOMER ||--o{ ORDER : places\n", dark, width);
        f.Canvas.SelectByIds(["CUSTOMER"]); Settle();
        var name = f.Field<TextBox>("Entity name");
        Assert.Contains("selection-name", name.Classes);
        Assert.Equal("CUSTOMER", name.Text);
        var rail = f.Window.FindControl<DockPanel>("NodePane")!;
        Assert.Equal(80, rail.Bounds.Width);
        Assert.True(f.Canvas.Bounds.Width >= width * .5);
        Assert.DoesNotContain(f.Window.FindControl<Border>("FamilyPaletteHost")!.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible);
        Assert.Equal("Instrument Sans", f.Window.FontFamily.Name);
        Assert.Equal(dark ? Color.Parse("#191527") : Colors.White,
            ((ISolidColorBrush)f.Window.Background!).Color);
        foreach (var button in f.Window.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible && b.Content is UiIcon))
            Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(button)));
        Assert.NotNull(ToolTip.GetTip(f.Field<Button>("Add relationship")));
        Assert.IsType<UiIcon>(f.Field<Button>("Add relationship").Content);
        f.Canvas.FitToView(); Settle();
        f.Capture($"er-{width}-{(dark ? "dark" : "light")}");
    }

    [AvaloniaFact]
    public void OutlineIsAViewOnlyChoiceAndSelectsTheRealObject()
    {
        using var f = new Fixture("pie\n\"Alpha\":30\n\"Beta\":70\n");
        var before = f.Window.CurrentSource;
        f.Window.FindControl<ToggleButton>("OutlineToggle")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle();
        Assert.True(f.Window.FindControl<ScrollViewer>("OutlineScroller")!.IsVisible);
        Assert.False(f.Window.FindControl<ScrollViewer>("FamilyPaletteScroller")!.IsVisible);
        Assert.Equal(216, f.Window.FindControl<DockPanel>("NodePane")!.Bounds.Width);
        var choice = f.Window.FindControl<StackPanel>("OutlineItems")!.GetVisualDescendants().OfType<Button>()
            .Single(b => AutomationProperties.GetName(b) == "Select Beta");
        choice.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle();
        Assert.Equal("Beta", f.Field<TextBox>("Slice label").Text);
        f.Window.FindControl<ToggleButton>("ItemsToggle")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle();
        Assert.True(f.Window.FindControl<ScrollViewer>("FamilyPaletteScroller")!.IsVisible);
        Assert.Equal(before, f.Window.CurrentSource);
        Assert.False(f.Canvas.History!.CanUndo);
    }

    [AvaloniaTheory]
    [MemberData(nameof(WorkspacePanelsTests.Dialects), MemberType = typeof(WorkspacePanelsTests))]
    public void EveryFamilyHasAnAccessiblePictorialRailAndOutline(string family, string dialect)
    {
        using var f = new Fixture(MermaidDiagramCatalog.CreateEmpty(family, dialect).Source);
        if (family is "flowchart" or "state")
        {
            var item = f.Window.FindControl<ListBox>("PaletteList")!.Items.Cast<PaletteItem>()
                .First(p => family == "flowchart" || p.Name == "State");
            f.Canvas.AddFromPalette(item);
        }
        else
        {
            var button = f.Window.FindControl<Border>("FamilyPaletteHost")!.GetVisualDescendants().OfType<Button>()
                .First(b => b.IsEnabled);
            Assert.NotNull(ToolTip.GetTip(button));
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        Settle();
        f.Window.FindControl<ToggleButton>("OutlineToggle")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle();
        Assert.NotEmpty(f.Window.FindControl<StackPanel>("OutlineItems")!.Children.OfType<Button>());
        var source = f.Window.CurrentSource;
        f.Window.FindControl<ToggleButton>("ItemsToggle")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle();
        Assert.Equal(source, f.Window.CurrentSource);
    }

    [AvaloniaFact]
    public void AppThemeDoesNotMutateDiagramColorsOrHistory()
    {
        using var f = new Fixture("pie\n\"Alpha\":30\n\"Beta\":70\n");
        var source = f.Window.CurrentSource;
        var scene = f.Canvas.ChartScene.Scene;
        var colors = scene.Figures.Select(x => (x.Id, x.Fill, x.Stroke)).ToArray();
        f.Window.FindControl<Button>("ThemeToggle")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle();
        Assert.Equal(ThemeVariant.Dark, f.Window.ActualThemeVariant);
        Assert.Equal(colors, f.Canvas.ChartScene.Scene.Figures.Select(x => (x.Id, x.Fill, x.Stroke)));
        Assert.Equal(source, f.Window.CurrentSource);
        Assert.False(f.Canvas.History!.CanUndo);
        f.Capture("pie-dark");
    }

    [AvaloniaFact]
    public void TimelineMatchesThePreviewWithAContinuousAxisAndGapOnlyConnectors()
    {
        using var f = new Fixture("timeline\nPast : One : Two\nNow : Three\nNext : Four\n");
        var scene = f.Canvas.DataScene.Scene;
        var axis = Assert.Single(scene.Figures, x => x.Id == "timeline-axis");
        var periods = scene.Figures.Where(x => x.Id.StartsWith("period-") && x.Kind == Merpeeps.Core.Layout.DrawingFigureKind.Rectangle).ToArray();
        Assert.Equal(3, periods.Length);
        Assert.All(periods, p => Assert.True(p.Bounds.Y + p.Bounds.Height < axis.Bounds.Y));
        var events = scene.Figures.Where(x => x.Id.StartsWith("event-")).ToArray();
        Assert.All(events, e => Assert.True(e.Bounds.Y > axis.Bounds.Y));
        foreach (var connector in scene.Figures.Where(x => x.Id.StartsWith("period-event-")))
            Assert.DoesNotContain(events, e => connector.Bounds.Y < e.Bounds.Y + e.Bounds.Height - .1 &&
                connector.Bounds.Y + connector.Bounds.Height > e.Bounds.Y + .1 &&
                connector.Bounds.X > e.Bounds.X && connector.Bounds.X < e.Bounds.X + e.Bounds.Width);
        f.Canvas.FitToView(); Settle();
        f.Capture("timeline-light");
    }

    private static void Settle() => Dispatcher.UIThread.RunJobs();
    private sealed class Fixture : IDisposable
    {
        public MainWindow Window { get; }
        public EditorCanvas Canvas => Window.CanvasForTest;
        public Fixture(string source, bool dark = false, int width = 1560)
        {
            Window = new MainWindow(new StartupOptions(null, dark ? ThemeVariant.Dark : ThemeVariant.Light)) { Width = width, Height = 900 };
            Window.Show(); Window.LoadSourceForTest(source); Settle();
        }
        public T Field<T>(string name) where T : Control => Window.FindControl<Border>("ClassInspectorHost")!.GetVisualDescendants().OfType<T>()
            .Single(c => c.IsEffectivelyVisible && AutomationProperties.GetName(c) == name);
        public void Capture(string name)
        {
            if (Environment.GetEnvironmentVariable("MERPEEPS_AMPLIFY_CAPTURES") is not { Length: > 0 } path) return;
            Directory.CreateDirectory(path);
            using var frame = Window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No native workspace rendering.");
            frame.Save(Path.Combine(path, name + ".png"));
        }
        public void Dispose() { foreach (var child in Window.OwnedWindows.ToArray()) child.Close(); Window.Close(); }
    }
}
