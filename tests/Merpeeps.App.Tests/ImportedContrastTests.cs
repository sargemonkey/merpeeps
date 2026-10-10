using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Merpeeps.Core;
using Merpeeps.Core.Editing;
using Merpeeps.Core.Model;
using Xunit;

namespace Merpeeps.App.Tests;

public class ImportedContrastTests
{
    [AvaloniaFact]
    public void ExportChoicesAreViewOnlyAndTransparentExportKeepsAuthoredFills()
    {
        const string source = "flowchart LR\nA[First] --> B[Second]\n";
        var window = new MainWindow();
        window.Show(); window.LoadSourceForTest(source); Dispatcher.UIThread.RunJobs();
        try
        {
            var before = window.CurrentSource;
            window.FindControl<MenuItem>("MenuSvgDocument")!.IsChecked = false;
            window.FindControl<MenuItem>("MenuSvgDark")!.IsChecked = true;
            window.FindControl<MenuItem>("MenuSvgTransparent")!.IsChecked = true;
            Assert.Contains("data-canvas-background=\"transparent\"", window.SvgForExport());
            Assert.Equal(before, window.CurrentSource);
            Assert.False(window.CanvasForTest.History!.CanUndo);
            window.FindControl<MenuItem>("MenuThemeDark")!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(before, window.CurrentSource);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false, "#FFFFFF")]
    [InlineData(true, "#FFFFFF")]
    [InlineData(false, "#FFFFFF80")]
    [InlineData(true, "#FFFFFF80")]
    public void ImportedWhiteGroupTitleKeepsReadableActualPixelsUnderEitherAppTheme(bool dark, string fill)
    {
        const string source = "flowchart LR\nsubgraph G[Owned]\nA[Node]\nend\n";
        var model = MerpeepsDocument.Load(source);
        var geometry = RenderedGraphGeometry.Parse("""
            {"spec":1,"nodes":{"A":{"x":80,"y":120,"w":240,"h":64,"text":"Node"}},
             "groups":{"G":{"x":0,"y":0,"w":400,"h":240,"text":"Owned","members":["A"],"fill":"#FFFFFF","stroke":"#64748B","strokeWidth":1,
             "label":{"fontFamily":"Segoe UI","fontSize":20,"fontWeight":400,"lines":[{"text":"Owned","x":120,"y":20,"w":120,"h":26}]}}},"edges":{}}
            """.Replace("\"#FFFFFF\"", "\"" + fill + "\""));
        new AdoptRenderedGeometryCommand(model, geometry).Do(model);
        var saved = MerpeepsDocument.SavePositions(source, model);
        var window = new MainWindow(new StartupOptions(null, dark ? ThemeVariant.Dark : ThemeVariant.Light)) { Width = 1560, Height = 920 };
        window.Show(); window.LoadSourceForTest(saved); Dispatcher.UIThread.RunJobs();
        try
        {
            var canvas = window.CanvasForTest;
            var pixels = RenderedFrameTests.CanvasPixels(window, canvas, out var width);
            var background = DiagramContrast.Composite(DiagramContrast.Parse(fill), DiagramContrast.Parse(dark ? "#0E0B1A" : "#F6F5FA"));
            var left = (int)(120 * canvas.Zoom + canvas.OffsetX);
            var top = (int)(20 * canvas.Zoom + canvas.OffsetY);
            var right = (int)(240 * canvas.Zoom + canvas.OffsetX);
            var bottom = (int)(46 * canvas.Zoom + canvas.OffsetY);
            var ratios = Enumerable.Range(top, bottom - top).SelectMany(y =>
                Enumerable.Range(left, right - left).Select(x => Against(pixels[y * width + x], background))).ToArray();
            Assert.True(ratios.Max() >= 4.5, $"Imported group title has no readable text pixels: contrast={ratios.Max():F2}, dark={dark}.");
            Assert.Equal(saved, window.CurrentSource);
            Assert.False(canvas.History!.CanUndo);
        }
        finally { window.Close(); }
    }

    private static double Against(uint pixel, ClassColor background) =>
        DiagramContrast.Ratio(new((byte)(pixel >> 16), (byte)(pixel >> 8), (byte)pixel), background);
}
