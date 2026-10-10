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
using Merpeeps.Core.Editing;
using Merpeeps.Core.Layout;
using Merpeeps.Core.Model;
using Xunit;

namespace Merpeeps.App.Tests;

public class SequenceWorkspaceTests
{
    [AvaloniaFact]
    public void GalleryCreatesEmptySequenceAndParticipantClicksComposeMessages()
    {
        using var fixture = new Fixture();
        fixture.CreateFromGallery();
        Assert.Empty(fixture.Model.Nodes); Assert.Empty(fixture.Model.Edges);
        Assert.NotNull(fixture.Model.Sequence);
        Assert.False(fixture.Window.FindControl<ComboBox>("EngineBox")!.IsVisible);
        fixture.AddParticipant("Client", SequenceParticipantKind.Actor);
        fixture.Window.FindControl<MenuItem>("MenuClassAdd")!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        fixture.Settle();
        fixture.Field<TextBox>("Participant identity").Text = "Service";
        fixture.Click("Apply participant");
        fixture.ClickParticipant("Client");
        fixture.Click("Add message");
        Assert.Empty(fixture.Sequence.Statements.OfType<SequenceMessage>());
        fixture.ClickParticipant("Service");
        var message = Assert.Single(fixture.Sequence.Statements.OfType<SequenceMessage>());
        Assert.Equal("Client", message.From); Assert.Equal("Service", message.To);
        fixture.Field<TextBox>("Message text").Text = "Hello\nagain;";
        Assert.False(fixture.Window.FindControl<Button>("SaveBtn")!.IsEnabled);
        fixture.Click("Apply message");
        Assert.True(fixture.Window.FindControl<Button>("SaveBtn")!.IsEnabled);
        var reopened = MerpeepsDocument.Load(fixture.Window.CurrentSource);
        Assert.Equal("Hello\nagain;", reopened.Sequence!.Statements.OfType<SequenceMessage>().Single().Text);
        Assert.Equal(message.Id, reopened.Sequence.Statements.OfType<SequenceMessage>().Single().Id);
        Assert.Empty(reopened.Nodes); Assert.Empty(reopened.Edges);
        fixture.Canvas.Undo();
        Assert.Equal("", fixture.Sequence.Statements.OfType<SequenceMessage>().Single().Text);
        fixture.Canvas.Undo();
        Assert.Empty(fixture.Sequence.Statements.OfType<SequenceMessage>());
    }

    [AvaloniaFact]
    public void SequenceSelectionNeverTurnsDraggingIntoGraphCoordinates()
    {
        using var fixture = new Fixture("sequenceDiagram\nparticipant A\nparticipant B\nA->>B: Request\n");
        var before = fixture.Window.CurrentSource;
        var header = fixture.Header("A");
        var at = fixture.ToWindow(new(header.Bounds.X + header.Bounds.Width / 2, header.Bounds.Y + header.Bounds.Height / 2));
        fixture.Window.MouseDown(at, MouseButton.Left);
        fixture.Window.MouseMove(at + new Vector(80, 70));
        fixture.Window.MouseUp(at + new Vector(80, 70), MouseButton.Left);
        fixture.Settle();
        Assert.Equal(before, fixture.Window.CurrentSource);
        Assert.Empty(fixture.Model.Nodes);
        Assert.Equal("A", fixture.Canvas.SelectedSequenceElement!.Id);
        Assert.False(fixture.Canvas.History!.CanUndo);
    }

    [AvaloniaFact]
    public void TimelineEditsNestedBranchesAndPreservesUnappliedMessageText()
    {
        using var fixture = new Fixture("sequenceDiagram\nparticipant A\nparticipant B\nalt condition\nA->>B: One\nelse other\nB->>A: Two\nend\n");
        var block = fixture.Sequence.Statements.OfType<SequenceBlock>().Single();
        fixture.Canvas.SelectSequenceId(block.Id);
        fixture.Settle();
        fixture.Click("First branch");
        fixture.Click("Add message…");
        fixture.Field<TextBox>("Message text").Text = "Inside";
        fixture.Click("Apply message");
        var updated = (SequenceBlock)fixture.Sequence.Statement(block.Id);
        Assert.Equal(2, updated.Branches[0].Statements.OfType<SequenceMessage>().Count());
        var first = updated.Branches[0].Statements.OfType<SequenceMessage>().First();
        fixture.Canvas.SelectSequenceId(first.Id);
        fixture.Settle();
        fixture.Field<TextBox>("Message text").Text = "Unapplied";
        fixture.ClickParticipant("B");
        fixture.Canvas.SelectSequenceId(first.Id);
        fixture.Settle();
        Assert.Equal("Unapplied", fixture.Field<TextBox>("Message text").Text);
        fixture.Click("Cancel edit");
        Assert.Equal("Unapplied", ((SequenceMessage)fixture.Sequence.Statement(first.Id)).Text);
        fixture.Canvas.Undo();
        Assert.Equal("One", ((SequenceMessage)fixture.Sequence.Statement(first.Id)).Text);
    }

    [AvaloniaFact]
    public void InvalidActivationDeletionIsExplicitAndDoesNotChangeTheSequence()
    {
        using var fixture = new Fixture("sequenceDiagram\nA->>+B: Start\nB-->>-A: Finish\n");
        var first = fixture.Sequence.Statements.OfType<SequenceMessage>().First();
        fixture.Canvas.SelectSequenceId(first.Id);
        fixture.Settle();
        var before = fixture.Window.CurrentSource;
        fixture.Click("Delete sequence item");
        Assert.Equal(before, fixture.Window.CurrentSource);
        Assert.Contains(fixture.Panel.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains("not active") == true);
        Assert.False(fixture.Canvas.History!.CanUndo);
    }

    [AvaloniaFact]
    public void UnsupportedNativeLifetimeKeepsRawSourceAndStructuredReason()
    {
        const string source = "sequenceDiagram\nA->>B: First\ndestroy B\nA-xB: End\nB->>A: Later\n";
        using var fixture = new Fixture(source);
        Assert.Null(fixture.Canvas.Model);
        Assert.Equal(source, fixture.Window.CurrentSource);
        Assert.Contains("participant-lifetime", fixture.Window.FindControl<TextBlock>("CapabilityText")!.Text);
        Assert.DoesNotContain("syntax error", fixture.Window.FindControl<TextBlock>("CapabilityText")!.Text, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public void EmptyBoxAndNumberingAreRealEditableSequenceItems()
    {
        using var fixture = new Fixture("sequenceDiagram\nparticipant A\nparticipant B\n");
        fixture.Window.FindControl<MenuItem>("MenuClassNamespaces")!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        fixture.Settle();
        fixture.Click("Add participant box");
        fixture.Field<TextBox>("Box title").Text = "Services";
        FullColourDialogTests.Click(fixture.Field<ColourField>("Box color"));
        var colorDialog = Assert.Single(fixture.Window.OwnedWindows);
        FullColourDialogTests.Field<TextBox>(colorDialog, "Hex color").Text = "#224466";
        FullColourDialogTests.Click(FullColourDialogTests.Field<Button>(colorDialog, "Use color"));
        fixture.Click("Apply participant box");
        Assert.Single(fixture.Sequence.Boxes);
        Assert.Contains(fixture.Canvas.SequenceScene.Regions, r => r.Kind == SequenceElementKind.ParticipantBox);
        var source = fixture.Window.CurrentSource;
        Assert.Contains("box rgb(34,68,102)", source);
        fixture.Window.FindControl<MenuItem>("MenuClassNavigate")!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        fixture.Settle();
        fixture.Click("Number messages…");
        fixture.Field<TextBox>("Start value").Text = "1.25";
        fixture.Field<TextBox>("Increment").Text = "0.25";
        fixture.Click("Apply numbering");
        Assert.Equal(.25m, fixture.Sequence.Statements.OfType<SequenceNumbering>().Single().Step);
    }

    [AvaloniaTheory]
    [InlineData(1280, 720, false)]
    [InlineData(1280, 720, true)]
    [InlineData(1600, 900, false)]
    [InlineData(1600, 900, true)]
    public void SequenceEditorStatesExposePrimaryActionsAndRetainCaret(int width, int height, bool dark)
    {
        const string source = """
            sequenceDiagram
            actor Client
            participant API as Public API
            participant DB@{"type":"database"}
            Client->>+API: Request
            alt Allowed
              API->>DB: Lookup
            else Denied
              Note over Client,API: No access<br/>Try again
            end
            API-->>-Client: Response
            """;
        using var fixture = new Fixture(source);
        fixture.Window.Width = width; fixture.Window.Height = height;
        fixture.Window.RequestedThemeVariant = dark ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light;
        fixture.Settle();
        fixture.Canvas.FitToView();
        Capture("scene");
        var message = SequenceDiagramModel.Walk(fixture.Sequence.Statements).OfType<SequenceMessage>().Single(m => m.Text == "Lookup");
        fixture.Canvas.SelectSequenceId(message.Id);
        fixture.Settle();
        var input = fixture.Field<TextBox>("Message text");
        input.Text = "Lookup account";
        input.CaretIndex = 6;
        input.Focus();
        fixture.Window.RequestedThemeVariant = dark ? Avalonia.Styling.ThemeVariant.Light : Avalonia.Styling.ThemeVariant.Dark;
        fixture.Settle();
        Assert.Same(input, fixture.Field<TextBox>("Message text"));
        Assert.Equal(6, input.CaretIndex);
        var pane = fixture.Window.FindControl<ScrollViewer>("PropertyPane")!;
        var position = input.TranslatePoint(new(), pane)!.Value;
        Assert.True(position.Y >= 0 && position.Y + input.Bounds.Height <= pane.Bounds.Height,
            $"The inline message field is outside the editor: {position.Y + input.Bounds.Height} > {pane.Bounds.Height}.");
        fixture.Window.RequestedThemeVariant = dark ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light;
        fixture.Settle();
        Capture("message");
        fixture.Click("Cancel edit");

        void Capture(string state)
        {
            var path = Environment.GetEnvironmentVariable("MERPEEPS_SEQUENCE_CAPTURES");
            if (string.IsNullOrWhiteSpace(path)) return;
            Directory.CreateDirectory(path);
            using var frame = fixture.Window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            frame.Save(Path.Combine(path, $"sequence-{width}-{height}-{(dark ? "dark" : "light")}-{state}.png"));
        }
    }

    private sealed class Fixture : IDisposable
    {
        public MainWindow Window { get; } = new();
        public EditorCanvas Canvas => Window.CanvasForTest;
        public DiagramModel Model => Canvas.Model!;
        public SequenceDiagramModel Sequence => Model.Sequence!;
        public Border Panel => Window.FindControl<Border>("ClassInspectorHost")!;
        public Fixture(string? source = null)
        {
            Window.Show();
            if (source is not null) Window.LoadSourceForTest(source);
            Settle();
            if (Canvas.Model?.Sequence is not null) Canvas.FitToView();
        }
        public void CreateFromGallery()
        {
            var tile = Window.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Create Sequence diagram");
            tile.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Settle();
        }
        public void AddParticipant(string id, SequenceParticipantKind kind)
        {
            WorkspaceControls.OpenCreationForm(Window);
            Field<TextBox>("Participant identity").Text = id;
            Field<ComboBox>("Declaration").SelectedItem = kind;
            Click("Apply participant");
        }
        public T Field<T>(string name) where T : Control => WorkspaceControls.Find<T>(Window, name);
        public void Click(string name) => WorkspaceControls.Click(Window, name);
        public SequenceRegion Header(string id) => Canvas.SequenceScene.Regions.First(r => r.OwnerId == id && r.Kind == SequenceElementKind.Participant && r.Bounds.Width > 20);
        public Point ToWindow(DiagramPoint point) => Canvas.TranslatePoint(new(point.X * Canvas.Zoom + Canvas.OffsetX, point.Y * Canvas.Zoom + Canvas.OffsetY), Window)!.Value;
        public void ClickParticipant(string id)
        {
            var header = Header(id).Bounds;
            var point = ToWindow(new(header.X + header.Width / 2, header.Y + header.Height / 2));
            Window.MouseDown(point, MouseButton.Left); Window.MouseUp(point, MouseButton.Left); Settle();
        }
        public void Settle() => Dispatcher.UIThread.RunJobs();
        public void Dispose()
        {
            foreach (var dialog in Window.OwnedWindows.ToArray()) dialog.Close();
            Window.Close();
        }
    }
}
