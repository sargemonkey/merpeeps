using System.Globalization;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Merpeeps.Core.Editing;
using Merpeeps.Core.Model;

namespace Merpeeps.App;

public sealed partial class EditorCanvas
{
    private DiagramNode? _classRelationshipSource;
    private DiagramNode? _classRelationshipTarget;
    private DiagramEdge? _classReconnectEdge;
    private bool _classReconnectSource;
    private Point _classRelationshipPoint;
    public DiagramNode? ClassRelationshipSource => _classRelationshipSource;
    public string? ClassRelationshipMessage { get; private set; }
    public bool IsReconnectingClassRelationship => _classReconnectEdge is not null;
    public event Action<DiagramEdge, DiagramEdge>? ClassRelationshipReconnected;
    public event EventHandler? HistoryNavigated;

    public void BeginClassRelationship(DiagramNode source)
    {
        RequireClassTarget(source);
        SelectByIds([source.Id]);
        _classReconnectEdge = null;
        StartClassConnection(source);
    }

    public void BeginClassReconnect(DiagramEdge edge, bool sourceEnd)
    {
        if (_model?.ClassOptions is null || !_model.Edges.Contains(edge))
            throw new InvalidOperationException("Select a relationship in the current class diagram.");
        if (edge.ClassRelationship is { FromEnd: ClassRelationEnd.Lollipop } or { ToEnd: ClassRelationEnd.Lollipop })
            throw new ArgumentException("An interface occurrence belongs to its owner; recreate its connection instead.");
        var fixedNode = _model.Node(sourceEnd ? edge.To : edge.From)!;
        RequireClassTarget(fixedNode);
        SelectEdge(edge);
        _classReconnectEdge = edge;
        _classReconnectSource = sourceEnd;
        StartClassConnection(fixedNode);
    }

    private void StartClassConnection(DiagramNode source)
    {
        if (IsEditing) CommitLabel();
        _classRelationshipSource = source;
        _classRelationshipTarget = null;
        _classRelationshipPoint = new(source.X + source.Width + 30, source.Y + source.Height / 2);
        ClassRelationshipMessage = IsReconnectingClassRelationship
            ? "Click the replacement class on the canvas. Escape cancels."
            : $"Click another class to connect from {source.Display}. Escape cancels.";
        Tool = CanvasTool.Select;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        Focus();
        InvalidateVisual();
    }

    public void CompleteClassRelationship(DiagramNode target)
    {
        var source = _classRelationshipSource
            ?? throw new InvalidOperationException("Start a relationship from a selected class first.");
        RequireClassTarget(source);
        RequireClassTarget(target);
        if (ReferenceEquals(source, target))
            throw new ArgumentException("Choose another class. Self-relationship routing is not available.");
        DiagramEdge selected;
        if (_classReconnectEdge is { } old)
        {
            var command = new ReconnectClassRelationshipCommand(old,
                _classReconnectSource ? target.Id : source.Id,
                _classReconnectSource ? source.Id : target.Id);
            _history!.Apply(command);
            selected = command.Result!;
            ClassRelationshipReconnected?.Invoke(old, selected);
        }
        else
        {
            var ordinal = _model!.Edges.Where(e => e.From == source.Id && e.To == target.Id)
                .Select(e => e.Ordinal + 1).DefaultIfEmpty(0).Max();
            selected = new()
            {
                From = source.Id,
                To = target.Id,
                Ordinal = ordinal,
                Kind = EdgeKind.Open,
                ClassRelationship = new(),
            };
            _history!.Apply(new AddClassRelationshipCommand(selected));
        }
        ResetClassConnection();
        SelectEdge(selected);
        ModelChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    public void CancelClassRelationship()
    {
        if (_classRelationshipSource is null) return;
        ResetClassConnection();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    private void ResetClassConnection()
    {
        _classRelationshipSource = null;
        _classRelationshipTarget = null;
        _classReconnectEdge = null;
        ClassRelationshipMessage = null;
    }

    private void RequireClassTarget(DiagramNode node)
    {
        if (_model?.ClassOptions is null || _history is null || !_model.Nodes.Contains(node) ||
            node.Class?.Kind != ClassNodeKind.Class)
            throw new ArgumentException("Choose a class card, not a note or interface.");
    }

    private void DrawClassConnection(DrawingContext context)
    {
        if (_classRelationshipSource is not { } source) return;
        var from = ClassDiagramGeometry.Anchor(source, new(_classRelationshipPoint.X, _classRelationshipPoint.Y));
        var to = _classRelationshipTarget is { } target
            ? ClassDiagramGeometry.Anchor(target, ClassDiagramGeometry.Center(source))
            : new DiagramPoint(_classRelationshipPoint.X, _classRelationshipPoint.Y);
        context.DrawLine(_theme.Ghost, new(from.X, from.Y), new(to.X, to.Y));
        DrawArrowHead(context, new(from.X, from.Y), new(to.X, to.Y), _theme.Ghost);
    }

    public void ApplyClassEdit(IEditCommand command, string? selectedId = null)
    {
        if (_model?.ClassOptions is null || _history is null)
            throw new InvalidOperationException("Open a class diagram before editing class structure.");
        ApplyDiagramEdit(command, selectedId);
    }

    public void ApplyDiagramEdit(IEditCommand command, string? selectedId = null)
    {
        if (_model is null || _history is null) throw new InvalidOperationException("Open a diagram before editing.");
        EndGraphDrag(cancel: true);
        CancelEntityConnection();
        ResetClassConnection();
        _history.Apply(command);
        _selection.RemoveWhere(n => !_model.Nodes.Contains(n));
        if (_selectedEdge is not null && !_model.Edges.Contains(_selectedEdge)) _selectedEdge = null;
        _groupSelection.RemoveWhere(g => !_model.Subgraphs.Contains(g));
        if (selectedId is not null)
        {
            SelectByIds([selectedId]);
            RevealFirstSelected();
        }
        ModelChanged?.Invoke(this, EventArgs.Empty);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    private static IBrush Brush(ClassColor color) =>
        new SolidColorBrush(Color.FromArgb(color.Alpha, color.Red, color.Green, color.Blue));

    private void DrawClassNode(DrawingContext context, DiagramNode node)
    {
        var definition = node.Class!;
        var style = ClassDiagramStyle.Resolve(node, _showStyling);
        var fill = style.Fill is { } f ? Brush(f) : _diagramTheme is { } dt ? new SolidColorBrush(dt.Fill) : _theme.NodeFill;
        var stroke = style.Stroke is { } s ? Brush(s) : _diagramTheme is { } ds ? new SolidColorBrush(ds.Stroke) : _theme.NodeStroke.Brush;
        var text = style.TextColor is { } c ? Brush(c) : ReadableOn(fill);
        var pen = new Pen(stroke, style.StrokeWidth)
        {
            DashStyle = style.DashPattern.Count == 0 ? null
                : new DashStyle(style.DashPattern.Select(d => d / style.StrokeWidth), 0),
        };
        var bounds = new Rect(node.X, node.Y, node.Width, node.Height);
        if (ReferenceEquals(node, _classRelationshipTarget))
            context.DrawRectangle(null, _theme.TargetStroke, bounds.Inflate(3));
        if (_selection.Contains(node)) context.DrawRectangle(null, _theme.NodeStrokeSelected, bounds.Inflate(3));
        if (definition.Kind == ClassNodeKind.Lollipop)
        {
            var center = ClassDiagramGeometry.Center(node);
            context.DrawEllipse(fill, pen, new Point(center.X, center.Y), 8, 8);
            DrawRow(new(node.Display), new(node.X, node.Y + 25, node.Width, node.Height - 25), true);
            return;
        }
        if (definition.Kind == ClassNodeKind.Note)
        {
            context.DrawGeometry(fill, pen, Poly(bounds.TopLeft, new(bounds.Right - 12, bounds.Top),
                new(bounds.Right, bounds.Top + 12), bounds.BottomRight, bounds.BottomLeft));
            var index = 0;
            foreach (var line in node.Display.Split('\n'))
                DrawRow(new(line), new(node.X, node.Y + index++ * style.FontSize * 1.4, node.Width, style.FontSize * 1.4), false);
            return;
        }
        context.DrawRectangle(fill, pen, bounds);
        if (Zoom <= .3) return;
        var content = ClassDiagramText.Content(node, _model!.ClassOptions!);
        var regions = ClassDiagramGeometry.CardRegions(node, _model.ClassOptions!, style.FontSize * 1.4);
        DrawRows(content.Header, regions.Header, true);
        if (content.ShowMemberCompartments)
        {
            foreach (var y in new[] { regions.Attributes.Y, regions.Operations.Y })
                context.DrawLine(pen, new(node.X, y), new(node.X + node.Width, y));
            DrawRows(content.Attributes, regions.Attributes, false);
            DrawRows(content.Operations, regions.Operations, false);
        }

        void DrawRows(IEnumerable<ClassTextRow> rows, DiagramRect region, bool centered)
        {
            var index = 0;
            foreach (var row in rows)
                DrawRow(row, new(region.X, region.Y + index++ * style.FontSize * 1.4, region.Width, style.FontSize * 1.4), centered);
        }
        void DrawRow(ClassTextRow row, DiagramRect region, bool centered)
        {
            var font = new Typeface(style.FontFamily,
                row.Italic || style.Italic ? FontStyle.Italic : FontStyle.Normal,
                row.Bold || style.Bold ? FontWeight.Bold : FontWeight.Normal);
            var formatted = new FormattedText(row.Text, CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, font, style.FontSize, text);
            var x = centered ? region.X + (region.Width - formatted.Width) / 2 : region.X + 10;
            var y = region.Y + 6;
            context.DrawText(formatted, new(x, y));
            if (row.Underline) context.DrawLine(new Pen(text, 1), new(x, y + formatted.Height), new(x + formatted.Width, y + formatted.Height));
        }
    }

    private void DrawClassEdge(DrawingContext context, DiagramEdge edge)
    {
        var model = _model!;
        var from = model.Node(edge.From)!;
        var to = model.Node(edge.To)!;
        var start = ClassDiagramGeometry.Anchor(from, ClassDiagramGeometry.Center(to));
        var end = ClassDiagramGeometry.Anchor(to, ClassDiagramGeometry.Center(from));
        var relation = edge.ClassRelationship!;
        var pen = relation.Dashed ? _theme.EdgeDotted : _theme.Edge;
        Point Point(DiagramPoint p) => new(p.X, p.Y);
        if (ReferenceEquals(edge, _selectedEdge)) context.DrawLine(_theme.EdgeSelectedHalo, Point(start), Point(end));
        context.DrawLine(pen, Point(start), Point(end));
        foreach (var (marker, tip, toward) in new[] { (relation.FromEnd, start, end), (relation.ToEnd, end, start) })
        {
            var geometry = ClassDiagramGeometry.Marker(marker, tip, toward);
            if (geometry.Points.Count == 0) continue;
            if (geometry.Closed)
                context.DrawGeometry(geometry.Filled ? pen.Brush : _theme.NodeFill, new Pen(pen.Brush, 1.4),
                    Poly(geometry.Points.Select(Point).ToArray()));
            else
                for (var i = 1; i < geometry.Points.Count; i++)
                    context.DrawLine(new Pen(pen.Brush, 1.4), Point(geometry.Points[i - 1]), Point(geometry.Points[i]));
        }
        Label(relation.FromMultiplicity, .15);
        Label(relation.ToMultiplicity, .85);
        Label(edge.Label, .5);
        void Label(string? value, double position)
        {
            if (string.IsNullOrEmpty(value)) return;
            var text = new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face, 11, _theme.Label);
            var x = start.X + (end.X - start.X) * position;
            var y = start.Y + (end.Y - start.Y) * position;
            context.DrawText(text, new(x - text.Width / 2, y - text.Height - 5));
        }
    }

    private void DrawClassNamespaces(DrawingContext context)
    {
        foreach (var group in _model!.Subgraphs)
        {
            if (_model.ClassOptions!.HierarchicalNamespaces == false && !group.IsExplicit) continue;
            var bounds = GraphGroupGeometry.Bounds(_model, group);
            context.DrawRectangle(_theme.SubgraphFill, _groupSelection.Contains(group) ? _theme.NodeStrokeSelected : _theme.SubgraphStroke,
                new(bounds.X, bounds.Y, bounds.Width, bounds.Height));
            var label = new FormattedText(group.Title ?? group.Id, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face, 12, _theme.Label);
            context.DrawText(label, new(bounds.X + 8, bounds.Y + 6));
        }
        foreach (var note in _model.Nodes.Where(n => n.Class?.NoteFor is not null))
        {
            var target = _model.Node(note.Class!.NoteFor!);
            if (target is null) continue;
            var start = ClassDiagramGeometry.Anchor(note, ClassDiagramGeometry.Center(target));
            var end = ClassDiagramGeometry.Anchor(target, ClassDiagramGeometry.Center(note));
            context.DrawLine(_theme.EdgeDotted, new(start.X, start.Y), new(end.X, end.Y));
        }
    }
}
