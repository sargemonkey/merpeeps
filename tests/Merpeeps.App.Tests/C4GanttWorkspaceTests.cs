using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Merpeeps.Core;
using Merpeeps.Core.Model;
using Merpeeps.Core.Layout;
using Xunit;

namespace Merpeeps.App.Tests;

public class C4GanttWorkspaceTests
{
    [AvaloniaTheory]
    [InlineData(false, 1600, 900)]
    [InlineData(false, 900, 600)]
    [InlineData(true, 1600, 900)]
    [InlineData(true, 900, 600)]
    public void C4AndGanttHaveReadableCanvasAndContextualEditors(bool gantt, int width, int height)
    {
        using var fixture = new Fixture(gantt
            ? "gantt\nsection Build\nImplement :a,2024-01-05,2d\nVerify :b,after a,1d\n"
            : "C4Container\nContainer_Boundary(g,\"Application\") {\nContainer(a,\"API\",\"C#\",\"Handles requests\")\nContainerDb(b,\"Data\",\"SQL\")\n}\nRel(a,b,\"Reads\",\"JDBC\")\n", width, height);
        var canvas = fixture.Window.CanvasForTest;
        Assert.True(canvas.Bounds.Width >= width * .5);
        var id = gantt ? canvas.Model!.Gantt!.Tasks[0].Id : canvas.Model!.C4!.Elements[1].Id;
        canvas.SelectStructural(new(gantt ? "GanttTask" : "C4Element", id)); fixture.Settle();
        fixture.Capture($"{(gantt ? "gantt" : "c4")}-{width}-selected");
        if (!gantt) Assert.NotNull(fixture.Field<TextBox>("Element label"));
        fixture.Capture($"{(gantt ? "gantt" : "c4")}-{width}-editing");
    }
    [AvaloniaTheory]
    [InlineData("C4Context")]
    [InlineData("C4Container")]
    [InlineData("C4Component")]
    [InlineData("C4Dynamic")]
    [InlineData("C4Deployment")]
    public void EachC4DialectCreatesTypedContentAndPreservesItsHeader(string directive)
    {
        using var fixture = new Fixture(directive + "\n");
        WorkspaceControls.OpenCreationForm(fixture.Window);
        fixture.Field<TextBox>("Source alias").Text = "app";
        fixture.Field<TextBox>("Element label").Text = "Application";
        fixture.Click("Apply C4 element");
        var model = fixture.Window.CanvasForTest.Model!;
        Assert.Empty(model.Nodes);
        Assert.Equal("Application", model.C4!.Elements.Single().Label);
        var saved = MerpeepsDocument.Load(fixture.Window.CurrentSource);
        Assert.Equal(directive, saved.Kind);
        Assert.Equal(model.C4.Elements.Single().Id, saved.C4!.Elements.Single().Id);
        fixture.Window.CanvasForTest.Undo();
        Assert.Empty(model.C4!.Elements);
    }
    [AvaloniaFact]
    public void GanttTaskUsesRawDateAndDurationAndRetainsInvalidDraft()
    {
        using var fixture = new Fixture("gantt\n");
        WorkspaceControls.OpenCreationForm(fixture.Window);
        fixture.Field<TextBox>("Task label").Text = "Implement";
        fixture.Field<Expander>("Identity, section and status").IsExpanded = true; fixture.Settle();
        fixture.Field<TextBox>("Task source ID").Text = "work";
        fixture.Field<TextBox>("Start date / time").Text = "2024-01-05";
        fixture.Field<TextBox>("Duration").Text = "2";
        fixture.Click("Apply Gantt task");
        var model = fixture.Window.CanvasForTest.Model!;
        Assert.Empty(model.Nodes);
        var task = model.Gantt!.Tasks.Single();
        Assert.Equal("2024-01-05", Assert.IsType<GanttStartSpec.Date>(task.Start).Text);
        Assert.Equal(2, Assert.IsType<GanttEndSpec.Duration>(task.End).Amount);
        fixture.Field<TextBox>("Duration").Text = "invalid";
        fixture.Click("Apply Gantt task");
        Assert.Equal("invalid", fixture.Field<TextBox>("Duration").Text);
        Assert.Equal(task, model.Gantt.Tasks.Single());
        fixture.Field<TextBox>("Duration").Text = "3";
        fixture.Click("Apply Gantt task");
        Assert.Equal(3, Assert.IsType<GanttEndSpec.Duration>(MerpeepsDocument.Load(fixture.Window.CurrentSource).Gantt!.Tasks.Single().End).Amount);
    }
    [AvaloniaFact]
    public void GanttReferenceTimeIsExplicitAndRefreshingItDoesNotEditSource()
    {
        using var fixture = new Fixture("gantt\ndateFormat HH:mm\nTask :a,17:49,2m\n");
        var canvas = fixture.Window.CanvasForTest;
        var source = fixture.Window.CurrentSource;
        var context = new GanttEvaluationContext(new DateTimeOffset(2026, 2, 10, 12, 0, 0, TimeSpan.Zero), TimeZoneInfo.Utc);
        canvas.SetGanttContext(context); fixture.Settle();
        Assert.Equal(new DateTimeOffset(2026, 2, 10, 17, 49, 0, TimeSpan.Zero), canvas.GanttLayout.Schedule.Tasks.Single().Start);
        canvas.SetGanttContext(context with { ReferenceInstant = context.ReferenceInstant.AddDays(1) }); fixture.Settle();
        Assert.Equal(new DateTimeOffset(2026, 2, 11, 17, 49, 0, TimeSpan.Zero), canvas.GanttLayout.Schedule.Tasks.Single().Start);
        Assert.Equal(source, fixture.Window.CurrentSource);
        Assert.False(canvas.History!.CanUndo);
    }

    [AvaloniaFact]
    public void GanttInvalidDateSourceDoesNotBecomeAnEditableZeroDurationTask()
    {
        using var fixture = new Fixture("gantt\nTask :2024-01-01,3dX\n");
        Assert.Null(fixture.Window.CanvasForTest.Model);
        Assert.Contains("3dX", fixture.Window.CurrentSource);
    }
    [AvaloniaFact]
    public void C4NativeTextMeasurementDoesNotEraseWordSpaces()
    {
        var model = MerpeepsDocument.Load("C4Container\nContainer(a,\"Application\",\"C#\",\"Handles requests\")\n");
        var layout = C4DiagramLayout.Create(model, EditorCanvas.MeasureDrawingText);
        var words = layout.Scene.Labels.Where(l => l.Owner.Id == model.C4!.Elements[0].Id).ToArray();
        var handles = words.Single(l => l.Text == "Handles");
        var requests = words.Single(l => l.Text == "requests");
        Assert.True(requests.Bounds.X > handles.Bounds.X + handles.Bounds.Width + 1, "Native measurement dropped the space between C4 words.");
    }
    [AvaloniaFact]
    public void GanttStartsAtReadableActualSizeInsteadOfShrinkingTimeLabels()
    {
        using var fixture = new Fixture("gantt\nTask :a,2024-01-01,2d\n", 900, 600);
        Assert.Equal(1, fixture.Window.CanvasForTest.Zoom);
    }
    private sealed class Fixture : IDisposable
    {
        public MainWindow Window { get; }
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
            if (Environment.GetEnvironmentVariable("MERPEEPS_C4_GANTT_CAPTURES") is not { Length: > 0 } directory) return;
            Directory.CreateDirectory(directory);
            using var frame = Window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No real C4/Gantt frame.");
            frame.Save(Path.Combine(directory, name + ".png"));
        }
        public void Dispose() { foreach (var child in Window.OwnedWindows.ToArray()) child.Close(); Window.Close(); }
    }
}
