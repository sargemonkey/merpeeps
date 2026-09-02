using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Xunit;

namespace Merpeeps.App.Tests;

/// <summary>
/// Moving around the canvas.
/// </summary>
/// <remarks>
/// Panning existed before these tests — middle-drag, space-drag, Ctrl-drag —
/// but the wheel zoomed, which is the minority convention. Figma, draw.io,
/// Excalidraw and Visio all scroll on the wheel, so a canvas that lurched in and
/// out when someone meant to look further down read as "there is no panning".
/// A gesture nobody reaches for is a gesture nobody has.
/// </remarks>
public class CanvasPanningTests
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

    private static void Wheel(MainWindow w, EditorCanvas c, Vector delta, RawInputModifiers mods = RawInputModifiers.None)
    {
        var centre = new Point(c.Bounds.Width / 2, c.Bounds.Height / 2);
        w.MouseWheel(centre, delta, mods);
    }

    [AvaloniaFact]
    public void TheWheelScrollsVertically()
    {
        var (w, c) = Open();
        var (x, y, zoom) = (c.OffsetX, c.OffsetY, c.Zoom);

        Wheel(w, c, new Vector(0, -1));

        Assert.NotEqual(y, c.OffsetY);
        Assert.Equal(x, c.OffsetX);
        Assert.Equal(zoom, c.Zoom);   // scrolling must not zoom
    }

    [AvaloniaFact]
    public void ShiftAndTheWheelScrollsSideways()
    {
        var (w, c) = Open();
        var (x, y) = (c.OffsetX, c.OffsetY);

        Wheel(w, c, new Vector(0, -1), RawInputModifiers.Shift);

        Assert.NotEqual(x, c.OffsetX);
        Assert.Equal(y, c.OffsetY);
    }

    [AvaloniaFact]
    public void ControlAndTheWheelStillZooms()
    {
        var (w, c) = Open();
        var zoom = c.Zoom;

        Wheel(w, c, new Vector(0, 1), RawInputModifiers.Control);

        Assert.True(c.Zoom > zoom, "Ctrl+wheel up should zoom in");
    }

    /// <summary>
    /// Scrolling up then down returns to where it started.
    /// </summary>
    /// <remarks>
    /// Cheap, but it catches the two mistakes this kind of code invites: a sign
    /// error that only moves one way, and a step that scales with zoom so a
    /// round trip drifts.
    /// </remarks>
    [AvaloniaFact]
    public void ScrollingBackReturnsToWhereItStarted()
    {
        var (w, c) = Open();
        var y = c.OffsetY;

        Wheel(w, c, new Vector(0, 3));
        Assert.NotEqual(y, c.OffsetY);

        Wheel(w, c, new Vector(0, -3));
        Assert.Equal(y, c.OffsetY, 3);
    }

    /// <summary>
    /// The scroll step is in screen pixels, so it moves the same distance
    /// however far the view is zoomed in.
    /// </summary>
    [AvaloniaFact]
    public void TheScrollStepDoesNotChangeWithZoom()
    {
        var (w, c) = Open();

        var before = c.OffsetY;
        Wheel(w, c, new Vector(0, -1));
        var atDefaultZoom = c.OffsetY - before;

        c.ZoomBy(3);
        before = c.OffsetY;
        Wheel(w, c, new Vector(0, -1));
        var atHighZoom = c.OffsetY - before;

        Assert.Equal(atDefaultZoom, atHighZoom, 3);
    }

    /// <summary>Ctrl+wheel keeps the point under the cursor fixed.</summary>
    /// <remarks>
    /// Zooming about the window centre instead sends whatever was being examined
    /// off the edge, which is the difference between zoom that helps and zoom
    /// that has to be undone with a pan.
    /// </remarks>
    [AvaloniaFact]
    public void ZoomingKeepsThePointUnderTheCursorStill()
    {
        var (w, c) = Open();
        var at = new Point(120, 90);

        var worldBefore = c.ToWorldForTest(at);
        w.MouseWheel(at, new Vector(0, 1), RawInputModifiers.Control);
        var worldAfter = c.ToWorldForTest(at);

        Assert.Equal(worldBefore.X, worldAfter.X, 3);
        Assert.Equal(worldBefore.Y, worldAfter.Y, 3);
    }
}
