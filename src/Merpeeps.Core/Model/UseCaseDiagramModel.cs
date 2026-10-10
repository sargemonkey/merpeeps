using System.Text.Json;
using System.Text.Json.Nodes;

namespace Merpeeps.Core.Model;

public enum UseCaseNodeKind { Actor, UseCase, JsonTable, Note }
public enum UseCaseActorKind { Normal, Hollow, Awesome, Icon }
public enum UseCaseShape { Ellipse, Rectangle }
public enum UseCaseRelationshipKind { Association, Include, Extend, Generalization }
public enum UseCaseEnd { None, Arrow, Circle, Cross, Triangle }
public sealed record UseCaseText(string Text, bool Markdown = false);
public sealed record UseCaseNode(string Id, string SourceId, UseCaseNodeKind Kind, UseCaseText Label)
{
    public bool Declared { get; init; } = true;
    public UseCaseActorKind ActorKind { get; init; }
    public string? Icon { get; init; }
    public bool Business { get; init; }
    public UseCaseShape Shape { get; init; }
    public string? BoundaryId { get; init; }
    public string? NoteTargetId { get; init; }
    public string? Stereotype { get; init; }
    public JsonObject? Data { get; init; }
    public IReadOnlyList<string> Classes { get; init; } = [];
    public JsonObject Style { get; init; } = new();
    public UseCaseNode DeepCopy() => this with { Data = Data?.DeepClone().AsObject(), Classes = Classes.ToArray(), Style = (JsonObject)Style.DeepClone() };
}
public sealed record UseCaseBoundary(string Id, string SourceId, UseCaseText Title, bool Package = false)
{
    public IReadOnlyList<string> Classes { get; init; } = [];
    public JsonObject Style { get; init; } = new();
    public UseCaseBoundary DeepCopy() => this with { Classes = Classes.ToArray(), Style = (JsonObject)Style.DeepClone() };
}
public sealed record UseCaseRelationship(string Id, string From, string To)
{
    public string? SourceId { get; init; }
    public UseCaseRelationshipKind Kind { get; init; }
    public UseCaseEnd FromEnd { get; init; }
    public UseCaseEnd ToEnd { get; init; } = UseCaseEnd.Arrow;
    public UseCaseText Label { get; init; } = new("");
    public int MinimumLength { get; init; } = 1;
    public bool Animated { get; init; }
    public string? AnimationSpeed { get; init; }
    public IReadOnlyList<string> Classes { get; init; } = [];
    public JsonObject Style { get; init; } = new();
    public UseCaseRelationship DeepCopy() => this with { Classes = Classes.ToArray(), Style = (JsonObject)Style.DeepClone() };
}
public sealed class UseCaseDiagramDocument
{
    public Direction Direction { get; set; } = Direction.TB;
    public List<UseCaseNode> Nodes { get; } = new();
    public List<UseCaseBoundary> Boundaries { get; } = new();
    public List<UseCaseRelationship> Relationships { get; } = new();
    public Dictionary<string, JsonObject> Classes { get; } = new(StringComparer.Ordinal);
    public string AccessibilityTitle { get; set; } = "";
    public string AccessibilityDescription { get; set; } = "";
    public JsonObject Configuration { get; set; } = new();
    public JsonObject ThemeVariables { get; set; } = new();
    public JsonObject GlobalConfiguration { get; set; } = new();
    public List<string> Comments { get; } = new();
    public UseCaseDiagramDocument Clone()
    {
        var copy = new UseCaseDiagramDocument
        {
            Direction = Direction, AccessibilityTitle = AccessibilityTitle, AccessibilityDescription = AccessibilityDescription,
            Configuration = (JsonObject)Configuration.DeepClone(), ThemeVariables = (JsonObject)ThemeVariables.DeepClone(),
            GlobalConfiguration = (JsonObject)GlobalConfiguration.DeepClone(),
        };
        copy.Nodes.AddRange(Nodes.Select(n => n.DeepCopy())); copy.Boundaries.AddRange(Boundaries.Select(b => b.DeepCopy()));
        copy.Relationships.AddRange(Relationships.Select(r => r.DeepCopy())); copy.Comments.AddRange(Comments);
        foreach (var pair in Classes) copy.Classes[pair.Key] = (JsonObject)pair.Value.DeepClone();
        return copy;
    }
}
public sealed record UseCaseJsonRow(string Path, string Value);
public static class UseCaseJson
{
    public static JsonObject Parse(string text)
    {
        using var doc = JsonDocument.Parse(text);
        return Read(doc.RootElement) as JsonObject ?? throw new FormatException("Use-case JSON tables require an object root.");
        static JsonNode? Read(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                var result = new JsonObject();
                foreach (var property in element.EnumerateObject()) result[property.Name] = Read(property.Value);
                return result;
            }
            if (element.ValueKind == JsonValueKind.Array)
            {
                var array = new JsonArray(); foreach (var item in element.EnumerateArray()) array.Add(Read(item)); return array;
            }
            return JsonNode.Parse(element.GetRawText());
        }
    }
    public static IReadOnlyList<UseCaseJsonRow> Rows(JsonObject data)
    {
        var result = new List<UseCaseJsonRow>(); Walk(data, "");
        return result;
        void Walk(JsonNode? value, string path)
        {
            if (value is JsonObject { Count: > 0 } map) foreach (var pair in map) Walk(pair.Value, path.Length == 0 ? pair.Key : path + "." + pair.Key);
            else if (value is JsonArray { Count: > 0 } array) for (var i = 0; i < array.Count; i++) Walk(array[i], path + "[" + i + "]");
            else result.Add(new(path, value is JsonValue scalar && scalar.TryGetValue<string>(out var text) ? text : value?.ToJsonString() ?? "null"));
        }
    }
}
