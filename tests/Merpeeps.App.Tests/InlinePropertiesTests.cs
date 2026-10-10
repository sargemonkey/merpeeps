using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Merpeeps.Core;
using Merpeeps.Core.Model;
using Xunit;

namespace Merpeeps.App.Tests;

public class InlinePropertiesTests
{
    [AvaloniaFact]
    public void EntityNameIsImmediatelyEditableAndEnterCommitsOnce()
    {
        using var f = new Fixture("erDiagram\nCUSTOMER {\n int id PK\n}\n");
        f.Canvas.SelectByIds(["CUSTOMER"]); Settle();
        var name = f.Field<TextBox>("Entity name");
        Assert.Equal("CUSTOMER", name.Text);
        Assert.DoesNotContain(f.Buttons(), b => AutomationProperties.GetName(b) == "Rename entity");
        name.Focus(); name.Text = "Customer accounts";
        Assert.Null(f.Canvas.Model!.Nodes[0].Label);
        Enter(name);
        Assert.Equal("Customer accounts", f.Canvas.Model.Nodes[0].Label);
        Assert.True(f.Window.FindControl<Button>("SaveBtn")!.IsEnabled);
        Assert.DoesNotContain(f.Buttons(), b => (AutomationProperties.GetName(b) ?? "").StartsWith("Apply ", StringComparison.Ordinal));
        Enter(f.Field<TextBox>("Entity name"));
        f.Canvas.Undo(); Settle();
        Assert.Null(f.Canvas.Model.Nodes[0].Label);
        Assert.False(f.Canvas.History!.CanUndo);
        Assert.Equal("CUSTOMER", f.Field<TextBox>("Entity name").Text);
        f.Canvas.Redo(); Settle();
        Assert.Equal("Customer accounts", MerpeepsDocument.Load(f.Window.CurrentSource).Nodes[0].Label);
    }

    [AvaloniaFact]
    public void LeavingNameCommitsAndPreservesTheNextFieldFocus()
    {
        using var f = new Fixture("erDiagram\nCUSTOMER {\n int id PK\n}\n");
        f.Canvas.SelectByIds(["CUSTOMER"]); Settle();
        var name = f.Field<TextBox>("Entity name");
        name.Focus(); name.Text = "Accounts";
        f.Field<Expander>("Identity and grouping").IsExpanded = true; Settle();
        f.Field<TextBox>("Entity identity").Focus(); Settle();
        Assert.Equal("Accounts", f.Canvas.Model!.Nodes[0].Label);
        Assert.True(f.Field<TextBox>("Entity identity").IsFocused);
        Assert.True(f.Field<Expander>("Identity and grouping").IsExpanded);
    }

    [AvaloniaFact]
    public void EscapeCancelsWithoutCommittingOrLosingTheField()
    {
        using var f = new Fixture("classDiagram\nclass A\n");
        f.Canvas.SelectByIds(["A"]); Settle();
        var name = f.Field<TextBox>("Class name");
        name.Focus(); name.Text = "Discard";
        name.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
        Settle();
        Assert.Equal("A", f.Field<TextBox>("Class name").Text);
        Assert.True(f.Field<TextBox>("Class name").IsFocused);
        Assert.False(f.Canvas.History!.CanUndo);
    }

    [AvaloniaFact]
    public void SelectionChangeCommitsTheOriginalOwnerWithoutStealingTheNewSelection()
    {
        using var f = new Fixture("erDiagram\nA {\n int id\n}\nB {\n int id\n}\n");
        f.Canvas.SelectByIds(["A"]); Settle();
        var name = f.Field<TextBox>("Entity name"); name.Focus(); name.Text = "First";
        f.Canvas.SelectByIds(["B"]); Settle();
        Assert.Equal("First", f.Canvas.Model!.Node("A")!.Label);
        Assert.Null(f.Canvas.Model.Node("B")!.Label);
        Assert.Equal("B", f.Canvas.Selected!.Id);
        Assert.Equal("B", f.Field<TextBox>("Entity name").Text);
    }

    [AvaloniaFact]
    public void InvalidIdentityStaysAsAnErrorAndSurvivesSelectionWithoutChangingAnyEntity()
    {
        using var f = new Fixture("erDiagram\nA {\n int id\n}\nB {\n int id\n}\n");
        f.Canvas.SelectByIds(["A"]); Settle();
        f.Field<Expander>("Identity and grouping").IsExpanded = true; Settle();
        var identity = f.Field<TextBox>("Entity identity");
        identity.Focus(); identity.Text = "B"; Enter(identity);
        Assert.NotNull(f.Canvas.Model!.Node("A"));
        Assert.False(f.Canvas.History!.CanUndo);
        Assert.False(f.Window.FindControl<Button>("SaveBtn")!.IsEnabled);
        Assert.Equal("B", identity.Text);
        Assert.True(DataValidationErrors.GetHasErrors(identity));
        f.Canvas.SelectByIds(["B"]); Settle();
        f.Canvas.SelectByIds(["A"]); Settle();
        Assert.Equal("B", f.Field<TextBox>("Entity identity").Text);
        var corrected = f.Field<TextBox>("Entity identity"); corrected.Focus(); corrected.Text = "C"; Enter(corrected);
        Assert.NotNull(f.Canvas.Model.Node("C"));
        Assert.Equal("C", f.Canvas.Selected!.Id);
        Assert.True(f.Window.FindControl<Button>("SaveBtn")!.IsEnabled);
    }

    [AvaloniaTheory]
    [InlineData("architecture-beta\nservice db(database)[Orders]\n", "ArchitectureNode", "Service label", "Customers")]
    [InlineData("C4Context\nSystem(s, \"Service\")\n", "C4Element", "Element label", "Application")]
    [InlineData("usecase-beta\nactor User\n", "UseCaseNode", "Element label", "Customer")]
    [InlineData("agentflow-beta\nTask@{ shape: task }\n", "AgentFlowElement", "Element label", "Review")]
    public void CommonPropertiesDoNotRequireAnEditButton(string source, string role, string field, string value)
    {
        using var f = new Fixture(source);
        var model = f.Canvas.Model!;
        var id = model.Architecture?.Nodes[0].Id ?? model.C4?.Elements[0].Id ?? model.UseCase?.Nodes[0].Id ?? model.AgentFlow!.Elements[0].Id;
        f.Canvas.SelectStructural(new(role, id)); Settle();
        var input = f.Field<TextBox>(field);
        input.Focus(); input.Text = value; Enter(input, input.AcceptsReturn ? KeyModifiers.Control : KeyModifiers.None);
        Assert.True(f.Canvas.History!.CanUndo);
        Assert.Contains(value, f.Window.CurrentSource);
        Assert.Equal(id, f.Canvas.SelectedStructuralTarget!.Id);
        f.Canvas.Undo(); Settle();
        Assert.False(f.Canvas.History.CanUndo);
    }

    [AvaloniaFact]
    public void ComboChangeCommitsAndInvalidMultiLineTextUsesCtrlEnter()
    {
        using var f = new Fixture("sequenceDiagram\nparticipant A\n");
        f.Canvas.SelectSequenceId("A"); Settle();
        f.Field<Expander>("More properties").IsExpanded = true; Settle();
        f.Field<ComboBox>("Declaration").SelectedItem = SequenceParticipantKind.Actor; Settle();
        Assert.Equal(SequenceParticipantKind.Actor, f.Canvas.Model!.Sequence!.Participants[0].Kind);
        var label = f.Field<TextBox>("Participant label");
        label.Focus(); label.Text = "First\nSecond"; Enter(label, KeyModifiers.Control);
        Assert.True(f.Canvas.Model.Sequence.Participants[0].Alias == "First\nSecond", f.Feedback());
        f.Canvas.Undo(); f.Canvas.Undo(); Settle();
        Assert.False(f.Canvas.History!.CanUndo);
    }

    [AvaloniaFact]
    public void ClickingConnectCommitsTheNameThenStartsExactlyOneConnection()
    {
        using var f = new Fixture("classDiagram\nclass A\nclass B\n");
        f.Canvas.SelectByIds(["A"]); Settle();
        var name = f.Field<TextBox>("Class name"); name.Focus(); name.Text = "Renamed";
        f.Field<Button>("Add relationship").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle();
        Assert.Equal("Renamed", f.Canvas.Model!.Node("A")!.Display);
        Assert.Equal("A", f.Canvas.ClassRelationshipSource!.Id);
        Assert.Empty(f.Canvas.Model.Edges);
        f.Canvas.CancelClassRelationship();
        f.Canvas.Undo();
        Assert.False(f.Canvas.History!.CanUndo);
    }

    [AvaloniaFact]
    public void OpeningDiagramOptionsCommitsAValidNameAndKeepsItUndoable()
    {
        using var f = new Fixture("erDiagram\nCUSTOMER\n");
        f.Canvas.SelectByIds(["CUSTOMER"]); Settle();
        var name = f.Field<TextBox>("Entity name"); name.Focus(); name.Text = "Accounts";
        f.Window.FindControl<MenuItem>("MenuClassDisplay")!.RaiseClick(); Settle();
        Assert.Equal("Accounts", f.Canvas.Model!.Node("CUSTOMER")!.Display);
        Assert.NotNull(f.Field<Button>("Apply direction"));
        f.Canvas.Undo(); Settle();
        Assert.Equal("CUSTOMER", f.Canvas.Model.Node("CUSTOMER")!.Display);
    }

    [AvaloniaFact]
    public void ReturningAnInvalidValueToItsOriginalValueClearsTheErrorWithoutHistory()
    {
        using var f = new Fixture("erDiagram\nA\nB\n");
        f.Canvas.SelectByIds(["A"]); Settle();
        f.Field<Expander>("Identity and grouping").IsExpanded = true; Settle();
        var input = f.Field<TextBox>("Entity identity"); input.Focus(); input.Text = "B"; Enter(input);
        Assert.True(DataValidationErrors.GetHasErrors(input));
        input.Text = "A"; Settle();
        Assert.False(DataValidationErrors.GetHasErrors(input));
        Assert.True(f.Window.FindControl<Button>("SaveBtn")!.IsEnabled);
        f.Canvas.Focus(); Settle();
        Assert.False(f.Canvas.History!.CanUndo);
    }

    [AvaloniaTheory]
    [InlineData("architecture-beta\n", "Service label")]
    [InlineData("C4Context\n", "Element label")]
    [InlineData("sequenceDiagram\n", "Participant label")]
    [InlineData("erDiagram\n", "Entity name")]
    [InlineData("classDiagram\n", "Class name")]
    [InlineData("mindmap\n", "Node label")]
    public void CreatedElementsExposeTheirNameWithoutAnEditStep(string source, string name)
    {
        using var f = new Fixture(source);
        var palette = f.Window.FindControl<Border>("FamilyPaletteHost")!;
        var item = palette.GetVisualDescendants().OfType<Button>().First(b => b.IsEffectivelyVisible && b.IsEnabled);
        item.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle();
        Assert.NotNull(f.Field<TextBox>(name));
        Assert.DoesNotContain(f.Buttons(), b =>
            AutomationProperties.GetName(b) is "Rename entity" or "Rename class" or "Edit service / junction" or "Edit C4 element" or "Edit participant" or "Edit node");
        Assert.True(f.Window.FindControl<Button>("SaveBtn")!.IsEnabled);
    }

    [AvaloniaTheory]
    [InlineData(false, 900)]
    [InlineData(true, 1600)]
    public void InlineEntitySectionsRenderWithThePrimaryFieldVisible(bool dark, int width)
    {
        using var f = new Fixture("erDiagram\nCUSTOMER {\n int id PK\n string email UK\n}\n");
        f.Window.Width = width; f.Window.Height = 760;
        f.Window.RequestedThemeVariant = dark ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light;
        f.Canvas.SelectByIds(["CUSTOMER"]); Settle();
        Assert.False(f.Field<Expander>("Appearance").IsExpanded);
        Assert.True(f.Field<Expander>("Attributes").IsExpanded);
        var name = f.Field<TextBox>("Entity name");
        Assert.True(name.Bounds.Height >= 30);
        Capture("selected");
        name.Focus(); name.Text = "Customer accounts"; Enter(name);
        f.Field<Expander>("Appearance").IsExpanded = true; Settle();
        Capture("appearance");
        void Capture(string state)
        {
            if (Environment.GetEnvironmentVariable("MERPEEPS_INLINE_CAPTURES") is not { Length: > 0 } directory) return;
            Directory.CreateDirectory(directory);
            using var frame = f.Window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No inline inspector rendering.");
            frame.Save(Path.Combine(directory, $"er-{width}-{(dark ? "dark" : "light")}-{state}.png"));
        }
    }

    private static void Enter(Control input, KeyModifiers modifiers = KeyModifiers.None)
    {
        input.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, KeyModifiers = modifiers });
        Settle();
    }
    private static void Settle() => Dispatcher.UIThread.RunJobs();
    private sealed class Fixture : IDisposable
    {
        public MainWindow Window { get; } = new();
        public EditorCanvas Canvas => Window.CanvasForTest;
        private Border Panel => Window.FindControl<Border>("ClassInspectorHost")!;
        public Fixture(string source) { Window.Show(); Window.LoadSourceForTest(source); Settle(); }
        public T Field<T>(string name) where T : Control
        {
            var field = Panel.GetVisualDescendants().OfType<T>().SingleOrDefault(c => c.IsEffectivelyVisible && AutomationProperties.GetName(c) == name);
            if (field is not null) return field;
            var more = Panel.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>()
                .FirstOrDefault(c => AutomationProperties.GetName(c) == "More properties");
            if (more is not null) more.IsChecked = true;
            Settle();
            return Panel.GetVisualDescendants().OfType<T>().Single(c => c.IsEffectivelyVisible && AutomationProperties.GetName(c) == name);
        }
        public IEnumerable<Button> Buttons() => Panel.GetVisualDescendants().OfType<Button>().Where(c => c.IsEffectivelyVisible);
        public string Feedback() => string.Join(" | ", Panel.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
        public void Dispose()
        {
            foreach (var child in Window.OwnedWindows.ToArray()) child.Close();
            Window.Close();
        }
    }
}
