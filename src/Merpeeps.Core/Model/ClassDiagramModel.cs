using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Merpeeps.Core.Model;

public enum ClassNodeKind { Class, Note, Lollipop }
public enum ClassMemberKind { Attribute, Operation }
public enum ClassVisibility { Unspecified, Public, Private, Protected, Package }
public enum ClassRelationEnd { None, Arrow, Triangle, Composition, Aggregation, Lollipop }

/// <summary>A member's UML signature is data, not a multiline diagram-source field.</summary>
public sealed record ClassMember
{
    public string Id { get; }
    public ClassMemberKind Kind { get; }
    public string Signature { get; }
    public ClassVisibility Visibility { get; }
    public bool IsStatic { get; }
    public bool IsAbstract { get; }

    public ClassMember(string id, ClassMemberKind kind, string signature,
        ClassVisibility visibility = ClassVisibility.Unspecified,
        bool isStatic = false, bool isAbstract = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(signature);
        if (signature.IndexOfAny(['\r', '\n', '{', '}']) >= 0)
            throw new ArgumentException("A class member must be one signature, without diagram blocks.", nameof(signature));
        if (kind == ClassMemberKind.Attribute && signature.Contains('('))
            throw new ArgumentException("Use an operation for a signature with parameters.", nameof(signature));
        if (kind == ClassMemberKind.Operation &&
            (signature.IndexOf('(') <= 0 || signature.LastIndexOf(')') < signature.IndexOf('(')))
            throw new ArgumentException("An operation needs a name and a parenthesized parameter list.", nameof(signature));
        if (isStatic && isAbstract)
            throw new ArgumentException("A member cannot be both static and abstract.");
        if (kind == ClassMemberKind.Attribute && isAbstract)
            throw new ArgumentException("Only operations can be abstract.");
        Id = id;
        Kind = kind;
        Signature = signature.Trim();
        Visibility = visibility;
        IsStatic = isStatic;
        IsAbstract = isAbstract;
    }

    public string Declaration => VisibilityPrefix(Visibility) + Signature
        + (IsStatic ? "$" : IsAbstract ? "*" : "");

    public string DisplayText => VisibilityPrefix(Visibility) + ClassDiagramText.Display(Signature);

    public static ClassMember Parse(string id, string declaration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(declaration);
        var text = declaration.Trim();
        var visibility = text[0] switch
        {
            '+' => ClassVisibility.Public,
            '-' => ClassVisibility.Private,
            '#' => ClassVisibility.Protected,
            '~' => ClassVisibility.Package,
            _ => ClassVisibility.Unspecified,
        };
        if (visibility != ClassVisibility.Unspecified) text = text[1..].TrimStart();
        var kind = text.Contains('(') ? ClassMemberKind.Operation : ClassMemberKind.Attribute;
        var isStatic = text.EndsWith('$');
        var isAbstract = kind == ClassMemberKind.Operation && text.EndsWith('*');
        if (isStatic || isAbstract) text = text[..^1].TrimEnd();
        var close = text.LastIndexOf(')');
        if (kind == ClassMemberKind.Operation && close >= 0 && close + 1 < text.Length &&
            text[close + 1] is '$' or '*')
        {
            isStatic |= text[close + 1] == '$';
            isAbstract |= text[close + 1] == '*';
            text = text.Remove(close + 1, 1);
        }
        return new(id, kind, text, visibility, isStatic, isAbstract);
    }

    private static string VisibilityPrefix(ClassVisibility visibility) => visibility switch
    {
        ClassVisibility.Public => "+",
        ClassVisibility.Private => "-",
        ClassVisibility.Protected => "#",
        ClassVisibility.Package => "~",
        _ => "",
    };
}

/// <summary>
/// Class-family semantics attached to the existing node. Identity, label and
/// geometry remain exclusively on DiagramNode.
/// </summary>
public sealed class ClassNodeDefinition
{
    public ClassNodeKind Kind { get; set; }
    public string? GenericType { get; set; }
    public List<string> Annotations { get; } = new();
    public List<ClassMember> Members { get; } = new();
    public string? NoteFor { get; set; }
    public string? ReferenceName { get; set; }
    public List<string> CssClasses { get; } = new();
    public string? Interaction { get; set; }
    public Dictionary<string, string> InlineStyle { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> ResolvedStyle { get; } = new(StringComparer.Ordinal);

    public ClassNodeDefinition Clone()
    {
        var copy = new ClassNodeDefinition
        {
            Kind = Kind,
            GenericType = GenericType,
            NoteFor = NoteFor,
            ReferenceName = ReferenceName,
            Interaction = Interaction,
        };
        copy.Annotations.AddRange(Annotations);
        copy.Members.AddRange(Members);
        copy.CssClasses.AddRange(CssClasses);
        foreach (var entry in InlineStyle) copy.InlineStyle.Add(entry.Key, entry.Value);
        foreach (var entry in ResolvedStyle) copy.ResolvedStyle.Add(entry.Key, entry.Value);
        return copy;
    }
}

public sealed record ClassRelationship(
    ClassRelationEnd FromEnd = ClassRelationEnd.None,
    ClassRelationEnd ToEnd = ClassRelationEnd.Arrow,
    bool Dashed = false,
    string? FromMultiplicity = null,
    string? ToMultiplicity = null);

public sealed class ClassDiagramOptions
{
    public bool HideEmptyMembersBox { get; set; }
    public bool HierarchicalNamespaces { get; set; } = true;
    public Dictionary<string, Dictionary<string, string>> StyleDefinitions { get; } = new(StringComparer.Ordinal);
}

public sealed record ClassTextRow(string Text, bool Bold = false, bool Italic = false, bool Underline = false);
public sealed record ClassCardContent(
    IReadOnlyList<ClassTextRow> Header,
    IReadOnlyList<ClassTextRow> Attributes,
    IReadOnlyList<ClassTextRow> Operations,
    bool ShowMemberCompartments);

/// <summary>Shared content interpretation; hosts provide actual font measurement.</summary>
public static class ClassDiagramText
{
    public static ClassCardContent Content(DiagramNode node, ClassDiagramOptions options)
    {
        var definition = node.Class
            ?? throw new ArgumentException("The node has no class semantics.", nameof(node));
        if (definition.Kind != ClassNodeKind.Class)
            throw new ArgumentException("Notes and lollipop labels are plain text, not class-card content.", nameof(node));
        var header = definition.Annotations.Select(a => new ClassTextRow($"\u00AB{a}\u00BB")).ToList();
        var title = node.Display;
        if (!string.IsNullOrEmpty(definition.GenericType)) title += Display("~" + definition.GenericType + "~");
        header.Add(new(title, Bold: true));
        var attributes = definition.Members.Where(m => m.Kind == ClassMemberKind.Attribute)
            .Select(m => new ClassTextRow(m.DisplayText, Underline: m.IsStatic)).ToArray();
        var operations = definition.Members.Where(m => m.Kind == ClassMemberKind.Operation)
            .Select(m => new ClassTextRow(m.DisplayText, Italic: m.IsAbstract, Underline: m.IsStatic)).ToArray();
        return new(header.AsReadOnly(), Array.AsReadOnly(attributes), Array.AsReadOnly(operations),
            !options.HideEmptyMembersBox || attributes.Length + operations.Length > 0);
    }

    public static string Display(string text)
    {
        text = WebUtility.HtmlDecode(text);
        var output = new StringBuilder();
        var depth = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '~') { output.Append(text[i]); continue; }
            if (i + 1 < text.Length && (char.IsLetterOrDigit(text[i + 1]) || text[i + 1] == '_'))
            {
                output.Append('<');
                depth++;
            }
            else if (depth > 0)
            {
                output.Append('>');
                depth--;
            }
            else return text;
        }
        return depth == 0 ? output.ToString() : text;
    }
}
