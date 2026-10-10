using System.Text.RegularExpressions;

namespace Merpeeps.Core.Model;

public enum EntityCardinality { ZeroOrOne, ExactlyOne, ZeroOrMore, OneOrMore }
[Flags]
public enum EntityKey { None = 0, Primary = 1, Foreign = 2, Unique = 4 }

public sealed record EntityAttribute
{
    public string Id { get; }
    public string Type { get; }
    public string Name { get; }
    public EntityKey Keys { get; }
    public string? Comment { get; }
    public EntityKey EffectiveKeys => Keys | (Name.StartsWith('*') ? EntityKey.Primary : EntityKey.None);

    public EntityAttribute(string id, string type, string name, EntityKey keys = EntityKey.None, string? comment = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (!Regex.IsMatch(type, @"^[\p{L}_][\p{L}\p{N}_\-\[\]()]*(?:\?)?$"))
            throw new ArgumentException("Attribute type must be a name, optionally ending in '?'.", nameof(type));
        if (!Regex.IsMatch(name, @"^\*?[\p{L}_][\p{L}\p{N}_\-\[\]()]*(?:\?)?$"))
            throw new ArgumentException("Attribute name must be an identifier.", nameof(name));
        if ((keys & ~(EntityKey.Primary | EntityKey.Foreign | EntityKey.Unique)) != 0)
            throw new ArgumentException("Unknown attribute key.", nameof(keys));
        if (comment?.IndexOfAny(['"', '\r', '\n']) >= 0)
            throw new ArgumentException("Attribute comments cannot contain double quotes or line breaks.", nameof(comment));
        Id = id; Type = type; Name = name; Keys = keys; Comment = comment;
    }

    public string KeyText => string.Join(", ", new[]
    {
        Keys.HasFlag(EntityKey.Primary) || Name.StartsWith('*') ? "PK" : null,
        Keys.HasFlag(EntityKey.Foreign) ? "FK" : null,
        Keys.HasFlag(EntityKey.Unique) ? "UK" : null,
    }.Where(k => k is not null));
    public EntityAttribute WithKeys(EntityKey keys) => new(Id, Type, Name.TrimStart('*'), keys, Comment);
    public string Declaration => Type + " " + Name +
        (Keys == EntityKey.None ? "" : " " + string.Join(", ", new[]
        {
            Keys.HasFlag(EntityKey.Primary) ? "PK" : null,
            Keys.HasFlag(EntityKey.Foreign) ? "FK" : null,
            Keys.HasFlag(EntityKey.Unique) ? "UK" : null,
        }.Where(k => k is not null))) +
        (Comment is null ? "" : " \"" + Comment + "\"");
}

/// <summary>Entity content only. DiagramNode remains the single identity/alias/geometry owner.</summary>
public sealed class EntityNodeDefinition
{
    public List<EntityAttribute> Attributes { get; } = new();
    public List<string> StyleClasses { get; } = new();
    public Dictionary<string, string> InlineStyle { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> ResolvedStyle { get; } = new(StringComparer.Ordinal);

    public EntityNodeDefinition Clone()
    {
        var copy = new EntityNodeDefinition();
        copy.Attributes.AddRange(Attributes);
        copy.StyleClasses.AddRange(StyleClasses);
        foreach (var entry in InlineStyle) copy.InlineStyle.Add(entry.Key, entry.Value);
        foreach (var entry in ResolvedStyle) copy.ResolvedStyle.Add(entry.Key, entry.Value);
        return copy;
    }
}

public sealed record EntityRelationship(
    EntityCardinality FromCardinality = EntityCardinality.ExactlyOne,
    EntityCardinality ToCardinality = EntityCardinality.ZeroOrMore,
    bool Identifying = true);

public sealed class EntityDiagramOptions
{
    public Dictionary<string, Dictionary<string, string>> StyleDefinitions { get; } = new(StringComparer.Ordinal);
    internal Dictionary<string, DiagramRect> EmptyGroupLayouts { get; } = new(StringComparer.Ordinal);
}

public sealed record EntityAttributeRow(string Id, string Type, string Name, EntityKey KeyFlags, string Keys, string? Comment);
public sealed record EntityTextRun(string Text, bool Bold = false, bool Italic = false);
public sealed record EntityCardContent(string Name, IReadOnlyList<EntityTextRun> NameRuns, IReadOnlyList<EntityAttributeRow> Attributes);

public static class EntityDiagramText
{
    public static EntityCardContent Content(DiagramNode node)
    {
        var entity = node.Entity ?? throw new ArgumentException("The node is not an entity.");
        var runs = NameRuns(node.Display);
        return new(string.Concat(runs.Select(r => r.Text)), runs, entity.Attributes.Select(a => new EntityAttributeRow(
            a.Id, a.Type, a.Name.TrimStart('*'), a.EffectiveKeys, a.KeyText, a.Comment)).ToArray());
    }

    public static IReadOnlyList<EntityTextRun> NameRuns(string source)
    {
        var runs = new List<EntityTextRun>();
        Parse(source, false, false);
        return runs.AsReadOnly();

        void Parse(string text, bool bold, bool italic)
        {
            var plain = new System.Text.StringBuilder();
            void Flush()
            {
                if (plain.Length == 0) return;
                runs.Add(new(System.Net.WebUtility.HtmlDecode(plain.ToString()), bold, italic));
                plain.Clear();
            }
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] == '\\' && i + 1 < text.Length) { plain.Append(text[++i]); continue; }
                var marker = text[i];
                if (marker is not ('*' or '_') ||
                    marker == '_' && i > 0 && char.IsLetterOrDigit(text[i - 1]))
                {
                    plain.Append(marker);
                    continue;
                }
                var count = i + 1 < text.Length && text[i + 1] == marker ? 2 : 1;
                var delimiter = new string(marker, count);
                var end = text.IndexOf(delimiter, i + count, StringComparison.Ordinal);
                if (end <= i + count || char.IsWhiteSpace(text[i + count]) ||
                    marker == '_' && end + count < text.Length && char.IsLetterOrDigit(text[end + count]))
                {
                    plain.Append(marker);
                    continue;
                }
                Flush();
                Parse(text[(i + count)..end], bold || count == 2, italic || count == 1);
                i = end + count - 1;
            }
            Flush();
        }
    }
}
