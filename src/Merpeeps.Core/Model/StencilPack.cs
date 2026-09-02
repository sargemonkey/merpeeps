using System.Text.RegularExpressions;

namespace Merpeeps.Core.Model;

/// <summary>
/// A collection of ready-made nodes for one kind of work — AWS architecture,
/// BPMN, C4 modelling — plus the icons those nodes use.
///
/// <para>A pack is <em>data</em>, not code. One declarative file, readable by
/// the C# editor and the JavaScript plugin alike, so a pack author does not have
/// to publish to npm and NuGet and keep the two in step. Adding a stencil set
/// should not require anyone to compile anything.</para>
///
/// <para>Icons carry their own vector geometry. The alternative — a name that
/// only resolves inside a particular renderer — would make a pack useless to
/// every other reader, which defeats the point of the format.</para>
/// </summary>
public sealed class StencilPack
{
    /// <summary>
    /// Lowercase identifier, and the prefix of every icon the pack defines. A
    /// pack declaring <c>id: aws</c> owns <c>aws:*</c> and nothing else.
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>Human-readable name, shown in the collection dropdown.</summary>
    public string Name { get; set; } = "";

    /// <summary>Schema version, so the loader can refuse a future pack politely.</summary>
    public int Spec { get; set; } = 1;

    /// <summary>Optional provenance — who made it, and under what licence.</summary>
    public string? Author { get; set; }
    public string? Licence { get; set; }
    public string? Url { get; set; }

    /// <summary>
    /// Icon geometry by local name. Keys are unprefixed here; the pack id is
    /// applied when they are exposed, so a pack cannot accidentally claim a name
    /// outside its own namespace.
    /// </summary>
    public Dictionary<string, PackIcon> Icons { get; set; } = new();

    /// <summary>The ready-made nodes this pack contributes to the palette.</summary>
    public List<PackStencil> Stencils { get; set; } = new();

    /// <summary>The current schema version this build understands.</summary>
    public const int CurrentSpec = 1;
}

/// <summary>Vector geometry for one icon.</summary>
public sealed class PackIcon
{
    /// <summary>SVG path data, drawn inside <see cref="ViewBox"/>.</summary>
    public string Path { get; set; } = "";

    /// <summary>Coordinate space for <see cref="Path"/>, as in SVG.</summary>
    public string ViewBox { get; set; } = "0 0 24 24";

    /// <summary>
    /// When true the path is filled; otherwise stroked. Most glyph sets are one
    /// or the other throughout, and guessing produces smudges.
    /// </summary>
    public bool Filled { get; set; } = true;
}

/// <summary>One entry in a palette collection: a configured node, not a bare shape.</summary>
public sealed class PackStencil
{
    /// <summary>Label in the palette list.</summary>
    public string Label { get; set; } = "";

    /// <summary>Default text of the node once placed. Falls back to the label.</summary>
    public string? Text { get; set; }

    /// <summary>Shape name, matching <see cref="NodeShape"/>. Unknown values fall back to a rectangle.</summary>
    public string Shape { get; set; } = "Rectangle";

    /// <summary>Icon name. Unprefixed values are resolved against this pack first.</summary>
    public string? Icon { get; set; }

    public string? Fill { get; set; }
    public string? Stroke { get; set; }

    /// <summary>One line explaining when to use it.</summary>
    public string? Hint { get; set; }
}

/// <summary>
/// Reads packs, and refuses the ones that would make icons untrustworthy.
/// </summary>
public static partial class StencilPackLoader
{
    [GeneratedRegex("^[a-z0-9-]+$")]
    private static partial Regex PackIdPattern();

    /// <summary>Why a pack was rejected, or a warning about one that loaded.</summary>
    public readonly record struct Problem(string PackId, string Message)
    {
        public override string ToString() =>
            PackId.Length == 0 ? Message : $"{PackId}: {Message}";
    }

    public readonly record struct LoadResult(
        IReadOnlyList<StencilPack> Packs,
        IReadOnlyList<Problem> Problems)
    {
        public bool Ok => Problems.Count == 0;
    }

    /// <summary>
    /// Parse one pack. Returns null and a reason rather than a half-built pack:
    /// a partially loaded stencil set is worse than an absent one, because it
    /// looks like it worked.
    /// </summary>
    /// <remarks>
    /// Mapped by hand from the generic YAML object model rather than through
    /// <c>Deserialize&lt;StencilPack&gt;</c>. The typed deserialiser reflects
    /// over the target type, which NativeAOT cannot support — and the CLI is
    /// AOT-compiled. Hand mapping also lets a malformed field produce a message
    /// naming the field instead of a serialiser stack trace.
    /// </remarks>
    public static StencilPack? Parse(string yaml, out Problem? problem)
    {
        problem = null;

        object? root;
        try
        {
            root = MerpeepsDoc.FromYaml(yaml);
        }
        catch (Exception ex)
        {
            problem = new Problem("", $"could not be parsed — {ex.Message.Split('\n')[0]}");
            return null;
        }

        if (root is not Dictionary<string, object?> map)
        {
            problem = new Problem("", "is empty or is not a mapping");
            return null;
        }

        var pack = new StencilPack
        {
            Id = Str(map, "id") ?? "",
            Name = Str(map, "name") ?? "",
            Spec = (int)(Num(map, "spec") ?? 1),
            Author = Str(map, "author"),
            Licence = Str(map, "licence") ?? Str(map, "license"),
            Url = Str(map, "url"),
        };

        if (string.IsNullOrWhiteSpace(pack.Id) || !PackIdPattern().IsMatch(pack.Id))
        {
            problem = new Problem(pack.Id,
                "has no valid id — must be lowercase letters, digits or hyphens");
            return null;
        }

        if (pack.Spec > StencilPack.CurrentSpec)
        {
            problem = new Problem(pack.Id,
                $"declares spec {pack.Spec}, but this build understands {StencilPack.CurrentSpec}");
            return null;
        }

        if (map.GetValueOrDefault("icons") is Dictionary<string, object?> icons)
        {
            foreach (var (local, raw) in icons)
            {
                // A pack that claimed a core name could silently repaint an
                // existing diagram the moment it was installed. SPEC §6b forbids
                // it, and the loader is where that gets enforced.
                if (IconVocabulary.IsKnown(local))
                {
                    problem = new Problem(pack.Id,
                        $"defines '{local}', which is a core icon name and cannot be overridden");
                    return null;
                }

                if (raw is not Dictionary<string, object?> icon)
                {
                    problem = new Problem(pack.Id, $"icon '{local}' is not a mapping");
                    return null;
                }

                var path = Str(icon, "path");
                if (string.IsNullOrWhiteSpace(path))
                {
                    problem = new Problem(pack.Id, $"icon '{local}' has no path data");
                    return null;
                }

                pack.Icons[local] = new PackIcon
                {
                    Path = path,
                    ViewBox = Str(icon, "viewBox") ?? "0 0 24 24",
                    Filled = Bool(icon, "filled") ?? true,
                };
            }
        }

        if (map.GetValueOrDefault("stencils") is List<object?> stencils)
        {
            foreach (var raw in stencils)
            {
                if (raw is not Dictionary<string, object?> s)
                {
                    problem = new Problem(pack.Id, "a stencil entry is not a mapping");
                    return null;
                }

                var label = Str(s, "label");
                if (string.IsNullOrWhiteSpace(label))
                {
                    problem = new Problem(pack.Id, "a stencil has no label");
                    return null;
                }

                pack.Stencils.Add(new PackStencil
                {
                    Label = label,
                    Text = Str(s, "text"),
                    Shape = Str(s, "shape") ?? "Rectangle",
                    Icon = Str(s, "icon"),
                    Fill = Str(s, "fill"),
                    Stroke = Str(s, "stroke"),
                    Hint = Str(s, "hint"),
                });
            }
        }

        if (string.IsNullOrWhiteSpace(pack.Name)) pack.Name = pack.Id;
        return pack;
    }

    private static string? Str(Dictionary<string, object?> map, string key) =>
        map.GetValueOrDefault(key) switch
        {
            string s when s.Length > 0 => s,
            long l => l.ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => null,
        };

    private static double? Num(Dictionary<string, object?> map, string key) =>
        map.GetValueOrDefault(key) switch
        {
            long l => l,
            double d => d,
            _ => null,
        };

    private static bool? Bool(Dictionary<string, object?> map, string key) =>
        map.GetValueOrDefault(key) as bool?;

    /// <summary>
    /// Load every <c>*.merpeeps-pack.yml</c> in a directory.
    ///
    /// <para>A missing directory is not an error — packs are optional, and the
    /// editor must work with none installed.</para>
    /// </summary>
    public static LoadResult LoadFrom(string directory)
    {
        var packs = new List<StencilPack>();
        var problems = new List<Problem>();

        if (!Directory.Exists(directory)) return new LoadResult(packs, problems);

        foreach (var file in Directory.EnumerateFiles(directory, "*.merpeeps-pack.yml")
                     .OrderBy(f => f, StringComparer.Ordinal))
        {
            string text;
            try
            {
                text = File.ReadAllText(file);
            }
            catch (Exception ex)
            {
                problems.Add(new Problem(Path.GetFileName(file), $"could not be read — {ex.Message}"));
                continue;
            }

            var pack = Parse(text, out var problem);
            if (pack is null)
            {
                problems.Add(problem ?? new Problem(Path.GetFileName(file), "could not be loaded"));
                continue;
            }

            // First one wins, so load order is the tie-break and a later pack can
            // never quietly repaint an earlier one's icons.
            if (packs.Any(p => string.Equals(p.Id, pack.Id, StringComparison.OrdinalIgnoreCase)))
            {
                problems.Add(new Problem(pack.Id, "is already loaded; the duplicate was ignored"));
                continue;
            }

            packs.Add(pack);
        }

        return new LoadResult(packs, problems);
    }

    /// <summary>
    /// Turn a pack into a palette collection.
    ///
    /// <para>Stencil icon names are namespaced here rather than by the author,
    /// so a pack physically cannot reference another pack's artwork or shadow a
    /// core name by writing it unprefixed.</para>
    /// </summary>
    public static PaletteCollection ToCollection(StencilPack pack)
    {
        var items = new List<PaletteItem>(pack.Stencils.Count);

        foreach (var s in pack.Stencils)
        {
            var shape = Enum.TryParse<NodeShape>(s.Shape, ignoreCase: true, out var parsed)
                ? parsed
                : NodeShape.Rectangle;

            items.Add(new PaletteItem(
                s.Label,
                s.Text ?? s.Label,
                shape,
                Qualify(pack, s.Icon),
                s.Fill,
                s.Stroke,
                s.Hint ?? ""));
        }

        return new PaletteCollection(pack.Name, items);
    }

    /// <summary>
    /// Resolve a stencil's icon reference to a full name: core names pass
    /// through, anything else is scoped to the declaring pack.
    /// </summary>
    internal static string? Qualify(StencilPack pack, string? icon)
    {
        if (string.IsNullOrWhiteSpace(icon)) return null;
        if (IconVocabulary.IsKnown(icon)) return icon;
        return icon.Contains(':') ? icon : $"{pack.Id}:{icon}";
    }
}
