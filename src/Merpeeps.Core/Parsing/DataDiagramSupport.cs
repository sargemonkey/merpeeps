using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Merpeeps.Core.Model;

namespace Merpeeps.Core.Parsing;

internal sealed class DataIdentity
{
    private readonly List<Dictionary<string, object?>> _prior;
    private readonly Dictionary<string, int> _seen = new();
    public DataIdentity(string source, string family)
    {
        var root = MerpeepsDoc.Parse(source) as Dictionary<string, object?>;
        var identity = (root?.GetValueOrDefault("identity") as Dictionary<string, object?>)?.GetValueOrDefault(family) as Dictionary<string, object?>;
        if (identity?.GetValueOrDefault("spec") is { } spec && spec is not (1 or 1L)) throw new FormatException("Unsupported diagram identity version.");
        _prior = (identity?.GetValueOrDefault("entries") as List<object?>)?.Select(e => e as Dictionary<string, object?>
            ?? throw new FormatException("Invalid identity record.")).ToList() ?? new();
    }
    public string Next(string signature)
    {
        var hash = Hash(signature); var occurrence = _seen.GetValueOrDefault(hash); _seen[hash] = occurrence + 1;
        var entries = _prior.Where(p => p.GetValueOrDefault("fingerprint") as string == hash).ToArray();
        return occurrence < entries.Length && entries[occurrence].GetValueOrDefault("id") is string id ? id : "__data_" + hash[..24] + "_" + occurrence;
    }
    public object Entry(string id, string signature)
    {
        var old = _prior.FirstOrDefault(p => p.GetValueOrDefault("id") as string == id);
        var entry = old is null ? new Dictionary<string, object?>() : new(old);
        entry["id"] = id; entry["fingerprint"] = Hash(signature);
        return entry;
    }
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}

internal static class DataDiagramSupport
{
    public static IReadOnlyList<DataDiagramDiagnostic> ValidateOptions(JsonObject configuration, JsonObject theme, JsonObject global,
        string family, IEnumerable<string> numbers, IEnumerable<string> booleans)
    {
        var issues = new List<DataDiagramDiagnostic>();
        foreach (var key in numbers)
            if (configuration[key] is { } value)
                try
                {
                    if (ChartConfiguration.ParseNumber(ChartConfiguration.Raw(value)) < 0) throw new FormatException("must be nonnegative");
                }
                catch (Exception error) when (error is FormatException or ArgumentException)
                { issues.Add(new("option", "", key + ": " + error.Message, true)); }
        foreach (var key in booleans)
            if (configuration[key] is { } value && (value is not JsonValue scalar || !scalar.TryGetValue<bool>(out _)))
                issues.Add(new("option", "", key + " must be true or false.", true));
        if (global["fontFamily"] is { } font && (font is not JsonValue text || !text.TryGetValue<string>(out _)))
            issues.Add(new("font-family", "", "fontFamily must be text.", true));
        if (theme[family] is JsonObject familyTheme)
            foreach (var property in familyTheme)
            {
                if (property.Value is { } value && property.Key.EndsWith("FontSize", StringComparison.Ordinal))
                    try
                    {
                        if (ChartConfiguration.ParseNumber(ChartConfiguration.Raw(value)) <= 0) throw new FormatException("must be positive");
                    }
                    catch (Exception error) when (error is FormatException or ArgumentException)
                    { issues.Add(new("font-size", "", property.Key + ": " + error.Message, true)); }
                else if (property.Value is { } paint && (property.Key.EndsWith("Color", StringComparison.Ordinal) || property.Key is "highlightBg" or "highlightStroke") &&
                    !SequenceColors.TryParse(ChartConfiguration.Raw(paint), out _))
                    issues.Add(new("color", "", property.Key + " is not a supported native color.", true));
            }
        foreach (var property in theme.Where(p => p.Key.StartsWith("cScale", StringComparison.Ordinal)))
            if (property.Value is { } paint && !SequenceColors.TryParse(ChartConfiguration.Raw(paint), out _))
                issues.Add(new("color", "", property.Key + " is not a supported native color.", true));
        foreach (var key in new[] { "filenameIcons", "extensionIcons" })
            if (configuration[key] is { } mapping && (mapping is not JsonObject map ||
                map.Any(p => p.Value is not JsonValue scalar || !scalar.TryGetValue<string>(out _))))
                issues.Add(new("icon-map", "", key + " must map names to icon strings.", true));
        return issues;
    }
    public static JsonObject Object(object? value) => value is null ? new() :
        JsonNode.Parse(MerpeepsDoc.ToJson(value)) as JsonObject ?? throw new FormatException("Diagram options must be an object.");
    public static (JsonObject Configuration, JsonObject Theme, JsonObject Global) Configuration(string source, string key)
    {
        var config = MerpeepsDoc.Split(source).Frontmatter.GetValueOrDefault("config") as Dictionary<string, object?>;
        var global = Object(config);
        var local = Object(config?.GetValueOrDefault(key));
        var theme = Object(config?.GetValueOrDefault("themeVariables"));
        global.Remove(key); global.Remove("themeVariables");
        return (local, theme, global);
    }
    public static string ApplyConfiguration(string source, string key, JsonObject local, JsonObject theme, JsonObject global, bool setTitle = false, string title = "")
    {
        var config = (JsonObject)global.DeepClone();
        config[key] = local.DeepClone(); config["themeVariables"] = theme.DeepClone();
        using var document = System.Text.Json.JsonDocument.Parse(config.ToJsonString());
        return MerpeepsDoc.WithChartConfiguration(source, MerpeepsDoc.Normalise(document.RootElement)!, setTitle, title);
    }
    public static string PlainText(string text)
    {
        if (text.Contains('\r')) text = text.Replace("\r\n", "\n");
        return text;
    }
    public static string Encode(string text) => text.Replace("\r\n", "\n").Replace("\n", "<br/>");
}
