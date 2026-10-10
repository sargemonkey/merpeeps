using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Merpeeps.Core;
using Merpeeps.Core.Model;
using Xunit;

namespace Merpeeps.App.Tests;

public class WorkspacePanelsTests
{
    public static IEnumerable<object[]> Dialects()
    {
        Assert.Equal(33, MermaidDiagramCatalog.Families.Count);
        return MermaidDiagramCatalog.Families.SelectMany(f => f.Dialects.Select(d => new object[] { f.Id, d.Id }));
    }

    [AvaloniaTheory]
    [MemberData(nameof(Dialects))]
    public void EveryDialectHasCreationOnTheLeftAndEmptyPropertiesOnTheRight(string family, string dialect)
    {
        using var fixture = new Fixture(MermaidDiagramCatalog.CreateEmpty(family, dialect).Source);
        var window = fixture.Window;
        var left = window.FindControl<DockPanel>("NodePane")!;
        var right = window.FindControl<ScrollViewer>("PropertyPane")!;
        var canvas = window.CanvasForTest;
        Assert.True(left.IsEffectivelyVisible);
        Assert.InRange(left.Bounds.Width, 72, 88);
        Assert.True(left.TranslatePoint(new(left.Bounds.Width, 0), window)!.Value.X <= canvas.TranslatePoint(default, window)!.Value.X);
        Assert.True(canvas.TranslatePoint(new(canvas.Bounds.Width, 0), window)!.Value.X <= right.TranslatePoint(default, window)!.Value.X);
        Assert.False(window.FindControl<DockPanel>("SourcePane")!.IsVisible);
        Assert.False(window.FindControl<MenuItem>("MenuShowSource")!.IsChecked);
        Assert.Empty(Visible<TextBox>(right));
        Assert.Empty(Visible<ComboBox>(right));
        Assert.DoesNotContain(Visible<Button>(right), b => (AutomationProperties.GetName(b) ?? "").StartsWith("Add ", StringComparison.Ordinal));
        if (family is "flowchart" or "state")
        {
            Assert.NotEmpty(window.FindControl<ListBox>("PaletteList")!.Items);
            Assert.False(window.FindControl<ScrollViewer>("FamilyPaletteScroller")!.IsVisible);
        }
        else
        {
            Assert.True(window.FindControl<ScrollViewer>("FamilyPaletteScroller")!.IsVisible);
            var items = Visible<Button>(fixture.Palette).Where(b => b.IsEnabled).ToArray();
            Assert.NotEmpty(items);
            foreach (var item in items)
            {
                var name = AutomationProperties.GetName(item);
                Assert.Single(item.GetVisualDescendants().OfType<DiagramStencilPreview>());
                var before = window.CurrentSource;
                Click(item);
                Assert.True(canvas.History!.CanUndo, name);
                Assert.True(window.FindControl<Button>("SaveBtn")!.IsEnabled, name);
                Assert.NotEqual(before, window.CurrentSource);
                Assert.True(MerpeepsDocument.GetCapabilities(window.CurrentSource).CanEdit, name + ": " + window.CurrentSource);
                Assert.Equal(canvas.Model!.Kind, MerpeepsDocument.Load(window.CurrentSource).Kind);
                Assert.True(fixture.Palette.IsEffectivelyVisible, name);
                canvas.Undo(); Settle();
                Assert.False(canvas.History.CanUndo, name);
            }
        }
        Assert.False(canvas.History!.CanUndo);
    }

    [AvaloniaTheory]
    [InlineData("architecture-beta\n", "Add junction", "Node kind", ArchitectureNodeKind.Junction)]
    [InlineData("C4Context\n", "Add C4 person", "Element level", C4ElementLevel.Person)]
    [InlineData("usecase-beta\n", "Add actor", "Element kind", UseCaseNodeKind.Actor)]
    [InlineData("agentflow-beta\n", "Add flow container", "Workflow element kind", AgentFlowElementKind.Flow)]
    [InlineData("zenuml\n", "Add database", "Participant symbol", ZenParticipantKind.Database)]
    [InlineData("sequenceDiagram\n", "Add actor", "Declaration", SequenceParticipantKind.Actor)]
    [InlineData("block\n", "Add spacer", "Block kind", BlockItemKind.Space)]
    [InlineData("xychart\n", "Add bar series", "Series kind", ChartSeriesKind.Bar)]
    public void PaletteItemsCreateTheirTypedPresetImmediately(string source, string item, string field, object value)
    {
        using var fixture = new Fixture(source);
        Click(fixture.Item(item));
        var model = fixture.Window.CanvasForTest.Model!;
        object actual = model.Architecture is { } architecture ? architecture.Nodes.Single().Kind
            : model.C4 is { } c4 ? c4.Elements.Single().Level
            : model.UseCase is { } cases ? cases.Nodes.Single().Kind
            : model.AgentFlow is { } agent ? agent.Elements.Single().Kind
            : model.Zen is { } zen ? zen.Participants.Single().Kind
            : model.Sequence is { } sequence ? sequence.Participants.Single().Kind
            : model.Blocks is { } blocks ? blocks.Items.Single().Kind
            : ((XyChart)model.Chart!).Series.Single().Kind;
        Assert.Equal(value, actual);
        Assert.True(fixture.Window.CanvasForTest.History!.CanUndo, field);
        Assert.True(fixture.Window.FindControl<Button>("SaveBtn")!.IsEnabled);
    }

    [AvaloniaFact]
    public void ClickingAStencilCommitsValidPropertiesBeforeCreating()
    {
        using var fixture = new Fixture("architecture-beta\nservice existing(server)[Existing]\n");
        var canvas = fixture.Window.CanvasForTest;
        canvas.SelectStructural(new("ArchitectureNode", canvas.Model!.Architecture!.Nodes[0].Id));
        Settle();
        var before = fixture.Window.CurrentSource;
        fixture.Property<TextBox>("Service label").Text = "Pending service";
        Assert.False(fixture.Window.FindControl<Button>("SaveBtn")!.IsEnabled);
        Assert.True(fixture.Item("Add junction").IsEnabled);
        Click(fixture.Item("Add junction"));
        Assert.Equal(ArchitectureNodeKind.Junction, fixture.Property<ComboBox>("Node kind").SelectedItem);
        Assert.NotEqual(before, fixture.Window.CurrentSource);
        Assert.Equal(2, canvas.Model.Architecture.Nodes.Count);
        Assert.Equal("Pending service", canvas.Model.Architecture.Nodes[0].Label);
        Assert.True(fixture.Window.FindControl<Button>("SaveBtn")!.IsEnabled);
        Assert.True(fixture.Item("Add junction").IsEffectivelyVisible);
        canvas.Undo();
        Assert.Single(canvas.Model.Architecture.Nodes);
        canvas.Undo();
        Assert.Equal("Existing", canvas.Model.Architecture.Nodes[0].Label);
    }

    [AvaloniaFact]
    public void PalettePrerequisitesRefreshAfterApplyAndUndoWithoutReplacingTheButtons()
    {
        using var fixture = new Fixture("radar-beta\n");
        var curve = fixture.Item("Add curve");
        Assert.False(curve.IsEnabled);
        Click(fixture.Item("Add axis"));
        Assert.Same(curve, fixture.Item("Add curve"));
        Assert.True(curve.IsEnabled);
        fixture.Window.CanvasForTest.Undo();
        Settle();
        Assert.False(curve.IsEnabled);
    }

    [AvaloniaFact]
    public void SourceAndPaletteChoicesSurviveResizingAndFamilyChangesWithoutHistory()
    {
        using var fixture = new Fixture("classDiagram\n");
        var window = fixture.Window;
        var paletteMenu = window.FindControl<MenuItem>("MenuShowNodes")!;
        paletteMenu.IsChecked = false; paletteMenu.RaiseClick(); Settle();
        var sourceToggle = window.FindControl<ToggleButton>("SourceToggle")!;
        sourceToggle.IsChecked = true; Settle();
        Assert.True(window.FindControl<MenuItem>("MenuShowSource")!.IsChecked);
        var before = window.CurrentSource;
        window.Width = 900; Settle();
        window.Width = 1600; Settle();
        Assert.False(window.FindControl<DockPanel>("NodePane")!.IsVisible);
        Assert.True(window.FindControl<DockPanel>("SourcePane")!.IsVisible);
        Assert.Equal(before, window.CurrentSource);
        Assert.False(window.CanvasForTest.History!.CanUndo);
        window.LoadSourceForTest("architecture-beta\n");
        Assert.False(window.FindControl<DockPanel>("NodePane")!.IsVisible);
        Assert.True(window.FindControl<DockPanel>("SourcePane")!.IsVisible);
        paletteMenu.IsChecked = true; paletteMenu.RaiseClick();
        var sourceMenu = window.FindControl<MenuItem>("MenuShowSource")!;
        sourceMenu.IsChecked = false; sourceMenu.RaiseClick(); Settle();
        Assert.False(sourceToggle.IsChecked);
        Assert.Equal(0, window.FindControl<Grid>("WorkspaceGrid")!.ColumnDefinitions[6].Width.Value);
        Assert.True(fixture.Item("Add service").IsEffectivelyVisible);
        Assert.False(window.CanvasForTest.History!.CanUndo);
    }

    [AvaloniaTheory]
    [InlineData(900, 650, false)]
    [InlineData(900, 650, true)]
    [InlineData(1600, 900, false)]
    [InlineData(1600, 900, true)]
    public void PaletteCanvasAndSelectedPropertiesRemainUsable(int width, int height, bool dark)
    {
        using var fixture = new Fixture("architecture-beta\n", width, height);
        fixture.Window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        Settle();
        fixture.Capture($"{width}-{(dark ? "dark" : "light")}-empty");
        Click(fixture.Item("Add service"));
        var canvas = fixture.Window.CanvasForTest;
        Assert.True(canvas.Bounds.Width >= width * .5);
        Assert.True(canvas.Bounds.Height >= height * .5);
        Assert.True(fixture.Item("Add junction").IsEffectivelyVisible);
        Assert.DoesNotContain(Visible<Button>(fixture.Properties), b => AutomationProperties.GetName(b) == "Add group");
        Assert.Equal("Service1", fixture.Property<TextBox>("Service label").Text);
        fixture.Capture($"{width}-{(dark ? "dark" : "light")}-selected");
        WorkspaceControls.Click(fixture.Window, "Cancel edit");
    }

    private static IEnumerable<T> Visible<T>(Visual parent) where T : Control =>
        parent.GetVisualDescendants().OfType<T>().Where(c => c.IsEffectivelyVisible);
    private static void Settle() => Dispatcher.UIThread.RunJobs();
    private static void Click(Button button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle(); }

    private sealed class Fixture : IDisposable
    {
        public MainWindow Window { get; }
        public Border Palette => Window.FindControl<Border>("FamilyPaletteHost")!;
        public Border Properties => Window.FindControl<Border>("ClassInspectorHost")!;
        public Fixture(string source, int width = 1600, int height = 900)
        {
            Window = new MainWindow { Width = width, Height = height };
            Window.Show(); Window.LoadSourceForTest(source); Settle();
        }
        public Button Item(string name) => Visible<Button>(Palette).Single(c => AutomationProperties.GetName(c) == name);
        public T Property<T>(string name) where T : Control => Visible<T>(Properties).Single(c => AutomationProperties.GetName(c) == name);
        public void Capture(string name)
        {
            if (Environment.GetEnvironmentVariable("MERPEEPS_PANELS_CAPTURES") is not { Length: > 0 } path) return;
            Directory.CreateDirectory(path);
            using var frame = Window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No rendered workspace frame.");
            frame.Save(Path.Combine(path, name + ".png"));
        }
        public void Dispose()
        {
            foreach (var child in Window.OwnedWindows.ToArray()) child.Close();
            Window.Close();
        }
    }
}
