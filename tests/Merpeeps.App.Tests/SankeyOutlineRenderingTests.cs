using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Merpeeps.Core;
using Merpeeps.Core.Editing;
using Merpeeps.Core.Layout;
using Xunit;

namespace Merpeeps.App.Tests;

public class SankeyOutlineRenderingTests
{
    [AvaloniaFact]
    public void OutlinedLabelPaintsAGlyphHaloOverTheFlowRibbon()
    {
        const string source = """
            ---
            config:
              sankey:
                showValues: false
                linkColor: "#0033cc"
            ---
            sankey
            Power grid,Industry,3
            Power grid,Other,1
            """;
        var model = MerpeepsDocument.Load(source);
        var canvas = new EditorCanvas();
        var window = new Window { Width = 1100, Height = 650, Content = canvas };
        window.Show();
        try
        {
            canvas.SetModel(model);
            canvas.ResetZoom();
            Dispatcher.UIThread.RunJobs();
            var id = model.Sankey!.Nodes[0].Id;
            var labels = canvas.StructuralScene.Labels.Where(l => l.Owner.Id == id).ToArray();
            var minX = (int)Math.Floor(labels.Min(l => l.Bounds.X) + canvas.OffsetX) - 3;
            var minY = (int)Math.Floor(labels.Min(l => l.Bounds.Y) + canvas.OffsetY) - 3;
            var maxX = (int)Math.Ceiling(labels.Max(l => l.Bounds.X + l.Bounds.Width) + canvas.OffsetX) + 3;
            var maxY = (int)Math.Ceiling(labels.Max(l => l.Bounds.Y + l.Bounds.Height) + canvas.OffsetY) + 3;
            var before = Pixels(canvas);
            canvas.ApplyDiagramEdit(SankeyEditing.SetOption("labelStyle", System.Text.Json.Nodes.JsonValue.Create("outlined")));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(labels.Select(l => l.Bounds), canvas.StructuralScene.Labels.Where(l => l.Owner.Id == id).Select(l => l.Bounds));
            var after = Pixels(canvas);
            var addedOutlinePixels = 0;
            for (var y = minY; y < maxY; y++)
                for (var x = minX; x < maxX; x++)
                {
                    var index = y * 1100 + x;
                    if (White(after[index]) && !White(before[index])) addedOutlinePixels++;
                }
            Assert.True(addedOutlinePixels > 20, $"Expected a real glyph halo over the ribbon, found {addedOutlinePixels} changed white pixels.");
        }
        finally { window.Close(); }
    }

    private static unsafe uint[] Pixels(EditorCanvas canvas)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(1100, 650));
        bitmap.Render(canvas);
        var pixels = new uint[1100 * 650];
        fixed (uint* pointer = pixels)
            bitmap.CopyPixels(new PixelRect(0, 0, 1100, 650), (nint)pointer, pixels.Length * sizeof(uint), 1100 * sizeof(uint));
        return pixels;
    }

    private static bool White(uint pixel) => (pixel & 0xff) > 245 && ((pixel >> 8) & 0xff) > 245 && ((pixel >> 16) & 0xff) > 245;
}
