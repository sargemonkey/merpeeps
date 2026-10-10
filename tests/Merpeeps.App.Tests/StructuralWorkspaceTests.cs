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

public class StructuralWorkspaceTests
{
    [AvaloniaFact]
    public void RequirementFieldsAndStyleValidationUseTypedData()
    {
        using var f = new Fixture("requirementDiagram\n");
        WorkspaceControls.OpenCreationForm(f.Window);
        f.Field<TextBox>("Name").Text = "Payment";
        f.Field<TextBox>("Displayed requirement ID").Text = "R-1";
        f.Field<TextBox>("Requirement text").Text = "Must work";
        f.Field<ComboBox>("Risk").SelectedItem = RequirementRisk.High;
        f.Field<ComboBox>("Verification").SelectedItem = RequirementVerification.Test;
        f.Click("Apply requirement / element");
        var model = f.Window.CanvasForTest.Model!;
        Assert.Equal("R-1", model.Requirements!.Items[0].DisplayId);
        Assert.Empty(model.Nodes);
        f.Click("Element appearance…");
        FullColourDialogTests.Click(f.Field<ColourField>("fill"));
        var picker = Assert.Single(f.Window.OwnedWindows);
        FullColourDialogTests.Field<TextBox>(picker, "Hex color").Text = "not-a-color";
        Assert.False(FullColourDialogTests.Field<Button>(picker, "Use color").IsEnabled);
        Assert.Empty(model.Requirements.Items[0].Style);
        FullColourDialogTests.Field<TextBox>(picker, "Hex color").Text = "#abcdef";
        FullColourDialogTests.Click(FullColourDialogTests.Field<Button>(picker, "Use color"));
        f.Click("Apply appearance");
        var reopened = MerpeepsDocument.Load(f.Window.CurrentSource);
        Assert.Equal("#ABCDEF", reopened.Requirements!.Items[0].Style["fill"]!.GetValue<string>());
        f.Window.CanvasForTest.Undo();
        Assert.Empty(model.Requirements!.Items[0].Style);
    }
    [AvaloniaFact]
    public void GitOperationEditorReplaysDiagramBranchesWithoutRepositoryState()
    {
        using var f = new Fixture("gitGraph\n");
        WorkspaceControls.OpenCreationForm(f.Window);
        f.Field<TextBox>("Custom commit ID").Text = "A";
        f.Click("Apply diagram operation");
        f.Window.FindControl<MenuItem>("MenuClassAdd")!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); f.Settle();
        f.Field<ComboBox>("Operation").SelectedItem = GitDiagramOperationKind.Branch; f.Settle();
        f.Field<TextBox>("Branch / cherry-pick source").Text = "dev";
        f.Click("Apply diagram operation");
        Assert.Equal("dev", f.Window.CanvasForTest.Model!.GitDiagram!.Replay().CurrentBranch);
        f.Window.FindControl<MenuItem>("MenuClassAdd")!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); f.Settle();
        f.Field<TextBox>("Custom commit ID").Text = "B";
        f.Click("Apply diagram operation");
        var reopened = MerpeepsDocument.Load(f.Window.CurrentSource);
        Assert.Equal(2, reopened.GitDiagram!.Replay().Commits.Count);
        Assert.Equal("dev", reopened.GitDiagram.Replay().Commits[1].Branch);
        Assert.Empty(reopened.Nodes);
        f.Window.CanvasForTest.Undo(); f.Settle();
        Assert.Single(f.Window.CanvasForTest.Model!.GitDiagram!.Replay().Commits);
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
