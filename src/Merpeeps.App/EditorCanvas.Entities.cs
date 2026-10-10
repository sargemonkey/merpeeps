using System.Globalization;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Merpeeps.Core.Editing;
using Merpeeps.Core.Model;

namespace Merpeeps.App;

public sealed partial class EditorCanvas
{
    private string? _entityConnectionSource;
    private DiagramEdge? _entityReconnect;
    private bool _entityReplaceSource;
    public string? EntityConnectionSource => _entityConnectionSource;
    public DiagramSubgraph? SelectedEntityGroup => _model?.EntityOptions is null ? null : _selectedGroup;
    public string? EntityConnectionMessage { get; private set; }
    public event Action<DiagramEdge, DiagramEdge>? EntityRelationshipReconnected;
    public event EventHandler? EntityRenameRequested;

    public void SelectEntityGroup(DiagramSubgraph? group)
    {
        if (group is not null && (_model?.EntityOptions is null || !_model.Subgraphs.Contains(group)))
            throw new ArgumentException("Choose a subgraph in the current ER diagram.");
        SelectGroup(group);
    }

    public void BeginEntityRelationship(string sourceId, DiagramEdge? reconnect = null, bool replaceSource = false)
    {
        if (_model?.EntityOptions is null || _history is null) throw new InvalidOperationException("Open an ER diagram.");
        _ = EntityDiagramGeometry.Endpoint(_model, sourceId);
        _entityConnectionSource = sourceId; _entityReconnect = reconnect; _entityReplaceSource = replaceSource;
        EntityConnectionMessage = "Click an entity or subgraph to connect. Escape cancels.";
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        Focus();
    }

    public void CompleteEntityRelationship(string targetId)
    {
        if (_model?.EntityOptions is null || _history is null || _entityConnectionSource is not { } sourceId)
            throw new InvalidOperationException("Start from an ER endpoint first.");
        _ = EntityDiagramGeometry.Endpoint(_model, targetId);
        DiagramEdge selected;
        if (_entityReconnect is { } original)
        {
            var command = new ReconnectEntityRelationshipCommand(original,
                _entityReplaceSource ? targetId : sourceId, _entityReplaceSource ? sourceId : targetId);
            _history.Apply(command);
            selected = command.Result!;
            EntityRelationshipReconnected?.Invoke(original, selected);
        }
        else
        {
            selected = new()
            {
                From = sourceId,
                To = targetId,
                Label = "",
                Kind = EdgeKind.Open,
                EntityRelationship = new(),
                Ordinal = _model.Edges.Where(e => e.From == sourceId && e.To == targetId).Select(e => e.Ordinal + 1).DefaultIfEmpty(0).Max(),
            };
            _history.Apply(new AddEntityRelationshipCommand(selected));
        }
        _entityConnectionSource = null; _entityReconnect = null; EntityConnectionMessage = null;
        SelectEdge(selected);
        ModelChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    public void CancelEntityConnection()
    {
        if (_entityConnectionSource is null) return;
        _entityConnectionSource = null; _entityReconnect = null; EntityConnectionMessage = null;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    private DiagramSubgraph? HitEntityGroup(Point point)
    {
        return _model?.EntityOptions is null ? null : HitGroup(point);
    }

    private void DrawEntityGroups(DrawingContext context)
    {
        foreach (var group in _model!.Subgraphs)
        {
            var bounds = EntityDiagramGeometry.GroupBounds(_model, group);
            var rectangle = new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
            context.DrawRectangle(_theme.SubgraphFill, _groupSelection.Contains(group) ? _theme.NodeStrokeSelected : _theme.SubgraphStroke, rectangle);
            var title = new FormattedText(group.Title ?? group.Id, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face, 12, _theme.Label);
            context.DrawText(title, new(rectangle.X + 8, rectangle.Y + 7));
        }
    }

    private void DrawEntityNode(DrawingContext context, DiagramNode node)
    {
        var style = EntityDiagramStyle.Resolve(node, _showStyling);
        var fill = style.Fill is { } f ? Brush(f) : _diagramTheme is { } palette ? new SolidColorBrush(palette.Fill) : _theme.NodeFill;
        var stroke = style.Stroke is { } s ? Brush(s) : _diagramTheme is { } outline ? new SolidColorBrush(outline.Stroke) : _theme.NodeStroke.Brush;
        var ink = style.TextColor is { } t ? Brush(t) : ReadableOn(fill);
        var pen = new Pen(stroke, style.StrokeWidth)
        {
            DashStyle = style.DashPattern.Count == 0 ? null : new DashStyle(style.DashPattern.Select(d => d / style.StrokeWidth), 0),
        };
        var rectangle = new Rect(node.X, node.Y, node.Width, node.Height);
        if (_selection.Contains(node)) context.DrawRectangle(null, _theme.NodeStrokeSelected, rectangle.Inflate(3));
        context.DrawRectangle(fill, pen, rectangle);
        if (Zoom <= .3) return;
        var face = new Typeface(style.FontFamily, style.Italic ? FontStyle.Italic : FontStyle.Normal, style.Bold ? FontWeight.Bold : FontWeight.Normal);
        FormattedText Text(string text, Typeface? font = null) => new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, font ?? face, style.FontSize, ink);
        var content = EntityDiagramText.Content(node);
        var names = content.NameRuns.Select(run => Text(run.Text, new(style.FontFamily,
            run.Italic || style.Italic ? FontStyle.Italic : FontStyle.Normal, run.Bold || style.Bold ? FontWeight.Bold : FontWeight.Normal))).ToArray();
        var x = node.X + (node.Width - names.Sum(n => n.Width)) / 2;
        foreach (var text in names) { context.DrawText(text, new(x, node.Y + 7)); x += text.Width; }
        var header = style.FontSize * 1.4 + 16;
        var rowHeight = style.FontSize * 1.4 + 8;
        var columns = EntityDiagramGeometry.Columns(node, style.FontSize, value => Text(value).Width);
        if (content.Attributes.Count > 0) context.DrawLine(pen, new(node.X, node.Y + header), new(node.X + node.Width, node.Y + header));
        for (var index = 0; index < content.Attributes.Count; index++)
        {
            var row = content.Attributes[index];
            var values = new[] { row.Type, row.Name, row.Keys, row.Comment ?? "" };
            var top = node.Y + header + index * rowHeight;
            x = node.X;
            for (var col = 0; col < values.Length; col++)
            {
                context.DrawText(Text(values[col]), new(x + 7, top + 4));
                x += columns[col];
                if (col < values.Length - 1) context.DrawLine(pen, new(x, top), new(x, top + rowHeight));
            }
            if (index < content.Attributes.Count - 1) context.DrawLine(pen, new(node.X, top + rowHeight), new(node.X + node.Width, top + rowHeight));
        }
    }

    private void DrawEntityEdge(DrawingContext context, DiagramEdge edge)
    {
        var points = EntityDiagramGeometry.Route(_model!, edge);
        var pen = edge.EntityRelationship!.Identifying ? _theme.Edge : _theme.EdgeDotted;
        static Point Point(DiagramPoint p) => new(p.X, p.Y);
        for (var i = 1; i < points.Count; i++)
        {
            if (ReferenceEquals(edge, _selectedEdge)) context.DrawLine(_theme.EdgeSelectedHalo, Point(points[i - 1]), Point(points[i]));
            context.DrawLine(pen, Point(points[i - 1]), Point(points[i]));
        }
        foreach (var marker in new[]
        {
            EntityDiagramGeometry.Marker(edge.EntityRelationship.FromCardinality, points[0], points[1]),
            EntityDiagramGeometry.Marker(edge.EntityRelationship.ToCardinality, points[^1], points[^2]),
        })
        {
            foreach (var line in marker.Lines) context.DrawLine(_theme.Edge, Point(line.Start), Point(line.End));
            foreach (var circle in marker.Circles) context.DrawEllipse(_theme.Background, _theme.Edge, Point(circle.Center), circle.Radius, circle.Radius);
        }
        if (string.IsNullOrEmpty(edge.Label)) return;
        var mid = points.Count == 2 ? new DiagramPoint((points[0].X + points[1].X) / 2, (points[0].Y + points[1].Y) / 2) : points[points.Count / 2];
        var label = new FormattedText(edge.Label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face, 11, _theme.Label);
        context.DrawText(label, new(mid.X - label.Width / 2, mid.Y - label.Height - 5));
    }
}
