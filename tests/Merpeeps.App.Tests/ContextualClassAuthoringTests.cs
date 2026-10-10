using Avalonia;
using Avalonia.Automation;
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

public class ContextualClassAuthoringTests
{
    private const string Source = """
        ---
        x-merpeeps:
          spec: 1
          layout:
            nodes:
              A: { x: 40, y: 50, w: 210, h: 110 }
              B: { x: 400, y: 50, w: 210, h: 110 }
              N: { x: 40, y: 260, w: 210, h: 110 }
        ---
        classDiagram
        class A
        class B
        class N
        """;

    [AvaloniaFact]
    public void SelectionShowsOnlyItsOwnToolsAndPointerChoosesTheRelationship()
    {
        using var fixture = new Fixture();
        Assert.Empty(fixture.Buttons());
        Assert.NotNull(WorkspaceControls.Find<Button>(fixture.Window, "Add class"));
        Assert.Empty(fixture.Fields());
        fixture.ClickNode("A");
        Assert.Contains("Add attribute", fixture.Buttons());
        Assert.Contains("Add method", fixture.Buttons());
        Assert.Contains("Add relationship", fixture.Buttons());
        Assert.DoesNotContain("Add class", fixture.Buttons());
        Assert.DoesNotContain("From class", fixture.Fields());
        Assert.Contains("Class name", fixture.Fields());
        var before = fixture.Window.CurrentSource;
        fixture.Click("Add relationship");
        Assert.Same(fixture.Model.Node("A"), fixture.Canvas.ClassRelationshipSource);
        Assert.Equal(before, fixture.Window.CurrentSource);
        Assert.Equal(new[] { "Cancel relationship" }, fixture.Buttons());
        fixture.ClickNode("B");
        var edge = Assert.Single(fixture.Model.Edges);
        Assert.Equal("A", edge.From);
        Assert.Equal("B", edge.To);
        Assert.Same(edge, fixture.Canvas.SelectedEdge);
        Assert.Contains("Relationship label", fixture.Fields());
        Assert.DoesNotContain("Class identity", fixture.Fields());
        Assert.DoesNotContain("Add method", fixture.Buttons());
        fixture.ClickNode("A");
        Assert.Contains("Add method", fixture.Buttons());
        Assert.DoesNotContain("Relationship label", fixture.Fields());
        fixture.ClickWorld(325, 105);
        Assert.Same(edge, fixture.Canvas.SelectedEdge);
        Assert.Contains("Delete relationship", fixture.Buttons());
        fixture.ClickBlank();
        Assert.Null(fixture.Canvas.SelectedEdge);
        Assert.Null(fixture.Canvas.Selected);
        Assert.Empty(fixture.Buttons());
        fixture.Canvas.Undo();
        Assert.Empty(fixture.Model.Edges);
    }

    [AvaloniaFact]
    public void EscapeInvalidTargetsAndHistoryChangesDoNotCreateEdges()
    {
        using var fixture = new Fixture();
        fixture.ClickNode("A");
        fixture.Click("Add relationship");
        fixture.ClickNode("A");
        Assert.Empty(fixture.Model.Edges);
        Assert.NotNull(fixture.Canvas.ClassRelationshipSource);
        fixture.Key(PhysicalKey.Escape);
        Assert.Null(fixture.Canvas.ClassRelationshipSource);
        Assert.Equal("A", fixture.Canvas.Selected!.Id);
        Assert.False(fixture.Canvas.History!.CanUndo);
        fixture.Click("Add relationship");
        fixture.ClickBlank();
        Assert.Null(fixture.Canvas.ClassRelationshipSource);
        Assert.Null(fixture.Canvas.Selected);
        fixture.ClickNode("A");
        fixture.Click("Add relationship");
        fixture.Canvas.Undo();
        Assert.Null(fixture.Canvas.ClassRelationshipSource);
        Assert.Empty(fixture.Model.Edges);
    }

    [AvaloniaFact]
    public void KeyboardTargetingUsesTheSameSingleUndoConnection()
    {
        using var fixture = new Fixture();
        fixture.ClickNode("A");
        fixture.Click("Add relationship");
        fixture.Key(PhysicalKey.Tab);
        fixture.Key(PhysicalKey.Enter);
        Assert.Equal("B", Assert.Single(fixture.Model.Edges).To);
        fixture.Canvas.Undo();
        Assert.Empty(fixture.Model.Edges);
        Assert.False(fixture.Canvas.History!.CanUndo);
    }

    [AvaloniaFact]
    public void MemberDraftSurvivesSelectionAndIsNeverAppliedToAnotherClass()
    {
        using var fixture = new Fixture();
        fixture.ClickNode("A");
        fixture.Click("Add attribute");
        fixture.Field<TextBox>("Signature").Text = "String owner";
        Assert.False(fixture.Window.FindControl<Button>("SaveBtn")!.IsEnabled);
        fixture.ClickNode("B");
        Assert.Empty(fixture.Model.Node("B")!.Class!.Members);
        Assert.DoesNotContain("Signature", fixture.Fields());
        fixture.ClickNode("A");
        Assert.Equal("String owner", fixture.Field<TextBox>("Signature").Text);
        fixture.Click("Apply member");
        Assert.Equal("String owner", Assert.Single(fixture.Model.Node("A")!.Class!.Members).Signature);
        Assert.Empty(fixture.Model.Node("B")!.Class!.Members);
        Assert.True(fixture.Window.FindControl<Button>("SaveBtn")!.IsEnabled);
        Assert.DoesNotContain("Signature", fixture.Fields());
        fixture.Canvas.Undo();
        Assert.Empty(fixture.Model.Node("A")!.Class!.Members);
        fixture.Click("Add method");
        fixture.Field<TextBox>("Signature").Text = "save() bool";
        fixture.Click("Cancel edit");
        Assert.Empty(fixture.Model.Node("A")!.Class!.Members);
    }

    [AvaloniaFact]
    public void RelationshipDraftSurvivesSelectionAndReverseIsOneUndo()
    {
        using var fixture = new Fixture();
        fixture.ClickNode("A"); fixture.Click("Add relationship"); fixture.ClickNode("B");
        var edge = fixture.Model.Edges.Single();
        fixture.Field<TextBox>("Relationship label").Text = "pending";
        fixture.ClickNode("N");
        Assert.Equal("pending", edge.Label);
        fixture.ClickWorld(325, 105);
        Assert.Equal("pending", fixture.Field<TextBox>("Relationship label").Text);
        fixture.Click("Reverse direction");
        var reversed = fixture.Model.Edges.Single();
        Assert.Equal("B", reversed.From);
        Assert.Equal("A", reversed.To);
        Assert.Equal("pending", reversed.Label);
        fixture.Canvas.Undo();
        Assert.Same(edge, fixture.Model.Edges.Single());
        Assert.Equal("pending", edge.Label);
        fixture.Canvas.Undo();
        Assert.Null(edge.Label);
    }

    [AvaloniaFact]
    public void DiagramMenusKeepGlobalTasksOutsideTheSelectionPanel()
    {
        using var fixture = new Fixture();
        fixture.InvokeMenu("MenuClassDisplay");
        Assert.Contains("Apply display options", fixture.Buttons());
        Assert.DoesNotContain("Class identity", fixture.Fields());
        fixture.Click("Cancel edit");
        fixture.InvokeMenu("MenuClassNamespaces");
        Assert.Contains("Add namespace", fixture.Buttons());
        Assert.DoesNotContain("Signature", fixture.Fields());
        fixture.Click("Add namespace");
        fixture.Field<TextBox>("Qualified namespace identity").Text = "Domain";
        fixture.Click("Apply namespace");
        Assert.Single(fixture.Model.Subgraphs);
        fixture.ClickNode("A");
        Assert.DoesNotContain("Apply namespace", fixture.Buttons());
    }

    [AvaloniaFact]
    public void InvalidMemberDraftSurvivesSelectionAndEscapeCancelsOnlyTheDraft()
    {
        using var fixture = new Fixture();
        fixture.ClickNode("A");
        fixture.Click("Add method");
        fixture.Field<TextBox>("Signature").Text = "missing parameters";
        var source = fixture.Window.CurrentSource;
        fixture.Click("Apply member");
        Assert.Empty(fixture.Model.Node("A")!.Class!.Members);
        Assert.Equal(source, fixture.Window.CurrentSource);
        Assert.Equal("missing parameters", fixture.Field<TextBox>("Signature").Text);
        fixture.ClickNode("B");
        fixture.ClickNode("A");
        Assert.Equal("missing parameters", fixture.Field<TextBox>("Signature").Text);
        fixture.Field<TextBox>("Signature").RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
        fixture.SettleNow();
        Assert.DoesNotContain("Signature", fixture.Fields());
        Assert.False(fixture.Canvas.History!.CanUndo);
        Assert.True(fixture.Window.FindControl<Button>("SaveBtn")!.IsEnabled);
    }

    [AvaloniaFact]
    public void ReconnectPreservesUnappliedRelationDraftAndChangesOnlyOneHistoryStep()
    {
        using var fixture = new Fixture();
        fixture.ClickNode("A"); fixture.Click("Add relationship"); fixture.ClickNode("B");
        var original = fixture.Model.Edges.Single();
        fixture.Field<TextBox>("Relationship label").Text = "Draft label";
        fixture.Menu("Reconnect…", "Change target…");
        fixture.ClickNode("N");
        var replacement = fixture.Model.Edges.Single();
        Assert.Equal("N", replacement.To);
        Assert.Equal("Draft label", replacement.Label);
        Assert.Equal("Draft label", fixture.Field<TextBox>("Relationship label").Text);
        fixture.Click("Apply relationship");
        Assert.Equal("Draft label", replacement.Label);
        fixture.Canvas.Undo();
        Assert.Same(original, fixture.Model.Edges.Single());
        fixture.Canvas.SelectEdge(original);
        fixture.SettleNow();
        Assert.Equal("Draft label", fixture.Field<TextBox>("Relationship label").Text);
        fixture.Canvas.Undo();
        Assert.Same(original, fixture.Model.Edges.Single());
        Assert.Null(original.Label);
    }

    [AvaloniaFact]
    public void ModelReplacementCancelsConnectionAndClearsOnlyTheOldDocumentDrafts()
    {
        using var fixture = new Fixture();
        fixture.ClickNode("A");
        fixture.Click("Add attribute");
        fixture.Field<TextBox>("Signature").Text = "String pending";
        fixture.ClickNode("B");
        fixture.Click("Add relationship");
        fixture.Window.LoadSourceForTest("classDiagram\nclass Fresh\n");
        Assert.Null(fixture.Canvas.ClassRelationshipSource);
        Assert.Empty(fixture.Buttons());
        Assert.True(fixture.Window.FindControl<Button>("SaveBtn")!.IsEnabled);
        Assert.False(fixture.Canvas.History!.CanUndo);
    }

    [AvaloniaFact]
    public void InterfaceRelationshipIsSelectedOnItsActuallyDrawnLine()
    {
        using var fixture = new Fixture("classDiagram\nclass A\nA --() Port\n");
        var edge = fixture.Model.Edges.Single();
        var from = fixture.Model.Node(edge.From)!;
        var to = fixture.Model.Node(edge.To)!;
        from.X = 40; from.Y = 50; from.Width = 210; from.Height = 110;
        to.X = 400; to.Y = 50; to.Width = 100; to.Height = 160;
        fixture.Canvas.FitToView();
        var start = ClassDiagramGeometry.Anchor(from, ClassDiagramGeometry.Center(to));
        var end = ClassDiagramGeometry.Anchor(to, ClassDiagramGeometry.Center(from));
        fixture.ClickWorld((start.X + end.X) / 2, (start.Y + end.Y) / 2);
        Assert.Same(edge, fixture.Canvas.SelectedEdge);
        Assert.Contains("Relationship label", fixture.Fields());
        Assert.DoesNotContain("Relationship type", fixture.Fields());
    }

    [AvaloniaTheory]
    [InlineData(1280, 720, false)]
    [InlineData(1280, 720, true)]
    [InlineData(1600, 900, false)]
    [InlineData(1600, 900, true)]
    public void ContextualStatesStayBoundedAndThemeChangesRetainMemberCaret(int width, int height, bool dark)
    {
        using var fixture = new Fixture();
        fixture.Window.Width = width;
        fixture.Window.Height = height;
        fixture.Window.RequestedThemeVariant = dark ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light;
        fixture.SettleNow();
        fixture.Canvas.FitToView();
        Capture("empty");
        fixture.ClickNode("A");
        Capture("class");
        fixture.Click("Add method");
        var input = fixture.Field<TextBox>("Signature");
        input.Text = "save() bool";
        input.CaretIndex = 4;
        input.Focus();
        fixture.Window.RequestedThemeVariant = dark ? Avalonia.Styling.ThemeVariant.Light : Avalonia.Styling.ThemeVariant.Dark;
        fixture.SettleNow();
        Assert.Same(input, fixture.Field<TextBox>("Signature"));
        Assert.Equal(4, input.CaretIndex);
        Assert.Equal("save() bool", input.Text);
        fixture.Window.RequestedThemeVariant = dark ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light;
        fixture.SettleNow();
        Capture("member");
        fixture.Click("Cancel edit");
        fixture.Click("Add relationship");
        fixture.ClickNode("B");
        Capture("relationship");
        var pane = fixture.Window.FindControl<ScrollViewer>("PropertyPane")!;
        var fields = fixture.Window.FindControl<Border>("ClassInspectorHost")!;
        Assert.True(fields.Bounds.Height <= pane.Bounds.Height + 1,
            $"The default relationship inspector needs scrolling: {fields.Bounds.Height} > {pane.Bounds.Height}.");

        void Capture(string state)
        {
            var path = Environment.GetEnvironmentVariable("MERPEEPS_CONTEXT_CAPTURE_DIR");
            if (string.IsNullOrEmpty(path)) return;
            Directory.CreateDirectory(path);
            using var frame = fixture.Window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            frame.Save(Path.Combine(path, $"{width}-{height}-{(dark ? "dark" : "light")}-{state}.png"));
        }
    }

    [AvaloniaFact]
    public void CloseCannotSilentlyDiscardAnUnappliedMember()
    {
        using var fixture = new Fixture();
        fixture.ClickNode("A"); fixture.Click("Add attribute");
        fixture.Field<TextBox>("Signature").Text = "String pending";
        fixture.Window.Close();
        fixture.SettleNow();
        Assert.True(fixture.Window.IsVisible);
        var dialog = Assert.Single(fixture.Window.OwnedWindows);
        Assert.Equal("Unapplied inspector edits", dialog.Title);
        dialog.Close();
        fixture.SettleNow();
        Assert.Equal("String pending", fixture.Field<TextBox>("Signature").Text);
        fixture.Click("Cancel edit");
    }

    private sealed class Fixture : IDisposable
    {
        public MainWindow Window { get; } = new();
        public EditorCanvas Canvas => Window.CanvasForTest;
        public DiagramModel Model => Canvas.Model!;
        private Control Panel => Window.FindControl<Border>("ClassInspectorHost")!.Child!;
        public Fixture(string source = Source)
        {
            Window.Show();
            Window.LoadSourceForTest(source);
            Window.ApplyTemplate();
            Settle();
            Canvas.FitToView();
        }
        public IEnumerable<string> Fields() => Panel.GetVisualDescendants()
            .Where(c => c is TextBox or ComboBox).OfType<Control>()
            .Where(c => c.IsEffectivelyVisible).Select(c => AutomationProperties.GetName(c) ?? "");
        public string[] Buttons() => Panel.GetVisualDescendants().OfType<Button>()
            .Where(c => c.IsEffectivelyVisible).Select(c => AutomationProperties.GetName(c) ?? "").ToArray();
        public T Field<T>(string name) where T : Control => Panel.GetVisualDescendants().OfType<T>()
            .Single(c => c.IsEffectivelyVisible && AutomationProperties.GetName(c) == name);
        public void Click(string name)
        {
            WorkspaceControls.Click(Window, name);
        }
        public void Menu(string buttonName, string command)
        {
            var button = Field<Button>(buttonName);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Settle();
            button = Field<Button>(buttonName);
            var item = button.ContextMenu!.Items.Cast<MenuItem>().Single(i => (string?)i.Header == command);
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            button.ContextMenu.Close();
            Settle();
        }
        public void InvokeMenu(string name)
        {
            Window.FindControl<MenuItem>(name)!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Settle();
        }
        public void ClickNode(string id)
        {
            var node = Model.Node(id)!;
            ClickWorld(node.X + node.Width / 2, node.Y + node.Height / 2);
        }
        public void ClickWorld(double x, double y)
        {
            var point = Canvas.TranslatePoint(new Point(x * Canvas.Zoom + Canvas.OffsetX, y * Canvas.Zoom + Canvas.OffsetY), Window)!.Value;
            Window.MouseMove(point);
            Window.MouseDown(point, MouseButton.Left);
            Window.MouseUp(point, MouseButton.Left);
            Settle();
        }
        public void ClickBlank()
        {
            var point = Canvas.TranslatePoint(new Point(Canvas.Bounds.Width - 10, Canvas.Bounds.Height - 10), Window)!.Value;
            Window.MouseDown(point, MouseButton.Left);
            Window.MouseUp(point, MouseButton.Left);
            Settle();
        }
        public void Key(PhysicalKey key)
        {
            Canvas.Focus();
            Window.KeyPressQwerty(key, RawInputModifiers.None);
            Window.KeyReleaseQwerty(key, RawInputModifiers.None);
            Settle();
        }
        public void Dispose() => Window.Close();
        public void SettleNow() => Settle();
        private static void Settle() => Dispatcher.UIThread.RunJobs();
    }
}
