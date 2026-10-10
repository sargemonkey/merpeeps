using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Merpeeps.Core.Editing;
using Merpeeps.Core.Layout;
using Merpeeps.Core.Model;

namespace Merpeeps.App;

public sealed partial class EditorCanvas
{
    private ChartDocument? _chartCacheModel;
    private string? _chartCacheTheme;
    private ChartTarget? _chartSelection;
    private ChartTarget? _chartHover;
    private ChartTarget? _chartCacheHover;
    private ChartLayout? _chartLayout;
    private ChartDocument? _chartDragPreview;
    private ChartLayout? _chartDragLayout;
    private ChartTarget? _chartDragTarget;
    private DiagramPoint _chartDragValue;
    public ChartTarget? SelectedChartTarget => _chartSelection;
    public Func<bool>? ChartEditReady { get; set; }
    public string? ChartActionError { get; private set; }
    public event EventHandler? ChartEditRequested;

    public ChartLayout ChartScene
    {
        get
        {
            var chart = _chartDragPreview ?? _model?.Chart ?? throw new InvalidOperationException("Open a data chart.");
            if (!ReferenceEquals(chart, _chartCacheModel) || _chartCacheTheme != _model!.Theme || _chartCacheHover != _chartHover)
            {
                _chartLayout = ChartDiagramLayout.Create(new DiagramModel { Chart = chart, Theme = _model!.Theme }, MeasureDrawingText, hovered: _chartHover);
                _chartCacheModel = chart; _chartCacheTheme = _model.Theme; _chartCacheHover = _chartHover;
            }
            return _chartLayout!;
        }
    }
    public static DrawingTextSize MeasureDrawingText(string text, DrawingFont font)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(font.Family, font.Italic ? FontStyle.Italic : FontStyle.Normal, (FontWeight)font.Weight), font.Size, Brushes.Black);
        return new(formatted.WidthIncludingTrailingWhitespace, formatted.Height);
    }
    public void SelectChart(ChartTarget? target)
    {
        ChartActionError = null;
        if (_chartSelection != target) _chartSelection = target;
        _selection.Clear(); _selectedEdge = null; _selectedSequence = null; _selectedGroup = null;
        SelectionChanged?.Invoke(this, EventArgs.Empty); InvalidateVisual();
    }
    public void DeleteChartSelection()
    {
        if (_model?.Chart is null || _chartSelection is not { } target) return;
        try
        {
            ChartEditCommand? command = (_model.Chart, target.Role) switch
            {
                (PieChart, ChartElementRole.Slice) => ChartEditing.DeleteSlice(target.Id),
                (QuadrantChart, ChartElementRole.Point) => ChartEditing.DeleteQuadrantPoint(target.Id),
                (XyChart, ChartElementRole.Series) => ChartEditing.DeleteSeries(target.Id),
                (XyChart, ChartElementRole.Point) => ChartEditing.DeleteValue(target.DetailId!, target.Id),
                (RadarChart, ChartElementRole.Axis) => ChartEditing.DeleteRadarAxis(target.Id),
                (RadarChart, ChartElementRole.Curve) => ChartEditing.DeleteRadarCurve(target.Id),
                _ => null,
            };
            if (command is null) return;
            ApplyDiagramEdit(command); SelectChart(command.Result);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException)
        {
            ChartActionError = error.Message;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    private void ClearChartState()
    {
        _chartSelection = _chartHover = _chartCacheHover = _chartDragTarget = null;
        _chartCacheModel = _chartDragPreview = null; _chartDragLayout = null;
    }
    public bool ChartTargetExists(ChartTarget target) => _model?.Chart switch
    {
        PieChart pie when target.Role == ChartElementRole.Slice => pie.Slices.Any(s => s.Id == target.Id),
        QuadrantChart quadrant when target.Role == ChartElementRole.Point => quadrant.Points.Any(p => p.Id == target.Id),
        XyChart xy when target.Role == ChartElementRole.Series => xy.Series.Any(s => s.Id == target.Id),
        XyChart xy when target.Role == ChartElementRole.Point => xy.Series.Any(s => s.Id == target.DetailId && s.Values.Any(v => v.Id == target.Id)),
        XyChart when target.Role == ChartElementRole.Axis => target.Id is "x" or "y",
        RadarChart radar when target.Role == ChartElementRole.Axis => radar.Axes.Any(a => a.Id == target.Id),
        RadarChart radar when target.Role == ChartElementRole.Curve => radar.Curves.Any(c => c.Id == target.Id),
        RadarChart radar when target.Role == ChartElementRole.CurveValue => radar.Curves.Any(c => c.Id == target.Id && c.Values.Any(v => v.AxisId == target.DetailId)),
        _ => target.Role == ChartElementRole.Chart,
    };
    private bool ChartPointer(Point point)
    {
        var target = ChartDiagramLayout.Hit(ChartScene, new(point.X, point.Y), 6 / Zoom);
        SelectChart(target);
        if (target is null) return false;
        var editable = _model!.Chart is QuadrantChart && target.Role == ChartElementRole.Point ||
            _model.Chart is XyChart && target.Role == ChartElementRole.Point && target.DetailId is not null ||
            _model.Chart is RadarChart && target.Role == ChartElementRole.CurveValue;
        if (!editable) return false;
        if (ChartEditReady?.Invoke() == false)
        {
            ChartActionError = "Apply or cancel the unapplied editor fields before dragging chart data.";
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            return false;
        }
        _chartDragTarget = target; _chartDragLayout = ChartScene; _chartDragPreview = _model.Chart!.Clone();
        if (_chartDragLayout.Coordinates is { } map && map.YMinimum == map.YMaximum && _model.Chart is XyChart)
        {
            var delta = Math.Max(1, Math.Abs(map.YMinimum) * .1);
            _chartDragLayout = _chartDragLayout with { Coordinates = map with { YMinimum = map.YMinimum - delta, YMaximum = map.YMaximum + delta } };
        }
        _chartDragValue = _model.Chart switch
        {
            QuadrantChart q => new(q.Points.Single(p => p.Id == target.Id).X, q.Points.Single(p => p.Id == target.Id).Y),
            XyChart xy => new(0, xy.Series.Single(s => s.Id == target.DetailId).Values.Single(v => v.Id == target.Id).Value),
            RadarChart radar => new(0, radar.Curves.Single(c => c.Id == target.Id).Values.Single(v => v.AxisId == target.DetailId).Value),
            _ => default,
        };
        return true;
    }
    private void ChartPointerMove(Point point)
    {
        if (_chartDragTarget is not { } target || _chartDragPreview is null || _chartDragLayout is null)
        {
            var hover = ChartDiagramLayout.Hit(ChartScene, new(point.X, point.Y), 6 / Zoom);
            if (hover != _chartHover) { _chartHover = hover; InvalidateVisual(); }
            return;
        }
        switch (_chartDragPreview)
        {
            case QuadrantChart quadrant:
                _chartDragValue = _chartDragLayout.Coordinates!.FromPoint(new(point.X, point.Y));
                var index = quadrant.Points.FindIndex(p => p.Id == target.Id);
                quadrant.Points[index] = quadrant.Points[index] with { X = _chartDragValue.X, Y = _chartDragValue.Y };
                break;
            case XyChart xy:
                _chartDragValue = _chartDragLayout.Coordinates!.FromPoint(new(point.X, point.Y));
                var seriesIndex = xy.Series.FindIndex(s => s.Id == target.DetailId);
                xy.Series[seriesIndex] = xy.Series[seriesIndex] with
                {
                    Values = xy.Series[seriesIndex].Values.Select(v => v.Id == target.Id ? v with { Value = _chartDragValue.Y } : v).ToArray(),
                };
                xy.YAxis = new ChartAxis(ChartAxisKind.Range, xy.YAxis.Title, _chartDragLayout.Coordinates.YMinimum, _chartDragLayout.Coordinates.YMaximum);
                break;
            case RadarChart radar:
                _chartDragValue = new(0, _chartDragLayout.RadarCoordinates!.ValueAt(target.DetailId!, new(point.X, point.Y)));
                var curveIndex = radar.Curves.FindIndex(c => c.Id == target.Id);
                radar.Curves[curveIndex] = radar.Curves[curveIndex] with
                {
                    Values = radar.Curves[curveIndex].Values.Select(v => v.AxisId == target.DetailId ? v with { Value = _chartDragValue.Y } : v).ToArray(),
                };
                radar.Maximum = _chartDragLayout.RadarCoordinates.Maximum;
                break;
        }
        _chartCacheModel = null;
        InvalidateVisual();
    }
    private void CommitChartDrag()
    {
        if (_chartDragTarget is not { } target || _model?.Chart is null || _history is null) return;
        ChartEditCommand? command = _model.Chart switch
        {
            QuadrantChart q when q.Points.Single(p => p.Id == target.Id) is var point &&
                (Math.Abs(point.X - _chartDragValue.X) > 1e-9 || Math.Abs(point.Y - _chartDragValue.Y) > 1e-9)
                => ChartEditing.MoveQuadrantPoint(target.Id, _chartDragValue.X, _chartDragValue.Y),
            XyChart xy when xy.Series.Single(s => s.Id == target.DetailId).Values.Single(v => v.Id == target.Id) is var value &&
                Math.Abs(value.Value - _chartDragValue.Y) > 1e-9
                => ChartEditing.UpdateValue(target.DetailId!, value with { Value = _chartDragValue.Y }),
            RadarChart radar when Math.Abs(radar.Curves.Single(c => c.Id == target.Id).Values.Single(v => v.AxisId == target.DetailId).Value - _chartDragValue.Y) > 1e-9
                => ChartEditing.SetRadarValue(target.Id, target.DetailId!, _chartDragValue.Y),
            _ => null,
        };
        _chartDragTarget = null; _chartDragPreview = null; _chartDragLayout = null; _chartCacheModel = null;
        try
        {
            if (command is not null) ApplyDiagramEdit(command);
            SelectChart(target);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException)
        {
            ChartActionError = error.Message;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    private void CancelChartDrag()
    {
        _chartDragTarget = null; _chartDragPreview = null; _chartDragLayout = null; _chartCacheModel = null;
        InvalidateVisual();
    }

    private void DrawChart(DrawingContext context)
    {
        DrawNeutralScene(context, ChartScene.Scene, _chartSelection is { } target ? new DrawingOwner(target.Role.ToString(), target.Id, target.DetailId) : null);
    }

    private void DrawNeutralScene(DrawingContext context, DrawingScene scene, DrawingOwner? selectedOwner)
    {
        foreach (var figure in scene.Figures)
        {
            if (figure.Clip is { } clip)
            {
                using var pushed = context.PushClip(new Rect(clip.X, clip.Y, clip.Width, clip.Height));
                DrawFigure(figure);
            }
            else DrawFigure(figure);
        }
        foreach (var label in scene.Labels)
        {
            var text = new FormattedText(label.Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(label.Font.Family, label.Font.Italic ? FontStyle.Italic : FontStyle.Normal, (FontWeight)label.Font.Weight),
                label.Font.Size, Brush(label.Color));
            var r = label.Bounds;
            var center = new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
            using var rotated = context.PushTransform(Matrix.CreateTranslation(-center.X, -center.Y) *
                Matrix.CreateRotation(label.Rotation * Math.PI / 180) * Matrix.CreateTranslation(center.X, center.Y));
            if (label.Outline is { } outline && label.OutlineWidth > 0 && text.BuildGeometry(new Point(r.X, r.Y)) is { } geometry)
                context.DrawGeometry(null, new Pen(Brush(outline), label.OutlineWidth, lineJoin: PenLineJoin.Round), geometry);
            context.DrawText(text, new(r.X, r.Y));
        }
        if (selectedOwner is { } selected)
        {
            foreach (var region in scene.Regions.Where(r => r.Owner == selected).Take(1))
            {
                var r = region.Bounds;
                context.DrawRectangle(null, _theme.NodeStrokeSelected, new Rect(r.X, r.Y, r.Width, r.Height).Inflate(2));
            }
        }
        void DrawFigure(DrawingFigure figure)
        {
            IBrush? fill = figure.Fill is { } f ? Brush(f) : null;
            if (figure.Gradient is { } gradient)
            {
                var angle = gradient.Angle * Math.PI / 180;
                fill = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(.5 - .5 * Math.Cos(angle), .5 - .5 * Math.Sin(angle), RelativeUnit.Relative),
                    EndPoint = new RelativePoint(.5 + .5 * Math.Cos(angle), .5 + .5 * Math.Sin(angle), RelativeUnit.Relative),
                    GradientStops = new GradientStops
                    {
                        new GradientStop(Color.FromArgb(gradient.Start.Alpha, gradient.Start.Red, gradient.Start.Green, gradient.Start.Blue), 0),
                        new GradientStop(Color.FromArgb(gradient.End.Alpha, gradient.End.Red, gradient.End.Green, gradient.End.Blue), 1),
                    },
                };
            }
            IPen? pen = figure.Stroke is { } stroke && figure.StrokeWidth > 0 ? new Pen(Brush(stroke), figure.StrokeWidth,
                figure.DashPattern is { Count: > 0 } pattern ? new DashStyle(pattern.Select(value => value / figure.StrokeWidth), 0)
                    : figure.Dashed ? new DashStyle([4 / figure.StrokeWidth, 4 / figure.StrokeWidth], 0) : null) : null;
            var r = figure.Bounds;
            switch (figure.Kind)
            {
                case DrawingFigureKind.Rectangle: context.DrawRectangle(fill, pen, new(r.X, r.Y, r.Width, r.Height)); break;
                case DrawingFigureKind.Ellipse: context.DrawEllipse(fill, pen, new(r.X + r.Width / 2, r.Y + r.Height / 2), r.Width / 2, r.Height / 2); break;
                case DrawingFigureKind.Polygon:
                    if (figure.Points.Count > 0) context.DrawGeometry(fill, pen, Poly(figure.Points.Select(p => new Point(p.X, p.Y)).ToArray()));
                    break;
                case DrawingFigureKind.Polyline:
                    if (pen is not null)
                        for (var i = 1; i < figure.Points.Count; i++)
                            context.DrawLine(pen, new(figure.Points[i - 1].X, figure.Points[i - 1].Y), new(figure.Points[i].X, figure.Points[i].Y));
                    break;
                case DrawingFigureKind.Path:
                    var geometry = new StreamGeometry();
                    using (var path = geometry.Open())
                    {
                        path.SetFillRule(FillRule.EvenOdd);
                        var open = false;
                        foreach (var part in figure.Path)
                        {
                            var end = new Point(part.End.X, part.End.Y);
                            switch (part.Kind)
                            {
                                case DrawingPathKind.Move:
                                    if (open) path.EndFigure(false);
                                    path.BeginFigure(end, fill is not null); open = true; break;
                                case DrawingPathKind.Line: path.LineTo(end); break;
                                case DrawingPathKind.Cubic: path.CubicBezierTo(new(part.Control1.X, part.Control1.Y), new(part.Control2.X, part.Control2.Y), end); break;
                                case DrawingPathKind.Close: path.EndFigure(true); open = false; break;
                            }
                        }
                        if (open) path.EndFigure(false);
                    }
                    context.DrawGeometry(fill, pen, geometry);
                    break;
            }
        }
    }
}
