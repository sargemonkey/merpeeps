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

public class WorkflowWorkspaceTests
{
    [AvaloniaTheory]
    [InlineData("agentflow-beta", 1600, 900)]
    [InlineData("agentflow-beta", 900, 600)]
    [InlineData("eventmodeling", 1600, 900)]
    [InlineData("eventmodeling", 900, 600)]
    [InlineData("zenuml", 1600, 900)]
    [InlineData("zenuml", 900, 600)]
    public void FinalFamiliesRenderTypedContentAtBothWorkspaceSizes(string kind, int width, int height)
    {
        var source = kind switch
        {
            "agentflow-beta" => "agentflow-beta LR\ninput[\"Request\"]@{ shape: input }\nflow work[\"Review agent\"]\ncheck[\"Review\"]@{ shape: task }\ntool[\"Check policy\"]@{ shape: tool }\ncheck --> tool\nend\ninput --> work\n",
            "eventmodeling" => "eventmodeling\ntf 01 ui CartUI\ntf 02 cmd AddItem { description: string }\ntf 03 evt ItemAdded\n",
            _ => "zenuml\n@Actor Client\nClient->Service.load() {\n DB.query()\n return result\n}\n",
        };
        using var f = new Fixture(source, width, height);
        Assert.Equal(1, f.Canvas.Zoom); Assert.NotEmpty(f.Canvas.StructuralScene.Labels);
        f.Capture($"{kind}-{width}");
    }
    [AvaloniaFact]
    public void AgentFlowCreationPreservesMetadataAndDoesNotCreateGraphNodes()
    {
        using var f = new Fixture("agentflow-beta\n");
        WorkspaceControls.OpenCreationForm(f.Window); f.Field<TextBox>("Element source ID").Text = "research"; f.Field<TextBox>("Element label").Text = "Research";
        f.Field<TextBox>("Metadata (inert YAML / JSON)").Text = "{ shape: task, instruction: Read carefully, custom: { keep: true } }";
        f.Click("Apply workflow element");
        var d = f.Canvas.Model!.AgentFlow!; Assert.Single(d.Elements); Assert.Empty(f.Canvas.Model.Nodes);
        var reopened = MerpeepsDocument.Load(f.Window.CurrentSource).AgentFlow!;
        Assert.Equal("Read carefully", reopened.Elements[0].Metadata["instruction"]!.GetValue<string>());
        Assert.Equal(d.Elements[0].Id, reopened.Elements[0].Id);
    }
    [AvaloniaFact]
    public void EventFrameCreationPreservesTemporalOccurrenceIdentity()
    {
        using var f = new Fixture("eventmodeling\n");
        WorkspaceControls.OpenCreationForm(f.Window); f.Field<TextBox>("Unique source number").Text = "03"; f.Field<TextBox>("Entity / namespace name").Text = "Cart.Added";
        f.Field<ComboBox>("Entity type").SelectedItem = EventEntityKind.Event; f.Click("Apply timeframe");
        var frame = f.Canvas.Model!.EventModeling!.Frames.Single();
        Assert.Equal("03", frame.Number);
        Assert.Equal(frame.Id, MerpeepsDocument.Load(f.Window.CurrentSource).EventModeling!.Frames.Single().Id);
    }
    [AvaloniaFact]
    public void ZenParticipantAndCallAreAuthoredThroughTheirOwnControls()
    {
        using var f = new Fixture("zenuml\n");
        WorkspaceControls.OpenCreationForm(f.Window); f.Field<TextBox>("Participant source ID").Text = "Service"; f.Click("Apply participant");
        f.Canvas.SelectStructural(null); f.Settle();
        f.Click("Add statement"); f.Field<ComboBox>("Recipient").SelectedIndex = 0;
        f.Field<TextBox>("Method name").Text = "load"; f.Click("Apply ZenUML statement");
        var d = f.Canvas.Model!.Zen!;
        Assert.Single(d.Statements); Assert.Equal(ZenStatementKind.Call, d.Statements[0].Kind);
        Assert.Contains("Service.load()", f.Window.CurrentSource);
        Assert.Null(f.Canvas.Model.Sequence); Assert.Empty(f.Canvas.Model.Nodes);
        Assert.Equal(d.Statements[0].Id, MerpeepsDocument.Load(f.Window.CurrentSource).Zen!.Statements[0].Id);
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
            using var image = Window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No real final-family frame.");
            image.Save(Path.Combine(path, name + ".png"));
        }
        public void Dispose() { foreach (var child in Window.OwnedWindows.ToArray()) child.Close(); Window.Close(); }
    }
}
