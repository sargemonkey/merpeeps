using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Merpeeps.Core.Editing;
using Merpeeps.Core.Model;
using Merpeeps.Core.Parsing;

namespace Merpeeps.App;

/// <summary>
/// What a left-drag on the canvas means.
/// </summary>
/// <remarks>
/// <para>Panning was always available on right-drag, middle-drag, Ctrl-drag and
/// space-drag, and left-drag on empty canvas marquee-selects — which is what
/// Figma, Visio, draw.io and Excalidraw all do. It was still reported as "drag
/// pan is not implemented", because someone reached for the gesture they knew,
/// got a selection rectangle, and concluded the feature was absent.</para>
///
/// <para>A hint in the status bar had already documented it. That is the lesson:
/// a gesture with no visible control is a gesture people decide you do not have,
/// however well it is described in text nobody reads. So panning gets a mode
/// with a button and a key, alongside the gestures — which stay, because the
/// people who already know them should not have to learn a mode.</para>
/// </remarks>
public enum CanvasTool
{
    /// <summary>Left-drag selects, moves and marquees.</summary>
    Select,

    /// <summary>Left-drag pans. The canvas becomes purely navigational.</summary>
    Hand,
}

/// <summary>
/// The editing surface: select, multi-select, drag, create, connect, rename
/// and delete.
///
/// <para>All structural changes go through <see cref="EditHistory"/> so undo
/// and redo behave uniformly; the canvas never mutates the model directly.</para>
/// </summary>
public sealed partial class EditorCanvas : Control
{
    private DiagramModel? _model;
    private EditHistory? _history;

    private readonly HashSet<DiagramNode> _selection = new();

    private DiagramNode? _dragging;
    private Vector _dragOffset;
    private Point? _panFrom;
    private double _panStartX, _panStartY;

    private DiagramNode? _connectFrom;
    private Point _connectTo;
    private DiagramNode? _connectTarget;

    private Point? _marqueeStart;
    private Rect _marquee;
    private DiagramNode[] _marqueeNodes = [];
    private DiagramSubgraph[] _marqueeGroups = [];

    private DiagramNode? _editing;
    private DiagramEdge? _editingEdge;
    private string _editBuffer = "";
    private bool _editReplaceOnFirstKey;
    private bool _ignoreGalleryDoubleClick;

    internal void IgnoreGalleryDoubleClick() => _ignoreGalleryDoubleClick = true;

    public double Zoom { get; private set; } = 1.0;
    public double OffsetX { get; private set; }
    public double OffsetY { get; private set; }

    public DiagramModel? Model => _model;
    public EditHistory? History => _history;
    public IReadOnlyCollection<DiagramNode> Selection => _selection;
    public DiagramNode? Selected => _selection.Count == 1 && _groupSelection.Count == 0 ? _selection.First() : null;

    /// <summary>The selected link, or null.</summary>
    /// <remarks>
    /// <para>Separate from the node selection rather than folded into it. The two
    /// support genuinely different operations — a node can be dragged, styled and
    /// given an icon; a link can be relabelled and restyled — and a single
    /// selection holding either kind pushes that difference into a type test at
    /// every use.</para>
    ///
    /// <para>Selecting one clears the other, because a property pane showing both
    /// at once has nothing coherent to say.</para>
    /// </remarks>
    public DiagramEdge? SelectedEdge
    {
        get => _selectedEdge;
        private set
        {
            if (ReferenceEquals(_selectedEdge, value)) return;
            _selectedEdge = value;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private DiagramEdge? _selectedEdge;

    public event EventHandler? ModelChanged;
    public event EventHandler? SelectionChanged;

    /// <summary>
    /// Colours the canvas draws with. Swapping this is how dark mode works.
    ///
    /// <para>Named <c>CanvasColours</c> rather than <c>Theme</c> because
    /// <see cref="StyledElement.Theme"/> already exists and means a control
    /// template — shadowing it would be a trap for the next reader.</para>
    /// </summary>
    public CanvasTheme CanvasColours
    {
        get => _theme;
        set { _theme = value; InvalidateVisual(); }
    }
    private CanvasTheme _theme = CanvasTheme.Light;

    /// <summary>
    /// Colours for nodes that carry no explicit styling, taken from the
    /// document's mermaid theme.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="CanvasColours"/> because the two answer
    /// different questions. The canvas theme is the user's preference for this
    /// application — chrome, gridlines, whether they like dark mode. The diagram
    /// theme is a property of the file that every other renderer will honour
    /// too. Someone can perfectly reasonably edit a dark-themed diagram in a
    /// light-themed editor, and conflating the two would make that impossible.
    ///
    /// <para>Null means the document names no theme, and the canvas keeps its
    /// own neutral defaults.</para>
    /// </remarks>
    public DiagramPalette.Entry? DiagramTheme
    {
        get => _diagramTheme;
        set { _diagramTheme = value; InvalidateVisual(); }
    }

    private DiagramPalette.Entry? _diagramTheme;

    /// <summary>
    /// When false, per-node styling stored in Merpeeps is ignored and everything
    /// is drawn with the theme's defaults — which is what a stock mermaid
    /// renderer would show. Useful for checking at a glance that the diagram
    /// still reads correctly without the extra layer.
    /// </summary>
    public bool ShowMerpeepsStyling
    {
        get => _showStyling;
        set { _showStyling = value; InvalidateVisual(); }
    }
    private bool _showStyling = true;

    /// <summary>True while space is held, which turns a drag into a pan.</summary>
    private bool _spaceHeld;

    /// <summary>
    /// What a left-drag does. Setting it updates the cursor and raises
    /// <see cref="ToolChanged"/> so the toolbar button can follow.
    /// </summary>
    public CanvasTool Tool
    {
        get => _tool;
        set
        {
            if (_tool == value) return;
            EndGraphDrag(cancel: true);
            _tool = value;

            // Leaving the hand tool mid-drag would otherwise strand the pan,
            // because the release that ends it arrives in the other mode.
            _panFrom = null;

            UpdateCursor();
            ToolChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        }
    }
    private CanvasTool _tool = CanvasTool.Select;

    /// <summary>Raised when <see cref="Tool"/> changes, however it was changed.</summary>
    /// <remarks>
    /// The keyboard can change the tool as well as the button, so the button
    /// has to be told rather than assumed to be the only source of truth —
    /// otherwise pressing H leaves a toggle that says the canvas is still
    /// selecting while it pans.
    /// </remarks>
    public event EventHandler? ToolChanged;

    /// <summary>True when a drag would pan rather than select.</summary>
    private bool Panning => _tool == CanvasTool.Hand || _spaceHeld;

    /// <summary>
    /// Point the cursor at whatever the next drag will actually do.
    /// </summary>
    /// <remarks>
    /// One place rather than five. The cursor was previously assigned wherever
    /// a mode changed, which was already easy to get wrong with a single
    /// modifier — every new state multiplies the assignments that have to agree,
    /// and the failure is a canvas stuck showing a gesture it is no longer in.
    /// </remarks>
    private void UpdateCursor() =>
        Cursor =
            _panFrom is not null ? new Cursor(StandardCursorType.SizeAll)
            : Panning ? new Cursor(StandardCursorType.Hand)
            : Cursor.Default;

    private static readonly Typeface Face = new(FontFamily.Default);

    private const double HandleRadius = 6;

    public EditorCanvas()
    {
        Focusable = true;
        ClipToBounds = true;
        // Space-held panning has no key-up if focus leaves mid-gesture, so the
        // modifier is cleared here too rather than stranding the canvas.
        LostFocus += (_, _) => ReleasePanModifier();
    }

    public void SetModel(DiagramModel model, GanttEvaluationContext? evaluationContext = null)
    {
        EndGraphDrag(cancel: true);
        ClearStructuralState();
        _ganttContext = model.Gantt is null ? null : evaluationContext ?? CaptureGanttContext();
        ClearDataState();
        ClearChartState();
        _sequenceCacheModel = null; _sequenceMessageSource = null; _selectedSequence = null;
        ResetClassConnection();
        _entityConnectionSource = null; _entityReconnect = null; _selectedGroup = null;
        Core.Parsing.DiagramAdapters.ForModel(model);
        _model = model;
        _history = new EditHistory(model);
        _selection.Clear();
        _selectedEdge = null;
        _marqueeStart = null; _marqueeNodes = []; _marqueeGroups = [];
        _editing = null;
        _editingEdge = null;
        MeasureLabels();
        if (model.Nodes.Count == 0)
        {
            Zoom = 1;
            OffsetX = 40;
            OffsetY = 40;
        }
        if (model.Gantt is not null || IsStrategyDiagram || IsSetGrammarDiagram || IsOwnershipDiagram || IsWorkflowDiagram) { Zoom = 1; OffsetX = 20; OffsetY = 20; }
        else FitToView();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    public void ClearModel()
    {
        EndGraphDrag(cancel: true);
        ClearStructuralState();
        ClearDataState();
        ClearChartState();
        _sequenceCacheModel = null; _sequenceMessageSource = null; _selectedSequence = null;
        ResetClassConnection();
        _entityConnectionSource = null; _entityReconnect = null; _selectedGroup = null;
        _model = null;
        _history = null;
        _selection.Clear();
        _selectedEdge = null;
        _dragging = null;
        _connectFrom = null;
        _connectTarget = null;
        _panFrom = null;
        _marqueeStart = null;
        _marqueeNodes = []; _marqueeGroups = [];
        _editing = null;
        _editingEdge = null;
        _spaceHeld = false;
        UpdateCursor();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    public void MeasureLabels()
    {
        if (_model is null) return;
        foreach (var n in _model.Nodes) MeasureNode(n);
    }

    /// <summary>
    /// Size a node to its text so labels are never clipped.
    ///
    /// <para>Icon width is reserved whenever the node <em>has</em> an icon,
    /// not whenever one is currently drawn. Size is a property of the document
    /// — it is written to the file as <c>w</c>/<c>h</c> — so letting a view
    /// toggle change it would make looking at the diagram edit it.</para>
    /// </summary>
    private static void MeasureNode(DiagramNode n)
    {
        if (n.Class is not null || n.Entity is not null || n.HasExplicitSize) return;
        // Any brush works here — only the measured extent is used, and that
        // does not depend on colour. Kept static so measuring never needs the
        // theme, which would tie sizing to appearance for no reason.
        var ft = new FormattedText(n.Display, CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, Face, 13, Brushes.Black);
        var extra = IconVocabulary.IsKnown(n.Icon) ? 42 : 0;   // glyph plus its gutter
        var w = Math.Max(90, Math.Ceiling(ft.Width) + 34 + extra);
        var h = Math.Max(46, Math.Ceiling(ft.Height) + 24);
        if (n.Shape is NodeShape.Circle or NodeShape.DoubleCircle) w = h = Math.Max(w, h);
        n.Width = w;
        n.Height = h;
    }

    /// <summary>Apply presentation to the current selection.</summary>
    public void ApplyStyle(StyleNodesCommand.Style style)
    {
        if (_history is null || _selection.Count == 0) return;
        _history.Apply(new StyleNodesCommand(_selection.ToList(), style));
        foreach (var n in _selection) MeasureNode(n);
        ModelChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    public void FitToView()
    {
        EndGraphDrag(cancel: true);
        if (IsWorkflowDiagram || IsStrategyDiagram || IsSetGrammarDiagram || IsOwnershipDiagram || _model?.Requirements is not null || _model?.GitDiagram is not null || _model?.Sankey is not null || _model?.Treemap is not null || _model?.Blocks is not null || _model?.Architecture is not null || _model?.C4 is not null || _model?.Gantt is not null)
        {
            _structuralCacheModel = null;
            var scene = StructuralScene;
            if (Bounds.Width < 2 || Bounds.Height < 2) return;
            Zoom = Math.Clamp(Math.Min((Bounds.Width - 40) / scene.Width, (Bounds.Height - 40) / scene.Height), .08, 1.5);
            OffsetX = (Bounds.Width - scene.Width * Zoom) / 2; OffsetY = (Bounds.Height - scene.Height * Zoom) / 2;
            InvalidateVisual(); return;
        }
        if (_model?.Outline is not null || _model?.Temporal is not null || _model?.Packet is not null || _model?.Kanban is not null)
        {
            _dataCacheModel = null;
            var scene = DataScene.Scene;
            if (Bounds.Width < 2 || Bounds.Height < 2) return;
            Zoom = Math.Clamp(Math.Min((Bounds.Width - 40) / scene.Width, (Bounds.Height - 40) / scene.Height), .08, 1.5);
            OffsetX = (Bounds.Width - scene.Width * Zoom) / 2; OffsetY = (Bounds.Height - scene.Height * Zoom) / 2;
            InvalidateVisual(); return;
        }
        if (_model?.Chart is not null)
        {
            _chartCacheModel = null;
            var scene = ChartScene.Scene;
            if (Bounds.Width < 2 || Bounds.Height < 2) return;
            Zoom = Math.Clamp(Math.Min((Bounds.Width - 40) / scene.Width, (Bounds.Height - 40) / scene.Height), .08, 1.5);
            OffsetX = (Bounds.Width - scene.Width * Zoom) / 2; OffsetY = (Bounds.Height - scene.Height * Zoom) / 2;
            InvalidateVisual(); return;
        }
        if (_model?.Sequence is not null)
        {
            var scene = SequenceScene;
            var available = Bounds.Size;
            if (available.Width < 2 || available.Height < 2) return;
            Zoom = Math.Clamp(Math.Min((available.Width - 40) / scene.Width, (available.Height - 40) / scene.Height), .08, 1.5);
            OffsetX = (available.Width - scene.Width * Zoom) / 2;
            OffsetY = (available.Height - scene.Height * Zoom) / 2;
            InvalidateVisual();
            return;
        }
        if (_model is null || _model.Nodes.Count == 0 && (_model.EntityOptions is null || _model.Subgraphs.Count == 0)) return;
        var b = WorldBounds().Inflate(40);
        if (Bounds.Width < 2 || Bounds.Height < 2) { Zoom = 1; return; }
        Zoom = Math.Clamp(Math.Min(Bounds.Width / b.Width, Bounds.Height / b.Height), 0.1, 2.5);
        OffsetX = (Bounds.Width - b.Width * Zoom) / 2 - b.X * Zoom;
        OffsetY = (Bounds.Height - b.Height * Zoom) / 2 - b.Y * Zoom;
        InvalidateVisual();
    }

    private Rect WorldBounds()
    {
        if (_model is null || _model.Nodes.Count == 0 && !_model.Subgraphs.Any(g => GraphGroupGeometry.IsVisible(_model, g))) return new Rect(0, 0, 1, 1);
        double x0 = double.MaxValue, y0 = double.MaxValue,
               x1 = double.MinValue, y1 = double.MinValue;
        foreach (var n in _model.Nodes)
        {
            x0 = Math.Min(x0, n.X); y0 = Math.Min(y0, n.Y);
            x1 = Math.Max(x1, n.X + n.Width); y1 = Math.Max(y1, n.Y + n.Height);
        }
        foreach (var group in _model.Subgraphs.Where(g => GraphGroupGeometry.IsVisible(_model, g)))
        {
            var r = GraphGroupGeometry.Bounds(_model, group);
            x0 = Math.Min(x0, r.X); y0 = Math.Min(y0, r.Y);
            x1 = Math.Max(x1, r.X + r.Width); y1 = Math.Max(y1, r.Y + r.Height);
        }
        if (_model.EntityOptions is not null)
        {
            foreach (var point in _model.Edges.SelectMany(e => EntityDiagramGeometry.Route(_model, e)))
            {
                x0 = Math.Min(x0, point.X); y0 = Math.Min(y0, point.Y);
                x1 = Math.Max(x1, point.X); y1 = Math.Max(y1, point.Y);
            }
        if (_model.RenderedGeometry?.Caption is { } caption)
            foreach (var line in caption.Lines)
            {
                x0 = Math.Min(x0, line.Bounds.X); y0 = Math.Min(y0, line.Bounds.Y);
                x1 = Math.Max(x1, line.Bounds.X + line.Bounds.Width); y1 = Math.Max(y1, line.Bounds.Y + line.Bounds.Height);
            }
        }
        else if (_model.ClassOptions is null)
            foreach (var point in _model.Edges.SelectMany(e => GraphEdgeGeometry.Route(_model, e)))
            {
                x0 = Math.Min(x0, point.X); y0 = Math.Min(y0, point.Y);
                x1 = Math.Max(x1, point.X); y1 = Math.Max(y1, point.Y);
            }
        return new Rect(x0, y0, Math.Max(1, x1 - x0), Math.Max(1, y1 - y0));
    }

    private Point ToWorld(Point p) => new((p.X - OffsetX) / Zoom, (p.Y - OffsetY) / Zoom);

    /// <summary>Screen-to-world, for tests that check what zoom does to a point.</summary>
    internal Point ToWorldForTest(Point p) => ToWorld(p);

    // ------------------------------------------------------------- rendering

    public override void Render(DrawingContext ctx)
    {
        ctx.FillRectangle(_theme.Background, new Rect(Bounds.Size));
        if (_model is null) return;

        using (ctx.PushTransform(
            Matrix.CreateScale(Zoom, Zoom) * Matrix.CreateTranslation(OffsetX, OffsetY)))
        {
            if (_model.Sequence is not null) { DrawSequence(ctx); return; }
            if (_model.Chart is not null) { DrawChart(ctx); return; }
            if (_model.Outline is not null || _model.Temporal is not null || _model.Packet is not null || _model.Kanban is not null) { DrawDataDiagram(ctx); return; }
            if (IsWorkflowDiagram || IsStrategyDiagram || IsSetGrammarDiagram || IsOwnershipDiagram || _model.Requirements is not null || _model.GitDiagram is not null || _model.Sankey is not null || _model.Treemap is not null || _model.Blocks is not null || _model.Architecture is not null || _model.C4 is not null || _model.Gantt is not null) { DrawStructural(ctx); return; }
            DrawSubgraphs(ctx);
            foreach (var e in _model.Edges) DrawEdge(ctx, e);
            foreach (var n in _model.Nodes) DrawNode(ctx, n);

            if (_connectFrom is not null) DrawGhostLink(ctx);
            if (_classRelationshipSource is not null) DrawClassConnection(ctx);
            if (Selected is { } sel && _dragging is null && !IsEditing)
                DrawHandles(ctx, sel);
            if (_editing is not null) DrawLabelEditor(ctx, _editing);
            if (_editingEdge is not null) DrawEdgeLabelEditor(ctx, _editingEdge);
        }

        if (_marqueeStart is not null)
        {
            ctx.FillRectangle(_theme.MarqueeFill, _marquee);
            ctx.DrawRectangle(null, _theme.MarqueePen, _marquee);
        }
    }

    private void DrawSubgraphs(DrawingContext ctx)
    {
        if (_model is null) return;
        if (_model.RenderedGeometry?.Caption is { } caption) DrawRenderedText(ctx, caption, _theme.Label);
        if (_model.EntityOptions is not null) { DrawEntityGroups(ctx); return; }
        if (_model.ClassOptions is not null) { DrawClassNamespaces(ctx); return; }
        foreach (var sg in _model.Subgraphs)
        {
            var bounds = GraphGroupGeometry.Bounds(_model, sg);
            var r = new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
            var imported = _model.RenderedGeometry?.Groups.GetValueOrDefault(sg.Id);
            var fill = imported?.Fill is { } f ? new SolidColorBrush(ParseColor(f, Colors.Transparent)) : _theme.SubgraphFill;
            var stroke = imported?.Stroke is { } s ? new Pen(new SolidColorBrush(ParseColor(s, _theme.DefaultNodeStroke)), imported.StrokeWidth) : _theme.SubgraphStroke;
            ctx.DrawRectangle(fill, _groupSelection.Contains(sg) ? _theme.NodeStrokeSelected : stroke, r, imported is null ? 8 : 0, imported is null ? 8 : 0);
            if (_model.RenderedGeometry?.GroupLabel(_model, sg) is { } label) { DrawRenderedText(ctx, label, _theme.Label); continue; }
            var ft = new FormattedText(sg.Title ?? sg.Id, CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, Face, 12, Brush(DiagramContrast.ReadableInk(BrushColor(_theme.Label),
                    _model.RenderedGeometry is null ? DiagramContrast.Composite(BrushColor(fill), BrushColor(_theme.Background))
                        : DiagramContrast.SurfaceAt(_model, new(r.X + 10, r.Y + 8), BrushColor(_theme.Background)))));
            ctx.DrawText(ft, new Point(r.X + 10, r.Y + 8));
        }
    }

    private void DrawNode(DrawingContext ctx, DiagramNode n)
    {
        if (n.Entity is not null) { DrawEntityNode(ctx, n); return; }
        if (n.Class is not null) { DrawClassNode(ctx, n); return; }
        var sel = _selection.Contains(n);
        var isTarget = ReferenceEquals(n, _connectTarget);

        // One decision about how this node looks, made in one place. Asking
        // "is styling on?" at each of fill, outline, text and icon is how a
        // half-styled render sneaks in.
        var look = NodeAppearance.For(n, _showStyling);

        var fill = look.Fill is not null
            ? new SolidColorBrush(ParseColor(look.Fill, _theme.DefaultNodeFill))
            : sel ? _theme.NodeFillSelected
            : _diagramTheme is { } dt ? new SolidColorBrush(dt.Fill)
            : _theme.NodeFill;

        IPen pen;
        if (isTarget) pen = _theme.TargetStroke;
        else if (sel) pen = _theme.NodeStrokeSelected;
        else if (look.Stroke is not null || look.StrokeWidth is not null || look.Dashed)
        {
            var brush = new SolidColorBrush(
                ParseColor(look.Stroke, _theme.DefaultNodeStroke));
            pen = new Pen(brush, look.StrokeWidth ?? 1.6)
            {
                DashStyle = look.Dashed ? new DashStyle(new double[] { 5, 3 }, 0) : null,
            };
        }
        else if (_diagramTheme is { } ds) pen = new Pen(new SolidColorBrush(ds.Stroke), 1.6);
        else pen = _theme.NodeStroke;

        var r = new Rect(n.X, n.Y, n.Width, n.Height);

        // Shared with the palette's preview thumbnails, so the shape someone
        // picks is drawn by the same code that will draw it on the canvas.
        ShapeRenderer.Draw(ctx, n.Shape, r, fill, pen);

        var importedLabel = _model?.RenderedGeometry?.NodeLabel(_model, n);
        if (Zoom <= 0.3 && importedLabel is null || ReferenceEquals(n, _editing)) return;

        var textBrush = look.TextColor is not null
            ? new SolidColorBrush(ParseColor(look.TextColor, _theme.DefaultNodeText))
            : Brush(DiagramContrast.ReadableInk(BrushColor(ReadableOn(fill)), DiagramContrast.Composite(BrushColor(fill),
                _model is null ? BrushColor(_theme.Background)
                    : DiagramContrast.SurfaceAt(_model, new(n.X + n.Width / 2, n.Y + n.Height / 2), BrushColor(_theme.Background)))));
        if (importedLabel is not null) { DrawRenderedText(ctx, importedLabel, textBrush, fill, authoredInk: look.TextColor is not null); return; }

        // With an icon the glyph sits on the left and the label beside it.
        if (look.HasIcon)
        {
            var iconBox = new Rect(r.X + 8, r.Y + (r.Height - 26) / 2, 26, 26);
            IconSet.Draw(ctx, look.Icon!, iconBox, textBrush);
            var ft = new FormattedText(n.Display, CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, Face, 13, textBrush);
            ctx.DrawText(ft, new Point(iconBox.Right + 8, r.Center.Y - ft.Height / 2));
        }
        else
        {
            var ft = new FormattedText(n.Display, CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, Face, 13, textBrush);
            ctx.DrawText(ft, new Point(
                r.Center.X - ft.Width / 2, r.Center.Y - ft.Height / 2));
        }
    }

    /// <summary>Tolerate malformed colours from a hand-edited Merpeeps block.</summary>
    private static Color ParseColor(string? value, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        if (SequenceColors.TryParse(value, out var css)) return Color.FromArgb(css.Alpha, css.Red, css.Green, css.Blue);
        return Color.TryParse(value, out var c) ? c : fallback;
    }

    private void DrawHandles(DrawingContext ctx, DiagramNode n)
    {
        foreach (var p in HandlePoints(n))
            ctx.DrawEllipse(_theme.HandleFill, null, p, HandleRadius, HandleRadius);
    }

    private static IEnumerable<Point> HandlePoints(DiagramNode n)
    {
        if (n.Class is { Kind: not ClassNodeKind.Class }) yield break;
        yield return new Point(n.X + n.Width / 2, n.Y);
        yield return new Point(n.X + n.Width / 2, n.Y + n.Height);
        yield return new Point(n.X, n.Y + n.Height / 2);
        yield return new Point(n.X + n.Width, n.Y + n.Height / 2);
    }

    private void DrawGhostLink(DrawingContext ctx)
    {
        if (_connectFrom is null) return;
        var from = EdgePoint(_connectFrom, _connectTo);
        ctx.DrawLine(_theme.Ghost, from, _connectTo);
        DrawArrowHead(ctx, from, _connectTo, _theme.Ghost);
    }

    private void DrawLabelEditor(DrawingContext ctx, DiagramNode n) =>
        DrawLabelEditorAt(ctx, new Point(n.X + n.Width / 2, n.Y + n.Height / 2));

    /// <summary>The same inline editor, centred on a link's midpoint.</summary>
    private void DrawEdgeLabelEditor(DrawingContext ctx, DiagramEdge e)
    {
        if (Midpoint(e) is not { } mid) return;
        DrawLabelEditorAt(ctx, mid);
    }

    /// <summary>Where a link's label sits: halfway along the drawn line.</summary>
    private Point? Midpoint(DiagramEdge e)
    {
        if (_model is null) return null;
        var a = _model.Node(e.From);
        var b = _model.Node(e.To);
        if (a is null || b is null) return null;
        if (e.ClassRelationship is not null)
        {
            var from = ClassDiagramGeometry.Anchor(a, ClassDiagramGeometry.Center(b));
            var to = ClassDiagramGeometry.Anchor(b, ClassDiagramGeometry.Center(a));
            return new Point((from.X + to.X) / 2, (from.Y + to.Y) / 2);
        }

        var ca = new Point(a.X + a.Width / 2, a.Y + a.Height / 2);
        var cb = new Point(b.X + b.Width / 2, b.Y + b.Height / 2);
        var p1 = EdgePoint(a, cb);
        var p2 = EdgePoint(b, ca);
        if (_model.ClassOptions is null && _model.EntityOptions is null)
        {
            var route = GraphEdgeGeometry.Route(_model, e);
            if (_model.RenderedGeometry?.EdgeLabel(_model, e) is { Lines.Count: > 0 } label)
            {
                var r = label.Lines[0].Bounds; return new(r.X + r.Width / 2, r.Y + r.Height / 2);
            }
            var center = GraphEdgeGeometry.LabelPoint(route); return new(center.X, center.Y);
        }
        return new Point((p1.X + p2.X) / 2, (p1.Y + p2.Y) / 2);
    }

    private void DrawLabelEditorAt(DrawingContext ctx, Point centre)
    {
        var ft = new FormattedText(_editBuffer.Length == 0 ? " " : _editBuffer,
            CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face, 13, _theme.Label);
        var w = Math.Max(70, ft.Width + 18);
        var box = new Rect(centre.X - w / 2, centre.Y - 14, w, 28);
        ctx.DrawRectangle(_theme.EditBackground, _theme.EditPen, box, 3, 3);

        // While the first keystroke would replace everything, show the text as
        // selected so the behaviour is visible rather than surprising.
        if (_editReplaceOnFirstKey && _editBuffer.Length > 0)
            ctx.FillRectangle(_theme.SelectionFill,
                new Rect(box.X + 8, box.Y + 4, ft.Width + 2, box.Height - 8));

        ctx.DrawText(ft, new Point(box.X + 9, box.Y + 5));

        if (!_editReplaceOnFirstKey)
        {
            var caretX = box.X + 9 + ft.Width + 1;
            ctx.DrawLine(new Pen(_theme.Label, 1.2),
                new Point(caretX, box.Y + 5), new Point(caretX, box.Bottom - 5));
        }
    }

    private static StreamGeometry Poly(params Point[] pts)
    {
        var g = new StreamGeometry();
        using var c = g.Open();
        c.BeginFigure(pts[0], true);
        for (var i = 1; i < pts.Length; i++) c.LineTo(pts[i]);
        c.EndFigure(true);
        return g;
    }

    private void DrawEdge(DrawingContext ctx, DiagramEdge e)
    {
        if (_model is null) return;
        if (e.EntityRelationship is not null) { DrawEntityEdge(ctx, e); return; }
        if (e.ClassRelationship is not null) { DrawClassEdge(ctx, e); return; }
        var a = _model.Node(e.From);
        var b = _model.Node(e.To);
        if (a is null || b is null || e.Kind == EdgeKind.Invisible) return;

        var ca = new Point(a.X + a.Width / 2, a.Y + a.Height / 2);
        var cb = new Point(b.X + b.Width / 2, b.Y + b.Height / 2);
        var p1 = EdgePoint(a, cb);
        var p2 = EdgePoint(b, ca);
        var route = GraphEdgeGeometry.Route(_model, e);

        var pen = e.Kind switch
        {
            EdgeKind.Dotted or EdgeKind.DottedArrow => _theme.EdgeDotted,
            EdgeKind.Thick or EdgeKind.ThickArrow => _theme.EdgeThick,
            _ => _theme.Edge,
        };
        if (_model.RenderedGeometry is not null)
        {
            var paint = DiagramContrast.ConnectorInk(_model, route, BrushColor(pen.Brush!), BrushColor(_theme.Background));
            if (paint.Halo is { } halo)
            {
                var haloPen = new Pen(Brush(halo), pen.Thickness + 2);
                // Dash lengths are stroke-width multiples; align the halo gaps with the original route.
                if (pen.DashStyle is { Dashes: { } dashes } dash)
                {
                    var scale = pen.Thickness / haloPen.Thickness;
                    haloPen.DashStyle = new DashStyle(dashes.Select(d => d * scale).ToArray(), dash.Offset * scale);
                }
                DrawGraphRoute(ctx, route, haloPen);
            }
            pen = new Pen(Brush(paint.Ink), pen.Thickness) { DashStyle = pen.DashStyle };
        }

        // A selected link is drawn twice: a wide halo underneath, then the line
        // itself unchanged on top. Recolouring the line instead would hide which
        // kind of link it is at exactly the moment the user is about to change
        // that, and the halo reads clearly over both light and dark canvases.
        if (ReferenceEquals(e, _selectedEdge))
        {
            DrawGraphRoute(ctx, route, _theme.EdgeSelectedHalo);
        }

        DrawGraphRoute(ctx, route, pen);
        if (e.Kind is EdgeKind.Arrow or EdgeKind.DottedArrow or EdgeKind.ThickArrow && route.Count > 1)
        {
            var end = route[^1];
            var prior = route.Take(route.Count - 1).LastOrDefault(p => p != end);
            var arrowPen = _model.RenderedGeometry is null ? pen : new Pen(
                Brush(DiagramContrast.ReadableInk(BrushColor(pen.Brush!), DiagramContrast.SurfaceAt(_model, end, BrushColor(_theme.Background)), 3)), pen.Thickness);
            DrawArrowHead(ctx, new(prior.X, prior.Y), new(end.X, end.Y), arrowPen);
        }

        if (_model.RenderedGeometry?.EdgeLabel(_model, e) is { } importedText)
        {
            DrawRenderedText(ctx, importedText, _theme.Label);
            return;
        }

        if (!string.IsNullOrEmpty(e.Label) && Zoom > 0.4)
        {
            var midpoint = GraphEdgeGeometry.LabelPoint(route);
            var mid = new Point(midpoint.X, midpoint.Y);
            var ft = new FormattedText(e.Label!, CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, Face, 11, _theme.Label);
            var box = new Rect(mid.X - ft.Width / 2 - 3, mid.Y - ft.Height / 2 - 1,
                               ft.Width + 6, ft.Height + 2);
            ctx.FillRectangle(_theme.EdgeLabelBackground, box, 3);
            ctx.DrawText(ft, new Point(box.X + 3, box.Y + 1));
        }
    }

    private static void DrawArrowHead(DrawingContext ctx, Point from, Point to, IPen pen)
    {
        var dx = to.X - from.X; var dy = to.Y - from.Y;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 0.001) return;
        dx /= len; dy /= len;
        const double size = 9;
        var bx = to.X - dx * size; var by = to.Y - dy * size;
        var nx = -dy * size * 0.42; var ny = dx * size * 0.42;
        ctx.DrawGeometry(pen.Brush, null, Poly(
            to, new Point(bx + nx, by + ny), new Point(bx - nx, by - ny)));
    }

    private static Point EdgePoint(DiagramNode n, Point target)
    {
        var cx = n.X + n.Width / 2; var cy = n.Y + n.Height / 2;
        var dx = target.X - cx; var dy = target.Y - cy;
        if (Math.Abs(dx) < 0.001 && Math.Abs(dy) < 0.001) return new Point(cx, cy);
        var hw = n.Width / 2 + 2; var hh = n.Height / 2 + 2;
        var scale = Math.Min(
            dx == 0 ? double.MaxValue : hw / Math.Abs(dx),
            dy == 0 ? double.MaxValue : hh / Math.Abs(dy));
        return new Point(cx + dx * scale, cy + dy * scale);
    }

    // ------------------------------------------------------------ interaction

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        if (_model is null || _history is null) return;

        if (IsEditing) CommitLabel();

        var pt = e.GetCurrentPoint(this);
        var world = ToWorld(pt.Position);
        if (_ignoreGalleryDoubleClick)
        {
            _ignoreGalleryDoubleClick = false;
            if (e.ClickCount > 1)
            {
                e.Handled = true;
                return;
            }
        }

        // The hand tool — and space, its held equivalent — make the canvas
        // purely navigational: every press pans, and nothing selects, creates
        // or connects. Checked before the double-click case on purpose, since
        // adding a node while navigating is never what was meant.
        if (Panning)
        {
            StartPan(pt.Position, e);
            e.Handled = true;
            return;
        }
        if (IsWorkflowDiagram || IsStrategyDiagram || IsSetGrammarDiagram || IsOwnershipDiagram || _model.Requirements is not null || _model.GitDiagram is not null || _model.Sankey is not null || _model.Treemap is not null || _model.Blocks is not null || _model.Architecture is not null || _model.C4 is not null || _model.Gantt is not null)
        {
            if (pt.Properties.IsLeftButtonPressed && !e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                var connecting = _gridConnectionSource is not null;
                StructuralPointer(world);
                if (!connecting && e.ClickCount == 1 && BeginWardleyDrag(world)) e.Pointer.Capture(this);
                if (e.ClickCount == 2) StructuralEditRequested?.Invoke(this, EventArgs.Empty);
            }
            else StartPan(pt.Position, e);
            e.Handled = true; return;
        }
        if (_model.Outline is not null || _model.Temporal is not null || _model.Packet is not null || _model.Kanban is not null)
        {
            if (pt.Properties.IsLeftButtonPressed && !e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                if (DataPointer(world)) e.Pointer.Capture(this);
                if (e.ClickCount == 2) DataEditRequested?.Invoke(this, EventArgs.Empty);
            }
            else StartPan(pt.Position, e);
            e.Handled = true; return;
        }
        if (_model.Chart is not null)
        {
            if (pt.Properties.IsLeftButtonPressed && !e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                if (ChartPointer(world)) e.Pointer.Capture(this);
                if (e.ClickCount == 2) ChartEditRequested?.Invoke(this, EventArgs.Empty);
            }
            else StartPan(pt.Position, e);
            e.Handled = true; return;
        }
        if (_model.Sequence is not null && pt.Properties.IsLeftButtonPressed && !e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            SequencePointer(world);
            if (e.ClickCount == 2 && _selectedSequence is not null) SequenceEditRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }
        if (_model.Sequence is not null)
        {
            if (pt.Properties.IsMiddleButtonPressed || pt.Properties.IsRightButtonPressed || e.KeyModifiers.HasFlag(KeyModifiers.Control))
                StartPan(pt.Position, e);
            e.Handled = true;
            return;
        }

        if (_entityConnectionSource is not null && pt.Properties.IsLeftButtonPressed &&
            !e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            var targetId = HitTest(world)?.Id ?? HitEntityGroup(world)?.Id;
            try
            {
                if (targetId is null) { CancelEntityConnection(); SelectByIds([]); }
                else CompleteEntityRelationship(targetId);
            }
            catch (ArgumentException error) { EntityConnectionMessage = error.Message; SelectionChanged?.Invoke(this, EventArgs.Empty); }
            e.Handled = true;
            return;
        }

        if (_classRelationshipSource is not null && pt.Properties.IsLeftButtonPressed &&
            !e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            try
            {
                if (HitTest(world) is { } target) CompleteClassRelationship(target);
                else
                {
                    CancelClassRelationship();
                    SelectByIds([]);
                }
            }
            catch (ArgumentException error)
            {
                ClassRelationshipMessage = error.Message;
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
            e.Handled = true;
            return;
        }

        if (e.ClickCount == 2 && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            // Node, then link, then empty canvas — the same precedence as a
            // single click, so a double-click never means something different
            // from what the first click already selected.
            if (HitTest(world) is { } target) BeginRename(target);
            else if (HitTestEdge(world) is { } edge)
            {
                _selection.Clear();
                _groupSelection.Clear();
                SelectedEdge = edge;
                SelectionChanged?.Invoke(this, EventArgs.Empty);
                BeginRelabel(edge);
            }
            else if (HitGroup(world) is { } group)
            {
                SelectGroup(group);
                if (_model.EntityOptions is not null) EntityRenameRequested?.Invoke(this, EventArgs.Empty);
            }
            else CreateNodeAt(world);

            e.Handled = true;
            InvalidateVisual();
            return;
        }

        // Middle-drag and right-drag both pan. Middle is the CAD convention,
        // right is Visio's; neither is universal, so supporting both costs one
        // line and removes a guess.
        if (pt.Properties.IsMiddleButtonPressed || pt.Properties.IsRightButtonPressed)
        {
            StartPan(pt.Position, e);
            e.Handled = true;
            return;
        }

        // A handle on the selected node starts a connection.
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Shift) && Selected is { } sel)
        {
            foreach (var h in HandlePoints(sel))
            {
                if (Dist(world, h) > HandleRadius + 4 / Zoom) continue;
                _connectFrom = sel;
                _connectTo = world;
                e.Pointer.Capture(this);
                e.Handled = true;
                return;
            }
        }

        var hit = HitTest(world);
        if (hit is not null)
        {
            // A node click clears any link selection: the two are alternatives,
            // and a property pane showing both has nothing coherent to say.
            SelectedEdge = null;

            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                if (!_selection.Add(hit)) _selection.Remove(hit);
            }
            else if (!_selection.Contains(hit))
            {
                _groupSelection.Clear();
                _selection.Clear();
                _selection.Add(hit);
            }
            NormalizeGraphSelection();
            SelectionChanged?.Invoke(this, EventArgs.Empty);

            BeginGraphDrag(world, e.Pointer, node: hit);
        }
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            StartPan(pt.Position, e);
        }
        else if (HitTestEdge(world) is { } edge)
        {
            _selectedGroup = null;
            // Tested after nodes, so a link running under a node never steals a
            // click meant for the node on top of it.
            _selection.Clear();
            SelectedEdge = edge;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
        else if (HitGroup(world) is { } group)
        {
            CancelEntityConnection(); ResetClassConnection();
            _selectedEdge = null;
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                if (!_groupSelection.Add(group)) _groupSelection.Remove(group);
            }
            else if (!_groupSelection.Contains(group))
            {
                _selection.Clear(); _groupSelection.Clear(); _groupSelection.Add(group);
            }
            NormalizeGraphSelection();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            BeginGraphDrag(world, e.Pointer, group: group);
        }
        else
        {
            _marqueeNodes = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? _selection.ToArray() : [];
            _marqueeGroups = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? _groupSelection.ToArray() : [];
            _marqueeStart = pt.Position;
            _marquee = new Rect(pt.Position, pt.Position);
            if (!e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                _selection.Clear();
                _groupSelection.Clear();
                SelectedEdge = null;
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
            e.Pointer.Capture(this);
        }
        InvalidateVisual();
    }

    private void StartPan(Point pos, PointerPressedEventArgs e)
    {
        EndGraphDrag(cancel: true);
        CancelWardleyDrag();
        _panFrom = pos;
        _panStartX = OffsetX; _panStartY = OffsetY;
        e.Pointer.Capture(this);

        // Feedback that the drag is a pan and not a selection. Without it a
        // right-drag looks like nothing is happening until the view moves.
        UpdateCursor();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = e.GetPosition(this);
        var world = ToWorld(pos);
        if (_wardleyDragId is not null) { MoveWardleyDrag(world); return; }
        if (_outlineDragStart is { } dataStart)
        {
            _outlineDragDelta = world - dataStart; InvalidateVisual(); return;
        }
        if (_model?.Chart is not null && _panFrom is null)
        {
            ChartPointerMove(world);
            return;
        }

        if (_classRelationshipSource is not null && _panFrom is null)
        {
            _classRelationshipPoint = world;
            var target = HitTest(world);
            _classRelationshipTarget = target?.Class?.Kind == ClassNodeKind.Class &&
                !ReferenceEquals(target, _classRelationshipSource) ? target : null;
            InvalidateVisual();
            return;
        }

        if (_connectFrom is not null)
        {
            _connectTo = world;
            var hit = HitTest(world);
            _connectTarget = ReferenceEquals(hit, _connectFrom) ||
                _model?.ClassOptions is not null && hit?.Class?.Kind != ClassNodeKind.Class ? null : hit;
            InvalidateVisual();
            return;
        }

        if (_dragging is not null || _draggingGroup is not null)
        {
            MoveGraphDrag(world, e.KeyModifiers);
            return;
        }

        if (_marqueeStart is { } start)
        {
            _marquee = new Rect(start, pos).Normalize();
            SelectInMarquee();
            InvalidateVisual();
            return;
        }

        if (_panFrom is { } from)
        {
            OffsetX = _panStartX + (pos.X - from.X);
            OffsetY = _panStartY + (pos.Y - from.Y);
            InvalidateVisual();
        }
    }

    private void SelectInMarquee()
    {
        if (_model is null) return;
        var world = new Rect(ToWorld(_marquee.TopLeft), ToWorld(_marquee.BottomRight))
            .Normalize();
        _selection.Clear();
        _selection.UnionWith(_marqueeNodes);
        _groupSelection.Clear();
        _groupSelection.UnionWith(_marqueeGroups);
        foreach (var n in _model.Nodes)
            if (world.Intersects(new Rect(n.X, n.Y, n.Width, n.Height)))
                _selection.Add(n);
        foreach (var group in _model.Subgraphs.Where(g => GraphGroupGeometry.IsVisible(_model, g)))
        {
            var bounds = GraphGroupGeometry.Bounds(_model, group);
            if (world.Contains(new Point(bounds.X, bounds.Y)) && world.Contains(new Point(bounds.X + bounds.Width, bounds.Y + bounds.Height)))
                _groupSelection.Add(group);
        }
        NormalizeGraphSelection();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_wardleyDragId is not null) { EndWardleyDrag(); e.Pointer.Capture(null); e.Handled = true; return; }
        if (_outlineDragStart is not null)
        {
            EndOutlineDrag(false); e.Pointer.Capture(null); e.Handled = true; return;
        }
        if (_chartDragTarget is not null)
        {
            CommitChartDrag(); e.Pointer.Capture(null); e.Handled = true; return;
        }

        if (_connectFrom is not null)
        {
            if (_connectTarget is not null && _history is not null && _model is not null)
            {
                var ord = _model.Edges.Where(x =>
                    x.From == _connectFrom.Id && x.To == _connectTarget.Id)
                    .Select(x => x.Ordinal + 1).DefaultIfEmpty(0).Max();
                var added = new DiagramEdge
                {
                    From = _connectFrom.Id,
                    To = _connectTarget.Id,
                    Ordinal = ord,
                    EntityRelationship = _model.EntityOptions is null ? null : new(),
                    Label = _model.EntityOptions is null ? null : "",
                };
                _history.Apply(_model.EntityOptions is null ? new AddEdgeCommand(added) : new AddEntityRelationshipCommand(added));
                if (_model.EntityOptions is not null) SelectEdge(added);
                ModelChanged?.Invoke(this, EventArgs.Empty);
            }
            _connectFrom = null;
            _connectTarget = null;
        }
        else if (_dragging is not null || _draggingGroup is not null)
        {
            EndGraphDrag(cancel: false);
        }

        _dragging = null;
        _panFrom = null;
        _marqueeStart = null;
        _marqueeNodes = []; _marqueeGroups = [];

        // Back to whatever the resting gesture is: the hand tool and a held
        // space both survive a release, an ordinary pan does not.
        UpdateCursor();

        e.Pointer.Capture(null);
        InvalidateVisual();
    }

    /// <summary>
    /// Wheel scrolls the view; Ctrl+wheel zooms about the pointer.
    /// </summary>
    /// <remarks>
    /// <para>The wheel used to zoom outright, which is the minority convention.
    /// Figma, draw.io, Excalidraw and Visio all scroll on the wheel and zoom on
    /// Ctrl+wheel, and someone arriving from any of them finds a canvas that
    /// lurches in and out when they meant to look further down. Panning already
    /// worked — middle-drag, space-drag, Ctrl-drag — but a gesture nobody
    /// reaches for is a gesture nobody has.</para>
    ///
    /// <para>Shift swaps the axis, which is how horizontal scrolling works
    /// everywhere else and costs nothing to support.</para>
    /// </remarks>
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        CancelWardleyDrag();
        base.OnPointerWheelChanged(e);

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            var pos = e.GetPosition(this);
            var before = ToWorld(pos);
            Zoom = Math.Clamp(Zoom * (e.Delta.Y > 0 ? 1.12 : 1 / 1.12), 0.08, 6.0);

            // Keep the point under the cursor fixed, so zooming homes in on
            // whatever is being looked at rather than the centre of the window.
            OffsetX = pos.X - before.X * Zoom;
            OffsetY = pos.Y - before.Y * Zoom;
        }
        else
        {
            // A trackpad reports both axes; a wheel reports only Y. Shift moves
            // that single axis sideways, matching every other scrolling surface.
            var dx = e.Delta.X;
            var dy = e.Delta.Y;
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && dx == 0)
            {
                dx = dy;
                dy = 0;
            }

            // In screen pixels, not world units: a scroll should move the view
            // by the same amount however far it is zoomed in.
            OffsetX += dx * WheelStep;
            OffsetY += dy * WheelStep;
        }

        InvalidateVisual();
        e.Handled = true;
    }

    /// <summary>Screen pixels moved per wheel notch.</summary>
    private const double WheelStep = 48;

    // ----------------------------------------------------------------- keys

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_model is null || _history is null) return;
        if ((IsWorkflowDiagram || IsStrategyDiagram || IsSetGrammarDiagram || IsOwnershipDiagram || _model.Requirements is not null || _model.GitDiagram is not null || _model.Sankey is not null || _model.Treemap is not null || _model.Blocks is not null || _model.Architecture is not null || _model.C4 is not null || _model.Gantt is not null) && e.Key == Key.Escape)
        {
            CancelWardleyDrag();
            CancelRequirementConnection(); SelectStructural(null); e.Handled = true; return;
        }
        if ((IsWorkflowDiagram || IsStrategyDiagram || IsSetGrammarDiagram || IsOwnershipDiagram || _model.Requirements is not null || _model.GitDiagram is not null || _model.Sankey is not null || _model.Treemap is not null || _model.Blocks is not null || _model.Architecture is not null || _model.C4 is not null || _model.Gantt is not null) && e.Key == Key.F2)
        {
            StructuralEditRequested?.Invoke(this, EventArgs.Empty); e.Handled = true; return;
        }
        if ((_model.Outline is not null || _model.Temporal is not null || _model.Packet is not null || _model.Kanban is not null) && e.Key == Key.Escape)
        {
            EndOutlineDrag(true); SelectData(null); e.Handled = true; return;
        }
        if ((_model.Outline is not null || _model.Temporal is not null || _model.Packet is not null || _model.Kanban is not null) && e.Key == Key.F2)
        {
            DataEditRequested?.Invoke(this, EventArgs.Empty); e.Handled = true; return;
        }
        if (_model.Chart is not null && e.Key == Key.Escape)
        {
            CancelChartDrag(); SelectChart(null); e.Handled = true; return;
        }
        if (_model.Chart is not null && e.Key == Key.F2)
        {
            ChartEditRequested?.Invoke(this, EventArgs.Empty); e.Handled = true; return;
        }
        if (_model.Chart is not null && e.Key is Key.Delete or Key.Back)
        {
            DeleteChartSelection(); e.Handled = true; return;
        }

        if (_model.Sequence is not null && e.Key == Key.Escape)
        {
            if (_sequenceMessageSource is not null) CancelSequenceMessage();
            else SelectSequence(null);
            e.Handled = true;
            return;
        }
        if (_model.Sequence is not null && e.Key is Key.Delete or Key.Back)
        {
            DeleteSequenceSelection(); e.Handled = true; return;
        }
        if (_model.Sequence is not null && e.Key == Key.F2)
        {
            SequenceEditRequested?.Invoke(this, EventArgs.Empty); e.Handled = true; return;
        }
        if (_entityConnectionSource is not null && e.Key == Key.Escape)
        {
            CancelEntityConnection(); e.Handled = true; return;
        }
        if (e.Key == Key.Escape && _classRelationshipSource is not null)
        {
            CancelClassRelationship();
            e.Handled = true;
            return;
        }
        if (_classRelationshipSource is not null && e.Key == Key.Tab)
        {
            var targets = _model.Nodes.Where(n => n.Class?.Kind == ClassNodeKind.Class &&
                !ReferenceEquals(n, _classRelationshipSource)).ToArray();
            if (targets.Length > 0)
            {
                var index = Array.IndexOf(targets, _classRelationshipTarget);
                var step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1;
                index = index < 0 ? (step > 0 ? 0 : targets.Length - 1)
                    : (index + step + targets.Length) % targets.Length;
                _classRelationshipTarget = targets[index];
                ClassRelationshipMessage = $"Target: {targets[index].Display}. Enter connects; Escape cancels.";
                SelectionChanged?.Invoke(this, EventArgs.Empty);
                InvalidateVisual();
            }
            e.Handled = true;
            return;
        }
        if (_classRelationshipSource is not null && e.Key == Key.Enter)
        {
            if (_classRelationshipTarget is { } target) CompleteClassRelationship(target);
            e.Handled = true;
            return;
        }

        // Space is a pan modifier, but only when not typing a label — inside
        // the inline editor a space is just a space.
        if (e.Key == Key.Space && !IsEditing)
        {
            if (!_spaceHeld) { _spaceHeld = true; UpdateCursor(); }
            e.Handled = true;
            return;
        }

        if (IsEditing) { HandleLabelKey(e); return; }

        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        switch (e.Key)
        {
            case Key.Delete or Key.Back when _selection.Count > 0:
                EndGraphDrag(cancel: true);
                ResetClassConnection();
                _history.Apply(new DeleteNodesCommand(_selection.ToList()));
                _selection.Clear();
                SelectionChanged?.Invoke(this, EventArgs.Empty);
                ModelChanged?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;

            // Deleting a link. Listed after the node case so a selection of both
            // is impossible to reach, and reached only when a link is what is
            // actually selected.
            case Key.Delete or Key.Back when _selectedEdge is { } edge:
                ResetClassConnection();
                _history.Apply(new DeleteEdgeCommand(edge));
                SelectedEdge = null;
                ModelChanged?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;

            case Key.F2 when _selectedEdge is { } toRelabel:
                BeginRelabel(toRelabel);
                e.Handled = true;
                break;

            case Key.Z when ctrl && shift:
            case Key.Y when ctrl:
                Redo(); e.Handled = true; break;

            case Key.Z when ctrl:
                Undo(); e.Handled = true; break;

            case Key.A when ctrl:
                SelectAll();
                e.Handled = true;
                break;

            case Key.F2 when Selected is { } s:
                EndGraphDrag(cancel: true);
                BeginRename(s); e.Handled = true; break;

            case Key.F2 when SelectedEntityGroup is not null:
                EntityRenameRequested?.Invoke(this, EventArgs.Empty); e.Handled = true; break;

            // H and V switch tools, as in Figma and Illustrator. Unmodified, so
            // they are guarded by the IsEditing check above — inside a label
            // these are just letters.
            case Key.H when !ctrl:
                Tool = CanvasTool.Hand; e.Handled = true; break;

            case Key.V when !ctrl:
                Tool = CanvasTool.Select; e.Handled = true; break;

            case Key.Escape:
                // The way out of any mode. Someone who has lost track of what a
                // drag will do needs one key that restores the ordinary canvas,
                // not a rule about which key exits which state.
                EndGraphDrag(cancel: true);
                Tool = CanvasTool.Select;
                _selection.Clear();
                _selectedEdge = null;
                _selectedGroup = null;
                _marqueeStart = null;
                _marqueeNodes = []; _marqueeGroups = [];
                SelectionChanged?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;
        }
        InvalidateVisual();
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.Key != Key.Space) return;
        ReleasePanModifier();
        e.Handled = true;
    }

    /// <summary>
    /// Clear the pan modifier. Called on key-up and on losing focus: alt-tabbing
    /// while space is held would otherwise leave the canvas stuck in pan mode
    /// with no key-up ever arriving to end it.
    /// </summary>
    private void ReleasePanModifier()
    {
        if (!_spaceHeld) return;
        _spaceHeld = false;
        UpdateCursor();
    }

    private void AfterHistory()
    {
        _structuralCacheModel = null; _requirementSource = null; _gridConnectionSource = null;
        CancelWardleyDrag();
        if (_structuralSelection is { } structural &&
            _model?.Requirements?.Items.Any(i => i.Id == structural.Id) != true &&
            _model?.Requirements?.Relations.Any(r => r.Id == structural.Id) != true &&
            _model?.GitDiagram?.Operations.Any(o => o.Id == structural.Id) != true &&
            _model?.Sankey?.Nodes.Any(n => n.Id == structural.Id) != true &&
            _model?.Sankey?.Links.Any(l => l.Id == structural.Id) != true &&
            _model?.Treemap?.Nodes.Any(n => n.Id == structural.Id) != true &&
            _model?.Blocks?.AllItems.Any(n => n.Id == structural.Id) != true &&
            _model?.Blocks?.Connections.Any(e => e.Id == structural.Id) != true &&
            _model?.Architecture?.Nodes.Any(n => n.Id == structural.Id) != true &&
            _model?.Architecture?.Groups.Any(g => g.Id == structural.Id) != true &&
            _model?.Architecture?.Connections.Any(e => e.Id == structural.Id) != true &&
            _model?.C4?.Elements.Any(e => e.Id == structural.Id) != true &&
            _model?.C4?.Relationships.Any(e => e.Id == structural.Id) != true &&
            _model?.Gantt?.Tasks.Any(t => t.Id == structural.Id) != true &&
            _model?.Gantt?.Sections.Any(s => s.Id == structural.Id) != true &&
            _model?.Wardley?.Components.Any(c => c.Id == structural.Id) != true &&
            _model?.Wardley?.Links.Any(l => l.Id == structural.Id) != true &&
            _model?.Wardley?.Annotations.Any(a => a.Id == structural.Id) != true &&
            _model?.Ishikawa?.AllCauses.Any(c => c.Id == structural.Id) != true &&
            _model?.Cynefin?.Items.Any(i => i.Id == structural.Id) != true &&
            _model?.Cynefin?.Transitions.Any(t => t.Id == structural.Id) != true &&
            _model?.Venn?.Sets.Any(s => s.Id == structural.Id) != true &&
            _model?.Venn?.Overlaps.Any(o => o.Id == structural.Id) != true &&
            _model?.Venn?.Texts.Any(t => t.Id == structural.Id) != true &&
            _model?.Railroad?.Rules.Any(r => r.Id == structural.Id || RailroadDiagramDocument.Walk(r.Expression).Any(e => e.Id == structural.Id)) != true &&
            _model?.Swimlane?.Nodes.Any(n => n.Id == structural.Id) != true &&
            _model?.Swimlane?.Groups.Any(g => g.Id == structural.Id) != true &&
            _model?.Swimlane?.Links.Any(l => l.Id == structural.Id) != true &&
            _model?.UseCase?.Nodes.Any(n => n.Id == structural.Id) != true &&
            _model?.UseCase?.Boundaries.Any(b => b.Id == structural.Id) != true &&
            _model?.UseCase?.Relationships.Any(r => r.Id == structural.Id) != true &&
            _model?.AgentFlow?.Elements.Any(e => e.Id == structural.Id) != true &&
            _model?.AgentFlow?.Links.Any(l => l.Id == structural.Id) != true &&
            _model?.EventModeling?.Frames.Any(f => f.Id == structural.Id) != true &&
            _model?.EventModeling?.DataBlocks.Any(d => d.Id == structural.Id) != true &&
            _model?.Zen?.Participants.Any(p => p.Id == structural.Id) != true &&
            (_model?.Zen is null || !ZenSemantics.Walk(_model.Zen.Statements).Any(s => s.Id == structural.Id)))
            _structuralSelection = null;
        _dataCacheModel = null; _outlineDragStart = null;
        if (_dataSelection is { } dataTarget && !DataTargetExists(dataTarget)) _dataSelection = null;
        CancelChartDrag(); _chartHover = null;
        if (_chartSelection is { } chartTarget && !ChartTargetExists(chartTarget)) _chartSelection = null;
        _sequenceMessageSource = null; _sequenceCacheModel = null;
        if (_model?.Sequence is not null && _selectedSequence is { } sequenceSelection) SelectSequenceId(sequenceSelection.Id);
        ResetClassConnection();
        _entityConnectionSource = null; _entityReconnect = null;
        // Undo can resurrect or remove nodes; drop stale selection entries.
        if (_model is not null)
        {
            _selection.RemoveWhere(n => !_model.Nodes.Contains(n));
            if (_selectedEdge is not null && !_model.Edges.Contains(_selectedEdge)) _selectedEdge = null;
            _groupSelection.RemoveWhere(g => !_model.Subgraphs.Contains(g));
        }
        MeasureLabels();
        HistoryNavigated?.Invoke(this, EventArgs.Empty);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        ModelChanged?.Invoke(this, EventArgs.Empty);
    }

    // ------------------------------------------------------------- editing

    private void CreateNodeAt(Point world)
    {
        if (_model is null || _history is null) return;
        var node = new DiagramNode
        {
            Id = NextId(),
            Label = "New node",
            X = Math.Round((world.X - 60) / 10) * 10,
            Y = Math.Round((world.Y - 23) / 10) * 10,
            HasExplicitPosition = true,
        };
        if (_model.ClassOptions is not null)
        {
            node.Class = new ClassNodeDefinition();
            node.Label = "New class";
            node.Width = 240;
            node.Height = 100;
        }
        else if (_model.Kind is "stateDiagram" or "stateDiagram-v2")
        {
            node.Label = "New state";
            node.Shape = NodeShape.RoundedRect;
        }
        else if (_model.EntityOptions is not null)
        {
            node.Entity = new EntityNodeDefinition(); node.Label = "New entity"; node.Width = 280; node.Height = 100;
        }
        MeasureNode(node);
        _history.Apply(_model.EntityOptions is null ? new AddNodeCommand(node) : new AddEntityCommand(node));
        _selection.Clear();
        _groupSelection.Clear();
        _selection.Add(node);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        ModelChanged?.Invoke(this, EventArgs.Empty);
        BeginRename(node);
    }

    private string NextId()
    {
        if (_model is null) return "n1";
        for (var i = 1; ; i++)
        {
            var id = "n" + i;
            if (_model.Node(id) is null) return id;
        }
    }

    /// <summary>
    /// Start editing a label. The existing text stays visible but the first
    /// keystroke replaces it, which is what every editor does and what stops
    /// "New node" from becoming "New nodeAnalytics".
    /// </summary>
    private void BeginRename(DiagramNode n)
    {
        if (n.Entity is not null)
        {
            SelectByIds([n.Id]);
            EntityRenameRequested?.Invoke(this, EventArgs.Empty);
            return;
        }
        _editingEdge = null;
        _editing = n;
        _editBuffer = n.Label ?? "";
        _editReplaceOnFirstKey = true;
        InvalidateVisual();
    }

    /// <summary>Edit a link's label with the same inline editor.</summary>
    /// <remarks>
    /// Shares the buffer, the key handling and the commit path with node
    /// renaming. Two editors would be two places for Escape to behave
    /// differently.
    /// </remarks>
    private void BeginRelabel(DiagramEdge edge)
    {
        _editing = null;
        _editingEdge = edge;
        _editBuffer = edge.Label ?? "";
        _editReplaceOnFirstKey = true;
        InvalidateVisual();
    }

    private void HandleLabelKey(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter: CommitLabel(); e.Handled = true; break;
            case Key.Escape: _editing = null; _editingEdge = null; e.Handled = true; break;
            case Key.Back:
                if (_editReplaceOnFirstKey) { _editBuffer = ""; _editReplaceOnFirstKey = false; }
                else if (_editBuffer.Length > 0) _editBuffer = _editBuffer[..^1];
                e.Handled = true;
                break;
        }
        InvalidateVisual();
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (!IsEditing || string.IsNullOrEmpty(e.Text)) return;
        if (_editReplaceOnFirstKey) { _editBuffer = ""; _editReplaceOnFirstKey = false; }
        _editBuffer += e.Text;
        e.Handled = true;
        InvalidateVisual();
    }

    /// <summary>True while a label — of a node or a link — is being typed.</summary>
    private bool IsEditing => _editing is not null || _editingEdge is not null;

    private void CommitLabel()
    {
        if (_history is null) return;

        if (_editingEdge is { } edge)
        {
            _editingEdge = null;

            var text = string.IsNullOrWhiteSpace(_editBuffer) ? null : _editBuffer.Trim();
            if (text != edge.Label)
            {
                _history.Apply(new RelabelEdgeCommand(edge, text));

                // An edge label is part of the mermaid body, so this genuinely
                // changes the document — unlike moving a node, which only
                // changes the metadata.
                ModelChanged?.Invoke(this, EventArgs.Empty);
            }
            InvalidateVisual();
            return;
        }

        if (_editing is null) return;
        var node = _editing;
        _editing = null;

        var next = string.IsNullOrWhiteSpace(_editBuffer) ? null : _editBuffer.Trim();
        if (next != node.Label)
        {
            _history.Apply(new RenameNodeCommand(node, next));
            MeasureNode(node);
            ModelChanged?.Invoke(this, EventArgs.Empty);
        }
        InvalidateVisual();
    }

    public void ApplyShape(NodeShape shape)
    {
        if (_history is null || _selection.Count == 0) return;
        foreach (var n in _selection.ToList())
        {
            _history.Apply(new ChangeShapeCommand(n, shape));
            MeasureNode(n);
        }
        ModelChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    public void Undo() { EndGraphDrag(cancel: true); _history?.Undo(); AfterHistory(); InvalidateVisual(); }
    public void Redo() { EndGraphDrag(cancel: true); _history?.Redo(); AfterHistory(); InvalidateVisual(); }

    // ------------------------------------------- commands the menu also uses

    /// <summary>Zoom about the centre of the viewport, as a menu item must.</summary>
    public void ZoomBy(double factor)
    {
        EndGraphDrag(cancel: true);
        CancelWardleyDrag();
        var centre = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var before = ToWorld(centre);
        Zoom = Math.Clamp(Zoom * factor, 0.08, 6.0);
        OffsetX = centre.X - before.X * Zoom;
        OffsetY = centre.Y - before.Y * Zoom;
        InvalidateVisual();
    }

    /// <summary>Back to 1:1, keeping the centre of the view fixed.</summary>
    public void ResetZoom() => ZoomBy(1 / Zoom);

    public void SelectAll()
    {
        if (_model is null) return;
        ResetClassConnection();
        EndGraphDrag(cancel: true);
        _selectedGroup = null;
        _selection.Clear();
        foreach (var n in _model.Nodes) _selection.Add(n);
        _selectedEdge = null;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    public void DeleteSelection()
    {
        EndGraphDrag(cancel: true);
        if (IsStrategyDiagram || _model?.Requirements is not null || _model?.GitDiagram is not null || _model?.Blocks is not null || _model?.Architecture is not null || _model?.C4 is not null || _model?.Gantt is not null) { DeleteStructuralSelection(); return; }
        if (_model?.Outline is not null || _model?.Temporal is not null || _model?.Packet is not null || _model?.Kanban is not null) { DeleteDataSelection(); return; }
        if (_model?.Chart is not null) { DeleteChartSelection(); return; }
        if (_model?.Sequence is not null) { DeleteSequenceSelection(); return; }
        ResetClassConnection();
        if (_history is null) return;

        // A link, when that is what is selected. Menu Delete and the Delete key
        // should not disagree about what "the selection" means.
        if (_selection.Count == 0 && _selectedEdge is { } edge)
        {
            _history.Apply(new DeleteEdgeCommand(edge));
            SelectedEdge = null;
            ModelChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
            return;
        }

        if (_selection.Count == 0) return;
        _history.Apply(new DeleteNodesCommand(_selection.ToList()));
        _selection.Clear();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        ModelChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    public void RenameSelected()
    {
        if (Selected is { } n) { BeginRename(n); InvalidateVisual(); }
        else if (_selectedEdge is { } e) { BeginRelabel(e); InvalidateVisual(); }
    }

    /// <summary>Change the selected link's arrow style.</summary>
    public void ApplyEdgeKind(EdgeKind kind)
    {
        if (_history is null || _selectedEdge is not { } edge || edge.Kind == kind) return;

        _history.Apply(new ChangeEdgeKindCommand(edge, kind));
        ModelChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    /// <summary>Set a link's label — the target named, not the selection.</summary>
    /// <remarks>
    /// Takes the edge explicitly so a caller can commit an edit against the link
    /// it was started on, even if the selection has since moved. Reading the
    /// selection here instead silently dropped the edit whenever focus was lost
    /// because the user clicked something else, which is the ordinary way to
    /// finish typing.
    /// </remarks>
    public void RelabelEdge(DiagramEdge edge, string? label)
    {
        if (_history is null) return;

        var next = string.IsNullOrWhiteSpace(label) ? null : label.Trim();
        if (next == edge.Label) return;

        _history.Apply(new RelabelEdgeCommand(edge, next));
        ModelChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    /// <summary>Select a link, or clear the link selection.</summary>
    public void SelectEdge(DiagramEdge? edge)
    {
        ResetClassConnection();
        CancelEntityConnection();
        EndGraphDrag(cancel: true);
        _selectedGroup = null;
        if (edge is not null) _selection.Clear();
        SelectedEdge = edge;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    /// <summary>Replace the selection by node id — used by the node list.</summary>
    public void SelectByIds(IEnumerable<string> ids)
    {
        if (_model is null) return;
        ResetClassConnection();
        CancelEntityConnection();
        EndGraphDrag(cancel: true);
        _selectedGroup = null;
        var want = ids.ToHashSet();
        _selection.Clear();
        foreach (var n in _model.Nodes)
            if (want.Contains(n.Id)) _selection.Add(n);

        // Node and link selection are alternatives, so picking nodes here has to
        // clear the link — otherwise choosing a node in the list left the pane
        // showing link properties and Delete meaning something else entirely.
        _selectedEdge = null;

        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    /// <summary>
    /// Place a palette shape in the middle of the current view, already styled
    /// and selected with its label open for editing.
    ///
    /// <para>Landing in the view's centre rather than at a fixed point matters
    /// once the canvas has been panned: a node added off-screen looks like
    /// nothing happened.</para>
    /// </summary>
    public void AddFromPalette(PaletteItem item)
    {
        var centre = ToWorld(new Point(Bounds.Width / 2, Bounds.Height / 2));
        AddFromPalette(item, centre);
    }

    /// <summary>
    /// Place a palette shape centred on a point in world coordinates.
    /// </summary>
    /// <remarks>
    /// The drop target for dragging a shape out of the palette. Same command,
    /// same undo entry, same rename prompt as double-clicking — a drop should
    /// not be a second, subtly different way of adding a node.
    /// </remarks>
    public void AddFromPalette(PaletteItem item, Point worldCentre)
    {
        if (_model is null || _history is null) return;
        if (_model.Sequence is not null) throw new ArgumentException("Sequence diagrams use participant and interaction controls, not graph shapes.", nameof(item));
        if (_model.Chart is not null) throw new ArgumentException("Charts use data controls, not graph shapes.", nameof(item));
        if (_model.Outline is not null || _model.Temporal is not null || _model.Packet is not null || _model.Kanban is not null) throw new ArgumentException("This diagram uses typed structured controls.", nameof(item));
        if (_model.Requirements is not null || _model.GitDiagram is not null) throw new ArgumentException("Use typed requirement/GitGraph controls.", nameof(item));
        var state = _model.Kind is "stateDiagram" or "stateDiagram-v2";
        var pseudoId = state ? item.Name switch
        {
            "Start" => Core.Parsing.StateAdapter.StartId,
            "End" => Core.Parsing.StateAdapter.EndId,
            _ => null,
        } : null;
        if (state && item.Name is not ("Start" or "State" or "End"))
            throw new ArgumentException("This shape is not a supported state-diagram tool.", nameof(item));
        if (pseudoId is not null && _model.Node(pseudoId) is not null)
            throw new ArgumentException($"This diagram already has a {item.Name.ToLowerInvariant()} marker. Select it on the canvas.");

        var node = new DiagramNode
        {
            Id = pseudoId ?? NextId(),
            Label = pseudoId is null ? item.Label : "",
            Shape = item.Shape,
            Icon = item.Icon,
            Fill = item.Fill,
            Stroke = item.Stroke,
            HasExplicitPosition = true,
        };

        // Measure first, then place. Positioning against an assumed width and
        // measuring afterwards leaves the node off-centre by half the
        // difference — unnoticeable when the target was "the middle of the
        // view", obvious when it was "under the pointer".
        MeasureNode(node);

        // Snapped to the same 10px grid a drag uses, so a dropped node lines up
        // with everything already placed by hand.
        node.X = Math.Round((worldCentre.X - node.Width / 2) / 10) * 10;
        node.Y = Math.Round((worldCentre.Y - node.Height / 2) / 10) * 10;

        _history.Apply(new AddNodeCommand(node));
        _selection.Clear();
        _groupSelection.Clear();
        _selection.Add(node);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        ModelChanged?.Invoke(this, EventArgs.Empty);
        if (pseudoId is null) BeginRename(node);
        InvalidateVisual();
    }

    /// <summary>Screen point to world point, for a drop from outside.</summary>
    public Point ScreenToWorld(Point p) => ToWorld(p);

    /// <summary>
    /// Scroll the first selected node into view if it is off-screen. Picking
    /// something in the list and seeing the canvas not move reads as a bug.
    /// </summary>
    public void RevealFirstSelected()
    {
        var n = _selection.FirstOrDefault();
        if (n is null || Bounds.Width < 2) return;

        var screen = new Rect(
            n.X * Zoom + OffsetX, n.Y * Zoom + OffsetY,
            n.Width * Zoom, n.Height * Zoom);
        var view = new Rect(0, 0, Bounds.Width, Bounds.Height).Deflate(40);
        if (view.Contains(screen.TopLeft) && view.Contains(screen.BottomRight)) return;

        OffsetX = Bounds.Width / 2 - (n.X + n.Width / 2) * Zoom;
        OffsetY = Bounds.Height / 2 - (n.Y + n.Height / 2) * Zoom;
        InvalidateVisual();
    }


    /// <summary>Remove all Merpeeps presentation from the selection.</summary>
    public void ClearStyle()
    {
        if (_history is null || _selection.Count == 0) return;
        _history.Apply(new StyleNodesCommand(_selection.ToList(),
            new StyleNodesCommand.Style(
                Fill: "", Stroke: "", TextColor: "", StrokeWidth: 0,
                Dashed: false, ClearIcon: true)));
        foreach (var n in _selection)
        {
            // empty string is the sentinel the command writes; normalise to null
            if (n.Fill is "") n.Fill = null;
            if (n.Stroke is "") n.Stroke = null;
            if (n.TextColor is "") n.TextColor = null;
            if (n.StrokeWidth is 0) n.StrokeWidth = null;
            MeasureNode(n);
        }
        ModelChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    private static double Dist(Point a, Point b)
    {
        var dx = a.X - b.X; var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// Black or white text, whichever is legible on <paramref name="fill"/>.
    ///
    /// <para>The theme's label colour is wrong whenever a node carries its own
    /// fill: in dark mode the label is near-white, and the user's pastels are
    /// light, so every styled node became unreadable. Contrast has to follow
    /// the colour actually behind the text, not the colour of the page.</para>
    ///
    /// <para>Uses relative luminance from WCAG rather than a plain average,
    /// because the eye is far more sensitive to green than to blue and an
    /// average calls a saturated blue "light" when it is not.</para>
    /// </summary>
    private static IBrush ReadableOn(IBrush fill)
    {
        if (fill is not ISolidColorBrush solid) return Brushes.Black;
        var c = solid.Color;

        static double Channel(byte v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        var luminance = 0.2126 * Channel(c.R)
                      + 0.7152 * Channel(c.G)
                      + 0.0722 * Channel(c.B);

        // 0.179 is where white and black text have equal contrast ratio.
        return luminance > 0.179 ? DarkInk : LightInk;
    }

    private static readonly IBrush DarkInk = new SolidColorBrush(Color.Parse("#1A1A1A"));
    private static readonly IBrush LightInk = new SolidColorBrush(Color.Parse("#F2F2F2"));

    private DiagramNode? HitTest(Point world)
    {
        if (_model is null) return null;
        for (var i = _model.Nodes.Count - 1; i >= 0; i--)
        {
            var n = _model.Nodes[i];
            if (world.X >= n.X && world.X <= n.X + n.Width &&
                world.Y >= n.Y && world.Y <= n.Y + n.Height)
                return n;
        }
        return null;
    }

    /// <summary>
    /// The link under a point, or null.
    /// </summary>
    /// <remarks>
    /// <para>The tolerance is in screen pixels, converted to world units, so a
    /// line stays as easy to hit when zoomed out as when zoomed in. A tolerance
    /// in world units would shrink on screen exactly when the line is already
    /// hardest to aim at.</para>
    ///
    /// <para>Invisible links are skipped. They are drawn as nothing on purpose,
    /// and a click that selects something the user cannot see is worse than a
    /// click that misses.</para>
    /// </remarks>
    private DiagramEdge? HitTestEdge(Point world)
    {
        if (_model is null) return null;

        const double ScreenTolerance = 6;
        var tolerance = ScreenTolerance / Zoom;

        DiagramEdge? best = null;
        var nearest = double.MaxValue;

        foreach (var e in _model.Edges)
        {
            if (e.Kind == EdgeKind.Invisible) continue;
            if (e.EntityRelationship is not null)
            {
                var route = EntityDiagramGeometry.Route(_model, e);
                var distance = Enumerable.Range(1, route.Count - 1).Min(i =>
                    DistanceToSegment(world, new(route[i - 1].X, route[i - 1].Y), new(route[i].X, route[i].Y)));
                if (distance <= tolerance && distance < nearest) { best = e; nearest = distance; }
                continue;
            }
            if (e.ClassRelationship is null)
            {
                var route = GraphEdgeGeometry.Route(_model, e);
                if (route.Count < 2) continue;
                var distance = Enumerable.Range(1, route.Count - 1).Min(i =>
                    DistanceToSegment(world, new(route[i - 1].X, route[i - 1].Y), new(route[i].X, route[i].Y)));
                if (distance <= tolerance && distance < nearest) { best = e; nearest = distance; }
                continue;
            }

            var a = _model.Node(e.From);
            var b = _model.Node(e.To);
            if (a is null || b is null) continue;

            var ca = new Point(a.X + a.Width / 2, a.Y + a.Height / 2);
            var cb = new Point(b.X + b.Width / 2, b.Y + b.Height / 2);
            var p1 = EdgePoint(a, cb);
            var p2 = EdgePoint(b, ca);

            if (e.ClassRelationship is not null)
            {
                var from = ClassDiagramGeometry.Anchor(a, ClassDiagramGeometry.Center(b));
                var to = ClassDiagramGeometry.Anchor(b, ClassDiagramGeometry.Center(a));
                p1 = new Point(from.X, from.Y);
                p2 = new Point(to.X, to.Y);
            }
            var d = DistanceToSegment(world, p1, p2);
            if (d > tolerance || d >= nearest) continue;

            nearest = d;
            best = e;
        }

        return best;
    }

    /// <summary>Shortest distance from a point to a line segment.</summary>
    private static double DistanceToSegment(Point p, Point a, Point b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var lengthSquared = dx * dx + dy * dy;

        // A degenerate segment is a point; without this the projection divides
        // by zero and every click "hits" it.
        if (lengthSquared < 1e-9) return Dist(p, a);

        // Clamped, so a click level with the line but past its end does not
        // register against the infinite line it sits on.
        var t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lengthSquared, 0, 1);
        return Dist(p, new Point(a.X + t * dx, a.Y + t * dy));
    }
}
