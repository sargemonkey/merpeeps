using System.Text.RegularExpressions;

namespace Merpeeps.Core.Model;

/// <summary>
/// Whether an icon name is well formed, and which vocabulary it belongs to.
///
/// <para>These live in Core rather than the editor because they are part of the
/// <em>format</em>: an icon name is written into the file under
/// <c>style.nodes.&lt;id&gt;.icon</c> and has to mean the same thing to every
/// implementation that reads it. How an icon is drawn is a rendering concern
/// and belongs to whatever is doing the drawing.</para>
///
/// <para>Two namespaces, per SPEC §6b. Unprefixed names are the core
/// vocabulary — small, generic, always available, and unclaimable by any pack.
/// Everything else is <c>pack:name</c>, where the prefix is what stops two
/// independent packs colliding on something as obvious as <c>gateway</c>.</para>
/// </summary>
public static partial class IconVocabulary
{
    /// <summary>
    /// The core names. Deliberately small and generic: vendor artwork brings
    /// licensing questions and a maintenance treadmill, and these are shapes
    /// anyone can redraw. The empty string means "no icon".
    /// </summary>
    public static readonly string[] Names =
    [
        "", "server", "database", "queue", "cache", "storage", "function",
        "gateway", "user", "browser", "mobile", "shield", "cloud", "cog",
    ];

    /// <summary>How a name is classified.</summary>
    public enum Kind
    {
        /// <summary>Empty — the node has no icon.</summary>
        None,

        /// <summary>An unprefixed name from the always-available core set.</summary>
        Core,

        /// <summary>A well-formed <c>pack:name</c>, whether or not that pack is loaded.</summary>
        Pack,

        /// <summary>
        /// Not a legal name at all. Still retained in the document, because the
        /// format never deletes what it cannot interpret.
        /// </summary>
        Malformed,
    }

    [GeneratedRegex("^[a-z0-9-]+$")]
    private static partial Regex PackIdPattern();

    [GeneratedRegex("^[a-z0-9_-]+$")]
    private static partial Regex LocalNamePattern();

    /// <summary>True when this is a core name we can always draw.</summary>
    public static bool IsKnown(string? name) =>
        !string.IsNullOrEmpty(name) &&
        Names.Contains(name, StringComparer.OrdinalIgnoreCase);

    /// <summary>Classify a name without needing to know which packs are loaded.</summary>
    public static Kind Classify(string? name)
    {
        if (string.IsNullOrEmpty(name)) return Kind.None;
        if (IsKnown(name)) return Kind.Core;

        var parts = name.Split(':');

        // More than one separator is ambiguous, and guessing at the intent is
        // how a format acquires dialects. SPEC §6b: treat it as unknown.
        if (parts.Length != 2) return Kind.Malformed;

        return PackIdPattern().IsMatch(parts[0]) && LocalNamePattern().IsMatch(parts[1])
            ? Kind.Pack
            : Kind.Malformed;
    }

    /// <summary>
    /// The pack a name belongs to, or null for core, empty and malformed names.
    /// Lets a reader report "this document wants the aws pack" without having
    /// any idea what that pack contains.
    /// </summary>
    public static string? PackOf(string? name) =>
        Classify(name) == Kind.Pack ? name!.Split(':')[0] : null;

    /// <summary>The part after the prefix, or the whole name when there is none.</summary>
    public static string LocalNameOf(string? name) =>
        Classify(name) == Kind.Pack ? name!.Split(':')[1] : name ?? "";

    /// <summary>
    /// Every distinct pack referenced by a document, in first-seen order.
    ///
    /// <para>This is what lets the editor say "install the aws pack to see 6
    /// icons" instead of quietly drawing plain boxes and leaving the person to
    /// wonder what happened.</para>
    /// </summary>
    public static IReadOnlyList<string> PacksReferencedBy(DiagramModel model)
    {
        var seen = new List<string>();
        foreach (var node in model.Nodes)
        {
            if (PackOf(node.Icon) is not { } pack) continue;
            if (!seen.Contains(pack, StringComparer.OrdinalIgnoreCase)) seen.Add(pack);
        }
        return seen;
    }
}
