using System.Text.RegularExpressions;
using Merpeeps.Core.Parsing;

namespace Merpeeps.Core.Model;

public static class EventModelingSemantics
{
    public static string Lane(EventFrame frame) => (frame.Namespace is { } ns ? ns + " / " : "") + (frame.Kind switch
    {
        EventEntityKind.UserInterface or EventEntityKind.Processor => "UI / Automation",
        EventEntityKind.Command or EventEntityKind.ReadModel => "Command / Read model", _ => "Events",
    });
    public static IReadOnlyList<EventRelation> Relations(EventModelingDocument d)
    {
        Check(d); var relations = new List<EventRelation>();
        for (var i = 0; i < d.Frames.Count; i++)
        {
            var frame = d.Frames[i]; if (frame.Reset) continue;
            if (frame.References.Count > 0) { relations.AddRange(frame.References.Select(id => new EventRelation(id, frame.Id, false))); continue; }
            for (var previous = i - 1; previous >= 0; previous--)
                if (Lane(d.Frames[previous]) != Lane(frame)) { relations.Add(new(d.Frames[previous].Id, frame.Id, true)); break; }
        }
        return relations;
    }
    public static void Check(EventModelingDocument d)
    {
        var ids = new HashSet<string>(); var numbers = new HashSet<string>(); var names = new HashSet<string>();
        foreach (var block in d.DataBlocks)
        {
            if (string.IsNullOrWhiteSpace(block.Id) || !ids.Add(block.Id) || !Regex.IsMatch(block.Name, @"^[A-Za-z0-9_]+$") || !names.Add(block.Name))
                throw new ArgumentException("Event data block identities and names must be unique.");
            Data(block.Data);
        }
        foreach (var frame in d.Frames)
        {
            if (string.IsNullOrWhiteSpace(frame.Id) || !ids.Add(frame.Id) || !Regex.IsMatch(frame.Number, @"^\d+$") || !numbers.Add(EventModelingAdapter.NormalizeNumber(frame.Number)))
                throw new ArgumentException("Timeframe numbers must be unique; source order, not numeric order, defines the timeline.");
            if (!Enum.IsDefined(frame.Kind) || !Regex.IsMatch(frame.EntityName, @"^[A-Za-z0-9_]+(?:\.[A-Za-z0-9_]+)?$")) throw new ArgumentException("Invalid Event Modeling entity kind or namespace.");
            if (frame.InlineData is not null && frame.DataBlockId is not null) throw new ArgumentException("Choose inline data or a named data block, not both.");
            if (frame.DataBlockId is not null && d.DataBlocks.All(b => b.Id != frame.DataBlockId)) throw new ArgumentException("Event data block reference does not exist.");
            if (frame.InlineData is { } data) Data(data);
            foreach (var reference in frame.References) if (d.Frames.All(f => f.Id != reference)) throw new ArgumentException("Timeframe reference does not exist.");
        }
        static void Data(EventDataValue value)
        {
            if (!Enum.IsDefined(value.Kind)) throw new ArgumentException("Unknown Event Modeling data kind.");
            // Payloads remain source text for every kind; no script, HTML, URL or Figma payload is executed.
        }
    }
}
