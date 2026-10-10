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

public class EntityWorkspaceTests
{
    [AvaloniaFact]
    public void GalleryAndContextualControlsCreateRealEntitiesAttributesAndRelationship()
    {
        using var fixture = new Fixture();
        var tile = fixture.Window.GetVisualDescendants().OfType<Button>()
            .Single(b => AutomationProperties.GetName(b) == "Create Entity relationship");
        tile.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        fixture.Settle();
        Assert.Equal("erDiagram", fixture.Canvas.Model!.Kind);
        Assert.Empty(fixture.Canvas.Model.Nodes);
        fixture.AddEntity("Customer");
        fixture.Click("Add attribute");
        fixture.Field<TextBox>("Data type").Text = "string?";
        fixture.Field<TextBox>("Attribute name").Text = "email";
        fixture.Field<CheckBox>("Unique key (UK)").IsChecked = true;
        fixture.Click("Apply attribute");
        var customer = fixture.Canvas.Selected!;
        var attribute = Assert.Single(customer.Entity!.Attributes);
        Assert.Equal(EntityKey.Unique, attribute.Keys);
        fixture.Window.FindControl<MenuItem>("MenuClassAdd")!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        fixture.Settle();
        fixture.Field<TextBox>("Entity identity").Text = "Order";
        fixture.Click("Create entity");
        var order = fixture.Canvas.Selected!;
        customer.X = 0; customer.Y = 0; order.X = 460; order.Y = 0;
        fixture.Canvas.FitToView();
        fixture.ClickNode(customer);
        fixture.Click("Add relationship");
        fixture.ClickNode(order);
        var edge = Assert.Single(fixture.Canvas.Model.Edges);
        Assert.Same(edge, fixture.Canvas.SelectedEdge);
        fixture.Field<ComboBox>("Target cardinality: Order").SelectedItem = EntityCardinality.OneOrMore;
        fixture.Field<CheckBox>("Identifying (solid line)").IsChecked = false;
        fixture.Field<TextBox>("Relationship role").Text = "places";
        fixture.Click("Apply ER relationship");
        var reopened = MerpeepsDocument.Load(fixture.Window.CurrentSource);
        Assert.Equal("erDiagram", reopened.Kind);
        Assert.Equal("string?", reopened.Node("Customer")!.Entity!.Attributes[0].Type);
        Assert.Equal(EntityCardinality.OneOrMore, reopened.Edges.Single().EntityRelationship!.ToCardinality);
        Assert.False(reopened.Edges.Single().EntityRelationship!.Identifying);
        Assert.Equal("places", reopened.Edges.Single().Label);
        Assert.Equal(fixture.Canvas.Model.Nodes.Select(n => (n.X, n.Y, n.Width, n.Height)),
            reopened.Nodes.Select(n => (n.X, n.Y, n.Width, n.Height)));
        fixture.Canvas.Undo();
        Assert.Equal(EntityCardinality.ZeroOrMore, edge.EntityRelationship!.ToCardinality);
    }

    [AvaloniaFact]
    public void ImportedStarKeyIsVisibleAndCanBeIntentionallyCleared()
    {
        using var fixture = new Fixture("erDiagram\nA {\nint *id\n}\n");
        var node = fixture.Canvas.Model!.Nodes.Single();
        fixture.ClickNode(node);
        fixture.Click("int id");
        Assert.True(fixture.Field<CheckBox>("Primary key (PK)").IsChecked);
        Assert.Equal("id", fixture.Field<TextBox>("Attribute name").Text);
        fixture.Field<CheckBox>("Primary key (PK)").IsChecked = false;
        fixture.Click("Apply attribute");
        var reopened = MerpeepsDocument.Load(fixture.Window.CurrentSource);
        Assert.Equal(EntityKey.None, reopened.Nodes.Single().Entity!.Attributes.Single().EffectiveKeys);
        fixture.Canvas.Undo();
        Assert.Equal(EntityKey.Primary, node.Entity!.Attributes.Single().EffectiveKeys);
    }

    [AvaloniaFact]
    public void GroupEndpointsAndSelfRoutesAreSelectableOnActualDrawingSegments()
    {
        using var fixture = new Fixture("erDiagram\nsubgraph G\nA\nend\nB\nG ||--o{ B : group\nB |o..o{ B : parent\n");
        var model = fixture.Canvas.Model!;
        fixture.Canvas.FitToView();
        foreach (var edge in model.Edges)
        {
            var route = EntityDiagramGeometry.Route(model, edge);
            var segment = route.Count > 2 ? 1 : 0;
            var point = new DiagramPoint((route[segment].X + route[segment + 1].X) / 2,
                (route[segment].Y + route[segment + 1].Y) / 2);
            fixture.ClickWorld(point);
            Assert.Same(edge, fixture.Canvas.SelectedEdge);
            Assert.NotNull(fixture.Field<TextBox>("Relationship role"));
        }
    }

    [AvaloniaFact]
    public void BadAttributesStayInTheirDraftAndDoNotAlterAnotherEntity()
    {
        using var fixture = new Fixture("erDiagram\nA\nB\n");
        var model = fixture.Canvas.Model!;
        fixture.ClickNode(model.Node("A")!);
        fixture.Click("Add attribute");
        fixture.Field<TextBox>("Data type").Text = "123";
        fixture.Field<TextBox>("Attribute name").Text = "id";
        fixture.Click("Apply attribute");
        Assert.Empty(model.Node("A")!.Entity!.Attributes);
        fixture.ClickNode(model.Node("B")!);
        Assert.Empty(model.Node("B")!.Entity!.Attributes);
        fixture.ClickNode(model.Node("A")!);
        Assert.Equal("123", fixture.Field<TextBox>("Data type").Text);
        fixture.Click("Cancel edit");
    }

    private sealed class Fixture : IDisposable
    {
        public MainWindow Window { get; } = new();
        public EditorCanvas Canvas => Window.CanvasForTest;
        private Border Panel => Window.FindControl<Border>("ClassInspectorHost")!;
        public Fixture(string? source = null)
        {
            Window.Show();
            if (source is not null) Window.LoadSourceForTest(source);
            Settle();
            if (source is not null) Canvas.FitToView();
        }
        public T Field<T>(string name) where T : Control => WorkspaceControls.Find<T>(Window, name);
        public void Click(string name) => WorkspaceControls.Click(Window, name);
        public void AddEntity(string name)
        {
            WorkspaceControls.OpenCreationForm(Window);
            Field<TextBox>("Entity identity").Text = name;
            Click("Create entity");
        }
        public void ClickNode(DiagramNode node) => ClickWorld(new(node.X + node.Width / 2, node.Y + node.Height / 2));
        public void ClickWorld(DiagramPoint point)
        {
            var at = Canvas.TranslatePoint(new(point.X * Canvas.Zoom + Canvas.OffsetX, point.Y * Canvas.Zoom + Canvas.OffsetY), Window)!.Value;
            Window.MouseDown(at, MouseButton.Left); Window.MouseUp(at, MouseButton.Left); Settle();
        }
        public void Settle() => Dispatcher.UIThread.RunJobs();
        public void Dispose() => Window.Close();
    }
}
