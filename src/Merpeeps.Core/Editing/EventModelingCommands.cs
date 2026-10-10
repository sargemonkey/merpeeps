using Merpeeps.Core.Layout;
using Merpeeps.Core.Model;
using Merpeeps.Core.Parsing;

namespace Merpeeps.Core.Editing;

public sealed class EventModelingEditCommand : IEditCommand
{
    private readonly Action<EventModelingDocument> _edit; private EventModelingDocument? _before;
    public string Description { get; }
    public string? ResultId { get; }
    internal EventModelingEditCommand(string description, string? id, Action<EventModelingDocument> edit) => (Description, ResultId, _edit) = (description, id, edit);
    public void Do(DiagramModel model)
    {
        var before = model.EventModeling ?? throw new ArgumentException("Choose Event Modeling.");
        var next = before.Clone(); _edit(next); EventModelingSemantics.Check(next); _ = EventModelingAdapter.Write(next);
        _ = EventModelingLayout.Create(new DiagramModel { Kind = "eventmodeling", EventModeling = next });
        _before = before; model.EventModeling = next;
    }
    public void Undo(DiagramModel model) => model.EventModeling = _before ?? throw new InvalidOperationException("Edit is not applied.");
}
public static class EventModelingEditing
{
    public static EventModelingEditCommand AddFrame(EventFrame frame, int? index = null)
    { var copy = frame.DeepCopy(); return new("Add timeframe occurrence", frame.Id, d => d.Frames.Insert(index ?? d.Frames.Count, copy.DeepCopy())); }
    public static EventModelingEditCommand UpdateFrame(EventFrame frame)
    { var copy = frame.DeepCopy(); return new("Edit timeframe occurrence", frame.Id, d => d.Frames[Index(d.Frames, f => f.Id == frame.Id)] = copy.DeepCopy()); }
    public static EventModelingEditCommand MoveFrame(string id, int index) => new("Reorder timeframe occurrence", id, d =>
    { var old = Index(d.Frames, f => f.Id == id); var frame = d.Frames[old]; d.Frames.RemoveAt(old); d.Frames.Insert(index, frame); });
    public static EventModelingEditCommand RemoveFrame(string id) => new("Remove timeframe and its references", null, d =>
    { d.Frames.RemoveAt(Index(d.Frames, f => f.Id == id)); for (var i = 0; i < d.Frames.Count; i++) d.Frames[i] = d.Frames[i] with { References = d.Frames[i].References.Where(r => r != id).ToArray() }; });
    public static EventModelingEditCommand SetDataBlock(EventDataBlock block) => new("Edit data example block", block.Id, d =>
    { var index = d.DataBlocks.FindIndex(b => b.Id == block.Id); if (index < 0) d.DataBlocks.Add(block); else d.DataBlocks[index] = block; });
    public static EventModelingEditCommand RemoveDataBlock(string id) => new("Remove unreferenced data block", null, d =>
    {
        if (d.Frames.Any(f => f.DataBlockId == id)) throw new ArgumentException("Move or detach data references before deleting this block.");
        d.DataBlocks.RemoveAt(Index(d.DataBlocks, b => b.Id == id));
    });
    private static int Index<T>(List<T> values, Predicate<T> predicate) => values.FindIndex(predicate) is var index && index >= 0 ? index : throw new ArgumentException("Event Modeling element no longer exists.");
}
