using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Merpeeps.Core;
using Merpeeps.Core.Model;
using Xunit;

namespace Merpeeps.App.Tests;

public class ClassWorkspaceTests
{
    [AvaloniaTheory]
    [InlineData("classDiagram")]
    [InlineData("classDiagram-v2")]
    public void StandaloneClassInspectorCreatesMembersRelationsAndRoundtrips(string dialect)
    {
        var window = new MainWindow();
        window.Show();
        window.LoadSourceForTest(dialect + "\n");
        Settle();
        try
        {
            var panel = window.FindControl<Border>("ClassInspectorHost")!.Child!;
            Assert.True(window.FindControl<Border>("ClassInspectorHost")!.IsVisible);
            Assert.True(window.FindControl<DockPanel>("NodePane")!.IsVisible);
            WorkspaceControls.OpenCreationForm(window);
            Field<TextBox>(panel, "Class identity").Text = "Account";
            Field<TextBox>(panel, "Display name (optional)").Text = "Customer account";
            Click(panel, "Create class");
            var model = window.CanvasForTest.Model!;
            Assert.NotNull(model.Node("Account")!.Class);
            Click(panel, "Add attribute");
            Field<TextBox>(panel, "Signature").Text = "String owner";
            Field<ComboBox>(panel, "Visibility").SelectedItem = ClassVisibility.Public;
            Click(panel, "Apply member");
            Click(panel, "Add method");
            Field<TextBox>(panel, "Signature").Text = "save() bool";
            Click(panel, "Apply member");
            Assert.Equal(2, model.Node("Account")!.Class!.Members.Count);
            window.FindControl<MenuItem>("MenuClassAdd")!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Settle();
            Field<TextBox>(panel, "Class identity").Text = "Order";
            Click(panel, "Create class");
            ClickNode(window, model.Node("Account")!);
            Click(panel, "Add relationship");
            ClickNode(window, model.Node("Order")!);
            var relation = Assert.Single(model.Edges);
            Assert.Same(relation, window.CanvasForTest.SelectedEdge);
            Assert.Equal("Connect Account to Order", window.CanvasForTest.History!.NextUndo);
            var types = Field<ComboBox>(panel, "Relationship type");
            types.SelectedItem = types.Items.Cast<object>().Single(i => i.ToString() == "Composition");
            Settle();
            Field<TextBox>(panel, "Multiplicity at Customer account").Text = "1";
            Field<TextBox>(panel, "Multiplicity at Order").Text = "*";
            Field<TextBox>(panel, "Relationship label").Text = "owns";
            WorkspaceControls.CommitProperties(window);
            var source = window.CurrentSource;
            var reopened = MerpeepsDocument.Load(source);
            Assert.Equal(dialect, reopened.Kind);
            Assert.Equal(2, reopened.Node("Account")!.Class!.Members.Count);
            Assert.Equal(
                model.Nodes.Select(n => (n.Id, n.X, n.Y, n.Width, n.Height)),
                reopened.Nodes.Select(n => (n.Id, n.X, n.Y, n.Width, n.Height)));
            Assert.Equal(ClassRelationEnd.Composition, reopened.Edges.Single().ClassRelationship!.FromEnd);
            Assert.Equal("*", reopened.Edges.Single().ClassRelationship!.ToMultiplicity);
            window.CanvasForTest.Undo();
            Assert.Equal(ClassRelationEnd.Composition, model.Edges.Single().ClassRelationship!.FromEnd);
            window.CanvasForTest.Undo();
            Assert.Equal(ClassRelationEnd.None, model.Edges.Single().ClassRelationship!.FromEnd);
            window.CanvasForTest.Undo();
            Assert.Empty(model.Edges);
            window.CanvasForTest.Redo();
            Assert.Single(model.Edges);
            using var image = window.CaptureRenderedFrame();
            Assert.NotNull(image);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void NewMenuOpensTypeChooserWithoutReplacingCurrentDocumentOnCancel()
    {
        var window = new MainWindow();
        window.Show();
        window.LoadSourceForTest("flowchart LR\nA-->B\n");
        var before = window.CurrentSource;
        try
        {
            var peer = ControlAutomationPeer.CreatePeerForElement(window.FindControl<MenuItem>("MenuNew")!);
            var invoke = peer!.GetProvider<IInvokeProvider>();
            Assert.NotNull(invoke);
            invoke.Invoke();
            Settle();
            var gallery = window.FindControl<Border>("NewDiagramHost")!;
            Assert.True(gallery.IsVisible);
            Assert.Empty(window.OwnedWindows);
            Field<Button>(gallery, "Back to diagram").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Settle();
            Assert.Equal(before, window.CurrentSource);

            invoke.Invoke();
            Settle();
            Field<Button>(gallery, "Create Class diagram").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Settle();
            Assert.Equal("classDiagram", window.CanvasForTest.Model!.Kind);
            Assert.Empty(window.CanvasForTest.Model.Nodes);
            Assert.Empty(window.CanvasForTest.Model.Edges);
            Assert.False(window.CanvasForTest.History!.CanUndo);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ClassGeometryIsNotRemeasuredWhenOpenedOrThemeChanges()
    {
        var source = "classDiagram\nclass A {\n+String name\n+save() bool\n}\n";
        var model = MerpeepsDocument.Load(source);
        model.Nodes[0].Width = 350;
        model.Nodes[0].Height = 180;
        source = MerpeepsDocument.SavePositions(source, model);
        var window = new MainWindow();
        window.Show();
        try
        {
            window.LoadSourceForTest(source);
            window.CanvasForTest.ShowMerpeepsStyling = false;
            window.CanvasForTest.MeasureLabels();
            Assert.Equal(350, window.CanvasForTest.Model!.Nodes[0].Width);
            Assert.Equal(180, window.CanvasForTest.Model.Nodes[0].Height);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void UnsupportedClassStyleExplainsExportBeforeOpeningAFilePicker()
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            window.LoadSourceForTest("classDiagram\nclass A\nstyle A opacity:0.5\n");
            var source = window.CurrentSource;
            window.FindControl<MenuItem>("MenuExportSvg")!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Settle();
            var dialog = Assert.Single(window.OwnedWindows);
            Assert.Equal("SVG export unavailable", dialog.Title);
            Assert.Equal(source, window.CurrentSource);
            dialog.Close();
        }
        finally { window.Close(); }
    }

    private static T Field<T>(Avalonia.Visual root, string name) where T : Control =>
        root.GetVisualDescendants().OfType<T>().Single(c => c.IsEffectivelyVisible && AutomationProperties.GetName(c) == name);
    private static void Click(Avalonia.Visual root, string name)
    {
        Settle();
        Field<Button>(root, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Settle();
    }
    private static void ClickNode(MainWindow window, DiagramNode node)
    {
        var canvas = window.CanvasForTest;
        var point = canvas.TranslatePoint(new Point(
            (node.X + node.Width / 2) * canvas.Zoom + canvas.OffsetX,
            (node.Y + node.Height / 2) * canvas.Zoom + canvas.OffsetY), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Settle();
    }
    private static void Settle() => Dispatcher.UIThread.RunJobs();
}
