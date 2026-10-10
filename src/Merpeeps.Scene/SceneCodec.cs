using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Merpeeps.Scene;

/// <summary>
/// Typed scene contract over JSON-compatible metadata. Markdown hosts own
/// frontmatter extraction/emission; the same tree can be represented as YAML.
/// </summary>
public static class SceneCodec
{
    private static readonly string[] DocumentKeys = ["spec", "scenes"];
    private static readonly string[] SceneKeys = ["width", "height", "background", "elements"];
    private static readonly string[] ElementKeys = ["id", "kind", "contentRef", "bounds", "text", "diagramTheme"];
    private static readonly string[] BoundsKeys = ["x", "y", "width", "height"];
    private static readonly string[] TextKeys = ["fontSize", "bold", "color", "align"];

    public static SceneReadResult Read(string json)
    {
        try
        {
            using var parsed = JsonDocument.Parse(json);
            var duplicates = new List<SceneDiagnostic>();
            FindDuplicateKeys(parsed.RootElement, "$", duplicates);
            if (duplicates.Count > 0) throw new SceneFormatException(duplicates);
            if (JsonNode.Parse(json) is not JsonObject root)
                throw Error("$", "The scene envelope must be an object.");
            return Read(root);
        }
        catch (JsonException error)
        {
            throw Error("$", $"Invalid scene JSON: {error.Message}");
        }
    }

    public static SceneReadResult Read(JsonObject envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var raw = (JsonObject)envelope.DeepClone();
        var issues = new List<SceneDiagnostic>();
        var version = Number(raw, "spec", "$", issues);
        if (version != SceneDocument.CurrentSpec)
        {
            Add(issues, "unsupported-version", "$.spec",
                $"This reader supports scene spec {SceneDocument.CurrentSpec}; preserve this envelope without editing.");
            return new SceneReadResult(null, raw, issues);
        }

        var result = new SceneDocument { Extra = Extras(raw, DocumentKeys) };
        if (raw["scenes"] is not JsonObject scenes)
            Add(issues, "invalid-value", "$.scenes", "Expected scenes keyed by their stable IDs.");
        else
        {
            foreach (var (id, node) in scenes)
            {
                var path = $"$.scenes['{id}']";
                if (node is not JsonObject data)
                {
                    Add(issues, "invalid-value", path, "A scene must be an object.");
                    continue;
                }
                var scene = new SceneModel
                {
                    Id = id,
                    Width = Number(data, "width", path, issues),
                    Height = Number(data, "height", path, issues),
                    Background = OptionalString(data, "background", path, issues),
                    Extra = Extras(data, SceneKeys),
                };
                if (data["elements"] is not JsonArray elements)
                    Add(issues, "invalid-value", path + ".elements", "Expected an element array.");
                else
                {
                    for (var i = 0; i < elements.Count; i++)
                    {
                        if (elements[i] is not JsonObject element)
                        {
                            Add(issues, "invalid-value", $"{path}.elements[{i}]", "An element must be an object.");
                            continue;
                        }
                        scene.Elements.Add(ReadElement(element, $"{path}.elements[{i}]", issues));
                    }
                }
                result.Scenes.Add(scene);
            }
        }
        issues.AddRange(Validate(result));
        return new SceneReadResult(issues.Count == 0 ? result : null, raw, issues);
    }

    private static SceneElement ReadElement(JsonObject data, string path, List<SceneDiagnostic> issues)
    {
        var element = new SceneElement
        {
            Id = RequiredString(data, "id", path, issues),
            Kind = RequiredString(data, "kind", path, issues),
            ContentRef = RequiredString(data, "contentRef", path, issues),
            DiagramTheme = OptionalString(data, "diagramTheme", path, issues),
            Extra = Extras(data, ElementKeys),
        };
        if (data["bounds"] is not JsonObject bounds)
            Add(issues, "invalid-value", path + ".bounds", "Expected explicit bounds.");
        else
        {
            element.Bounds = new SceneBounds(
                Number(bounds, "x", path + ".bounds", issues),
                Number(bounds, "y", path + ".bounds", issues),
                Number(bounds, "width", path + ".bounds", issues),
                Number(bounds, "height", path + ".bounds", issues));
            // Unknown transform semantics cannot be discarded or applied as if
            // this were an ordinary axis-aligned rectangle.
            foreach (var key in bounds.Select(p => p.Key).Except(BoundsKeys, StringComparer.Ordinal))
                Add(issues, "unsupported-bounds", path + ".bounds." + key,
                    "Unrecognized bounds field; retain the envelope read-only.");
        }
        if (data["text"] is JsonObject text)
        {
            var bold = false;
            if (text.TryGetPropertyValue("bold", out var boldNode) &&
                (boldNode is not JsonValue value || !value.TryGetValue(out bold)))
                Add(issues, "invalid-value", path + ".text.bold", "Expected a boolean.");
            element.Text = new SceneTextStyle
            {
                FontSize = text.ContainsKey("fontSize") ? Number(text, "fontSize", path + ".text", issues) : 26,
                Bold = bold,
                Color = OptionalString(text, "color", path + ".text", issues),
                Align = OptionalString(text, "align", path + ".text", issues) ?? "left",
                Extra = Extras(text, TextKeys),
            };
        }
        else if (data["text"] is not null)
            Add(issues, "invalid-value", path + ".text", "Expected a text-style object or null.");
        return element;
    }

    /// <summary>Validate without mutating the scene or filling missing identities.</summary>
    public static IReadOnlyList<SceneDiagnostic> Validate(SceneDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var issues = new List<SceneDiagnostic>();
        if (document.Spec != SceneDocument.CurrentSpec)
            Add(issues, "unsupported-version", "$.spec", "Unsupported scene version.");
        CheckExtras(document.Extra, DocumentKeys, "$", issues);
        var sceneIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var scene in document.Scenes)
        {
            var elementIds = new HashSet<string>(StringComparer.Ordinal);
            var path = $"$.scenes['{scene.Id}']";
            CheckId(scene.Id, sceneIds, path, issues);
            Positive(scene.Width, path + ".width", issues);
            Positive(scene.Height, path + ".height", issues);
            CheckColor(scene.Background, path + ".background", issues);
            CheckExtras(scene.Extra, SceneKeys, path, issues);
            foreach (var element in scene.Elements)
            {
                var ep = path + $".elements['{element.Id}']";
                CheckId(element.Id, elementIds, ep + ".id", issues);
                if (string.IsNullOrWhiteSpace(element.ContentRef))
                    Add(issues, "invalid-reference", ep + ".contentRef", "A stable content reference is required.");
                if (element.Kind is not (SceneKinds.Text or SceneKinds.Diagram))
                    Add(issues, "unsupported-kind", ep + ".kind",
                        $"Unsupported element kind '{element.Kind}'; retain the envelope read-only.");
                ValidateBounds(element.Bounds, ep + ".bounds", issues);
                CheckExtras(element.Extra, ElementKeys, ep, issues);
                if (element.Text is { } text) ValidateText(text, ep + ".text", issues);
                if (element.Kind != SceneKinds.Diagram && element.DiagramTheme is not null)
                    Add(issues, "invalid-value", ep + ".diagramTheme", "Only a diagram can carry diagram-theme provenance.");
            }
        }
        return issues;
    }

    public static JsonObject Write(SceneDocument document)
    {
        var issues = Validate(document);
        if (issues.Count > 0) throw new SceneFormatException(issues);
        var root = (JsonObject)document.Extra.DeepClone();
        root["spec"] = document.Spec;
        var scenes = new JsonObject();
        foreach (var scene in document.Scenes)
        {
            var data = (JsonObject)scene.Extra.DeepClone();
            data["width"] = scene.Width;
            data["height"] = scene.Height;
            data["background"] = scene.Background;
            var elements = new JsonArray();
            foreach (var element in scene.Elements)
            {
                var e = (JsonObject)element.Extra.DeepClone();
                e["id"] = element.Id;
                e["kind"] = element.Kind;
                e["contentRef"] = element.ContentRef;
                e["bounds"] = new JsonObject
                {
                    ["x"] = element.Bounds.X, ["y"] = element.Bounds.Y,
                    ["width"] = element.Bounds.Width, ["height"] = element.Bounds.Height,
                };
                if (element.Text is { } text)
                {
                    var t = (JsonObject)text.Extra.DeepClone();
                    t["fontSize"] = text.FontSize;
                    t["bold"] = text.Bold;
                    t["color"] = text.Color;
                    t["align"] = text.Align;
                    e["text"] = t;
                }
                if (element.DiagramTheme is not null) e["diagramTheme"] = element.DiagramTheme;
                elements.Add((JsonNode)e);
            }
            data["elements"] = elements;
            scenes[scene.Id] = data;
        }
        root["scenes"] = scenes;
        return root;
    }

    public static string Serialize(SceneDocument document) => Write(document).ToJsonString();

    /// <summary>Preserve a read-only envelope, including unsupported semantics.</summary>
    public static JsonObject Preserve(SceneReadResult result) => (JsonObject)result.Raw.DeepClone();

    internal static void ValidateBounds(SceneBounds bounds, string path, List<SceneDiagnostic> issues)
    {
        if (!double.IsFinite(bounds.X)) Add(issues, "invalid-number", path + ".x", "Expected a finite coordinate.");
        if (!double.IsFinite(bounds.Y)) Add(issues, "invalid-number", path + ".y", "Expected a finite coordinate.");
        Positive(bounds.Width, path + ".width", issues);
        Positive(bounds.Height, path + ".height", issues);
        if (!double.IsFinite(bounds.X + bounds.Width) || !double.IsFinite(bounds.Y + bounds.Height))
            Add(issues, "invalid-number", path, "Bounds must have finite extents.");
    }

    internal static void ValidateText(SceneTextStyle text, string path, List<SceneDiagnostic> issues)
    {
        Positive(text.FontSize, path + ".fontSize", issues);
        CheckColor(text.Color, path + ".color", issues);
        if (text.Align is not ("left" or "center" or "right" or "justify"))
            Add(issues, "unsupported-alignment", path + ".align", "Unsupported text alignment.");
        CheckExtras(text.Extra, TextKeys, path, issues);
    }

    private static void CheckColor(string? color, string path, List<SceneDiagnostic> issues)
    {
        // v1 intentionally uses the hosts' existing six-digit RGB convention.
        if (color is not null && (color.Length != 7 || color[0] != '#' ||
                !uint.TryParse(color.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out _)))
            Add(issues, "invalid-color", path, "Expected null (inherit) or #RRGGBB.");
    }

    private static void Positive(double value, string path, List<SceneDiagnostic> issues)
    {
        if (!double.IsFinite(value) || value <= 0)
            Add(issues, "invalid-number", path, "Expected a positive finite number.");
    }

    private static void CheckId(string id, HashSet<string> ids, string path, List<SceneDiagnostic> issues)
    {
        if (string.IsNullOrWhiteSpace(id))
            Add(issues, "missing-id", path, "A nonempty stable ID is required.");
        else if (!ids.Add(id))
            Add(issues, "duplicate-id", path, $"Duplicate ID '{id}'.");
    }

    private static JsonObject Extras(JsonObject data, string[] known) => new(
        data.Where(kv => !known.Contains(kv.Key, StringComparer.Ordinal))
            .Select(kv => KeyValuePair.Create(kv.Key, kv.Value?.DeepClone())));

    private static void CheckExtras(JsonObject extra, string[] known, string path, List<SceneDiagnostic> issues)
    {
        foreach (var key in extra.Select(kv => kv.Key).Intersect(known, StringComparer.Ordinal))
            Add(issues, "field-collision", path + "." + key, "An extension cannot override a modeled field.");
    }

    private static string RequiredString(JsonObject obj, string key, string path, List<SceneDiagnostic> issues)
    {
        var value = OptionalString(obj, key, path, issues);
        if (string.IsNullOrWhiteSpace(value))
            Add(issues, "missing-value", path + "." + key, "A nonempty string is required.");
        return value ?? "";
    }

    private static string? OptionalString(JsonObject obj, string key, string path, List<SceneDiagnostic> issues)
    {
        if (obj[key] is null) return null;
        if (obj[key] is JsonValue value && value.TryGetValue<string>(out var text)) return text;
        Add(issues, "invalid-value", path + "." + key, "Expected a string or null.");
        return null;
    }

    private static double Number(JsonObject obj, string key, string path, List<SceneDiagnostic> issues)
    {
        if (obj[key] is JsonValue value && value.GetValueKind() == JsonValueKind.Number &&
            double.TryParse(value.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) &&
            double.IsFinite(number))
            return number;
        Add(issues, "invalid-number", path + "." + key, "Expected a finite JSON number, not a quoted string.");
        return 0;
    }

    private static void FindDuplicateKeys(JsonElement node, string path, List<SceneDiagnostic> issues)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in node.EnumerateObject())
            {
                if (!keys.Add(property.Name))
                    Add(issues, "duplicate-key", path + "." + property.Name, "Duplicate property cannot be resolved safely.");
                FindDuplicateKeys(property.Value, path + "." + property.Name, issues);
            }
        }
        else if (node.ValueKind == JsonValueKind.Array)
        {
            var i = 0;
            foreach (var item in node.EnumerateArray()) FindDuplicateKeys(item, $"{path}[{i++}]", issues);
        }
    }

    private static void Add(List<SceneDiagnostic> issues, string code, string path, string message) =>
        issues.Add(new SceneDiagnostic(code, path, message));

    private static SceneFormatException Error(string path, string message) =>
        new([new SceneDiagnostic("invalid-json", path, message)]);
}
