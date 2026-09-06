using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Xunit;

namespace Merpeeps.App.Tests;

/// <summary>
/// Dragging the canvas to move the view.
/// </summary>
/// <remarks>
/// <para>Panning had four gestures and no test that pressed a mouse button.
/// <see cref="CanvasPanningTests"/> asserted in its own summary that middle,
/// space and Ctrl-drag worked, and then only ever turned the wheel — so every
/// drag gesture in the editor was carried by a comment. These drive real
/// pointer events instead.</para>
///
/// <para>The tests exist because of a report that "drag pan is not
/// implemented". It was: the reporter reached for left-drag, got a marquee, and
/// drew the obvious conclusion. Nothing was broken and something was still
/// wrong, which is why the hand tool now makes the capability visible — and why
/// the left-drag cases below are written as deliberate behaviour rather than
/// left to chance.</para>
/// </remarks>
public class CanvasDragPanningTests
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
    /// A point on the canvas, in the window's coordinates.
    /// </summary>
    /// <remarks>
    /// Translated rather than assumed. The canvas is one cell of a seven-column
    /// grid, so canvas-local coordinates passed straight to the window land in
    /// the shape palette — where a press would select a stencil and every
    /// assertion here would be measuring the wrong control.
    /// </remarks>
    private static Point On(MainWindow w, EditorCanvas c, double fx, double fy)
    {
        var local = new Point(c.Bounds.Width * fx, c.Bounds.Height * fy);
        return c.TranslatePoint(local, w) ?? local;
    }

    /// <summary>Press, move and release, returning how far the view moved.</summary>
    private static (double Dx, double Dy) Drag(
        MainWindow w, EditorCanvas c,
        MouseButton button = MouseButton.Left,
        RawInputModifiers mods = RawInputModifiers.None,
        double fromX = 0.6, double fromY = 0.6,
        double byX = 70, double byY = 45)
    {
        var from = On(w, c, fromX, fromY);
        var to = new Point(from.X + byX, from.Y + byY);

        var (x, y) = (c.OffsetX, c.OffsetY);

        w.MouseDown(from, button, mods);
        w.MouseMove(to, mods);
        w.MouseUp(to, button, mods);

        return (c.OffsetX - x, c.OffsetY - y);
    }

    // Qwerty variants, because the plain overloads want a physical key and a
    // key symbol as well; these derive both from the physical key.
    private static void Press(
        MainWindow w, PhysicalKey key, RawInputModifiers mods = RawInputModifiers.None) =>
        w.KeyPressQwerty(key, mods);

    private static void Release(
        MainWindow w, PhysicalKey key, RawInputModifiers mods = RawInputModifiers.None) =>
        w.KeyReleaseQwerty(key, mods);

    // ------------------------------------------------------- the gestures

    [AvaloniaFact]
    public void RightDragPans()
    {
        var (w, c) = Open();

        var (dx, dy) = Drag(w, c, MouseButton.Right);

        Assert.Equal(70, dx, 3);
        Assert.Equal(45, dy, 3);
    }

    [AvaloniaFact]
    public void MiddleDragPans()
    {
        var (w, c) = Open();

        var (dx, dy) = Drag(w, c, MouseButton.Middle);

        Assert.Equal(70, dx, 3);
        Assert.Equal(45, dy, 3);
    }

    [AvaloniaFact]
    public void ControlAndLeftDragPansOnEmptyCanvas()
    {
        var (w, c) = Open();

        var (dx, dy) = Drag(w, c, MouseButton.Left, RawInputModifiers.Control);

        Assert.Equal(70, dx, 3);
        Assert.Equal(45, dy, 3);
    }

    [AvaloniaFact]
    public void HoldingSpaceMakesALeftDragPan()
    {
        var (w, c) = Open();
        c.Focus();
        Press(w, PhysicalKey.Space, RawInputModifiers.None);

        var (dx, dy) = Drag(w, c);

        Assert.Equal(70, dx, 3);
        Assert.Equal(45, dy, 3);

        Release(w, PhysicalKey.Space, RawInputModifiers.None);
    }

    /// <summary>
    /// Releasing space ends the mode, so the next drag selects again.
    /// </summary>
    /// <remarks>
    /// A held modifier that outlives its key would leave the canvas unable to
    /// select at all, with nothing on screen explaining why.
    /// </remarks>
    [AvaloniaFact]
    public void ReleasingSpaceEndsTheMode()
    {
        var (w, c) = Open();
        c.Focus();

        Press(w, PhysicalKey.Space, RawInputModifiers.None);
        Assert.NotEqual(0, Drag(w, c).Dx);

        Release(w, PhysicalKey.Space, RawInputModifiers.None);
        Assert.Equal(0, Drag(w, c).Dx, 3);
    }

    // ------------------------------------------------------- the hand tool

    [AvaloniaFact]
    public void TheHandToolMakesAnOrdinaryLeftDragPan()
    {
        var (w, c) = Open();
        c.Tool = CanvasTool.Hand;

        var (dx, dy) = Drag(w, c);

        Assert.Equal(70, dx, 3);
        Assert.Equal(45, dy, 3);
    }

    /// <summary>
    /// The control for every pan test above.
    /// </summary>
    /// <remarks>
    /// Without it, a canvas that panned on any drag whatsoever would pass all of
    /// them — and would have destroyed marquee selection in the process.
    /// </remarks>
    [AvaloniaFact]
    public void AnOrdinaryLeftDragDoesNotPan()
    {
        var (w, c) = Open();

        var (dx, dy) = Drag(w, c);

        Assert.Equal(0, dx, 3);
        Assert.Equal(0, dy, 3);
    }

    /// <summary>
    /// And what it does instead: select. Asserting only that it fails to pan
    /// would be satisfied by a canvas that ignored the drag entirely.
    /// </summary>
    [AvaloniaFact]
    public void AnOrdinaryLeftDragMarqueeSelects()
    {
        var (w, c) = Open();
        var from = On(w, c, 0.02, 0.02);
        var to = On(w, c, 0.95, 0.95);

        w.MouseDown(from, MouseButton.Left);
        w.MouseMove(to);
        w.MouseUp(to, MouseButton.Left);

        Assert.Equal(2, c.Selection.Count);
    }

    /// <summary>
    /// The hand tool suppresses selection rather than merely adding panning.
    /// </summary>
    /// <remarks>
    /// A mode that panned <em>and</em> marquee-selected would leave a selection
    /// behind after every navigation, so the next Delete would remove whatever
    /// the user had scrolled past.
    /// </remarks>
    [AvaloniaFact]
    public void TheHandToolSelectsNothing()
    {
        var (w, c) = Open();
        c.Tool = CanvasTool.Hand;

        var from = On(w, c, 0.02, 0.02);
        var to = On(w, c, 0.95, 0.95);
        w.MouseDown(from, MouseButton.Left);
        w.MouseMove(to);
        w.MouseUp(to, MouseButton.Left);

        Assert.Empty(c.Selection);
    }

    /// <summary>
    /// Double-clicking in hand mode navigates rather than creating a node.
    /// </summary>
    /// <remarks>
    /// The double-click branch runs before the hit tests, so a hand tool added
    /// further down would have kept creating nodes on the empty canvas someone
    /// was trying to drag across.
    /// </remarks>
    [AvaloniaFact]
    public void TheHandToolDoesNotCreateNodesOnDoubleClick()
    {
        var (w, c) = Open();
        var before = c.Model!.Nodes.Count;
        c.Tool = CanvasTool.Hand;

        var at = On(w, c, 0.6, 0.6);
        w.MouseDown(at, MouseButton.Left);
        w.MouseUp(at, MouseButton.Left);
        w.MouseDown(at, MouseButton.Left);
        w.MouseUp(at, MouseButton.Left);

        Assert.Equal(before, c.Model!.Nodes.Count);
    }

    /// <summary>Nodes stay put while the view moves under them.</summary>
    /// <remarks>
    /// Panning writes to the view offset; dragging a node writes to the model.
    /// Confusing the two would silently edit the document every time someone
    /// scrolled, and the file would gain a diff nobody asked for.
    /// </remarks>
    [AvaloniaFact]
    public void PanningMovesTheViewAndNotTheDiagram()
    {
        var (w, c) = Open();
        var node = c.Model!.Nodes[0];
        var (nx, ny) = (node.X, node.Y);

        c.Tool = CanvasTool.Hand;
        Drag(w, c);

        Assert.Equal(nx, node.X, 3);
        Assert.Equal(ny, node.Y, 3);
    }

    // ------------------------------------------------------------ the keys

    [AvaloniaFact]
    public void HSelectsTheHandToolAndVReturnsToSelecting()
    {
        var (w, c) = Open();
        c.Focus();

        Press(w, PhysicalKey.H, RawInputModifiers.None);
        Assert.Equal(CanvasTool.Hand, c.Tool);

        Press(w, PhysicalKey.V, RawInputModifiers.None);
        Assert.Equal(CanvasTool.Select, c.Tool);
    }

    [AvaloniaFact]
    public void EscapeLeavesTheHandTool()
    {
        var (w, c) = Open();
        c.Focus();
        c.Tool = CanvasTool.Hand;

        Press(w, PhysicalKey.Escape, RawInputModifiers.None);

        Assert.Equal(CanvasTool.Select, c.Tool);
    }

    /// <summary>
    /// Ctrl+H and Ctrl+V are not tool shortcuts.
    /// </summary>
    /// <remarks>
    /// Ctrl+V is paste everywhere in the world. A tool bound to the unmodified
    /// key must not answer the modified one, or the canvas would switch mode
    /// every time somebody tried to paste into it.
    /// </remarks>
    [AvaloniaFact]
    public void ModifiedHAndVAreLeftAlone()
    {
        var (w, c) = Open();
        c.Focus();

        Press(w, PhysicalKey.H, RawInputModifiers.Control);
        Assert.Equal(CanvasTool.Select, c.Tool);

        c.Tool = CanvasTool.Hand;
        Press(w, PhysicalKey.V, RawInputModifiers.Control);
        Assert.Equal(CanvasTool.Hand, c.Tool);
    }

    /// <summary>
    /// Typing a label types letters; it does not switch tools.
    /// </summary>
    /// <remarks>
    /// The inline editor is reached by F2, and "h" is a perfectly ordinary
    /// character in a node name.
    /// </remarks>
    [AvaloniaFact]
    public void RenamingANodeIsNotAToolShortcut()
    {
        var (w, c) = Open();
        c.Focus();
        c.SelectByIds(["a"]);

        Press(w, PhysicalKey.F2, RawInputModifiers.None);
        Press(w, PhysicalKey.H, RawInputModifiers.None);

        Assert.Equal(CanvasTool.Select, c.Tool);
    }

    // --------------------------------------------------------- the toolbar

    /// <summary>The button drives the canvas.</summary>
    [AvaloniaFact]
    public void TheToolbarButtonTurnsPanningOn()
    {
        var (w, c) = Open();
        var toggle = w.FindControl<ToggleButton>("HandToggle")!;

        toggle.IsChecked = true;

        Assert.Equal(CanvasTool.Hand, c.Tool);
        Assert.NotEqual(0, Drag(w, c).Dx);
    }

    /// <summary>
    /// And the canvas drives the button.
    /// </summary>
    /// <remarks>
    /// The half that is easy to miss. A one-way binding leaves the button
    /// unlit while H pans, which is a worse signal than no button — it actively
    /// says the mode is off.
    /// </remarks>
    [AvaloniaFact]
    public void PressingHLightsTheToolbarButton()
    {
        var (w, c) = Open();
        var toggle = w.FindControl<ToggleButton>("HandToggle")!;
        c.Focus();

        Press(w, PhysicalKey.H, RawInputModifiers.None);
        Assert.True(toggle.IsChecked, "H panned the canvas but left the button unlit");

        Press(w, PhysicalKey.Escape, RawInputModifiers.None);
        Assert.False(toggle.IsChecked, "Escape left the canvas selecting but the button lit");
    }

    /// <summary>
    /// The hint names the gestures it actually has, from one place.
    /// </summary>
    /// <remarks>
    /// The line was duplicated: once in XAML and once in the styling toggle,
    /// which restored the older copy the moment anyone flipped it. Editing the
    /// visible one therefore changed nothing that lasted.
    /// </remarks>
    [AvaloniaFact]
    public void TheHintNamesPanningAndSurvivesTheStylingToggle()
    {
        var (w, _) = Open();
        var hint = w.FindControl<TextBlock>("HintText")!;

        Assert.Contains("Pan", hint.Text ?? "");

        var styling = w.FindControl<ToggleButton>("StylingToggle")!;
        styling.IsChecked = false;
        styling.IsChecked = true;

        Assert.Equal(MainWindow.CanvasHint, hint.Text);
    }
}
