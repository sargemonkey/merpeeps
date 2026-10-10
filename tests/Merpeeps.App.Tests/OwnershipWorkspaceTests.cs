using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Merpeeps.Core;
using Merpeeps.Core.Model;
using Xunit;

namespace Merpeeps.App.Tests;

public class OwnershipWorkspaceTests
{
    [AvaloniaTheory]
    [InlineData(false, 1600, 900)]
    [InlineData(false, 900, 600)]
    [InlineData(true, 1600, 900)]
    [InlineData(true, 900, 600)]
    public void ResponsibilityAndUmlShapesRenderAtBothWorkspaceSizes(bool useCase, int width, int height)
    {
        using var f = new Fixture(useCase
            ? "usecase-beta\ndirection LR\nactor User(\"Customer\")\nsystemBoundary App[\"Application\"]@{type:package}\nLogin(\"Sign in\")\nCheck(\"Verify session\")\nend\nUser --> Login\nLogin ..> : include Check\n"
            : "swimlane-beta LR\nsubgraph Team[Support]\nA[Receive request]\nend\nsubgraph Review[Review team]\nB{Approved?}\nend\nA -->|Handoff| B\n", width, height);
        Assert.Equal(1, f.Canvas.Zoom); Assert.NotEmpty(f.Canvas.StructuralScene.Labels);
        f.Capture($"{(useCase ? "usecase" : "swimlane")}-{width}");
    }
    [AvaloniaFact]
    public void CreatingALaneAndStepPersistsActualOwnership()
    {
        using var f = new Fixture("swimlane-beta LR\n");
        WorkspaceControls.OpenCreationForm(f.Window); f.Field<TextBox>("Lane source ID").Text = "Team"; f.Field<TextBox>("Lane label").Text = "Support"; f.Click("Apply lane");
        var laneId = f.Canvas.Model!.Swimlane!.Groups[0].Id;
        f.Click("Add step to lane"); f.Field<TextBox>("Step source ID").Text = "Triage"; f.Field<TextBox>("Step label").Text = "Triage request"; f.Click("Apply step");
        var d = f.Canvas.Model.Swimlane!;
        Assert.Equal(laneId, d.LaneOf(d.Nodes[0].Id)); Assert.Empty(f.Canvas.Model.Nodes);
        var reopened = MerpeepsDocument.Load(f.Window.CurrentSource);
        Assert.Equal(laneId, reopened.Swimlane!.LaneOf(d.Nodes[0].Id));
        Assert.Single(f.Canvas.SwimlaneLayout.Lanes);
    }
    [AvaloniaFact]
    public void ActorAndUseCaseControlsKeepTypedSemantics()
    {
        using var f = new Fixture("usecase-beta\n");
        WorkspaceControls.OpenCreationForm(f.Window); f.Field<ComboBox>("Element kind").SelectedItem = UseCaseNodeKind.Actor; f.Settle();
        f.Field<TextBox>("Element source ID").Text = "User"; f.Field<TextBox>("Element label").Text = "Customer"; f.Click("Apply use-case element");
        var actor = f.Canvas.Model!.UseCase!.Nodes.Single(); Assert.Equal(UseCaseNodeKind.Actor, actor.Kind);
        f.Canvas.SelectStructural(null); f.Settle();
        WorkspaceControls.OpenCreationForm(f.Window); f.Field<TextBox>("Element source ID").Text = "Login"; f.Field<TextBox>("Element label").Text = "Sign in"; f.Click("Apply use-case element");
        var d = f.Canvas.Model.UseCase!;
        Assert.Equal(UseCaseNodeKind.UseCase, d.Nodes.Single(n => n.SourceId == "Login").Kind);
        var saved = MerpeepsDocument.Load(f.Window.CurrentSource);
        Assert.Equal(actor.Id, saved.UseCase!.Nodes.Single(n => n.SourceId == "User").Id);
    }
    private sealed class Fixture : IDisposable
    {
        public MainWindow Window { get; }
        public EditorCanvas Canvas => Window.CanvasForTest;
        private Border Panel => Window.FindControl<Border>("ClassInspectorHost")!;
        public Fixture(string source, int width = 1600, int height = 900)
        {
            Window = new MainWindow { Width = width, Height = height };
            Window.Show(); Window.LoadSourceForTest(source); Settle();
        }
        public T Field<T>(string name) where T : Control => WorkspaceControls.Find<T>(Window, name);
        public void Click(string name) => WorkspaceControls.Click(Window, name);
        public void Settle() => Dispatcher.UIThread.RunJobs();
        public void Capture(string name)
        {
            if (Environment.GetEnvironmentVariable("MERPEEPS_FINAL_CAPTURES") is not { Length: > 0 } path) return;
            Directory.CreateDirectory(path);
            using var image = Window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No real ownership frame.");
            image.Save(Path.Combine(path, name + ".png"));
        }
        public void Dispose() { foreach (var child in Window.OwnedWindows.ToArray()) child.Close(); Window.Close(); }
    }
}
