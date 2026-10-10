using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Merpeeps.Core;
using Xunit;

namespace Merpeeps.App.Tests;

public class WeightedWorkspaceTests
{
    [AvaloniaFact]
    public void WeightedFlowCreatesEndpointsAndEditsWeightWithoutGraphNodes()
    {
        using var f = new Fixture("sankey\n");
        WorkspaceControls.OpenCreationForm(f.Window);
        f.Field<TextBox>("Source label").Text = "Power";
        f.Field<TextBox>("Target label").Text = "Industry";
        f.Field<TextBox>("Flow value").Text = "12.5";
        f.Click("Apply weighted flow");
        var model = f.Window.CanvasForTest.Model!;
        Assert.Equal(2, model.Sankey!.Nodes.Count);
        Assert.Single(model.Sankey.Links);
        Assert.Empty(model.Nodes);
        f.Field<TextBox>("Flow value").Text = "20";
        f.Click("Apply weighted flow");
        Assert.Equal(20, MerpeepsDocument.Load(f.Window.CurrentSource).Sankey!.Links[0].Value);
        f.Window.CanvasForTest.Undo(); f.Settle();
        Assert.Equal(12.5, model.Sankey!.Links[0].Value);
    }
    [AvaloniaFact]
    public void WeightedHierarchyKeepsLeafValueAndParentTotal()
    {
        using var f = new Fixture("treemap-beta\n");
        WorkspaceControls.OpenCreationForm(f.Window);
        f.Field<TextBox>("Node label").Text = "Budget";
        f.Click("Apply weighted node");
        f.Click("Add child");
        f.Field<TextBox>("Node label").Text = "Staff";
        f.Field<TextBox>("Leaf value (blank is section)").Text = "700";
        f.Click("Apply weighted node");
        var root = f.Window.CanvasForTest.Model!.Treemap!.Roots[0];
        Assert.Equal(700, root.Total);
        var saved = MerpeepsDocument.Load(f.Window.CurrentSource).Treemap!;
        Assert.Equal(root.Children[0].Id, saved.Roots[0].Children[0].Id);
        Assert.Equal(700, saved.Roots[0].Children[0].Value);
    }
    private sealed class Fixture : IDisposable
    {
        public MainWindow Window { get; } = new();
        private Border Panel => Window.FindControl<Border>("ClassInspectorHost")!;
        public Fixture(string source) { Window.Show(); Window.LoadSourceForTest(source); Settle(); }
        public T Field<T>(string name) where T : Control => WorkspaceControls.Find<T>(Window, name);
        public void Click(string name) => WorkspaceControls.Click(Window, name);
        public void Settle() => Dispatcher.UIThread.RunJobs();
        public void Dispose() { foreach (var child in Window.OwnedWindows.ToArray()) child.Close(); Window.Close(); }
    }
}
