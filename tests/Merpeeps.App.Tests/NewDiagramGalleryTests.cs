using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Merpeeps.Core;
using Merpeeps.Core.Editing;
using Merpeeps.Core.Model;
using Merpeeps.Core.Parsing;
using Xunit;

namespace Merpeeps.App.Tests;

public class NewDiagramGalleryTests
{
    [AvaloniaFact]
    public void StartupShowsTypeGalleryInsteadOfASampleDocument()
    {
        using var fixture = new Fixture();
        Assert.True(fixture.Gallery.IsVisible);
        Assert.False(fixture.Window.FindControl<DockPanel>("EditorWorkspace")!.IsVisible);
        Assert.Null(fixture.Window.CanvasForTest.Model);
        Assert.Equal(33, fixture.Gallery.GetVisualDescendants().OfType<DiagramTypePreview>().Count());
        Assert.NotNull(fixture.Find<Button>("Open existing diagram…"));
        Assert.DoesNotContain(fixture.Gallery.GetVisualDescendants().OfType<ComboBox>(), c => c.IsEffectivelyVisible);
        Assert.NotNull(fixture.Find<Button>("Create ZenUML"));
        Assert.DoesNotContain(fixture.Gallery.GetVisualDescendants().OfType<TextBlock>(), t =>
            t.IsEffectivelyVisible && t.Text?.Contains("not yet available", StringComparison.Ordinal) == true);
    }

    [AvaloniaTheory]
    [InlineData("Flowchart", "flowchart")]
    [InlineData("State diagram", "stateDiagram-v2")]
    [InlineData("Class diagram", "classDiagram")]
    [InlineData("Entity relationship", "erDiagram")]
    [InlineData("Sequence diagram", "sequenceDiagram")]
    [InlineData("Pie chart", "pie")]
    [InlineData("Quadrant chart", "quadrantChart")]
    [InlineData("XY chart", "xychart")]
    [InlineData("Radar chart", "radar-beta")]
    [InlineData("Timeline", "timeline")]
    [InlineData("User journey", "journey")]
    [InlineData("Mindmap", "mindmap")]
    [InlineData("Tree view", "treeView-beta")]
    [InlineData("Packet diagram", "packet")]
    [InlineData("Kanban", "kanban")]
    [InlineData("Requirement diagram", "requirementDiagram")]
    [InlineData("Git graph", "gitGraph")]
    [InlineData("Sankey diagram", "sankey")]
    [InlineData("Treemap", "treemap-beta")]
    [InlineData("Block diagram", "block")]
    [InlineData("Architecture diagram", "architecture-beta")]
    [InlineData("C4 architecture", "C4Context")]
    [InlineData("Gantt chart", "gantt")]
    [InlineData("Wardley map", "wardley-beta")]
    [InlineData("Ishikawa diagram", "ishikawa-beta")]
    [InlineData("Cynefin diagram", "cynefin-beta")]
    [InlineData("Venn diagram", "venn-beta")]
    [InlineData("Railroad diagram", "railroad-ebnf-beta")]
    [InlineData("Swimlane diagram", "swimlane-beta")]
    [InlineData("Use case diagram", "usecase-beta")]
    [InlineData("Agent flow", "agentflow-beta")]
    [InlineData("Event modeling", "eventmodeling")]
    [InlineData("ZenUML", "zenuml")]
    public void OneTileClickCreatesTheChosenEmptyFamily(string name, string kind)
    {
        using var fixture = new Fixture();
        var tile = fixture.Find<Button>("Create " + name);
        tile.BringIntoView();
        Settle();
        var position = tile.TranslatePoint(new(tile.Bounds.Width / 2, tile.Bounds.Height / 2), fixture.Window)!.Value;
        fixture.Window.MouseDown(position, MouseButton.Left);
        fixture.Window.MouseUp(position, MouseButton.Left);
        Settle();
        Assert.False(fixture.Gallery.IsVisible);
        Assert.True(fixture.Window.FindControl<DockPanel>("EditorWorkspace")!.IsVisible);
        var model = fixture.Window.CanvasForTest.Model!;
        Assert.Equal(kind, model.Kind);
        Assert.Empty(model.Nodes);
        Assert.Empty(model.Edges);
        Assert.False(fixture.Window.CanvasForTest.History!.CanUndo);
        Assert.Equal(kind, MerpeepsDocument.Load(fixture.Window.CurrentSource).Kind);
    }

    [AvaloniaTheory]
    [InlineData("State diagram", "State diagram (legacy directive)", "stateDiagram")]
    [InlineData("Class diagram", "Class diagram (v2 directive)", "classDiagram-v2")]
    [InlineData("Flowchart", "Graph", "graph")]
    public void SyntaxVariantsRemainAnExplicitSecondaryChoice(string family, string variant, string directive)
    {
        using var fixture = new Fixture();
        var button = fixture.Find<Button>(family + " syntax variants");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Settle();
        var item = button.ContextMenu!.Items.Cast<MenuItem>().Single(i => (string?)i.Header == variant);
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        button.ContextMenu.Close();
        Settle();
        Assert.Equal(directive, fixture.Window.CanvasForTest.Model!.Kind);
    }

    [AvaloniaFact]
    public void ExistingFileStartupBypassesGalleryAndKeepsItsType()
    {
        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mmd");
        File.WriteAllText(file, "classDiagram\nclass Account\n");
        var window = new MainWindow(file);
        try
        {
            window.Show();
            Settle();
            Assert.False(window.FindControl<Border>("NewDiagramHost")!.IsVisible);
            Assert.Equal("classDiagram", window.CanvasForTest.Model!.Kind);
            Assert.Equal("Account", Assert.Single(window.CanvasForTest.Model.Nodes).Id);
        }
        finally { window.Close(); File.Delete(file); }
    }

    [AvaloniaFact]
    public void NewAndBackRetainTheCurrentModelHistoryAndUnappliedDraft()
    {
        using var fixture = new Fixture();
        fixture.Click("Create Class diagram");
        var canvas = fixture.Window.CanvasForTest;
        canvas.ApplyClassEdit(new AddClassElementCommand(ClassEditing.CreateClass("Account")), "Account");
        Settle();
        var inspector = fixture.Window.FindControl<Border>("ClassInspectorHost")!;
        Field<Button>(inspector, "Add attribute").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Settle();
        var input = Field<TextBox>(inspector, "Signature");
        input.Text = "String draft";
        input.CaretIndex = 5;
        input.Focus();
        var model = canvas.Model;
        var source = fixture.Window.CurrentSource;
        var undo = canvas.History!.NextUndo;
        fixture.New();
        fixture.Click("Create State diagram");
        Assert.True(fixture.Gallery.IsVisible);
        Assert.Same(model, canvas.Model);
        fixture.Click("Keep current diagram");
        Assert.Same(input, Field<TextBox>(inspector, "Signature"));
        Assert.Equal("String draft", input.Text);
        Assert.Equal(5, input.CaretIndex);
        Assert.Equal(source, fixture.Window.CurrentSource);
        Assert.Equal(undo, canvas.History.NextUndo);
        Field<Button>(inspector, "Cancel edit").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Settle();
    }

    [AvaloniaFact]
    public void DirtyDocumentRequiresExplicitDiscardBeforeTypeReplacement()
    {
        using var fixture = new Fixture();
        fixture.Click("Create Class diagram");
        var canvas = fixture.Window.CanvasForTest;
        canvas.ApplyClassEdit(new AddClassElementCommand(ClassEditing.CreateClass("Account")));
        var previous = canvas.Model;
        fixture.New();
        fixture.Click("Create Flowchart");
        Assert.Same(previous, canvas.Model);
        Assert.True(fixture.Gallery.IsVisible);
        fixture.Click("Discard changes and create");
        Assert.False(fixture.Gallery.IsVisible);
        Assert.Equal("flowchart", canvas.Model!.Kind);
        Assert.Empty(canvas.Model.Nodes);
        Assert.False(canvas.History!.CanUndo);
    }

    [AvaloniaFact]
    public void OpeningAnotherFileHasItsOwnExplicitConsentAndDoesNotCreateADiagram()
    {
        var opened = 0;
        var created = 0;
        var back = 0;
        var gallery = new NewDiagramGallery(_ => created++, () => opened++, () => back++, () => true);
        var window = new Window { Width = 900, Height = 650, Content = gallery };
        window.Show();
        gallery.Show(true);
        Settle();
        try
        {
            Field<Button>(gallery, "Open existing diagram…").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Settle();
            Assert.Equal(0, opened);
            Field<Button>(gallery, "Keep current diagram").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, back);
            Assert.Equal(0, opened);
            gallery.Show(true);
            Field<Button>(gallery, "Open existing diagram…").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Settle();
            Field<Button>(gallery, "Discard changes and open…").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, opened);
            Assert.Equal(0, created);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void KeyboardCanCreateFromAGalleryTile()
    {
        using var fixture = new Fixture();
        fixture.Find<Button>("Create Class diagram").Focus();
        fixture.Window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        fixture.Window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Settle();
        Assert.Equal("classDiagram", fixture.Window.CanvasForTest.Model!.Kind);
        Assert.False(fixture.Gallery.IsVisible);
    }

    [AvaloniaFact]
    public void DoubleClickingATypeCannotInsertANodeThroughTheClosedGallery()
    {
        using var fixture = new Fixture();
        var tile = fixture.Find<Button>("Create Class diagram");
        var point = tile.TranslatePoint(new Point(tile.Bounds.Width / 2, tile.Bounds.Height / 2), fixture.Window)!.Value;
        fixture.Window.MouseDown(point, MouseButton.Left);
        fixture.Window.MouseUp(point, MouseButton.Left);
        fixture.Window.MouseDown(point, MouseButton.Left);
        fixture.Window.MouseUp(point, MouseButton.Left);
        Settle();
        Assert.Equal("classDiagram", fixture.Window.CanvasForTest.Model!.Kind);
        Assert.Empty(fixture.Window.CanvasForTest.Model.Nodes);
        Assert.False(fixture.Window.CanvasForTest.History!.CanUndo);
    }

    [AvaloniaFact]
    public void StatePaletteProducesStateSemanticsNotFlowchartStencils()
    {
        using var fixture = new Fixture();
        fixture.Click("Create State diagram");
        var canvas = fixture.Window.CanvasForTest;
        var items = fixture.Window.FindControl<ListBox>("PaletteList")!.Items.Cast<PaletteItem>().ToArray();
        canvas.AddFromPalette(items.Single(i => i.Name == "Start"), new(60, 60));
        canvas.AddFromPalette(items.Single(i => i.Name == "End"), new(300, 60));
        Assert.False(fixture.Window.FindControl<Button>("SaveBtn")!.IsEnabled);
        Assert.Contains("connect the start/end marker", fixture.Window.FindControl<TextBlock>("StatusText")!.Text);
        Assert.NotNull(canvas.Model!.Node(StateAdapter.StartId));
        Assert.NotNull(canvas.Model.Node(StateAdapter.EndId));
        Assert.DoesNotContain(canvas.Model.Nodes, n => n.Label is "Start" or "End");
        canvas.History!.Apply(new AddEdgeCommand(new() { From = StateAdapter.StartId, To = StateAdapter.EndId }));
        var source = fixture.Window.CurrentSource;
        Assert.Contains("[*] --> [*]", source);
        Assert.DoesNotContain("flowchart", source);
        var history = canvas.History.NextUndo;
        Assert.Throws<ArgumentException>(() => canvas.AddFromPalette(items.Single(i => i.Name == "Start")));
        Assert.Equal(history, canvas.History.NextUndo);
        Assert.Equal(2, canvas.Model.Nodes.Count);
    }

    [AvaloniaTheory]
    [InlineData(900, 650, false)]
    [InlineData(900, 650, true)]
    [InlineData(1440, 900, false)]
    [InlineData(1440, 900, true)]
    public void GalleryPreviewMatrixUsesDistinctReadableTypeThumbnails(int width, int height, bool dark)
    {
        using var fixture = new Fixture();
        fixture.Window.Width = width; fixture.Window.Height = height;
        fixture.Window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        Settle();
        var classTile = fixture.Find<Button>("Create Class diagram");
        classTile.BringIntoView();
        Settle();
        var topLeft = classTile.TranslatePoint(new Point(), fixture.Window)!.Value;
        Assert.InRange(topLeft.X + classTile.Bounds.Width, 0, width);
        Assert.InRange(topLeft.Y + classTile.Bounds.Height, 0, height);
        var previews = fixture.Gallery.GetVisualDescendants().OfType<DiagramTypePreview>().ToArray();
        var fingerprints = new List<string>();
        foreach (var preview in previews)
        {
            using var target = new RenderTargetBitmap(new PixelSize(268, 164));
            target.Render(preview);
            using var bytes = new MemoryStream();
            target.Save(bytes);
            fingerprints.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes.ToArray())));
        }
        Assert.Equal(33, fingerprints.Distinct().Count());
        var capture = Environment.GetEnvironmentVariable("MERPEEPS_GALLERY_CAPTURES");
        if (!string.IsNullOrEmpty(capture))
        {
            Directory.CreateDirectory(capture);
            using var image = fixture.Window.CaptureRenderedFrame();
            Assert.NotNull(image);
            image.Save(Path.Combine(capture, $"gallery-{width}-{height}-{(dark ? "dark" : "light")}.png"));
        }
    }

    private static T Field<T>(Visual parent, string name) where T : Control => parent.GetVisualDescendants().OfType<T>()
        .Single(c => c.IsEffectivelyVisible && AutomationProperties.GetName(c) == name);
    private static void Settle() => Dispatcher.UIThread.RunJobs();
    private sealed class Fixture : IDisposable
    {
        public MainWindow Window { get; } = new();
        public Border Gallery => Window.FindControl<Border>("NewDiagramHost")!;
        public Fixture() { Window.Show(); Window.ApplyTemplate(); Settle(); }
        public T Find<T>(string name) where T : Control => Field<T>(Gallery, name);
        public void Click(string name) { Find<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle(); }
        public void New() { Window.FindControl<MenuItem>("MenuNew")!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Settle(); }
        public void Dispose() => Window.Close();
    }
}
