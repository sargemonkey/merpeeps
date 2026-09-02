using System.Text.Json;
using System.Text.Json.Serialization;

namespace Merpeeps.Render;

/// <summary>
/// Where a renderer actually placed a node, in diagram coordinates.
/// </summary>
public readonly record struct HarvestedNode(double X, double Y, double W, double H);

/// <summary>
/// Parses the position map the reference renderer hands back.
///
/// <para>Lives here rather than in the app because it is the format half of
/// harvesting — the app only supplies a webview to run the JavaScript in.
/// Source-generated so it survives NativeAOT, which reflection-based JSON does
/// not.</para>
/// </summary>
public static class HarvestReader
{
    /// <summary>
    /// Read the JSON emitted by <c>window.merpeepsHarvest</c>.
    ///
    /// <para>Returns an empty map rather than throwing on anything malformed. A
    /// harvest is an optional convenience; failing it should cost the user a
    /// layout, never their document.</para>
    /// </summary>
    public static IReadOnlyDictionary<string, HarvestedNode> Parse(string? json)
    {
        var empty = new Dictionary<string, HarvestedNode>();
        if (string.IsNullOrWhiteSpace(json)) return empty;

        // A webview may hand back the value already quoted as a JS string, in
        // which case it needs unwrapping before it parses as an object.
        var text = json.Trim();
        if (text.StartsWith('"') && text.EndsWith('"'))
        {
            try
            {
                text = JsonSerializer.Deserialize(text, HarvestJson.Default.String) ?? "";
            }
            catch (JsonException)
            {
                return empty;
            }
        }

        if (!text.StartsWith('{')) return empty;

        try
        {
            var raw = JsonSerializer.Deserialize(text, HarvestJson.Default.DictionaryStringRawNode);
            if (raw is null) return empty;

            var result = new Dictionary<string, HarvestedNode>(raw.Count, StringComparer.Ordinal);
            foreach (var (id, n) in raw)
            {
                if (id.Length == 0 || n is null) continue;

                // Both coordinates must be present and usable. A node reported
                // without a position is dropped so the layout engine places it,
                // rather than being silently pinned to the origin.
                if (n.X is not { } x || n.Y is not { } y) continue;
                if (!IsFinite(x) || !IsFinite(y)) continue;

                result[id] = new HarvestedNode(x, y, n.W ?? 0, n.H ?? 0);
            }
            return result;
        }
        catch (JsonException)
        {
            return empty;
        }
    }

    private static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
}

/// <summary>Wire shape, kept separate so the public type can stay a struct.</summary>
/// <remarks>
/// Nullable on purpose. With plain doubles a missing or null coordinate
/// deserialises to 0, so a node the renderer never reported would silently land
/// at the origin — a wrong position is worse than no position, because the
/// layout engine would have placed it sensibly.
/// </remarks>
internal sealed class RawNode
{
    public double? X { get; set; }
    public double? Y { get; set; }
    public double? W { get; set; }
    public double? H { get; set; }
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(Dictionary<string, RawNode>))]
[JsonSerializable(typeof(string))]
internal partial class HarvestJson : JsonSerializerContext;
