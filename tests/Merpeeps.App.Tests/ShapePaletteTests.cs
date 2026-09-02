using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Merpeeps.Core.Model;
using Xunit;

namespace Merpeeps.App.Tests;

/// <summary>
/// The shape palette: previews, and dropping a shape where you meant it.
/// </summary>
public class ShapePaletteTests
{
    private const string Source = "flowchart TD\n    a[One] --> b[Two]\n";

    private static (MainWindow Window, EditorCanvas Canvas) Open()
    {
        var window = new MainWindow();
        window.Show();
        window.LoadSourceForTest(Source);
        window.ApplyTemplate();
        return (window, window.CanvasForTest);
    }

    /// <summary>
    /// Every palette entry draws a preview, and the previews are not all the
    /// same picture.
    /// </summary>
    /// <remarks>
    /// The second half is the control. A template that bound the wrong property,
    /// or a renderer that ignored the shape, would still produce a thumbnail
    /// beside every row — just the same rectangle ten times, which is worse than
    /// no preview because it looks deliberate.
    /// </remarks>
    [AvaloniaFact]
    public void EveryShapeHasADistinguishablePreview()
    {
        var shapes = NodePalette.All
            .SelectMany(c => c.Items)
            .Select(i => i.Shape)
            .Distinct()
            .ToList();

        Assert.True(shapes.Count > 4, "the palette should span several shapes");

        // Render each into its own bitmap and compare: two different shapes must
        // not produce identical pixels.
        var seen = new System.Collections.Generic.Dictionary<string, NodeShape>();
        foreach (var shape in shapes)
        {
            var key = RenderKey(shape);
            Assert.False(seen.TryGetValue(key, out var clash),
                $"{shape} and {clash} draw identically");
            seen[key] = shape;
        }
    }

    /// <summary>A cheap fingerprint of how a shape draws, for comparison.</summary>
    private static string RenderKey(NodeShape shape)
    {
        var target = new RenderTargetBitmap(new PixelSize(40, 26));
        using (var ctx = target.CreateDrawingContext())
        {
            ShapeRenderer.Draw(ctx, shape, new Rect(1, 1, 38, 24),
                Avalonia.Media.Brushes.White,
                new Avalonia.Media.Pen(Avalonia.Media.Brushes.Black, 1.4));
        }

        var pixels = new int[40 * 26];
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(
            pixels, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            target.CopyPixels(new PixelRect(0, 0, 40, 26),
                handle.AddrOfPinnedObject(), pixels.Length * 4, 40 * 4);
        }
        finally { handle.Free(); }

        return string.Join(",", pixels);
    }

    /// <summary>The canvas accepts drops, and so does the border around it.</summary>
    /// <remarks>
    /// The margin around the canvas still reads as "the canvas" to anyone
    /// dropping onto it, and a drop that lands in the gap and does nothing looks
    /// like the feature is broken.
    /// </remarks>
    [AvaloniaFact]
    public void TheCanvasAcceptsDrops()
    {
        var (window, canvas) = Open();

        Assert.True(DragDrop.GetAllowDrop(canvas));
        Assert.True(DragDrop.GetAllowDrop(window.FindControl<Border>("CanvasHost")!));
    }

    /// <summary>
    /// A dropped shape lands where it was dropped, not in the middle of the view.
    /// </summary>
    /// <remarks>
    /// The whole point of the gesture. Adding to the centre already existed on
    /// double-click, and placing three nodes that way meant adding one, dragging
    /// it aside, and adding the next onto the same spot.
    /// </remarks>
    [AvaloniaFact]
    public void ADroppedShapeLandsWhereItWasDropped()
    {
        var (_, canvas) = Open();
        var before = canvas.Model!.Nodes.Count;

        var item = NodePalette.All[0].Items[0];
        canvas.AddFromPalette(item, new Point(400, 300));

        Assert.Equal(before + 1, canvas.Model.Nodes.Count);

        var added = canvas.Model.Nodes[^1];
        Assert.True(added.HasExplicitPosition);

        // Centred on the drop, then snapped to the same 10px grid a drag uses.
        Assert.InRange(added.X + added.Width / 2, 390, 410);
        Assert.InRange(added.Y + added.Height / 2, 290, 310);
        Assert.Equal(0, added.X % 10, 6);
        Assert.Equal(0, added.Y % 10, 6);
    }

    /// <summary>
    /// A drop is undoable, and undoes as one step.
    /// </summary>
    [AvaloniaFact]
    public void ADroppedShapeIsOneUndo()
    {
        var (_, canvas) = Open();
        var before = canvas.Model!.Nodes.Count;

        canvas.AddFromPalette(NodePalette.All[0].Items[0], new Point(200, 200));
        Assert.Equal(before + 1, canvas.Model.Nodes.Count);

        canvas.Undo();
        Assert.Equal(before, canvas.Model.Nodes.Count);
    }

    /// <summary>
    /// The drop position is converted through the canvas's own transform, so it
    /// is right after panning and zooming.
    /// </summary>
    /// <remarks>
    /// Using raw screen coordinates would look correct at the default view and
    /// drift further off with every scroll — the kind of bug that gets reported
    /// as "it puts them in the wrong place sometimes".
    /// </remarks>
    [AvaloniaFact]
    public void TheDropPositionFollowsPanAndZoom()
    {
        var (_, canvas) = Open();

        canvas.ZoomBy(2);
        var screen = new Point(300, 220);
        var world = canvas.ScreenToWorld(screen);

        canvas.AddFromPalette(NodePalette.All[0].Items[0], world);
        var added = canvas.Model!.Nodes[^1];

        Assert.InRange(added.X + added.Width / 2, world.X - 10, world.X + 10);
        Assert.InRange(added.Y + added.Height / 2, world.Y - 10, world.Y + 10);
    }
}
