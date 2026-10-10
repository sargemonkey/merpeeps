using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;

namespace Merpeeps.Core;

/// <summary>Merpeeps v0.1 reference implementation. No UI dependencies.</summary>
public static class MerpeepsDoc
{
    private const string Key = "x-merpeeps";

    private static readonly Regex Frontmatter =
        new(@"\A---\r?\n([\s\S]*?)\r?\n---\r?\n?", RegexOptions.Compiled);

    private static readonly Regex CommentCarrier =
        new(@"^%%[ \t]*merpeeps1[ \t]+(\{[\s\S]*?\})[ \t]*$",
            RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>
    /// Removal pattern for the comment carrier. Unlike <see cref="CommentCarrier"/>
    /// this consumes the line terminator too: matching only the line content
    /// leaves a blank line behind, so stripping metadata would not restore the
    /// original body byte-for-byte — the one promise the format cannot break.
    /// </summary>
    private static readonly Regex CommentLine =
        new(@"^%%[ \t]*merpeeps1[ \t]+\{[\s\S]*?\}[ \t]*(?:\r?\n|$)",
            RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly ISerializer Ser = BuildSerializer();

    /// <summary>
    /// Built in a method rather than a field initialiser so the AOT suppression
    /// below actually applies — a field initialiser is compiled into the static
    /// constructor, and the warning is attributed there, not to the field.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "AOT", "IL3050",
        Justification = "Normalise guarantees only string/bool/long/double/null, " +
                        "Dictionary<string,object?> and List<object?> reach the " +
                        "serialiser, which needs no runtime code generation. " +
                        "Enforced by NormaliseProducesOnlyAotSafeTypes and verified " +
                        "end to end in a published NativeAOT binary.")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming", "IL2026", Justification = "See IL3050 above.")]
    private static ISerializer BuildSerializer() =>
        new SerializerBuilder()
            // Without this, a string like "0.5" is emitted unquoted and reads back
            // as a number — breaking type fidelity across a round-trip.
            // (js-yaml quotes these by default; YamlDotNet does not.)
            .WithQuotingNecessaryStrings()
            .Build();

    /// <summary>
    /// Read YAML preserving scalar TYPES. YamlDotNet's untyped deserializer
    /// collapses every scalar to string; we walk the low-level node graph and
    /// resolve plain (unquoted) scalars per the YAML 1.2 core schema, which is
    /// both correct and reflection-free (so NativeAOT-safe).
    /// </summary>
    /// <remarks>
    /// Internal rather than private because stencil packs need the same
    /// treatment: <c>Deserialize&lt;T&gt;</c> would be far less code but relies
    /// on reflecting over the target type, which does not survive NativeAOT.
    /// Everything that reads YAML here goes through this one door.
    /// </remarks>
    internal static object? FromYaml(string yamlText)
    {
        var stream = new YamlStream();
        using var reader = new StringReader(yamlText);
        stream.Load(reader);
        if (stream.Documents.Count == 0) return null;
        return FromNode(stream.Documents[0].RootNode);
    }

    private static object? FromNode(YamlNode node)
    {
        switch (node)
        {
            case YamlMappingNode map:
            {
                var d = new Dictionary<string, object?>();
                foreach (var (k, v) in map.Children)
                    d[((YamlScalarNode)k).Value ?? ""] = FromNode(v);
                return d;
            }
            case YamlSequenceNode seq:
                return seq.Children.Select(FromNode).ToList();
            case YamlScalarNode s:
            {
                // Quoted scalars are always strings; only plain ones resolve.
                if (s.Style is ScalarStyle.SingleQuoted or ScalarStyle.DoubleQuoted)
                    return s.Value;
                return ResolvePlain(s.Value);
            }
            default:
                return null;
        }
    }

    private static object? ResolvePlain(string? v)
    {
        if (v is null) return null;
        if (v is "" or "~" or "null" or "Null" or "NULL") return null;
        if (v is "true" or "True" or "TRUE") return true;
        if (v is "false" or "False" or "FALSE") return false;
        if (long.TryParse(v, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var l))
            return l;
        if (double.TryParse(v, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var d))
            return d;
        return v;
    }

    public sealed record Doc(Dictionary<string, object?> Frontmatter, string Body);

    public static Doc Split(string src)
    {
        var m = Frontmatter.Match(src);
        if (!m.Success) return new Doc(new(), src);

        Dictionary<string, object?> fm;
        try
        {
            fm = FromYaml(m.Groups[1].Value) as Dictionary<string, object?> ?? new();
        }
        catch { fm = new(); }

        return new Doc(fm, src[m.Length..]);
    }

    public static object? Parse(string src)
    {
        var doc = Split(src);
        if (doc.Frontmatter.TryGetValue(Key, out var v) && v is not null)
            return Normalise(v);

        var c = CommentCarrier.Match(src);
        if (c.Success)
        {
            try { return Normalise(FromYaml(c.Groups[1].Value)); }
            catch { /* fall through */ }
        }
        return null;
    }

    public static string Serialise(string src, object? merpeeps, string carrier = "frontmatter")
    {
        var doc = Split(src);

        // Only the frontmatter block is generated here; the body is passed
        // through untouched. That matters more than it looks: real files are
        // sometimes MIXED (mostly LF with a stray CRLF from a pasted hunk), and
        // normalising the whole document would silently rewrite those lines and
        // break the byte-preservation promise. Converting just the block we
        // emit leaves the author's body exactly as they wrote it.
        var ending = DominantLineEnding(src);

        if (carrier == "comment")
        {
            // The comment carrier is self-contained, so a frontmatter copy of
            // the block would be a second source of truth and is dropped — but
            // the author's *other* frontmatter keys are not ours to discard.
            var rest = new Dictionary<string, object?>(doc.Frontmatter);
            rest.Remove(Key);

            var body = StripComment(doc.Body);
            if (merpeeps is null) return WithFrontmatter(rest, body, ending);

            // Separate only when the body does not already end with a newline.
            // Trimming instead would delete trailing blank lines the author
            // deliberately wrote.
            var sep = body.Length == 0 || EndsWithNewline(body) ? "" : ending;
            return WithFrontmatter(
                rest, $"{body}{sep}%% merpeeps1 {ToJson(merpeeps)}{ending}", ending);
        }

        var next = new Dictionary<string, object?>(doc.Frontmatter);
        if (merpeeps is null) next.Remove(Key); else next[Key] = merpeeps;
        return WithFrontmatter(next, StripComment(doc.Body), ending);
    }

    /// <summary>
    /// Swap the diagram body, keeping the frontmatter exactly as it was.
    /// </summary>
    /// <remarks>
    /// For callers that regenerate the body from the model. The obvious
    /// spelling — serialising the *new* body and passing the Merpeeps block
    /// across — silently loses every other frontmatter key, because the new body
    /// has no frontmatter for them to be preserved from. That deleted the
    /// author's <c>title:</c> and <c>config:</c> on any edit large enough to
    /// need a regeneration.
    /// </remarks>
    public static string ReplaceBody(string source, string body)
    {
        var doc = Split(source);
        return doc.Frontmatter.Count == 0
            ? body
            : WithFrontmatter(doc.Frontmatter, body, DominantLineEnding(source));
    }

    private static bool EndsWithNewline(string s) => s.EndsWith('\n');

    /// <summary>
    /// The mermaid theme named by <c>config: theme:</c>, or null when the file
    /// does not name one.
    /// </summary>
    /// <remarks>
    /// This is mermaid's own key, not ours, and that is the point: a theme set
    /// here is honoured by every renderer — GitHub, GitLab, the mermaid live
    /// editor — with no plugin and no metadata. Storing it under
    /// <c>x-merpeeps</c> instead would have made a diagram look right only in this
    /// editor, which is precisely the failure mode the whole format exists to
    /// avoid. We read and write someone else's key here, carefully.
    /// </remarks>
    public static string? ThemeOf(string src)
    {
        var fm = Split(src).Frontmatter;
        if (!fm.TryGetValue("config", out var cfg)) return null;
        if (Normalise(cfg) is not Dictionary<string, object?> map) return null;
        return map.TryGetValue("theme", out var t) ? t?.ToString() : null;
    }

    /// <summary>
    /// Set or clear <c>config: theme:</c>, leaving every other key — inside
    /// <c>config</c> and beside it — exactly as the author wrote it.
    /// </summary>
    /// <param name="src">The document to rewrite.</param>
    /// <param name="theme">The theme name, or null to remove the key.</param>
    public static string WithTheme(string src, string? theme)
    {
        var doc = Split(src);
        var ending = DominantLineEnding(src);
        var next = new Dictionary<string, object?>(doc.Frontmatter);

        var config = next.TryGetValue("config", out var existing)
                     && Normalise(existing) is Dictionary<string, object?> map
            ? new Dictionary<string, object?>(map)
            : new Dictionary<string, object?>();

        if (theme is null) config.Remove("theme");
        else config["theme"] = theme;

        // An empty `config:` is noise. Removing it returns a file that had no
        // theme to byte-identical to how it started, so setting a theme and
        // clearing it again leaves no trace.
        if (config.Count == 0) next.Remove("config");
        else next["config"] = config;

        return WithFrontmatter(next, doc.Body, ending);
    }

    internal static string WithClassOptions(string source, bool hideEmptyMembersBox, bool hierarchicalNamespaces)
    {
        var doc = Split(source);
        var frontmatter = new Dictionary<string, object?>(doc.Frontmatter);
        var config = frontmatter.GetValueOrDefault("config") is Dictionary<string, object?> existing
            ? new Dictionary<string, object?>(existing) : new Dictionary<string, object?>();
        var options = config.GetValueOrDefault("class") is Dictionary<string, object?> prior
            ? new Dictionary<string, object?>(prior) : new Dictionary<string, object?>();
        options["hideEmptyMembersBox"] = hideEmptyMembersBox;
        options["hierarchicalNamespaces"] = hierarchicalNamespaces;
        config["class"] = options;
        frontmatter["config"] = config;
        return WithFrontmatter(frontmatter, doc.Body, DominantLineEnding(source));
    }

    internal static string WithDiagramConfiguration(string source, string family, Dictionary<string, object?> values, bool removeGlobalFontOverrides = false)
    {
        var doc = Split(source);
        var frontmatter = new Dictionary<string, object?>(doc.Frontmatter);
        var config = frontmatter.GetValueOrDefault("config") is Dictionary<string, object?> existing
            ? new Dictionary<string, object?>(existing) : new Dictionary<string, object?>();
        if (removeGlobalFontOverrides)
            foreach (var key in new[] { "fontFamily", "fontSize", "fontWeight" }) config.Remove(key);
        var options = config.GetValueOrDefault(family) is Dictionary<string, object?> prior
            ? new Dictionary<string, object?>(prior) : new Dictionary<string, object?>();
        foreach (var pair in values) options[pair.Key] = pair.Value;
        config[family] = options;
        frontmatter["config"] = config;
        return WithFrontmatter(frontmatter, doc.Body, DominantLineEnding(source));
    }

    internal static string WithChartConfiguration(string source, object configuration, bool setTitle, string title, bool setDisplayMode = false, string? displayMode = null)
    {
        var split = Split(source);
        var frontmatter = new Dictionary<string, object?>(split.Frontmatter) { ["config"] = configuration };
        if (setDisplayMode)
        {
            if (displayMode is null) frontmatter.Remove("displayMode"); else frontmatter["displayMode"] = displayMode;
        }
        if (setTitle) frontmatter["title"] = title;
        return WithFrontmatter(frontmatter, split.Body, DominantLineEnding(source));
    }

    /// <summary>
    /// Which line ending the generated block should use.
    ///
    /// <para>Mixed files are rare but real, so the majority wins rather than
    /// the first occurrence — a single pasted CRLF line should not flip an
    /// otherwise-LF document.</para>
    /// </summary>
    private static string DominantLineEnding(string src)
    {
        var crlf = 0;
        for (var i = 0; i + 1 < src.Length; i++)
            if (src[i] == '\r' && src[i + 1] == '\n') crlf++;

        var total = 0;
        foreach (var ch in src) if (ch == '\n') total++;

        return crlf > 0 && crlf * 2 >= total ? "\r\n" : "\n";
    }

    public static string Inject(string src, object patch)
    {
        var cur = Parse(src);
        var merged = cur is null ? Normalise(patch) : DeepMerge(cur, Normalise(patch));
        return Serialise(src, merged);
    }

    public static string BodyOf(string src) => StripComment(Split(src).Body);

    // ---------------------------------------------------------------- helpers

    // No blank-line collapsing: rewriting the author's whitespace would itself
    // be a body modification, and CommentLine already removes cleanly.
    private static string StripComment(string body) =>
        CommentLine.Replace(body, "");

    private static string WithFrontmatter(
        Dictionary<string, object?> fm, string body, string ending)
    {
        if (fm.Count == 0) return body;

        // YamlDotNet emits Environment.NewLine, so the same document would come
        // out different on Windows and Linux. Normalise its output, then use
        // the document's own ending.
        var text = Ser.Serialize(fm).Replace("\r\n", "\n").TrimEnd();
        if (ending != "\n") text = text.Replace("\n", ending);

        return $"---{ending}{text}{ending}---{ending}{body}";
    }

    /// <summary>Canonicalise every map to Dictionary&lt;string,object?&gt; so
    /// comparison and merging are stable regardless of YAML/JSON origin.
    ///
    /// <para>This also closes the value domain. Anything that is not a scalar,
    /// dictionary or list is reduced to its string form, so the YAML serialiser
    /// never meets a type it would need reflection to describe. That is what
    /// makes the NativeAOT build genuinely safe rather than merely
    /// warning-free.</para></summary>
    public static object? Normalise(object? v) => v switch
    {
        null => null,
        string s => s,
        bool or sbyte or byte or short or ushort or int or uint
            or long or ulong or float or double or decimal => v,
        // Callers that deserialised with System.Text.Json hand us JsonElement.
        // Without this the whole element would be serialised as its ValueKind.
        System.Text.Json.JsonElement je => FromJsonElement(je),
        Dictionary<string, object?> d =>
            d.ToDictionary(kv => kv.Key, kv => Normalise(kv.Value)),
        Dictionary<object, object?> d =>
            d.ToDictionary(kv => kv.Key?.ToString() ?? "", kv => Normalise(kv.Value)),
        System.Collections.IDictionary d => ToDict(d),
        System.Collections.IList l => l.Cast<object?>().Select(Normalise).ToList(),
        // Enums, DateTime, records, anything else: stringify rather than hand
        // the serialiser a type it can only inspect by reflection. Invariantly
        // where the type offers the choice, because this runs before ToJson and
        // would otherwise bake the machine's locale into the string a
        // comma-decimal or dd.MM.yyyy machine produced.
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => v.ToString(),
    };

    private static object? FromJsonElement(System.Text.Json.JsonElement e)
    {
        switch (e.ValueKind)
        {
            case System.Text.Json.JsonValueKind.Object:
            {
                var d = new Dictionary<string, object?>();
                foreach (var p in e.EnumerateObject()) d[p.Name] = FromJsonElement(p.Value);
                return d;
            }
            case System.Text.Json.JsonValueKind.Array:
                return e.EnumerateArray().Select(FromJsonElement).ToList();
            case System.Text.Json.JsonValueKind.String:
                return e.GetString();
            case System.Text.Json.JsonValueKind.Number:
                return e.TryGetInt64(out var l) ? l : e.GetDouble();
            case System.Text.Json.JsonValueKind.True:
                return true;
            case System.Text.Json.JsonValueKind.False:
                return false;
            default:
                return null;
        }
    }

    private static Dictionary<string, object?> ToDict(System.Collections.IDictionary d)
    {
        var outp = new Dictionary<string, object?>();
        foreach (System.Collections.DictionaryEntry e in d)
            outp[e.Key?.ToString() ?? ""] = Normalise(e.Value);
        return outp;
    }

    private static object? DeepMerge(object? a, object? b)
    {
        if (a is Dictionary<string, object?> da && b is Dictionary<string, object?> db)
        {
            var outp = new Dictionary<string, object?>(da);
            foreach (var (k, v) in db)
                outp[k] = da.TryGetValue(k, out var av) ? DeepMerge(av, v) : v;
            return outp;
        }
        return b;
    }

    /// <summary>Stable JSON for the comment carrier and for value comparison.</summary>
    public static string ToJson(object? v)
    {
        var sb = new StringBuilder();
        Write(Normalise(v), sb);
        return sb.ToString();

        static void Write(object? v, StringBuilder sb)
        {
            switch (v)
            {
                case null: sb.Append("null"); break;
                case string s:
                    sb.Append('"')
                      .Append(s.Replace("\\", "\\\\").Replace("\"", "\\\""))
                      .Append('"');
                    break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case Dictionary<string, object?> d:
                    sb.Append('{');
                    var i = 0;
                    foreach (var k in d.Keys.OrderBy(x => x, StringComparer.Ordinal))
                    {
                        if (i++ > 0) sb.Append(',');
                        Write(k, sb); sb.Append(':'); Write(d[k], sb);
                    }
                    sb.Append('}');
                    break;
                case System.Collections.IEnumerable e:
                    sb.Append('[');
                    var j = 0;
                    foreach (var item in e)
                    {
                        if (j++ > 0) sb.Append(',');
                        Write(Normalise(item), sb);
                    }
                    sb.Append(']');
                    break;

                // Numbers, formatted invariantly — the one branch that made
                // this method locale-dependent. `0.5` on a comma-decimal
                // machine produced `"labelT":0,5`, which is not that value
                // spelled differently but a different document: the carrier is
                // re-read as YAML, and the flow mapping parses as `labelT: 0`
                // followed by a second key `5` holding nothing. Nothing throws.
                //
                // IFormattable covers every numeric type at once, and also the
                // enums, dates and TimeSpans that Normalise leaves for the
                // default branch — all of which format differently by locale.
                case IFormattable n:
                    sb.Append(n.ToString(null, CultureInfo.InvariantCulture));
                    break;

                default: sb.Append(v.ToString()); break;
            }
        }
    }
}
