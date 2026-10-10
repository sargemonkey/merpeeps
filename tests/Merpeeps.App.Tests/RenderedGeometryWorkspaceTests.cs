using Avalonia.Controls;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Merpeeps.Core;
using Merpeeps.Core.Editing;
using Merpeeps.Core.Model;
using Xunit;

namespace Merpeeps.App.Tests;

public class RenderedGeometryWorkspaceTests
{
    [AvaloniaFact]
    public void ImportedNodeDimensionsSurviveNativeLoadAndSourceGeneration()
    {
        const string source = "---\nx-merpeeps:\n  spec: 1\n  layout:\n    engine: manual\n    nodes:\n      A: {x: 30, y: 40, w: 320, h: 96}\n---\nflowchart LR\nA[Long imported node label]\n";
        using var fixture = new Fixture(source);
        var node = fixture.Window.CanvasForTest.Model!.Nodes.Single();
        Assert.Equal((30d, 40d, 320d, 96d), (node.X, node.Y, node.Width, node.Height));
        var reopened = Core.MerpeepsDocument.Load(fixture.Window.CurrentSource).Nodes.Single();
        Assert.Equal((node.X, node.Y, node.Width, node.Height), (reopened.X, reopened.Y, reopened.Width, reopened.Height));
    }

    [AvaloniaFact]
    public void ImportedPolylineIsSelectableAtItsVisibleBend()
    {
        const string source = "---\nx-merpeeps:\n  spec: 1\n  layout:\n    nodes:\n      A: {x: 0, y: 0, w: 100, h: 50}\n      B: {x: 300, y: 0, w: 100, h: 50}\n    edges:\n      \"A->B#0\":\n        waypoints: [[120, 140], [280, 140]]\n---\nflowchart LR\nA --> B\n";
        using var fixture = new Fixture(source);
        var canvas = fixture.Window.CanvasForTest; canvas.FitToView();
        var at = canvas.TranslatePoint(new(200 * canvas.Zoom + canvas.OffsetX, 140 * canvas.Zoom + canvas.OffsetY), fixture.Window)!.Value;
        fixture.Window.MouseDown(at, MouseButton.Left); fixture.Window.MouseUp(at, MouseButton.Left);
        Assert.Same(canvas.Model!.Edges.Single(), canvas.SelectedEdge);
    }

    [AvaloniaFact]
    public void AdoptedReferenceLabelsAndDimensionsSurviveRealWindowAndHistoryNavigation()
    {
        const string source = "flowchart LR\nA[First] --> B[Second]\n";
        var model = MerpeepsDocument.Load(source);
        var geometry = RenderedGraphGeometry.Parse("""
            {"spec":1,"nodes":{"A":{"x":50.125,"y":60.25,"w":320.5,"h":96.75,"text":"First","label":{"fontFamily":"Segoe UI","fontSize":16,"fontWeight":400,"lines":[{"text":"First","x":180,"y":90,"w":50,"h":20}]}},"B":{"x":500.125,"y":60.25,"w":200.5,"h":96.75,"text":"Second"}},"edges":{}}
            """);
        new AdoptRenderedGeometryCommand(model, geometry).Do(model);
        using var fixture = new Fixture(MerpeepsDocument.SavePositions(source, model));
        var canvas = fixture.Window.CanvasForTest;
        var node = canvas.Model!.Nodes[0];
        Assert.Equal(geometry.Nodes["A"].Bounds.Width, node.Width);
        Assert.NotNull(canvas.Model.RenderedGeometry!.NodeLabel(canvas.Model, node));
        canvas.SelectByIds(["A","B"]);
        canvas.ArrangeSelection(GraphArrangement.Top);
        Assert.False(canvas.History!.CanUndo);
        Assert.Equal(node.Width, MerpeepsDocument.Load(fixture.Window.CurrentSource).Nodes[0].Width);
    }

    [AvaloniaFact]
    public void DenseImportedRouteKeepsVisibleDashGapsInsteadOfRestartingDashesAtEverySample()
    {
        const string source = "flowchart LR\nA --> B\n";
        var model = MerpeepsDocument.Load(source);
        var geometry = new RenderedGraphGeometry();
        geometry.Nodes["A"] = new(new(0, 0, 100, 50), "A", null);
        geometry.Nodes["B"] = new(new(500, 0, 100, 50), "B", null);
        geometry.Edges["A->B#0"] = new(Enumerable.Range(0, 201).Select(i => new DiagramPoint(100 + 2 * i, 25)).ToArray(), "", null);
        new AdoptRenderedGeometryCommand(model, geometry).Do(model);
        using var f = new Fixture(MerpeepsDocument.SavePositions(source, model));
        var canvas = f.Window.CanvasForTest;
        var solid = RenderedFrameTests.CanvasPixels(f.Window, canvas, out var width);
        canvas.Model!.Edges.Single().Kind = EdgeKind.DottedArrow;
        canvas.InvalidateVisual();
        var dashed = RenderedFrameTests.CanvasPixels(f.Window, canvas, out _);
        var y = (int)Math.Round(25 * canvas.Zoom + canvas.OffsetY);
        var left = (int)Math.Round(130 * canvas.Zoom + canvas.OffsetX);
        var right = (int)Math.Round(470 * canvas.Zoom + canvas.OffsetX);
        int InkColumns(uint[] pixels) => Enumerable.Range(left, right - left).Count(x =>
            Enumerable.Range(y - 1, 3).Any(row => Contrast(pixels[row * width + x], pixels[(y + 8) * width + x]) > 75));
        var solidInk = InkColumns(solid);
        var dashedInk = InkColumns(dashed);
        Assert.InRange(solidInk, (right - left) * 4 / 5, right - left);
        Assert.InRange(dashedInk, solidInk / 4, solidInk * 4 / 5);
        static int Contrast(uint a, uint b) => Math.Abs((int)(a & 255) - (int)(b & 255))
            + Math.Abs((int)((a >> 8) & 255) - (int)((b >> 8) & 255))
            + Math.Abs((int)((a >> 16) & 255) - (int)((b >> 16) & 255));
    }

    private sealed class Fixture : IDisposable
    {
        public MainWindow Window { get; } = new() { Width = 1560, Height = 920 };
        public Fixture(string source) { Window.Show(); Window.LoadSourceForTest(source); Dispatcher.UIThread.RunJobs(); }
        public void Dispose() { foreach (var child in Window.OwnedWindows.ToArray()) child.Close(); Window.Close(); }
    }
}
