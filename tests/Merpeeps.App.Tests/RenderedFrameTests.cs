using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Xunit;

namespace Merpeeps.App.Tests;

/// <summary>
/// Renders the window to real pixels and asserts on what is on screen.
///
/// <para>Every other test in this suite asserts what the code <em>intends</em>
/// to draw. That is a weaker claim than it sounds: a bug can sit between the
/// intent and the pixels, and the only thing that caught the last one was a
/// person looking at a screenshot and saying "I still see the icons". These
/// tests close that gap by capturing the frame and counting pixels.</para>
///
/// <para>They are deliberately coarse. Pixel-exact golden images break on font
/// hinting, GPU drivers and Skia upgrades, so instead we assert properties that
/// survive all three: ink is present, ink is absent, colours are distinct.</para>
/// </summary>
public class RenderedFrameTests
{
    private const string Styled = """
        ---
        x-merpeeps:
          spec: 1
          layout:
            nodes:
              a: {x: 40, y: 40, w: 160, h: 60}
              b: {x: 40, y: 180, w: 160, h: 60}
          style:
            nodes:
              a: {fill: '#DAE8FC', stroke: '#6C8EBF', icon: database}
              b: {fill: '#D5E8D4', stroke: '#82B366', icon: server}
        ---
        flowchart TD
            a[Alpha] --> b[Beta]
        """;

    private static (MainWindow Window, EditorCanvas Canvas) Open()
    {
        // Wide enough that the palette, property and source panes still leave the
        // canvas a usable area — at the default 800px the canvas gets ~50px and
        // there is nothing meaningful to sample.
        var window = new MainWindow { Width = 1600, Height = 1000 };
        window.Show();
        window.LoadSourceForTest(Styled);
        window.ApplyTemplate();
        Dispatcher.UIThread.RunJobs();
        return (window, window.CanvasForTest);
    }

    /// <summary>
    /// Capture the window and return the canvas region as a pixel array.
    /// Cropping to the canvas keeps chrome, menus and the source pane out of
    /// the counts, so a change to the toolbar cannot move these numbers.
    /// </summary>
    internal static uint[] CanvasPixels(MainWindow window, EditorCanvas canvas, out int width)
    {
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException(
                "no rendered frame — is UseHeadlessDrawing still true?");

        var origin = canvas.TranslatePoint(default, window) ?? default;
        var bounds = new PixelRect(
            (int)origin.X, (int)origin.Y,
            (int)canvas.Bounds.Width, (int)canvas.Bounds.Height);

        Assert.True(bounds is { Width: > 100, Height: > 100 },
            $"canvas has no usable area to sample: {bounds}");

        width = bounds.Width;
        var pixels = new uint[bounds.Width * bounds.Height];

        using var cropped = new CroppedBitmap(frame, bounds);
        using var target = new RenderTargetBitmap(bounds.Size);
        using (var ctx = target.CreateDrawingContext())
            ctx.DrawImage(cropped, new Rect(bounds.Size.ToSize(1)));

        unsafe
        {
            fixed (uint* p = pixels)
                target.CopyPixels(
                    new PixelRect(default, bounds.Size), (nint)p,
                    pixels.Length * sizeof(uint), bounds.Width * sizeof(uint));
        }

        return pixels;
    }

    /// <summary>
    /// Count distinct colours, ignoring near-duplicates from antialiasing by
    /// quantising each channel. Icons and fills add colours a plain diagram
    /// does not have, so this is a cheap, robust proxy for "is it styled".
    /// </summary>
    private static int DistinctColours(uint[] pixels)
    {
        var seen = new HashSet<uint>();
        foreach (var px in pixels)
        {
            // Drop the low 4 bits per channel: antialiasing produces hundreds of
            // one-off shades that would swamp a raw distinct count.
            seen.Add(px & 0xF0F0F0F0u);
        }
        return seen.Count;
    }

    [AvaloniaFact]
    public void TheCanvasActuallyRendersPixels()
    {
        var (window, canvas) = Open();
        var pixels = CanvasPixels(window, canvas, out _);

        // A blank canvas would be a single background colour. Anything drawn on
        // it — boxes, labels, arrows — pushes this well past one.
        Assert.True(DistinctColours(pixels) > 3,
            "the canvas rendered a flat colour, so nothing was drawn");
    }

    /// <summary>
    /// The regression test for the report that started this: with Merpeeps styling
    /// switched off, the canvas must lose the icon and fill ink, not merely
    /// intend to. Asserted on pixels because the intent was already correct.
    /// </summary>
    [AvaloniaFact]
    public void TurningStylingOffRemovesInkFromTheCanvas()
    {
        var (window, canvas) = Open();

        Assert.True(canvas.ShowMerpeepsStyling, "styling should start on");
        var styled = DistinctColours(CanvasPixels(window, canvas, out _));

        Find<ToggleButton>(window, "StylingToggle").IsChecked = false;
        Dispatcher.UIThread.RunJobs();

        var plain = DistinctColours(CanvasPixels(window, canvas, out _));

        Assert.False(canvas.ShowMerpeepsStyling);
        Assert.True(plain < styled,
            $"the plain view is not visibly plainer: {plain} colours vs {styled} styled. " +
            "Icons or fills are still being drawn with styling off.");
    }

    /// <summary>
    /// Switching styling off and on again must return the same picture. If it
    /// does not, the toggle is mutating something it should only be hiding.
    /// </summary>
    [AvaloniaFact]
    public void TheStylingToggleIsReversibleOnScreen()
    {
        var (window, canvas) = Open();
        var toggle = Find<ToggleButton>(window, "StylingToggle");

        var before = DistinctColours(CanvasPixels(window, canvas, out _));

        toggle.IsChecked = false;
        Dispatcher.UIThread.RunJobs();
        toggle.IsChecked = true;
        Dispatcher.UIThread.RunJobs();

        var after = DistinctColours(CanvasPixels(window, canvas, out _));

        Assert.Equal(before, after);
    }

    /// <summary>
    /// Dark mode has to repaint the diagram, not just the chrome around it. A
    /// canvas that draws with fixed brushes would pass every other test and
    /// still leave black-on-black labels, which is what shipped once already.
    /// </summary>
    [AvaloniaFact]
    public void DarkModeRepaintsTheCanvasItself()
    {
        var (window, canvas) = Open();

        var light = CanvasPixels(window, canvas, out _);
        Find<MenuItem>(window, "MenuThemeDark").RaiseClick();
        Dispatcher.UIThread.RunJobs();
        var dark = CanvasPixels(window, canvas, out _);

        Assert.Equal(light.Length, dark.Length);
        var changed = light.Zip(dark).Count(p => p.First != p.Second);

        // Most of the canvas is background, so switching theme should repaint
        // the clear majority of it.
        Assert.True(changed > light.Length / 2,
            $"only {changed} of {light.Length} canvas pixels changed with the theme");
    }

    private static T Find<T>(MainWindow w, string name) where T : Control =>
        w.FindControl<T>(name)
        ?? throw new InvalidOperationException($"no {typeof(T).Name} named '{name}'");
}
