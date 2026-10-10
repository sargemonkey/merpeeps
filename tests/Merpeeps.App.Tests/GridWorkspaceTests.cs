using Avalonia.Automation;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Merpeeps.Core;
using Merpeeps.Core.Model;
using Xunit;

namespace Merpeeps.App.Tests;

public class GridWorkspaceTests
{
    [AvaloniaTheory]
    [InlineData(false, 1600, 900)]
    [InlineData(false, 900, 600)]
    [InlineData(true, 1600, 900)]
    [InlineData(true, 900, 600)]
    public void GridAuthoringStatesHaveUsableCanvasArea(bool architecture, int width, int height)
    {
        var source = architecture
            ? "architecture-beta\ngroup api(cloud)[API]\nservice db(database)[Database] in api\nservice app(server)[Application] in api\ndb:R --> L:app\n"
            : "block\ncolumns 3\nA[\"First\"] space B[\"Second\"]\nblock:group:2\ncolumns 2\nC D\nend\nE\nA --> B\n";
        using var fixture = new Fixture(source, width, height);
        var canvas = fixture.Window.CanvasForTest;
        var id = architecture ? canvas.Model!.Architecture!.Nodes[0].Id : canvas.Model!.Blocks!.Items[0].Id;
        canvas.SelectStructural(new(architecture ? "ArchitectureNode" : "Block", id)); fixture.Settle();
        Assert.True(canvas.Bounds.Width >= width * .5 && canvas.Bounds.Height >= 180,
            $"The canvas must retain at least half the window width, not {canvas.Bounds.Width} of {width}.");
        Assert.True(canvas.Zoom >= .3, $"This small fixture should remain readable after pane layout, not zoom {canvas.Zoom}.");
        fixture.Capture($"{(architecture ? "architecture" : "block")}-{width}-selected");
        Assert.NotNull(fixture.Field<TextBox>(architecture ? "Service label" : "Block label"));
        fixture.Capture($"{(architecture ? "architecture" : "block")}-{width}-editing");
    }
    [AvaloniaFact]
    public void BlockCreationAndNestedGridEditsPreserveTheSourceFamily()
    {
        using var fixture = new Fixture("block\n");
        WorkspaceControls.OpenCreationForm(fixture.Window);
        fixture.Field<TextBox>("Source identifier").Text = "Group";
        fixture.Field<ComboBox>("Block kind").SelectedItem = BlockItemKind.Composite; fixture.Settle();
        fixture.Field<TextBox>("Child columns (blank is auto)").Text = "2";
        fixture.Click("Apply block");
        fixture.Click("Add child block");
        fixture.Field<TextBox>("Source identifier").Text = "A";
        fixture.Field<TextBox>("Block label").Text = "Inside";
        fixture.Field<TextBox>("Column span").Text = "2";
        fixture.Click("Apply block");
        var model = fixture.Window.CanvasForTest.Model!;
        Assert.Empty(model.Nodes);
        var root = model.Blocks!.Items.Single();
        Assert.Equal(BlockItemKind.Composite, root.Kind);
        Assert.Equal("A", root.Children.Single().SourceId);
        Assert.Equal(2, root.Children.Single().Span);
        Assert.Contains("block:Group", fixture.Window.CurrentSource);
        var saved = MerpeepsDocument.Load(fixture.Window.CurrentSource);
        Assert.Equal(root.Children[0].Id, saved.Blocks!.Items[0].Children[0].Id);
        fixture.Field<TextBox>("Column span").Text = "3";
        fixture.Click("Apply block");
        Assert.Equal(2, model.Blocks.Items[0].Children[0].Span);
    }
    [AvaloniaFact]
    public void ArchitectureServicePropertiesAndSpacingAreRealDiagramEdits()
    {
        using var fixture = new Fixture("architecture-beta\n");
        WorkspaceControls.OpenCreationForm(fixture.Window);
        fixture.Field<TextBox>("Source identifier").Text = "database";
        fixture.Field<TextBox>("Service label").Text = "Orders";
        fixture.Click("Apply service / junction");
        var model = fixture.Window.CanvasForTest.Model!;
        Assert.Empty(model.Nodes);
        Assert.Equal("Orders", model.Architecture!.Nodes.Single().Label);
        var id = model.Architecture.Nodes.Single().Id;
        fixture.Window.FindControl<MenuItem>("MenuClassDisplay")!.RaiseClick(); fixture.Settle();
        fixture.Field<TextBox>("iconSize").Text = "100";
        fixture.Click("Apply architecture spacing");
        Assert.Equal(100, fixture.Window.CanvasForTest.ArchitectureLayout.Nodes[id].IconBounds.Width);
        var reopened = MerpeepsDocument.Load(fixture.Window.CurrentSource);
        Assert.Equal(id, reopened.Architecture!.Nodes.Single().Id);
        fixture.Window.CanvasForTest.Undo(); fixture.Settle();
        Assert.Equal(80, fixture.Window.CanvasForTest.ArchitectureLayout.Nodes[id].IconBounds.Width);
    }

    [AvaloniaFact]
    public void BlockPointerTargetsCreateOneConnectionAndExposeItsOwnFields()
    {
        using var f = new Fixture("block\ncolumns 3\nA space B\n");
        var canvas = f.Window.CanvasForTest;
        var document = canvas.Model!.Blocks!;
        var a = document.Items[0].Id; var b = document.Items[2].Id;
        f.Point(canvas.BlockLayout.Placements[a].Bounds);
        f.Click("Add connection");
        Assert.Empty(document.Connections);
        f.Point(canvas.BlockLayout.Placements[b].Bounds);
        Assert.Single(canvas.Model.Blocks!.Connections);
        Assert.NotNull(f.Field<ComboBox>("Start marker"));
        f.Field<TextBox>("Connection label").Text = "owns";
        f.Click("Apply block connection");
        Assert.Equal("owns", MerpeepsDocument.Load(f.Window.CurrentSource).Blocks!.Connections.Single().Label);
        canvas.Undo(); f.Settle();
        Assert.Empty(canvas.Model.Blocks!.Connections.Single().Label);
        canvas.Undo(); f.Settle();
        Assert.Empty(canvas.Model.Blocks!.Connections);
    }

    [AvaloniaFact]
    public void ArchitecturePointerTargetsCreateExplicitPortConnections()
    {
        using var f = new Fixture("architecture-beta\nservice a(database)[Database]\nservice b(server)[Server]\n");
        var canvas = f.Window.CanvasForTest;
        var document = canvas.Model!.Architecture!;
        f.Point(canvas.ArchitectureLayout.Nodes[document.Nodes[0].Id].IconBounds);
        f.Click("Connect ports");
        f.Point(canvas.ArchitectureLayout.Nodes[document.Nodes[1].Id].IconBounds);
        var edge = canvas.Model.Architecture!.Connections.Single();
        Assert.Equal(ArchitecturePort.Right, edge.From.Port);
        Assert.Equal(ArchitecturePort.Left, edge.To.Port);
        f.Field<CheckBox>("Arrow at source").IsChecked = true;
        f.Click("Apply port connection");
        Assert.True(MerpeepsDocument.Load(f.Window.CurrentSource).Architecture!.Connections.Single().FromArrow);
        canvas.Undo(); f.Settle();
        Assert.False(canvas.Model.Architecture!.Connections.Single().FromArrow);
    }

    [AvaloniaFact]
    public void CompactSourcePaneDefaultsAreViewOnlyAndExplicitChoiceSurvivesResize()
    {
        using var fixture = new Fixture("block\nA space B\n", 900, 600);
        var source = fixture.Window.CurrentSource;
        var pane = fixture.Window.FindControl<DockPanel>("SourcePane")!;
        var menu = fixture.Window.FindControl<MenuItem>("MenuShowSource")!;
        Assert.False(pane.IsVisible);
        Assert.False(menu.IsChecked);
        Assert.False(fixture.Window.CanvasForTest.History!.CanUndo);
        menu.IsChecked = true; menu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); fixture.Settle();
        Assert.True(pane.IsVisible);
        fixture.Window.Width = 1600; fixture.Settle();
        fixture.Window.Width = 900; fixture.Settle();
        Assert.True(pane.IsVisible);
        Assert.Equal(source, fixture.Window.CurrentSource);
        Assert.False(fixture.Window.CanvasForTest.History!.CanUndo);
    }

    private sealed class Fixture : IDisposable
    {
        public MainWindow Window { get; }
        private Border Panel => Window.FindControl<Border>("ClassInspectorHost")!;
        public Fixture(string source, int width = 1600, int height = 900)
        {
            Window = new MainWindow { Width = width, Height = height };
            Window.Show(); Window.LoadSourceForTest(source); Settle();
        }
        public T Field<T>(string name) where T : Control => WorkspaceControls.Find<T>(Window, name);
        public void Click(string name) => WorkspaceControls.Click(Window, name);
        public void Settle() => Dispatcher.UIThread.RunJobs();
        public void Point(DiagramRect bounds)
        {
            var canvas = Window.CanvasForTest;
            var point = canvas.TranslatePoint(new Point((bounds.X + bounds.Width / 2) * canvas.Zoom + canvas.OffsetX,
                (bounds.Y + bounds.Height / 2) * canvas.Zoom + canvas.OffsetY), Window)!.Value;
            Window.MouseMove(point); Window.MouseDown(point, MouseButton.Left); Window.MouseUp(point, MouseButton.Left); Settle();
        }
        public void Capture(string name)
        {
            if (Environment.GetEnvironmentVariable("MERPEEPS_GRID_CAPTURES") is not { Length: > 0 } directory) return;
            Directory.CreateDirectory(directory);
            using var frame = Window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No real rendered grid frame.");
            frame.Save(Path.Combine(directory, name + ".png"));
        }
        public void Dispose() { foreach (var child in Window.OwnedWindows.ToArray()) child.Close(); Window.Close(); }
    }
}
