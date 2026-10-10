using System.Security.Cryptography;
using System.Text;
using Merpeeps.Core.Model;

namespace Merpeeps.Core.Parsing;

/// <summary>Identity-only metadata; authored class content stays in the Mermaid body.</summary>
internal sealed class ClassIdentity
{
    private readonly Dictionary<string, object?> _stored;
    private readonly Dictionary<string, int> _occurrences = new(StringComparer.Ordinal);

    public ClassIdentity(string source)
    {
        var metadata = MerpeepsDoc.Parse(source) as Dictionary<string, object?>;
        var identity = metadata?.GetValueOrDefault("identity") as Dictionary<string, object?>;
        _stored = identity?.GetValueOrDefault("classDiagram") as Dictionary<string, object?> ?? new();
        if (_stored.TryGetValue("spec", out var spec) && spec is not (1L or 1))
            throw new FormatException("Unsupported class identity metadata version.");
    }

    public string MemberId(string owner, string declaration) => Next("members", owner, declaration);
    public string NoteId(string? target, string text) => Next("notes", "", NoteSignature(target, text));
    public string InterfaceId(string owner, string name, bool from) =>
        Next("interfaces", "", InterfaceSignature(owner, name, from));

    private string Next(string kind, string owner, string text)
    {
        var fingerprint = Hash(text);
        var key = kind + "\0" + owner + "\0" + fingerprint;
        var occurrence = _occurrences.GetValueOrDefault(key);
        _occurrences[key] = occurrence + 1;
        var entries = Entries(_stored, kind, owner)
            .Where(e => e.GetValueOrDefault("fingerprint") as string == fingerprint).ToArray();
        if (occurrence < entries.Length && entries[occurrence].GetValueOrDefault("id") is string id &&
            !string.IsNullOrWhiteSpace(id) && id.IndexOfAny(['\r', '\n']) < 0)
            return id;
        return "__class_" + kind + "_" + Hash(owner + "\0" + text) + "_" + occurrence;
    }

    public static Dictionary<string, object?> Write(string source, DiagramModel model)
    {
        var prior = new ClassIdentity(source)._stored;
        var members = new Dictionary<string, object?>();
        var notes = new List<object?>();
        var interfaces = new List<object?>();
        foreach (var node in model.Nodes)
        {
            if (node.Class is not { } definition) continue;
            if (definition.Kind == ClassNodeKind.Class)
            {
                members[node.Id] = definition.Members.Select(m => (object?)Entry(
                    Entries(prior, "members", node.Id), m.Id, m.Declaration)).ToList();
            }
            else if (definition.Kind == ClassNodeKind.Note)
            {
                notes.Add(Entry(Entries(prior, "notes", ""), node.Id,
                    NoteSignature(definition.NoteFor, node.Display)));
            }
        }
        foreach (var edge in model.Edges)
        {
            foreach (var from in new[] { true, false })
            {
                var node = model.Node(from ? edge.From : edge.To);
                if (node?.Class is not { Kind: ClassNodeKind.Lollipop } definition) continue;
                interfaces.Add(Entry(Entries(prior, "interfaces", ""), node.Id,
                    InterfaceSignature(from ? edge.To : edge.From, definition.ReferenceName ?? node.Display, from)));
            }
        }
        return new Dictionary<string, object?>
        {
            ["spec"] = 1,
            ["members"] = members,
            ["notes"] = notes,
            ["interfaces"] = interfaces,
        };
    }

    private static Dictionary<string, object?> Entry(
        IEnumerable<Dictionary<string, object?>> prior, string id, string text)
    {
        var old = prior.FirstOrDefault(e => e.GetValueOrDefault("id") as string == id);
        var result = old is null ? new Dictionary<string, object?>() : new(old);
        result["id"] = id;
        result["fingerprint"] = Hash(text);
        return result;
    }

    private static IEnumerable<Dictionary<string, object?>> Entries(
        Dictionary<string, object?> stored, string kind, string owner)
    {
        object? entries = stored.GetValueOrDefault(kind);
        if (kind == "members") entries = (entries as Dictionary<string, object?>)?.GetValueOrDefault(owner);
        if (entries is null) return Array.Empty<Dictionary<string, object?>>();
        if (entries is not List<object?> list || list.Any(e => e is not Dictionary<string, object?>))
            throw new FormatException("Invalid class identity entries; the source must be preserved.");
        return list.Cast<Dictionary<string, object?>>();
    }

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    private static string NoteSignature(string? target, string text) => (target ?? "") + "\0" + text;
    private static string InterfaceSignature(string owner, string name, bool from) => owner + "\0" + name + "\0" + from;
}
