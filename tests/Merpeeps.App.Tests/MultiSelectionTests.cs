using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Merpeeps.Core;
using Merpeeps.Core.Editing;
using Merpeeps.Core.Model;
using Xunit;

namespace Merpeeps.App.Tests;

public class MultiSelectionTests
{
    [AvaloniaTheory]
    [InlineData("flowchart")]
    [InlineData("state")]
    [InlineData("class")]
    [InlineData("er")]
    public void ShiftClickAddsAndRemovesNodesAndSingleClickReplacesSelection(string family)
    {
        using var f = new Fixture(Source(family));
        f.Click(NodeCenter(f.Model.Node("A")!));
        f.Click(NodeCenter(f.Model.Node("B")!), RawInputModifiers.Shift);
        Assert.Equal(2, f.Canvas.SelectedObjectCount);
        Assert.Null(f.Canvas.Selected);
        Assert.True(f.Window.FindControl<Border>("ArrangementPanel")!.IsEffectivelyVisible);
        Assert.True(f.Button("Align left").IsEnabled);
        Assert.False(f.Button("Distribute horizontally").IsEnabled);
        f.Click(NodeCenter(f.Model.Node("A")!), RawInputModifiers.Shift);
        Assert.Same(f.Model.Node("B"), f.Canvas.Selected);
        Assert.False(f.Window.FindControl<Border>("ArrangementPanel")!.IsVisible);
        f.Click(NodeCenter(f.Model.Node("C")!));
        Assert.Same(f.Model.Node("C"), f.Canvas.Selected);
        Assert.False(f.Canvas.History!.CanUndo);
    }

    [AvaloniaTheory]
    [InlineData("flowchart")]
    [InlineData("state")]
    [InlineData("class")]
    [InlineData("er")]
    public void AdditiveMarqueeRetainsEarlierSelectionWhileShrinkingTheBox(string family)
    {
        using var f = new Fixture(Source(family));
        f.Click(NodeCenter(f.Model.Node("A")!));
        var b = f.Model.Node("B")!;
        var c = f.Model.Node("C")!;
        var from = new DiagramPoint(b.X - 10, b.Y - 10);
        f.Down(from, RawInputModifiers.Shift);
        f.Move(new(c.X + c.Width + 10, c.Y + c.Height + 10), RawInputModifiers.Shift);
        Assert.Equal(3, f.Canvas.SelectedObjectCount);
        var to = new DiagramPoint(b.X + b.Width + 10, b.Y + b.Height + 10);
        f.Move(to, RawInputModifiers.Shift);
        f.Up(to, RawInputModifiers.Shift);
        Assert.Equal(new[] { "A", "B" }, f.Canvas.Selection.Select(n => n.Id).Order());
        Assert.False(f.Canvas.History!.CanUndo);
    }

    [AvaloniaTheory]
    [InlineData(GraphArrangement.Left, "Align left")]
    [InlineData(GraphArrangement.HorizontalCenter, "Align horizontal centers")]
    [InlineData(GraphArrangement.Right, "Align right")]
    [InlineData(GraphArrangement.Top, "Align top")]
    [InlineData(GraphArrangement.VerticalCenter, "Align vertical centers")]
    [InlineData(GraphArrangement.Bottom, "Align bottom")]
    public void ActualAlignmentButtonUsesBoundsAndSupportsUndoSourceAndRedo(GraphArrangement arrangement, string name)
    {
        foreach (var family in new[] { "flowchart", "state", "class", "er" })
        {
            using var f = new Fixture(Source(family));
            f.Canvas.SelectByIds(["A", "B", "C"]);
            var before = f.Model.Nodes.Select(n => (n.X, n.Y, n.HasExplicitPosition)).ToArray();
            var sourceBefore = f.Window.CurrentSource;
            var left = f.Model.Nodes.Min(n => n.X); var right = f.Model.Nodes.Max(n => n.X + n.Width);
            var top = f.Model.Nodes.Min(n => n.Y); var bottom = f.Model.Nodes.Max(n => n.Y + n.Height);
            f.ClickControl(f.Button(name));
            Assert.All(f.Model.Nodes, n =>
            {
                var value = arrangement switch
                {
                    GraphArrangement.Left => n.X,
                    GraphArrangement.HorizontalCenter => n.X + n.Width / 2,
                    GraphArrangement.Right => n.X + n.Width,
                    GraphArrangement.Top => n.Y,
                    GraphArrangement.VerticalCenter => n.Y + n.Height / 2,
                    _ => n.Y + n.Height,
                };
                var expected = arrangement switch
                {
                    GraphArrangement.Left => left,
                    GraphArrangement.HorizontalCenter => (left + right) / 2,
                    GraphArrangement.Right => right,
                    GraphArrangement.Top => top,
                    GraphArrangement.VerticalCenter => (top + bottom) / 2,
                    _ => bottom,
                };
                Assert.Equal(expected, value, 6);
            });
            Assert.Equal(3, f.Canvas.SelectedObjectCount);
            var changed = f.Window.CurrentSource;
            var reopened = MerpeepsDocument.Load(changed);
            Assert.Equal(f.Model.Nodes.Select(n => (n.X, n.Y)), reopened.Nodes.Select(n => (n.X, n.Y)));
            f.Canvas.Undo(); Settle();
            Assert.Equal(before, f.Model.Nodes.Select(n => (n.X, n.Y, n.HasExplicitPosition)));
            Assert.Equal(sourceBefore, f.Window.CurrentSource);
            Assert.False(f.Canvas.History!.CanUndo);
            f.Canvas.Redo(); Settle();
            Assert.Equal(changed, f.Window.CurrentSource);
        }
    }

    [AvaloniaTheory]
    [InlineData("Distribute horizontally", true)]
    [InlineData("Distribute vertically", false)]
    public void LayoutMenuAndButtonBothDistributeWithEqualGaps(string name, bool horizontal)
    {
        foreach (var menu in new[] { false, true })
        {
            using var f = new Fixture(Source("er"));
            f.Model.Node("B")!.X += 60;
            f.Model.Node("B")!.Y += 35;
            f.Canvas.SelectByIds(["A", "B", "C"]);
            if (menu)
            {
                var item = f.Window.FindControl<MenuItem>("MenuDistributeSelection")!.ItemsSource!.Cast<MenuItem>()
                    .Single(i => AutomationProperties.GetName(i) == name);
                Assert.True(item.IsEnabled);
                item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Settle();
            }
            else f.ClickControl(f.Button(name));
            Assert.True(f.Canvas.History!.CanUndo);
            var nodes = f.Model.Nodes.OrderBy(n => horizontal ? n.X : n.Y).ToArray();
            Assert.Equal(horizontal ? nodes[1].X - nodes[0].X - nodes[0].Width : nodes[1].Y - nodes[0].Y - nodes[0].Height,
                horizontal ? nodes[2].X - nodes[1].X - nodes[1].Width : nodes[2].Y - nodes[1].Y - nodes[1].Height, 6);
            f.Canvas.Undo(); Settle();
            Assert.False(f.Canvas.History!.CanUndo);
        }
    }

    [AvaloniaTheory]
    [InlineData("flowchart")]
    [InlineData("state")]
    [InlineData("class")]
    [InlineData("er")]
    public void ShiftSelectedGroupsAndNodeDragAsOneSetAndCanBeAligned(string family)
    {
        using var f = new Fixture(Source(family, groups: true));
        var g = f.Model.Subgraphs.Single(s => s.Id == "G");
        var h = f.Model.Subgraphs.Single(s => s.Id == "H");
        f.Click(Header(f.Model, g));
        f.Click(Header(f.Model, h), RawInputModifiers.Shift);
        f.Click(NodeCenter(f.Model.Node("C")!), RawInputModifiers.Shift);
        Assert.Equal(3, f.Canvas.SelectedObjectCount);
        Assert.Equal(2, f.Canvas.SelectedGroups.Count);
        Assert.Single(f.Canvas.Selection);
        Assert.Null(f.Canvas.Selected);
        Assert.Null(f.Canvas.SelectedGroup);
        var before = f.Model.Nodes.Select(n => (n.X, n.Y, n.HasExplicitPosition)).ToArray();
        var at = Header(f.Model, g);
        f.Down(at);
        f.Move(new(at.X + 60, at.Y + 30), RawInputModifiers.Alt);
        f.Up(new(at.X + 60, at.Y + 30));
        Assert.All(f.Model.Nodes.Select((n, i) => (n, i)), pair => Assert.Equal(before[pair.i].X + 60, pair.n.X, 6));
        f.Canvas.Undo(); Settle();
        Assert.Equal(before, f.Model.Nodes.Select(n => (n.X, n.Y, n.HasExplicitPosition)));
        Assert.False(f.Canvas.History!.CanUndo);
        f.ClickControl(f.Button("Align top"));
        Assert.Equal(GraphGroupGeometry.Bounds(f.Model, g).Y, GraphGroupGeometry.Bounds(f.Model, h).Y, 6);
        Assert.Equal(GraphGroupGeometry.Bounds(f.Model, g).Y, f.Model.Node("C")!.Y, 6);
        f.Canvas.Undo(); Settle();
        Assert.Equal(before, f.Model.Nodes.Select(n => (n.X, n.Y, n.HasExplicitPosition)));
        Assert.False(f.Canvas.History.CanUndo);
    }

    [AvaloniaFact]
    public void MarqueeAroundAGroupDoesNotDoubleSelectOrMoveItsContents()
    {
        using var f = new Fixture(Source("er", groups: true));
        var group = f.Model.Subgraphs.Single(g => g.Id == "G");
        var r = GraphGroupGeometry.Bounds(f.Model, group);
        f.Down(new(r.X - 8, r.Y - 8));
        var end = new DiagramPoint(r.X + r.Width + 8, r.Y + r.Height + 8);
        f.Move(end); f.Up(end);
        Assert.Same(group, f.Canvas.SelectedGroup);
        Assert.Empty(f.Canvas.Selection);
        f.Click(NodeCenter(f.Model.Node("A")!), RawInputModifiers.Shift);
        Assert.Same(group, f.Canvas.SelectedGroup);
        Assert.Empty(f.Canvas.Selection);
        f.Canvas.SelectAll();
        Assert.Equal(3, f.Canvas.SelectedObjectCount);
        Assert.Empty(f.Canvas.SelectedGroups);
        Assert.Equal(3, f.Canvas.Selection.Count);
    }

    [AvaloniaFact]
    public void ShiftRemovingAnObjectCannotDragTheRemainingSelection()
    {
        using var f = new Fixture(Source("flowchart"));
        f.Canvas.SelectByIds(["A", "B"]);
        var original = f.Model.Nodes.Select(n => (n.X, n.Y, n.HasExplicitPosition)).ToArray();
        var start = NodeCenter(f.Model.Node("B")!);
        f.Down(start, RawInputModifiers.Shift);
        f.Move(new(start.X + 70, start.Y + 50), RawInputModifiers.Shift);
        f.Up(new(start.X + 70, start.Y + 50), RawInputModifiers.Shift);
        Assert.Same(f.Model.Node("A"), f.Canvas.Selected);
        Assert.Equal(original, f.Model.Nodes.Select(n => (n.X, n.Y, n.HasExplicitPosition)));
        Assert.False(f.Canvas.History!.CanUndo);
    }

    [AvaloniaTheory]
    [InlineData("pie\n\"One\":1\n")]
    [InlineData("timeline\nNow:Event\n")]
    [InlineData("sequenceDiagram\nA->>B:Hi\n")]
    [InlineData("mindmap\nRoot\n A\n")]
    public void SemanticFamiliesDoNotOfferGeometricAlignment(string source)
    {
        using var f = new Fixture(source, positionNodes: false);
        Assert.False(f.Canvas.SupportsArrangement);
        Assert.False(f.Window.FindControl<MenuItem>("MenuAlignSelection")!.IsEnabled);
        Assert.False(f.Window.FindControl<Border>("ArrangementPanel")!.IsVisible);
        var before = f.Window.CurrentSource;
        Assert.Throws<ArgumentException>(() => f.Canvas.ArrangeSelection(GraphArrangement.Left));
        Assert.Equal(before, f.Window.CurrentSource);
        Assert.False(f.Canvas.History!.CanUndo);
    }

    [AvaloniaFact]
    public void NoOpAlignmentDoesNotAddHistory()
    {
        using var f = new Fixture(Source("er"));
        foreach (var n in f.Model.Nodes) n.X = 10;
        f.Canvas.SelectByIds(["A", "B", "C"]);
        var before = f.Window.CurrentSource;
        f.ClickControl(f.Button("Align left"));
        Assert.False(f.Canvas.History!.CanUndo);
        Assert.Equal(before, f.Window.CurrentSource);
    }

    [AvaloniaFact]
    public void AlignmentCanBeActivatedFromKeyboardFocus()
    {
        using var f = new Fixture(Source("class"));
        f.Canvas.SelectByIds(["A", "B"]); Settle();
        var button = f.Button("Align left");
        Assert.True(button.Focus());
        f.Window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        f.Window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None); Settle();
        Assert.Equal(f.Model.Node("A")!.X, f.Model.Node("B")!.X, 6);
        Assert.True(f.Canvas.History!.CanUndo);
    }

    [AvaloniaFact]
    public void InvalidPendingPropertiesBlockAlignmentWithoutLosingTheirDraft()
    {
        using var f = new Fixture(Source("er"));
        f.Canvas.SelectByIds(["A"]); Settle();
        WorkspaceControls.Click(f.Window, "Add attribute");
        WorkspaceControls.Find<TextBox>(f.Window, "Data type").Text = "123";
        WorkspaceControls.Find<TextBox>(f.Window, "Attribute name").Text = "id";
        WorkspaceControls.Click(f.Window, "Apply attribute"); Settle();
        f.Canvas.SelectByIds(["A", "B"]); Settle();
        var before = f.Window.CurrentSource;
        f.ClickControl(f.Button("Align left"));
        Assert.Equal(before, f.Window.CurrentSource);
        Assert.False(f.Canvas.History!.CanUndo);
        Assert.True(f.Window.FindControl<Border>("ClassInspectorHost")!.IsVisible);
        Assert.Contains(f.Window.OwnedWindows, w => w.Title == "Finish the pending edit");
    }

    [AvaloniaTheory]
    [InlineData(1560, false)]
    [InlineData(900, false)]
    [InlineData(1560, true)]
    [InlineData(900, true)]
    public void ArrangementIconsFitCompactAndWidePanesWithAccessibleNames(int width, bool dark)
    {
        using var f = new Fixture(Source("er"), width: width, dark: dark);
        f.Canvas.SelectByIds(["A", "B", "C"]); Settle();
        var buttons = f.Window.FindControl<Border>("ArrangementPanel")!.GetVisualDescendants().OfType<Button>().ToArray();
        var pane = f.Window.FindControl<ScrollViewer>("PropertyPane")!;
        Assert.Equal(8, buttons.Length);
        Assert.All(buttons, button =>
        {
            Assert.IsType<UiIcon>(button.Content);
            Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(button)));
            Assert.NotNull(ToolTip.GetTip(button));
            var position = button.TranslatePoint(default, pane)!.Value;
            Assert.InRange(position.X, 0, pane.Bounds.Width - button.Bounds.Width);
        });
        if (Environment.GetEnvironmentVariable("MERPEEPS_MULTISELECT_CAPTURES") is { Length: > 0 } path)
        {
            Directory.CreateDirectory(path);
            using var image = f.Window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No rendered arrangement frame.");
            image.Save(System.IO.Path.Combine(path, $"selection-{width}-{(dark ? "dark" : "light")}.png"));
        }
    }

    private static string Source(string family, bool groups = false) => family switch
    {
        "flowchart" => groups ? "flowchart LR\nsubgraph G\nA[Alpha]\nend\nsubgraph H\nB[Beta]\nend\nC[Longer label]\n" : "flowchart LR\nA[Alpha]\nB[Beta]\nC[Longer label]\n",
        "state" => groups ? "stateDiagram-v2\nstate G {\nA\n}\nstate H {\nB\n}\nC\n" : "stateDiagram-v2\nA\nB\nC\n",
        "class" => groups ? "classDiagram\nnamespace G {\nclass A\n}\nnamespace H {\nclass B\n}\nclass C\n" : "classDiagram\nclass A\nclass B\nclass C\n",
        _ => groups ? "erDiagram\nsubgraph G\nA\nend\nsubgraph H\nB\nend\nC\n" : "erDiagram\nA\nB\nC\n",
    };
    private static DiagramPoint NodeCenter(DiagramNode n) => new(n.X + n.Width / 2, n.Y + n.Height / 2);
    private static DiagramPoint Header(DiagramModel model, DiagramSubgraph group)
    {
        var r = GraphGroupGeometry.Bounds(model, group);
        return new(r.X + r.Width / 2, r.Y + 12);
    }
    private static void Settle() => Dispatcher.UIThread.RunJobs();
    private sealed class Fixture : IDisposable
    {
        public MainWindow Window { get; }
        public EditorCanvas Canvas => Window.CanvasForTest;
        public DiagramModel Model => Canvas.Model!;
        public Fixture(string source, bool positionNodes = true, int width = 1560, bool dark = false)
        {
            Window = new(new StartupOptions(null, dark ? ThemeVariant.Dark : ThemeVariant.Light)) { Width = width, Height = 920 };
            Window.Show(); Window.LoadSourceForTest(source); Settle();
            Assert.NotNull(Canvas.Model);
            if (positionNodes)
                for (var i = 0; i < Model.Nodes.Count; i++) { Model.Nodes[i].X = 20 + i * 350; Model.Nodes[i].Y = 20 + i * 140; }
            Canvas.FitToView(); Settle();
        }
        private Point Screen(DiagramPoint p) => Canvas.TranslatePoint(new(p.X * Canvas.Zoom + Canvas.OffsetX, p.Y * Canvas.Zoom + Canvas.OffsetY), Window)!.Value;
        public Button Button(string name) => Window.FindControl<Border>("ArrangementPanel")!.GetVisualDescendants().OfType<Button>()
            .Single(b => AutomationProperties.GetName(b) == name);
        public void Click(DiagramPoint p, RawInputModifiers mods = RawInputModifiers.None) { Down(p, mods); Up(p, mods); }
        public void Down(DiagramPoint p, RawInputModifiers mods = RawInputModifiers.None) { var at = Screen(p); Window.MouseMove(at); Window.MouseDown(at, MouseButton.Left, mods); }
        public void Move(DiagramPoint p, RawInputModifiers mods = RawInputModifiers.None) { Window.MouseMove(Screen(p), mods); Settle(); }
        public void Up(DiagramPoint p, RawInputModifiers mods = RawInputModifiers.None) { Window.MouseUp(Screen(p), MouseButton.Left, mods); Settle(); }
        public void ClickControl(Control control)
        {
            Settle();
            Assert.True(control.IsEffectivelyVisible);
            Assert.True(control.Bounds.Width > 0 && control.Bounds.Height > 0);
            var at = control.TranslatePoint(new(control.Bounds.Width / 2, control.Bounds.Height / 2), Window)!.Value;
            Window.MouseMove(at); Window.MouseDown(at, MouseButton.Left); Window.MouseUp(at, MouseButton.Left); Settle();
        }
        public void Dispose() { foreach (var child in Window.OwnedWindows.ToArray()) child.Close(); Window.Close(); }
    }
}
