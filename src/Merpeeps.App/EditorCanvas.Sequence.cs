using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Merpeeps.Core.Editing;
using Merpeeps.Core.Layout;
using Merpeeps.Core.Model;

namespace Merpeeps.App;

public sealed record SequenceSelection(string Id, SequenceElementKind Kind);

public sealed partial class EditorCanvas
{
    private SequenceDiagramModel? _sequenceCacheModel;
    private SequenceOptions? _sequenceCacheOptions;
    private string? _sequenceCacheTheme;
    private SequenceLayout? _sequenceLayout;
    private SequenceSelection? _selectedSequence;
    private string? _sequenceMessageSource;
    public SequenceSelection? SelectedSequenceElement => _selectedSequence;
    public string? SequenceMessageSource => _sequenceMessageSource;
    public string? SequenceActionError { get; private set; }
    public event EventHandler? SequenceEditRequested;

    public SequenceLayout SequenceScene
    {
        get
        {
            if (_model?.Sequence is not { } sequence) throw new InvalidOperationException("Open a sequence diagram.");
            if (!ReferenceEquals(sequence, _sequenceCacheModel) || sequence.Options != _sequenceCacheOptions || _sequenceCacheTheme != _model.Theme)
            {
                _sequenceLayout = SequenceDiagramLayout.Create(_model, MeasureSequence);
                _sequenceCacheModel = sequence;
                _sequenceCacheOptions = sequence.Options;
                _sequenceCacheTheme = _model.Theme;
            }
            return _sequenceLayout!;
        }
    }

    public static SequenceTextSize MeasureSequence(string text, SequenceFont font)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(font.Family, FontStyle.Normal, (FontWeight)font.Weight), font.Size, Brushes.Black);
        return new(formatted.Width, formatted.Height);
    }

    public void SelectSequence(SequenceSelection? selection)
    {
        _sequenceMessageSource = null;
        SequenceActionError = null;
        if (!Equals(selection, _selectedSequence)) _selectedSequence = selection;
        _selection.Clear(); _selectedEdge = null; _selectedGroup = null;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    public void SelectSequenceId(string? id, bool reveal = true)
    {
        if (id is null) { SelectSequence(null); return; }
        var model = _model?.Sequence ?? throw new InvalidOperationException("Open a sequence diagram.");
        if (model.Participants.Any(p => p.Id == id)) SelectSequence(new(id, SequenceElementKind.Participant));
        else if (model.Boxes.Any(b => b.Id == id)) SelectSequence(new(id, SequenceElementKind.ParticipantBox));
        else if (SequenceDiagramModel.Walk(model.Statements).FirstOrDefault(s => s.Id == id) is { } statement)
            SelectSequence(new(id, SequenceKind(statement)));
        else if (SequenceDiagramModel.Walk(model.Statements).OfType<SequenceBlock>().Any(b => b.Branches.Any(branch => branch.Id == id)))
            SelectSequence(new(id, SequenceElementKind.Branch));
        else SelectSequence(null);
        if (reveal) RevealSequenceSelection();
    }

    public void BeginSequenceMessage(string participantId)
    {
        _ = _model?.Sequence?.Participant(participantId) ?? throw new InvalidOperationException("Select a sequence participant.");
        _sequenceMessageSource = participantId;
        SequenceActionError = null;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        Focus();
    }

    public void CancelSequenceMessage()
    {
        if (_sequenceMessageSource is null) return;
        _sequenceMessageSource = null;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    public void DeleteSequenceSelection()
    {
        if (_model?.Sequence is not { } sequence || _history is null || _selectedSequence is not { } selection) return;
        try
        {
            var command = sequence.Participants.Any(p => p.Id == selection.Id) ? SequenceEditing.RemoveParticipant(selection.Id)
                : sequence.Boxes.Any(b => b.Id == selection.Id) ? SequenceEditing.RemoveBox(selection.Id)
                : SequenceDiagramModel.Walk(sequence.Statements).Any(s => s.Id == selection.Id) ? SequenceEditing.Remove(selection.Id)
                : SequenceEditing.RemoveBranch(selection.Id);
            ApplyDiagramEdit(command);
            SelectSequence(null);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException)
        {
            SequenceActionError = error.Message;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void SequencePointer(Point world)
    {
        var selected = SequenceScene.Regions.Where(r => new Rect(r.Bounds.X, r.Bounds.Y, r.Bounds.Width, r.Bounds.Height).Contains(world))
            .OrderBy(r => r.Kind switch { SequenceElementKind.Message => 0, SequenceElementKind.Note => 1, SequenceElementKind.Activation => 2, _ => 3 })
            .ThenBy(r => r.Bounds.Width * r.Bounds.Height).FirstOrDefault();
        if (_sequenceMessageSource is { } source)
        {
            if (selected?.Kind == SequenceElementKind.Participant)
            {
                try
                {
                    var command = SequenceEditing.Insert(new SequenceMessage(SequenceEditing.NewId(), source, selected.OwnerId, ""));
                    ApplyDiagramEdit(command);
                    _sequenceMessageSource = null;
                    SelectSequenceId(command.ResultId);
                }
                catch (Exception error) when (error is ArgumentException or NotSupportedException)
                {
                    SequenceActionError = error.Message;
                    SelectionChanged?.Invoke(this, EventArgs.Empty);
                }
            }
            else if (selected is null) { CancelSequenceMessage(); SelectSequence(null); }
            return;
        }
        if (selected is null) SelectSequence(null);
        else SelectSequenceId(selected.OwnerId, reveal: false);
    }

    private void RevealSequenceSelection()
    {
        if (_selectedSequence is not { } selection || Bounds.Width < 2 || Bounds.Height < 2) return;
        var regions = SequenceScene.Regions.Where(r => r.OwnerId == selection.Id).ToArray();
        if (selection.Kind == SequenceElementKind.Participant)
            regions = regions.Where(r => r.Bounds.Width > 20).Take(1).ToArray();
        if (regions.Length == 0) return;
        var left = regions.Min(r => r.Bounds.X);
        var top = regions.Min(r => r.Bounds.Y);
        var right = regions.Max(r => r.Bounds.X + r.Bounds.Width);
        var bottom = regions.Max(r => r.Bounds.Y + r.Bounds.Height);
        var screen = new Rect(left * Zoom + OffsetX, top * Zoom + OffsetY, (right - left) * Zoom, (bottom - top) * Zoom);
        var visible = new Rect(Bounds.Size).Deflate(20);
        if (visible.Contains(screen.TopLeft) && visible.Contains(screen.BottomRight)) return;
        OffsetX = Bounds.Width / 2 - (left + right) / 2 * Zoom;
        OffsetY = Bounds.Height / 2 - (top + bottom) / 2 * Zoom;
        InvalidateVisual();
    }

    private static SequenceElementKind SequenceKind(SequenceStatement statement) => statement switch
    {
        SequenceMessage => SequenceElementKind.Message, SequenceNote => SequenceElementKind.Note,
        SequenceActivation => SequenceElementKind.Activation, SequenceBlock => SequenceElementKind.Fragment,
        _ => SequenceElementKind.Message,
    };

    private void DrawSequence(DrawingContext context)
    {
        var scene = SequenceScene;
        foreach (var figure in scene.Figures)
        {
            var fill = figure.Fill is { } f ? Brush(f) : null;
            IPen? pen = figure.Stroke is { } s ? new Pen(Brush(s), figure.StrokeWidth,
                figure.Dashed ? new DashStyle([4d / figure.StrokeWidth, 4d / figure.StrokeWidth], 0) : null) : null;
            var r = figure.Bounds;
            switch (figure.Kind)
            {
                case SequenceFigureKind.Rectangle:
                    context.DrawRectangle(fill, pen, new(r.X, r.Y, r.Width, r.Height));
                    break;
                case SequenceFigureKind.Ellipse:
                    context.DrawEllipse(fill, pen, new(r.X + r.Width / 2, r.Y + r.Height / 2), r.Width / 2, r.Height / 2);
                    break;
                case SequenceFigureKind.Polygon:
                    context.DrawGeometry(fill, pen, Poly(figure.Points.Select(p => new Point(p.X, p.Y)).ToArray()));
                    break;
                case SequenceFigureKind.Polyline:
                    if (pen is not null)
                        for (var i = 1; i < figure.Points.Count; i++)
                            context.DrawLine(pen, new(figure.Points[i - 1].X, figure.Points[i - 1].Y), new(figure.Points[i].X, figure.Points[i].Y));
                    break;
            }
        }
        foreach (var label in scene.Labels)
        {
            var lines = label.Text.Split('\n');
            for (var index = 0; index < lines.Length; index++)
            {
                var text = new FormattedText(lines[index], CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    new Typeface(label.Font.Family, FontStyle.Normal, (FontWeight)label.Font.Weight), label.Font.Size, Brush(label.Color));
                var x = label.Bounds.X + (label.Alignment switch { "center" => (label.Bounds.Width - text.Width) / 2, "right" => label.Bounds.Width - text.Width, _ => 0 });
                context.DrawText(text, new(x, label.Bounds.Y + index * label.Bounds.Height / lines.Length));
            }
        }
        if (_selectedSequence is { } selection)
            foreach (var region in scene.Regions.Where(r => r.OwnerId == selection.Id))
                context.DrawRectangle(null, _theme.NodeStrokeSelected, new Rect(region.Bounds.X, region.Bounds.Y, region.Bounds.Width, region.Bounds.Height).Inflate(2));
    }
}
