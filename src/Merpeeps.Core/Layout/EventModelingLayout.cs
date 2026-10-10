using Merpeeps.Core.Model;

namespace Merpeeps.Core.Layout;

public sealed record EventModelingLayoutResult(DrawingScene Scene, IReadOnlyDictionary<string, DiagramRect> Frames,
    IReadOnlyDictionary<string, DiagramRect> Lanes, IReadOnlyList<EventRelation> Relations);
public static class EventModelingLayout
{
    public static EventModelingLayoutResult Create(DiagramModel model, DrawingTextMeasure? measure = null, string? themeOverride = null)
    {
        var d = model.EventModeling ?? throw new ArgumentException("Event Modeling content is missing."); EventModelingSemantics.Check(d);
        var relations = EventModelingSemantics.Relations(d); var b = new DataDiagramLayouts.Builder(measure, d.ThemeVariables, d.GlobalConfiguration, themeOverride ?? model.Theme);
        var laneNames = d.Frames.Select(EventModelingSemantics.Lane).Distinct().ToArray();
        var sizes = new Dictionary<string, DrawingTextSize>(); var content = new Dictionary<string, string>();
        foreach (var frame in d.Frames)
        {
            var data = frame.InlineData ?? d.DataBlocks.FirstOrDefault(x => x.Id == frame.DataBlockId)?.Data;
            var text = frame.EntityName + (data is null ? "" : "\n" + data.Source.Trim());
            content[frame.Id] = text;
            var size = b.MeasureWrapped(text, 14, 220); sizes[frame.Id] = new(Math.Max(160, size.Width + 30), Math.Max(70, size.Height + 40));
        }
        var laneHeights = laneNames.ToDictionary(l => l, l => d.Frames.Where(f => EventModelingSemantics.Lane(f) == l).Select(f => sizes[f.Id].Height + 40).DefaultIfEmpty(110).Max());
        var laneY = new Dictionary<string, double>(); var y = 50d;
        foreach (var lane in laneNames) { laneY[lane] = y; y += laneHeights[lane]; }
        var frames = new Dictionary<string, DiagramRect>(); var x = 160d;
        foreach (var frame in d.Frames)
        {
            var size = sizes[frame.Id]; var lane = EventModelingSemantics.Lane(frame);
            frames[frame.Id] = new(x, laneY[lane] + 20, size.Width, size.Height); x += size.Width + 40;
        }
        var lanes = new Dictionary<string, DiagramRect>();
        foreach (var lane in laneNames)
        {
            var r = new DiagramRect(0, laneY[lane], Math.Max(400, x), laneHeights[lane]); lanes[lane] = r;
            b.Box("lane-" + lane, "EventLane", lane, r, b.Tint(b.Scale(Array.IndexOf(laneNames, lane))) with { Alpha = 70 }, b.Ink with { Alpha = 70 });
            b.Text("lane-label-" + lane, "EventLane", lane, lane, 14, 8, r.Y + 15, 140, true);
        }
        foreach (var (relation, i) in relations.Select((relation, i) => (relation, i)))
        {
            var a = frames[relation.From]; var z = frames[relation.To]; var start = new DiagramPoint(a.X + a.Width / 2, a.Y + a.Height / 2);
            var end = new DiagramPoint(z.X + z.Width / 2, z.Y + z.Height / 2);
            b.Line("relation-" + i, "EventRelation", relation.To, [start, new(end.X, start.Y), end], b.Ink, 1.5);
        }
        foreach (var frame in d.Frames)
        {
            var r = frames[frame.Id]; var color = frame.Kind switch
            {
                EventEntityKind.Command => new ClassColor(150, 194, 245), EventEntityKind.Event => new(245, 187, 111),
                EventEntityKind.ReadModel => new(166, 208, 156), EventEntityKind.Processor => new(216, 172, 213), _ => new(249, 231, 157),
            };
            b.Box(frame.Id, "EventFrame", frame.Id, r, color, b.Ink, frame.Reset ? 2.5 : 1);
            b.Text(frame.Id + "-number", "EventFrame", frame.Id, frame.Number + (frame.Reset ? " · reset" : ""), 11, r.X + 8, r.Y + 4, r.Width - 16);
            b.Text(frame.Id + "-content", "EventFrame", frame.Id, content[frame.Id], 14, r.X + 15, r.Y + 24, r.Width - 30);
            b.Hit("EventFrame", frame.Id, r, EventModelingSemantics.Lane(frame));
        }
        var scene = b.Finish(); var offset = b.OriginOffset;
        return new(scene, frames.ToDictionary(p => p.Key, p => Shift(p.Value)), lanes.ToDictionary(p => p.Key, p => Shift(p.Value)), relations);
        DiagramRect Shift(DiagramRect r) => r with { X = r.X + offset.X, Y = r.Y + offset.Y };
    }
}
