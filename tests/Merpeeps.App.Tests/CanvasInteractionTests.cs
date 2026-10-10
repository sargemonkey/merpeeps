using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Merpeeps.Core;
using Merpeeps.Core.Layout;
using Merpeeps.Core.Model;
using Xunit;

namespace Merpeeps.App.Tests;

public class CanvasInteractionTests
{
    [AvaloniaTheory]
    [InlineData("alt", "else", 0)]
    [InlineData("alt", "else", 1)]
    [InlineData("par", "and", 0)]
    [InlineData("par", "and", 1)]
    [InlineData("par_over", "and", 0)]
    [InlineData("par_over", "and", 1)]
    [InlineData("critical", "option", 0)]
    [InlineData("critical", "option", 1)]
    public void SequenceBranchConditionCommitsFromInspector(string opening, string separator, int index)
    {
        using var f = new Fixture($"sequenceDiagram\nparticipant A\nparticipant B\n{opening} Allowed\nA->>B: Yes\n{separator} Denied\nA->>B: No\nend\n");
        var block = f.Canvas.Model!.Sequence!.Statements.OfType<SequenceBlock>().Single();
        f.Canvas.SelectSequenceId(block.Branches[index].Id); Settle();
        var field = f.Window.GetVisualDescendants().OfType<TextBox>()
            .Single(t => t.IsEffectivelyVisible && AutomationProperties.GetName(t) == "Condition");
        var expected = field.Text + "Changed";
        field.Focus(); field.Text = expected;
        f.Key(PhysicalKey.Enter, RawInputModifiers.Control);
        var reopened = MerpeepsDocument.Load(f.Window.CurrentSource).Sequence!.Statements.OfType<SequenceBlock>().Single();
        Assert.Equal(expected, index == 0 ? reopened.Text : reopened.Branches[index].Text);
    }

    [AvaloniaTheory]
    [InlineData("flowchart", "flowchart")]
    [InlineData("flowchart", "graph")]
    [InlineData("flowchart", "flowchart-elk")]
    [InlineData("state", "stateDiagram-v2")]
    [InlineData("state", "stateDiagram")]
    [InlineData("class", "classDiagram")]
    [InlineData("class", "classDiagram-v2")]
    public void RenderedContainerHeaderMovesItsMembers(string family, string directive)
    {
        var source = family == "class" ? directive + "\nnamespace G {\nclass A\n}\n"
            : family == "state" ? directive + "\nstate G {\nA --> B\n}\n"
            : directive + " LR\nsubgraph G[Group]\nA[Alpha]\nend\n";
        using var f = new Fixture(source);
        var model = f.Canvas.Model!;
        var group = model.Subgraphs.Single();
        var members = family == "class" ? ClassDiagramGeometry.NamespaceMembers(model, group)
            : group.NodeIds.Select(model.Node).Where(n => n is not null).Cast<DiagramNode>().ToArray();
        Assert.NotEmpty(members);
        var start = new DiagramPoint(members.Min(n => n.X) + 12, members.Min(n => n.Y) - 22);
        var before = members.Select(n => (n.X, n.Y)).ToArray();
        f.Drag(start, new(start.X + 50, start.Y + 30));
        Assert.All(members.Select((n, i) => (n, i)), item =>
        {
            Assert.Equal(before[item.i].X + 50, item.n.X, 6);
            Assert.Equal(before[item.i].Y + 30, item.n.Y, 6);
        });
    }

    [AvaloniaTheory]
    [InlineData("swimlane", false)]
    [InlineData("swimlane", true)]
    [InlineData("usecase", false)]
    [InlineData("usecase", true)]
    [InlineData("agentflow", false)]
    [InlineData("agentflow", true)]
    public void ClickingAnExposedInternalLinkSelectsTheLink(string family, bool container)
    {
        var source = family switch
        {
            "swimlane" => "swimlane-beta LR\n" + (container ? "subgraph G[Team]\n" : "") + "A[First]\nB[Second]\n" + (container ? "end\n" : "") + "A --> B\n",
            "usecase" => "usecase-beta\ndirection LR\n" + (container ? "systemBoundary G[\"App\"]\n" : "") + "A(\"First\")\nB(\"Second\")\n" + (container ? "end\n" : "") + "A --> B\n",
            _ => "agentflow-beta LR\n" + (container ? "flow G[\"Work\"]\n" : "") + "A[\"First\"]@{shape: task}\nB[\"Second\"]@{shape: task}\n" + (container ? "end\n" : "") + "A --> B\n",
        };
        using var f = new Fixture(source);
        var role = family switch { "swimlane" => "SwimlaneLink", "usecase" => "UseCaseRelationship", _ => "AgentFlowLink" };
        var link = f.Canvas.StructuralScene.Figures.First(x => x.Owner.Role == role && x.Kind == DrawingFigureKind.Polyline);
        var points = link.Points;
        var segment = Enumerable.Range(1, points.Count - 1).First(i => points[i] != points[i - 1]);
        f.Click(new((points[segment - 1].X + points[segment].X) / 2, (points[segment - 1].Y + points[segment].Y) / 2));
        Assert.Equal(role, f.Canvas.SelectedStructuralTarget?.Role);
        Assert.Equal(link.Owner.Id, f.Canvas.SelectedStructuralTarget?.Id);
    }

    [AvaloniaTheory]
    [InlineData("A ..> : include B")]
    [InlineData("A ..> : extend B")]
    [InlineData("A --|> B")]
    public void FixedUmlRelationshipsDoNotOfferUnsavableLabelsOrMarkers(string relationship)
    {
        using var f = new Fixture("usecase-beta\ndirection LR\nsystemBoundary G[\"App\"]\nA(\"One\")\nB(\"Two\")\nend\n" + relationship + "\n");
        var edge = f.Canvas.Model!.UseCase!.Relationships.Single();
        var line = f.Canvas.StructuralScene.Regions.Single(r => r.Owner.Id == edge.Id && r.Kind == DrawingHitKind.Polyline);
        f.Click(new((line.Points[0].X + line.Points[1].X) / 2, (line.Points[0].Y + line.Points[1].Y) / 2));
        Assert.Equal(edge.Id, f.Canvas.SelectedStructuralTarget?.Id);
        Assert.DoesNotContain(f.Window.GetVisualDescendants().OfType<Control>(),
            c => c.IsEffectivelyVisible && AutomationProperties.GetName(c) is "Relationship label" or "Source marker" or "Target marker" or "Minimum length");
        WorkspaceControls.Find<ComboBox>(f.Window, "UML semantics").SelectedItem = UseCaseRelationshipKind.Association;
        Settle();
        var field = WorkspaceControls.Find<TextBox>(f.Window, "Relationship label");
        field.Focus(); field.Text = "calls";
        f.Key(PhysicalKey.Enter);
        var reopened = MerpeepsDocument.Load(f.Window.CurrentSource).UseCase!.Relationships.Single();
        Assert.Equal(UseCaseRelationshipKind.Association, reopened.Kind);
        Assert.Equal("calls", reopened.Label.Text);
    }

    [AvaloniaTheory]
    [InlineData("flowchart LR\nA[Alpha]\n")]
    [InlineData("stateDiagram-v2\nA\n")]
    [InlineData("classDiagram\nclass A\n")]
    [InlineData("erDiagram\nA\n")]
    public void DragUndoRestoresOriginalExplicitPositionIntent(string source)
    {
        using var f = new Fixture(source);
        var node = Assert.Single(f.Canvas.Model!.Nodes);
        var original = (node.X, node.Y, node.HasExplicitPosition);
        var before = f.Window.CurrentSource;
        var start = Center(node);
        f.Drag(start, new(start.X + 50, start.Y + 30));
        Assert.True(node.HasExplicitPosition);
        Assert.True(f.Canvas.History!.CanUndo);
        f.Canvas.Undo(); Settle();
        Assert.Equal(original, (node.X, node.Y, node.HasExplicitPosition));
        Assert.Equal(before, f.Window.CurrentSource);
        Assert.False(f.Canvas.History.CanUndo);
        f.Canvas.Redo(); Settle();
        Assert.Equal(original.X + 50, node.X, 6);
        Assert.True(node.HasExplicitPosition);
    }

    [AvaloniaTheory]
    [InlineData("flowchart LR\nA[Alpha]\n")]
    [InlineData("stateDiagram-v2\nA\n")]
    [InlineData("classDiagram\nclass A\n")]
    [InlineData("erDiagram\nA\n")]
    [InlineData("mindmap\nRoot\n A\n")]
    public void EscapeDuringDragRestoresTheOriginalPositionWithoutHistory(string source)
    {
        using var f = new Fixture(source);
        var before = f.Window.CurrentSource;
        var original = f.Canvas.Model!.Nodes.Select(n => (n.X, n.Y, n.HasExplicitPosition)).ToArray();
        var start = f.Canvas.Model.Outline is not null
            ? Center(f.Canvas.DataScene.Scene.Regions.First(r => r.Owner.Role == "Node").Bounds)
            : Center(f.Canvas.Model.Nodes[0]);
        f.Drag(start, new(start.X + 50, start.Y + 30), cancel: true);
        Assert.Equal(before, f.Window.CurrentSource);
        Assert.Equal(original, f.Canvas.Model.Nodes.Select(n => (n.X, n.Y, n.HasExplicitPosition)));
        Assert.False(f.Canvas.History!.CanUndo);
    }

    [AvaloniaTheory]
    [InlineData("erDiagram\nsubgraph G\nA\nend\n")]
    [InlineData("erDiagram\nsubgraph G\nsubgraph Nested\nA\nend\nend\n")]
    [InlineData("erDiagram\nsubgraph G\nend\n")]
    public void ErGroupDraggingMovesPopulatedNestedAndEmptyGroups(string source)
    {
        using var f = new Fixture(source);
        var group = f.Canvas.Model!.Subgraphs.Single(g => g.Id == "G");
        var before = EntityDiagramGeometry.GroupBounds(f.Canvas.Model, group);
        var start = new DiagramPoint(before.X + before.Width / 2, before.Y + 12);
        f.Drag(start, new(start.X + 50, start.Y + 30));
        Assert.Same(group, f.Canvas.SelectedEntityGroup);
        var after = EntityDiagramGeometry.GroupBounds(f.Canvas.Model, group);
        Assert.Equal(before with { X = before.X + 50, Y = before.Y + 30 }, after);
        Assert.True(f.Canvas.History!.CanUndo);
        var reopened = MerpeepsDocument.Load(f.Window.CurrentSource);
        Assert.Equal(after, EntityDiagramGeometry.GroupBounds(reopened, reopened.Subgraphs.Single(g => g.Id == "G")));
        f.Canvas.Undo(); Settle();
        Assert.Equal(before, EntityDiagramGeometry.GroupBounds(f.Canvas.Model, group));
        Assert.False(f.Canvas.History.CanUndo);
        f.Canvas.Redo(); Settle();
        Assert.Equal(after, EntityDiagramGeometry.GroupBounds(f.Canvas.Model, group));
    }

    [AvaloniaTheory]
    [InlineData("flowchart LR\nsubgraph G\nA[One]\nsubgraph Child\nB[Two]\nend\nsubgraph Empty\nend\nend\nC[Outside]\n")]
    [InlineData("stateDiagram-v2\nstate G {\nA\nstate Child {\nB\n}\nstate Empty {\n}\n}\nC\n")]
    [InlineData("classDiagram\nnamespace G {\nclass A\nnamespace Child {\nclass B\n}\nnamespace Empty {\n}\n}\nclass C\n")]
    [InlineData("erDiagram\nsubgraph G\nA\nsubgraph Child\nB\nend\nsubgraph Empty\nend\nend\nC\n")]
    public void NestedGroupGestureMovesDescendantsAtomicallyAndEscapeCancels(string source)
    {
        foreach (var cancel in new[] { false, true })
        {
            using var f = new Fixture(source);
            var model = f.Canvas.Model!;
            var group = model.Subgraphs.Single(g => g.Id == "G");
            var members = GraphGroupGeometry.Members(model, group);
            var original = model.Nodes.ToDictionary(n => n.Id, n => (n.X, n.Y, n.HasExplicitPosition));
            var before = f.Window.CurrentSource;
            var bounds = GraphGroupGeometry.Bounds(model, group);
            var start = new DiagramPoint(bounds.X + bounds.Width / 2, bounds.Y + 12);
            f.Drag(start, new(start.X + 60, start.Y - 30), cancel);
            if (cancel)
            {
                Assert.Equal(before, f.Window.CurrentSource);
                Assert.False(f.Canvas.History!.CanUndo);
            }
            else
            {
                Assert.Same(group, f.Canvas.SelectedGroup);
                Assert.Empty(f.Canvas.Selection);
                Assert.Null(f.Canvas.SelectedEdge);
                Assert.All(members, n =>
                {
                    Assert.Equal(original[n.Id].X + 60, n.X, 6);
                    Assert.Equal(original[n.Id].Y - 30, n.Y, 6);
                });
                Assert.Equal(original["C"], (model.Node("C")!.X, model.Node("C")!.Y, model.Node("C")!.HasExplicitPosition));
                var changed = f.Window.CurrentSource;
                f.Canvas.Undo(); Settle();
                Assert.Equal(before, f.Window.CurrentSource);
                Assert.False(f.Canvas.History!.CanUndo);
                f.Canvas.Redo(); Settle();
                Assert.Equal(changed, f.Window.CurrentSource);
                f.Canvas.Undo(); Settle();
            }
            Assert.All(model.Nodes, n => Assert.Equal(original[n.Id], (n.X, n.Y, n.HasExplicitPosition)));
            Assert.Empty(model.GroupLayouts);
        }
    }

    [AvaloniaTheory]
    [InlineData("flowchart LR\nsubgraph G\nend\n")]
    [InlineData("stateDiagram-v2\nstate G {\n}\n")]
    [InlineData("classDiagram\nnamespace G {\n}\n")]
    [InlineData("erDiagram\nsubgraph G\nend\n")]
    public void EmptyContainerCanBeSelectedMovedReopenedAndCancelled(string source)
    {
        using var f = new Fixture(source);
        var model = f.Canvas.Model!;
        var group = model.Subgraphs.Single();
        var before = f.Window.CurrentSource;
        var rect = GraphGroupGeometry.Bounds(model, group);
        var start = Center(rect);
        f.Drag(start, new(start.X + 80, start.Y + 40));
        Assert.Same(group, f.Canvas.SelectedGroup);
        Assert.Equal(rect with { X = rect.X + 80, Y = rect.Y + 40 }, GraphGroupGeometry.Bounds(model, group));
        var reopened = MerpeepsDocument.Load(f.Window.CurrentSource);
        Assert.Equal(GraphGroupGeometry.Bounds(model, group), GraphGroupGeometry.Bounds(reopened, reopened.Subgraphs.Single()));
        f.Canvas.Undo(); Settle();
        Assert.Equal(before, f.Window.CurrentSource);
        Assert.Empty(model.GroupLayouts);
    }

    [AvaloniaTheory]
    [InlineData("escape")]
    [InlineData("capture")]
    [InlineData("tool")]
    [InlineData("document")]
    public void InterruptedDragCannotCommitAfterItsPointerIsReleased(string interruption)
    {
        using var f = new Fixture("flowchart LR\nA[One]\n");
        var node = f.Canvas.Model!.Nodes.Single();
        var original = (node.X, node.Y, node.HasExplicitPosition);
        var before = f.Window.CurrentSource;
        IPointer? pointer = null;
        f.Canvas.PointerPressed += (_, e) => pointer = e.Pointer;
        var start = Center(node);
        f.BeginDrag(start);
        f.Move(new(start.X + 100, start.Y + 70));
        Assert.NotEqual(original.X, node.X);
        switch (interruption)
        {
            case "escape": f.Key(PhysicalKey.Escape); break;
            case "capture": Assert.NotNull(pointer); pointer.Capture(null); break;
            case "tool": f.Canvas.Tool = CanvasTool.Hand; break;
            case "document": f.Window.LoadSourceForTest("flowchart LR\nReplacement\n"); break;
        }
        f.Move(new(start.X + 150, start.Y + 90));
        f.EndDrag(new(start.X + 150, start.Y + 90));
        Assert.Equal(original, (node.X, node.Y, node.HasExplicitPosition));
        Assert.False(f.Canvas.History!.CanUndo);
        if (interruption != "document") Assert.Equal(before, f.Window.CurrentSource);
        else Assert.Equal("Replacement", f.Canvas.Model!.Nodes.Single().Id);
    }

    [AvaloniaFact]
    public void MultiNodeDragRestoresMixedPositionFlagsAndRecordsOnlyOneMove()
    {
        using var f = new Fixture("flowchart LR\nA[One] --> B[Two]\n");
        var model = f.Canvas.Model!;
        model.Node("A")!.HasExplicitPosition = true;
        var before = model.Nodes.Select(n => (n.X, n.Y, n.HasExplicitPosition)).ToArray();
        f.Canvas.SelectByIds(["A", "B"]);
        var start = Center(model.Node("A")!);
        f.BeginDrag(start);
        f.Move(new(start.X + 40, start.Y + 20));
        f.Move(new(start.X + 70, start.Y + 30));
        f.EndDrag(new(start.X + 70, start.Y + 30));
        Assert.All(model.Nodes.Select((n, i) => (n, i)), item => Assert.Equal(before[item.i].X + 70, item.n.X, 6));
        f.Canvas.Undo(); Settle();
        Assert.False(f.Canvas.History!.CanUndo);
        Assert.Equal(before, model.Nodes.Select(n => (n.X, n.Y, n.HasExplicitPosition)));
        f.Canvas.Redo(); Settle();
        Assert.All(model.Nodes, n => Assert.True(n.HasExplicitPosition));
    }

    [AvaloniaFact]
    public void ReturningDragToOriginDoesNotPinNodesOrAddHistory()
    {
        using var f = new Fixture("flowchart LR\nA[One]\n");
        var node = f.Canvas.Model!.Nodes.Single();
        var start = Center(node);
        f.BeginDrag(start);
        f.Move(new(start.X + 70, start.Y + 30));
        f.Move(start);
        f.EndDrag(start);
        Assert.False(node.HasExplicitPosition);
        Assert.False(f.Canvas.History!.CanUndo);
    }

    [AvaloniaFact]
    public void NamespaceSelectionShowsInlinePropertiesAndSurvivesRename()
    {
        using var f = new Fixture("classDiagram\nnamespace G {\nclass A\n}\n");
        var group = f.Canvas.Model!.Subgraphs.Single();
        var bounds = GraphGroupGeometry.Bounds(f.Canvas.Model, group);
        f.Click(new(bounds.X + 12, bounds.Y + 12));
        Assert.Same(group, f.Canvas.SelectedGroup);
        var field = WorkspaceControls.Find<TextBox>(f.Window, "Namespace display name");
        field.Focus(); field.Text = "Updated";
        f.Key(PhysicalKey.Enter);
        Assert.Equal("Updated", f.Canvas.SelectedGroup?.Title);
        Assert.Equal("Updated", MerpeepsDocument.Load(f.Window.CurrentSource).Subgraphs.Single().Title);
    }

    private static DiagramPoint Center(DiagramNode node) => new(node.X + node.Width / 2, node.Y + node.Height / 2);
    private static DiagramPoint Center(DiagramRect rect) => new(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
    private static void Settle() => Dispatcher.UIThread.RunJobs();

    private sealed class Fixture : IDisposable
    {
        public MainWindow Window { get; } = new() { Width = 1560, Height = 1000 };
        public EditorCanvas Canvas => Window.CanvasForTest;
        public Fixture(string source)
        {
            Window.Show(); Window.LoadSourceForTest(source); Settle();
            Assert.NotNull(Canvas.Model); Canvas.FitToView(); Settle();
        }
        private Point Screen(DiagramPoint point) =>
            Canvas.TranslatePoint(new(point.X * Canvas.Zoom + Canvas.OffsetX, point.Y * Canvas.Zoom + Canvas.OffsetY), Window)!.Value;
        public void Key(PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
        {
            Window.KeyPressQwerty(key, modifiers); Window.KeyReleaseQwerty(key, modifiers); Settle();
        }
        public void Click(DiagramPoint point)
        {
            var at = Screen(point);
            Window.MouseMove(at); Window.MouseDown(at, MouseButton.Left); Window.MouseUp(at, MouseButton.Left); Settle();
        }
        public void Drag(DiagramPoint start, DiagramPoint end, bool cancel = false)
        {
            BeginDrag(start);
            Move(end);
            if (cancel) Key(PhysicalKey.Escape);
            EndDrag(end);
        }
        public void BeginDrag(DiagramPoint point)
        {
            var at = Screen(point);
            Window.MouseMove(at); Window.MouseDown(at, MouseButton.Left);
        }
        public void Move(DiagramPoint point) => Window.MouseMove(Screen(point), RawInputModifiers.Alt);
        public void EndDrag(DiagramPoint point) { Window.MouseUp(Screen(point), MouseButton.Left); Settle(); }
        public void Dispose()
        {
            foreach (var child in Window.OwnedWindows.ToArray()) child.Close();
            Window.Close();
        }
    }
}
