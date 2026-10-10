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
using Avalonia.Input.Raw;
using Xunit;

namespace Merpeeps.App.Tests;

public class StrategyWorkspaceTests
{
    [AvaloniaFact]
    public void WardleyCreationUsesSemanticCoordinatesAndSaveRetainsIdentity()
    {
        using var f = new Fixture("wardley-beta\n");
        WorkspaceControls.OpenCreationForm(f.Window);
        f.Field<TextBox>("Component name").Text = "Customer";
        f.Field<TextBox>("Visibility (0–1, vertical)").Text = "0.9";
        f.Field<TextBox>("Evolution (0–1, horizontal)").Text = "0.4";
        f.Click("Apply map component");
        var model = f.Canvas.Model!; var component = model.Wardley!.Components.Single();
        Assert.Equal(.9, component.Visibility); Assert.Equal(.4, component.Evolution);
        Assert.Empty(model.Nodes);
        Assert.Equal(component.Id, MerpeepsDocument.Load(f.Window.CurrentSource).Wardley!.Components.Single().Id);
        f.Canvas.Undo(); Assert.Empty(model.Wardley!.Components);
    }
    [AvaloniaFact]
    public void FishboneCreationIsProblemThenNestedCauses()
    {
        using var f = new Fixture("ishikawa-beta\n");
        WorkspaceControls.OpenCreationForm(f.Window); f.Field<TextBox>("Problem").Text = "Blurry photo"; f.Click("Apply problem");
        WorkspaceControls.OpenCreationForm(f.Window); f.Field<TextBox>("Cause label").Text = "Equipment"; f.Click("Apply cause");
        f.Click("Add subcause"); f.Field<TextBox>("Cause label").Text = "Lens"; f.Click("Apply cause");
        var model = f.Canvas.Model!;
        Assert.Equal("Lens", model.Ishikawa!.Causes.Single().Children.Single().Label);
        var saved = MerpeepsDocument.Load(f.Window.CurrentSource);
        Assert.Equal(model.Ishikawa.Causes[0].Id, saved.Ishikawa!.Causes[0].Id);
        Assert.Empty(model.Nodes);
    }
    [AvaloniaFact]
    public void ComplexityItemsCanMoveDomainsWithoutChangingIdentity()
    {
        using var f = new Fixture("cynefin-beta\n");
        WorkspaceControls.OpenCreationForm(f.Window); f.Field<TextBox>("Item label").Text = "Experiment";
        f.Click("Apply domain item");
        var id = f.Canvas.Model!.Cynefin!.Items.Single().Id;
        f.Field<ComboBox>("Complexity domain").SelectedItem = CynefinDomain.Clear;
        f.Click("Apply domain item");
        var reopened = MerpeepsDocument.Load(f.Window.CurrentSource).Cynefin!;
        Assert.Equal(id, reopened.Items.Single().Id); Assert.Equal(CynefinDomain.Clear, reopened.Items[0].Domain);
        f.Canvas.Undo(); Assert.Equal(CynefinDomain.Complex, f.Canvas.Model.Cynefin!.Items[0].Domain);
    }
    [AvaloniaFact]
    public void WardleyPointerDragCommitsDataOnce()
    {
        using var f = new Fixture("wardley-beta\ncomponent A [0.8,0.2]\ncomponent B [0.2,0.8]\n");
        var id = f.Canvas.Model!.Wardley!.Components[0].Id;
        var coordinates = f.Canvas.WardleyLayout.Coordinates;
        var start = coordinates.ToPoint(.8, .2); var end = coordinates.ToPoint(.6, .4);
        var p = f.Screen(start); var q = f.Screen(end);
        f.Window.MouseMove(p); f.Window.MouseDown(p, MouseButton.Left); f.Window.MouseMove(q);
        Assert.Equal(.2, f.Canvas.Model.Wardley.Components[0].Evolution);
        f.Window.MouseUp(q, MouseButton.Left); f.Settle();
        Assert.Equal(.4, f.Canvas.Model.Wardley!.Components[0].Evolution, 7);
        Assert.Equal(.6, f.Canvas.Model.Wardley.Components[0].Visibility!.Value, 7);
        f.Canvas.Undo(); f.Settle(); Assert.Equal(.2, f.Canvas.Model.Wardley!.Components[0].Evolution);
        Assert.False(f.Canvas.History!.CanUndo);
    }
    [AvaloniaFact]
    public void WardleyEscapeCancelsPointerDraftWithoutHistory()
    {
        using var f = new Fixture("wardley-beta\ncomponent A [0.8,0.2]\n");
        var before = f.Window.CurrentSource;
        var coordinates = f.Canvas.WardleyLayout.Coordinates;
        var p = f.Screen(coordinates.ToPoint(.8, .2)); var q = f.Screen(coordinates.ToPoint(.5, .6));
        f.Window.MouseMove(p); f.Window.MouseDown(p, MouseButton.Left); f.Window.MouseMove(q);
        f.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None); f.Window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        f.Window.MouseUp(q, MouseButton.Left); f.Settle();
        Assert.Equal(before, f.Window.CurrentSource); Assert.False(f.Canvas.History!.CanUndo);
    }
    [AvaloniaFact]
    public void WardleyPendingFieldPreventsCanvasValueOverwrite()
    {
        using var f = new Fixture("wardley-beta\ncomponent A [0.8,0.2]\n");
        var id = f.Canvas.Model!.Wardley!.Components[0].Id;
        f.Canvas.SelectStructural(new("WardleyComponent", id)); f.Settle();
        f.Field<TextBox>("Evolution (0–1, horizontal)").Text = "0.7";
        var coordinates = f.Canvas.WardleyLayout.Coordinates;
        var p = f.Screen(coordinates.ToPoint(.8, .2)); var q = f.Screen(coordinates.ToPoint(.5, .6));
        f.Window.MouseMove(p); f.Window.MouseDown(p, MouseButton.Left); f.Window.MouseMove(q); f.Window.MouseUp(q, MouseButton.Left); f.Settle();
        Assert.Equal(.2, f.Canvas.Model.Wardley.Components[0].Evolution); Assert.False(f.Canvas.History!.CanUndo);
    }
    [AvaloniaTheory]
    [InlineData("wardley-beta", 1600, 900)]
    [InlineData("wardley-beta", 900, 600)]
    [InlineData("ishikawa-beta", 1600, 900)]
    [InlineData("ishikawa-beta", 900, 600)]
    [InlineData("cynefin-beta", 1600, 900)]
    [InlineData("cynefin-beta", 900, 600)]
    public void StrategyStatesRenderAtDesktopAndCompactSizes(string directive, int width, int height)
    {
        var source = directive switch
        {
            "wardley-beta" => "wardley-beta\ntitle Value chain\nanchor Customer [0.9,0.6]\ncomponent Product [0.65,0.35] (build)\ncomponent Platform [0.25,0.75] (buy)\nCustomer -> Product\nProduct +> Platform\nevolve Product 0.6\n",
            "ishikawa-beta" => "ishikawa-beta\nBlurry photo\nEquipment\n  Lens\n    Dirty\n  Sensor\nUser\n  Shaky hands\nEnvironment\n  Too dark\n",
            _ => "cynefin-beta\ntitle Delivery decisions\ncomplex\n\"Experiment\"\ncomplicated\n\"Analyze\"\nclear\n\"Follow procedure\"\nchaotic\n\"Respond\"\nconfusion\n\"Investigate\"\ncomplex --> complicated : \"Learned\"\n",
        };
        using var f = new Fixture(source, width, height);
        Assert.True(f.Canvas.Bounds.Width >= width * .5);
        if (width == 900) Assert.True(f.Canvas.Zoom >= .8, $"Strategy labels need a readable initial zoom, not {f.Canvas.Zoom}.");
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
        public Point Screen(DiagramPoint point) => Canvas.TranslatePoint(new(point.X * Canvas.Zoom + Canvas.OffsetX, point.Y * Canvas.Zoom + Canvas.OffsetY), Window)!.Value;
        public void Capture(string name)
        {
            if (Environment.GetEnvironmentVariable("MERPEEPS_STRATEGY_CAPTURES") is not { Length: > 0 } directory) return;
            Directory.CreateDirectory(directory);
            using var image = Window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No real rendered strategy frame.");
            image.Save(Path.Combine(directory, name + ".png"));
        }
        public void Dispose() { foreach (var child in Window.OwnedWindows.ToArray()) child.Close(); Window.Close(); }
    }
}
