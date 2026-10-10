using System.Text.Json;
using Merpeeps.Core;

namespace Merpeeps.Render;

public enum MermaidSyntaxStatus { Pending, Valid, Invalid, Unsupported, Unavailable }

/// <summary>
/// Parser-relative positions, not source-editor offsets. Upstream preprocessing
/// can remove frontmatter and directives; hosts must not highlight an unmapped line.
/// </summary>
public sealed record MermaidSyntaxDiagnostic(
    string Code, string Message, int? ParserLine, int? ParserColumn);

public sealed record MermaidSyntaxResult(
    string RequestId, string MermaidVersion, MermaidSyntaxStatus Status,
    string? DiagramType, IReadOnlyList<MermaidSyntaxDiagnostic> Diagnostics);

/// <summary>
/// Offline grammar validation using the real pinned Mermaid parser in a host's
/// existing web engine. This page neither renders nor enables native editing.
/// </summary>
public static class MermaidValidationPage
{
    public const string ReadyScript = "Boolean(window.merpeepsValidation?.ready)";

    public static string BuildShell()
    {
        if (RenderAssets.MermaidVersion != MermaidDiagramCatalog.MermaidVersion)
            throw new InvalidOperationException("The bundled Mermaid parser does not match the authoring target.");

        return "<!DOCTYPE html>\n<html data-mermaid-version=\"" + RenderAssets.MermaidVersion
            + "\" data-zenuml-version=\"" + RenderAssets.ZenUmlVersion
            + "\"><head><meta charset=\"utf-8\">"
            + "<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'\">"
            + "<title>Local Mermaid validation</title></head><body><div id=\"zenuml-parser\" hidden></div>"
            + "<script>\n" + RenderAssets.MermaidBundle + "\n</script>"
            + "<script>\n" + RenderAssets.ZenUmlBundle + "\n</script>"
            + "<script>\n" + RenderAssets.ZenUmlValidationScript + "\n</script>"
            + "<script>\n" + RenderAssets.ValidationScript + "\n</script>"
            + "</body></html>\n";
    }

    public static string StartScript(string requestId, string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        ArgumentNullException.ThrowIfNull(source);
        return $"window.merpeepsValidation.validate({Literal(requestId)},{Literal(source)})";
    }

    public static string ReadScript(string requestId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        return $"JSON.stringify(window.merpeepsValidation.take({Literal(requestId)}))";
    }

    public static string CancelScript(string requestId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        return $"window.merpeepsValidation.cancel({Literal(requestId)})";
    }

    /// <summary>Decode the JSON returned by ReadScript after any host-specific string unwrapping.</summary>
    public static MermaidSyntaxResult ParseResult(string json, string expectedRequestId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedRequestId);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var requestId = RequiredString(root, "requestId");
        if (requestId != expectedRequestId)
            throw new FormatException("The validation result belongs to a different source request.");
        var version = RequiredString(root, "mermaidVersion");
        if (version != MermaidDiagramCatalog.MermaidVersion)
            throw new FormatException($"Expected Mermaid {MermaidDiagramCatalog.MermaidVersion}, received {version}.");
        var status = RequiredString(root, "status") switch
        {
            "pending" => MermaidSyntaxStatus.Pending,
            "valid" => MermaidSyntaxStatus.Valid,
            "invalid" => MermaidSyntaxStatus.Invalid,
            "unsupported" => MermaidSyntaxStatus.Unsupported,
            "unavailable" => MermaidSyntaxStatus.Unavailable,
            var value => throw new FormatException($"Unknown Mermaid validation status '{value}'."),
        };
        var diagramType = root.GetProperty("diagramType").GetString();
        var diagnostics = root.GetProperty("diagnostics").EnumerateArray()
            .Select(d => new MermaidSyntaxDiagnostic(
                RequiredString(d, "code"), RequiredString(d, "message"),
                PositivePosition(d, "parserLine"), PositivePosition(d, "parserColumn")))
            .ToArray();
        if (status == MermaidSyntaxStatus.Valid &&
            (string.IsNullOrWhiteSpace(diagramType) || diagnostics.Length != 0))
            throw new FormatException("A valid result must identify its diagram type and have no errors.");
        if (status is MermaidSyntaxStatus.Invalid or MermaidSyntaxStatus.Unsupported or MermaidSyntaxStatus.Unavailable
            && diagnostics.Length == 0)
            throw new FormatException("A failed validation result must explain the failure.");
        return new(requestId, version, status, diagramType, Array.AsReadOnly(diagnostics));
    }

    private static string RequiredString(JsonElement element, string name)
    {
        var value = element.GetProperty(name).GetString();
        return !string.IsNullOrWhiteSpace(value) ? value
            : throw new FormatException($"Validation field '{name}' must be a nonempty string.");
    }

    private static int? PositivePosition(JsonElement element, string name)
    {
        var value = element.GetProperty(name);
        if (value.ValueKind == JsonValueKind.Null) return null;
        return value.TryGetInt32(out var position) && position > 0 ? position
            : throw new FormatException($"Validation position '{name}' must be positive or null.");
    }

    private static string Literal(string value) => RenderAssets.JsLiteralForHost(value);
}
