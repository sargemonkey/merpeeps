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

public class SetGrammarWorkspaceTests
{
    [AvaloniaFact]
    public void VennAuthoringCreatesRealSetsOverlapAndOwnedText()
    {
        using var f = new Fixture("venn-beta\n");
        WorkspaceControls.OpenCreationForm(f.Window); f.Field<TextBox>("Set identifier").Text = "A"; f.Field<TextBox>("Set size (blank uses default)").Text = "20"; f.Click("Apply set");
        f.Canvas.SelectStructural(null); f.Settle();
        WorkspaceControls.OpenCreationForm(f.Window); f.Field<TextBox>("Set identifier").Text = "B"; f.Field<TextBox>("Set size (blank uses default)").Text = "12"; f.Click("Apply set");
        f.Click("Create overlap"); f.Field<CheckBox>("Include A").IsChecked = true; f.Field<CheckBox>("Include B").IsChecked = true;
        f.Field<TextBox>("Overlap label (optional)").Text = "Shared"; f.Field<TextBox>("Overlap size (blank uses default)").Text = "3"; f.Click("Apply overlap");
        var model = f.Canvas.Model!; var overlap = model.Venn!.Overlaps.Single();
        Assert.Equal(3, f.Canvas.VennLayout.Areas.Single(a => !a.Implied).ActualArea, 7);
        f.Click("Add text to region"); f.Field<TextBox>("Text identifier").Text = "sharedText"; f.Field<TextBox>("Text label (optional)").Text = "Owned text"; f.Click("Apply region text");
        var saved = MerpeepsDocument.Load(f.Window.CurrentSource);
        Assert.Equal(overlap.Id, saved.Venn!.Texts.Single().AreaId);
        Assert.Empty(model.Nodes);
    }
    [AvaloniaTheory]
    [InlineData("railroad-ebnf-beta")]
    [InlineData("railroad-abnf-beta")]
    [InlineData("railroad-peg-beta")]
    [InlineData("railroad-beta")]
    public void FourNotationsComposeGrammarThroughSelectedExpressions(string directive)
    {
        using var f = new Fixture(directive + "\n");
        WorkspaceControls.OpenCreationForm(f.Window); f.Field<TextBox>("Rule name").Text = "rule"; f.Field<TextBox>("First symbol text").Text = "a"; f.Click("Apply grammar rule");
        var model = f.Canvas.Model!; var first = model.Railroad!.Rules.Single().Expression;
        f.Click("Edit rule expression"); f.Click("Combine with another symbol");
        f.Field<TextBox>("New symbol text").Text = "b"; f.Click("Compose expressions");
        Assert.Equal(RailroadExpressionKind.Sequence, model.Railroad!.Rules[0].Expression.Kind);
        Assert.Equal(first.Id, model.Railroad.Rules[0].Expression.Children[0].Id);
        var saved = MerpeepsDocument.Load(f.Window.CurrentSource);
        Assert.Equal(directive, saved.Kind);
        Assert.Equal(new[] { "a", "b" }, saved.Railroad!.Rules[0].Expression.Children.Select(c => c.Text));
        f.Canvas.Undo(); Assert.Equal(first.Id, model.Railroad!.Rules[0].Expression.Id);
    }
    [AvaloniaFact]
    public void InvalidOverlapSizeRetainsTheDraftAndDoesNotChangeTheModel()
    {
        using var f = new Fixture("venn-beta\nset A:10\nset B:10\nunion A,B:2\n");
        var model = f.Canvas.Model!; var overlap = model.Venn!.Overlaps[0];
        f.Canvas.SelectStructural(new("VennOverlap", overlap.Id)); f.Settle();
        f.Field<TextBox>("Overlap size (blank uses default)").Text = "11"; f.Click("Apply overlap");
        Assert.Equal(2, model.Venn.Overlaps[0].EffectiveSize);
        Assert.Equal("11", f.Field<TextBox>("Overlap size (blank uses default)").Text);
        Assert.False(f.Canvas.History!.CanUndo);
    }
    [AvaloniaFact]
    public void VennPointerSelectsTheActualCommonRegionRatherThanItsBoundingBox()
    {
        using var f = new Fixture("venn-beta\nset A:20\nset B:12\nunion A,B[\"Shared\"]:3\n");
        var scene = f.Canvas.VennLayout.Scene;
        var label = scene.Labels.Single(l => l.Owner.Role == "VennOverlap");
        f.Point(new(label.Bounds.X + label.Bounds.Width / 2, label.Bounds.Y + label.Bounds.Height / 2));
        Assert.Equal("VennOverlap", f.Canvas.SelectedStructuralTarget!.Role);
        Assert.NotNull(f.Field<TextBox>("Overlap size (blank uses default)"));
        var region = scene.Regions.Single(r => r.Owner.Role == "VennOverlap");
        f.Point(new(region.Bounds.X + .01, region.Bounds.Y + .01));
        Assert.NotEqual("VennOverlap", f.Canvas.SelectedStructuralTarget?.Role);
    }
    [AvaloniaFact]
    public void PegLookaheadAndChildOrderUseActualContextualActions()
    {
        using var f = new Fixture("railroad-peg-beta\nrule <- \"a\" \"b\" ;\n");
        var model = f.Canvas.Model!; var root = model.Railroad!.Rules[0].Expression;
        f.Canvas.SelectStructural(new("RailroadExpression", root.Id)); f.Settle();
        f.Click("Move child 2 earlier");
        Assert.Equal("b", model.Railroad!.Rules[0].Expression.Children[0].Text);
        f.Click("Exclude lookahead");
        Assert.Equal(RailroadExpressionKind.NotPredicate, model.Railroad!.Rules[0].Expression.Kind);
        var reopened = MerpeepsDocument.Load(f.Window.CurrentSource);
        Assert.Equal(RailroadExpressionKind.NotPredicate, reopened.Railroad!.Rules[0].Expression.Kind);
    }
    [AvaloniaTheory]
    [InlineData("venn-beta", 1600, 900)]
    [InlineData("venn-beta", 900, 600)]
    [InlineData("railroad-ebnf-beta", 1600, 900)]
    [InlineData("railroad-ebnf-beta", 900, 600)]
    public void SetAndGrammarWorkspacesRetainReadableText(string directive, int width, int height)
    {
        using var f = new Fixture(directive == "venn-beta"
            ? "venn-beta\ntitle Shared skills\nset A[\"Design\"]:20\nset B[\"Engineering\"]:12\nunion A,B[\"Product\"]:3\n  text t[\"Research\"]\n"
            : "railroad-ebnf-beta\ntitle Identifiers\nidentifier = letter (letter | digit)* ;\nletter = \"a\" | \"b\" ;\ndigit = \"0\" | \"1\" ;\n", width, height);
        Assert.True(f.Canvas.Bounds.Width >= width * .5);
        Assert.Equal(1, f.Canvas.Zoom);
        f.Capture($"{directive}-{width}");
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
        public void Point(DiagramPoint p)
        {
            var screen = Canvas.TranslatePoint(new Point(p.X * Canvas.Zoom + Canvas.OffsetX, p.Y * Canvas.Zoom + Canvas.OffsetY), Window)!.Value;
            Window.MouseMove(screen); Window.MouseDown(screen, MouseButton.Left); Window.MouseUp(screen, MouseButton.Left); Settle();
        }
        public void Capture(string name)
        {
            if (Environment.GetEnvironmentVariable("MERPEEPS_SET_GRAMMAR_CAPTURES") is not { Length: > 0 } path) return;
            Directory.CreateDirectory(path);
            using var image = Window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No actual set/grammar frame.");
            image.Save(Path.Combine(path, name + ".png"));
        }
        public void Dispose() { foreach (var child in Window.OwnedWindows.ToArray()) child.Close(); Window.Close(); }
    }
}
