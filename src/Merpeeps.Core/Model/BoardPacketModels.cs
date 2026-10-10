using System.Text.Json.Nodes;

namespace Merpeeps.Core.Model;

public sealed record PacketField(string Id, string Label, int Bits, int? ExplicitStart = null);
public sealed record ResolvedPacketField(PacketField Field, int Start, int End);
public sealed class PacketDocument
{
    public string Directive { get; set; } = "packet";
    public string Title { get; set; } = "";
    public bool TitleInFrontmatter { get; set; }
    public List<PacketField> Fields { get; } = new();
    public JsonObject Configuration { get; set; } = new();
    public JsonObject ThemeVariables { get; set; } = new();
    public JsonObject GlobalConfiguration { get; set; } = new();
    public List<string> Comments { get; } = new();
    public IReadOnlyList<ResolvedPacketField> Resolve()
    {
        var values = new List<ResolvedPacketField>();
        var next = 0;
        foreach (var field in Fields)
        {
            var start = field.ExplicitStart ?? next;
            if (field.Bits <= 0 || start != next) throw new ArgumentException("Packet fields must be positive-width, contiguous, and start at bit zero.");
            var end = checked(start + field.Bits - 1);
            values.Add(new(field, start, end)); next = checked(end + 1);
        }
        return values;
    }
    public PacketDocument Clone()
    {
        var copy = new PacketDocument { Directive = Directive, Title = Title, TitleInFrontmatter = TitleInFrontmatter,
            Configuration = (JsonObject)Configuration.DeepClone(), ThemeVariables = (JsonObject)ThemeVariables.DeepClone(), GlobalConfiguration = (JsonObject)GlobalConfiguration.DeepClone() };
        copy.Fields.AddRange(Fields); copy.Comments.AddRange(Comments); return copy;
    }
}
public sealed record KanbanCard(string Id, string Label, string? SourceId = null)
{
    public JsonObject Metadata { get; init; } = new();
    public string Display => Metadata["label"]?.GetValue<string>() ?? Label;
    public KanbanCard DeepCopy() => this with { Metadata = (JsonObject)Metadata.DeepClone() };
}
public sealed record KanbanColumn(string Id, string Label, IReadOnlyList<KanbanCard> Cards, string? SourceId = null);
public sealed class KanbanDocument
{
    public List<KanbanColumn> Columns { get; } = new();
    public JsonObject Configuration { get; set; } = new();
    public JsonObject ThemeVariables { get; set; } = new();
    public JsonObject GlobalConfiguration { get; set; } = new();
    public List<string> Comments { get; } = new();
    public KanbanDocument Clone()
    {
        var copy = new KanbanDocument { Configuration = (JsonObject)Configuration.DeepClone(), ThemeVariables = (JsonObject)ThemeVariables.DeepClone(), GlobalConfiguration = (JsonObject)GlobalConfiguration.DeepClone() };
        copy.Columns.AddRange(Columns.Select(c => c with { Cards = c.Cards.Select(t => t.DeepCopy()).ToArray() }));
        copy.Comments.AddRange(Comments); return copy;
    }
}
