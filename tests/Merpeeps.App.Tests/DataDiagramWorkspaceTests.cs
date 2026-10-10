using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Merpeeps.Core;
using Merpeeps.Core.Model;
using Xunit;

namespace Merpeeps.App.Tests;

public class DataDiagramWorkspaceTests
{
    [AvaloniaTheory]
    [InlineData("timeline")]
    [InlineData("journey")]
    [InlineData("mindmap")]
    [InlineData("treeView-beta")]
    public void FourFamiliesUseTheirOwnTypedModelsAndOptions(string directive)
    {
        using var fixture = new Fixture(directive + "\n");
        Assert.Empty(fixture.Model.Nodes); Assert.Empty(fixture.Model.Edges);
        Assert.Null(fixture.Model.Chart); Assert.Null(fixture.Model.Sequence);
        Assert.True(fixture.Model.Outline is not null || fixture.Model.Temporal is not null);
        Assert.False(fixture.Window.FindControl<ComboBox>("EngineBox")!.IsVisible);
    }

    [AvaloniaFact]
    public void MindmapAddsRootAndChildWithContextualShapeAndMarkdown()
    {
        using var fixture = new Fixture("mindmap\n");
        WorkspaceControls.OpenCreationForm(fixture.Window);
        fixture.Field<TextBox>("Node label").Text = "Root";
        fixture.Click("Apply node");
        var root = fixture.Model.Outline!.Roots[0];
        fixture.Click("Add child");
        fixture.Field<TextBox>("Node label").Text = "**Idea**";
        fixture.Field<ComboBox>("Node shape").SelectedItem = OutlineShape.Square;
        fixture.Field<CheckBox>("Markdown label").IsChecked = true;
        fixture.Click("Apply node");
        Assert.Single(fixture.Model.Outline!.Roots[0].Children);
        var source = fixture.Window.CurrentSource;
        var reopened = MerpeepsDocument.Load(source).Outline!;
        Assert.Equal(root.Id, reopened.Roots[0].Id);
        Assert.True(reopened.Roots[0].Children[0].Markdown);
        Assert.Contains(fixture.Canvas.DataScene.Scene.Labels, l => l.Text == "Idea" && l.Font.Weight == 700);
        fixture.Canvas.Undo(); fixture.Settle();
        Assert.Empty(fixture.Model.Outline!.Roots[0].Children);
    }

    [AvaloniaFact]
    public void TreeViewPreservesDirectoryDescriptionAndExactSpaces()
    {
        using var fixture = new Fixture("treeView-beta\n");
        WorkspaceControls.OpenCreationForm(fixture.Window);
        fixture.Field<TextBox>("Node label").Text = "my  project";
        fixture.Field<CheckBox>("Directory").IsChecked = true;
        fixture.Field<TextBox>("Description").Text = "Root files";
        fixture.Click("Apply node");
        fixture.Click("Add child");
        fixture.Field<TextBox>("Node label").Text = "index.ts";
        fixture.Field<TextBox>("Icon (blank auto, none hides)").Text = "none";
        fixture.Click("Apply node");
        var reopened = MerpeepsDocument.Load(fixture.Window.CurrentSource).Outline!;
        Assert.Equal("my  project", reopened.Roots[0].Label);
        Assert.True(reopened.Roots[0].Directory);
        Assert.Equal("Root files", reopened.Roots[0].Description);
        Assert.Equal("none", reopened.Roots[0].Children[0].Icon);
    }

    [AvaloniaFact]
    public void TimelineCreatesTextPeriodAndMultipleEventsWithoutDateSorting()
    {
        using var fixture = new Fixture("timeline\n");
        WorkspaceControls.OpenCreationForm(fixture.Window);
        fixture.Field<TextBox>("Period label").Text = "Next release";
        fixture.Click("Apply time period");
        fixture.Click("Add event");
        fixture.Field<TextBox>("Event text").Text = "Ship\nthen learn";
        fixture.Click("Apply event");
        var period = fixture.Model.Temporal!.Periods[0];
        fixture.Canvas.SelectData(new(DataDiagramRole.Period, period.Id)); fixture.Settle();
        fixture.Click("Add event");
        fixture.Field<TextBox>("Event text").Text = "Review";
        fixture.Click("Apply event");
        var reopened = MerpeepsDocument.Load(fixture.Window.CurrentSource).Temporal!;
        Assert.Equal("Next release", reopened.Periods[0].Label);
        Assert.Equal(new[] { "Ship\nthen learn", "Review" }, reopened.Periods[0].Events.Select(e => e.Text));
    }

    [AvaloniaFact]
    public void JourneyTaskValidatesScoresBeforeChangingItsModel()
    {
        using var fixture = new Fixture("journey\n");
        WorkspaceControls.OpenCreationForm(fixture.Window);
        fixture.Field<TextBox>("Task label").Text = "Learn";
        fixture.Field<TextBox>("Score (1–5)").Text = "9";
        fixture.Field<TextBox>("Actors (one per line)").Text = "Me\nTeam";
        fixture.Click("Apply task");
        Assert.Empty(fixture.Model.Temporal!.Tasks);
        fixture.Field<TextBox>("Score (1–5)").Text = "4";
        fixture.Click("Apply task");
        Assert.Equal(4, fixture.Model.Temporal!.Tasks[0].Score);
        Assert.Equal(new[] { "Me", "Team" }, MerpeepsDocument.Load(fixture.Window.CurrentSource).Temporal!.Tasks[0].Actors);
        fixture.Canvas.Undo(); fixture.Settle();
        Assert.Empty(fixture.Model.Temporal!.Tasks);
    }

    private sealed class Fixture : IDisposable
    {
        public MainWindow Window { get; } = new();
        public EditorCanvas Canvas => Window.CanvasForTest;
        public DiagramModel Model => Canvas.Model!;
        private Border Panel => Window.FindControl<Border>("ClassInspectorHost")!;
        public Fixture(string source) { Window.Show(); Window.LoadSourceForTest(source); Settle(); Canvas.FitToView(); }
        public T Field<T>(string name) where T : Control => WorkspaceControls.Find<T>(Window, name);
        public void Click(string name) => WorkspaceControls.Click(Window, name);
        public void Settle() => Dispatcher.UIThread.RunJobs();
        public void Dispose()
        {
            foreach (var dialog in Window.OwnedWindows.ToArray()) dialog.Close();
            Window.Close();
        }
    }
}
