using System.Text.Json.Nodes;

namespace Merpeeps.Scene;

/// <summary>Logical 96-DPI pixel bounds, independent of a renderer or window.</summary>
public readonly record struct SceneBounds(double X, double Y, double Width, double Height);

public static class SceneKinds
{
    public const string Text = "text";
    public const string Diagram = "diagram";
}

/// <summary>Null color inherits the host's presentation palette, not its UI theme.</summary>
public sealed class SceneTextStyle
{
    public double FontSize { get; set; } = 26;
    public bool Bold { get; set; }
    public string? Color { get; set; }
    public string Align { get; set; } = "left";
    public JsonObject Extra { get; set; } = new();

    public SceneTextStyle Clone() => new()
    {
        FontSize = FontSize, Bold = Bold, Color = Color, Align = Align,
        Extra = (JsonObject)Extra.DeepClone(),
    };
}

/// <summary>
/// A visual object referring to host-owned content. Text and diagram source
/// are not duplicated here; ContentRef resolves against stable body IDs.
/// </summary>
public sealed class SceneElement
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = SceneKinds.Text;
    public string ContentRef { get; set; } = "";
    public SceneBounds Bounds { get; set; } = new(0, 0, 160, 56);
    public SceneTextStyle? Text { get; set; }
    public string? DiagramTheme { get; set; }
    public JsonObject Extra { get; set; } = new();

    public SceneElement Clone() => new()
    {
        Id = Id, Kind = Kind, ContentRef = ContentRef, Bounds = Bounds,
        Text = Text?.Clone(), DiagramTheme = DiagramTheme,
        Extra = (JsonObject)Extra.DeepClone(),
    };
}

/// <summary>A visual surface; deck ordering and speaker notes belong to the host.</summary>
public sealed class SceneModel
{
    public string Id { get; set; } = "";
    public double Width { get; set; } = 1280;
    public double Height { get; set; } = 720;
    public string? Background { get; set; }
    public List<SceneElement> Elements { get; set; } = new();
    public JsonObject Extra { get; set; } = new();

    public SceneModel Clone() => new()
    {
        Id = Id, Width = Width, Height = Height, Background = Background,
        Elements = Elements.Select(e => e.Clone()).ToList(),
        Extra = (JsonObject)Extra.DeepClone(),
    };
}

/// <summary>
/// The separately versioned x-merpeeps-scenes envelope. This is not a
/// reinterpretation of diagram x-merpeeps metadata.
/// </summary>
public sealed class SceneDocument
{
    public const string FrontmatterKey = "x-merpeeps-scenes";
    public const int CurrentSpec = 1;

    public int Spec { get; set; } = CurrentSpec;
    public List<SceneModel> Scenes { get; set; } = new();
    public JsonObject Extra { get; set; } = new();

    public SceneDocument Clone() => new()
    {
        Spec = Spec, Scenes = Scenes.Select(s => s.Clone()).ToList(),
        Extra = (JsonObject)Extra.DeepClone(),
    };
}

public sealed record SceneDiagnostic(string Code, string Path, string Message);

/// <summary>
/// An unsupported/invalid envelope is retained verbatim as a JSON tree and
/// has no editable model. Hosts must surface Diagnostics and keep it read-only.
/// </summary>
public sealed record SceneReadResult(
    SceneDocument? Document, JsonObject Raw, IReadOnlyList<SceneDiagnostic> Diagnostics)
{
    public bool CanEdit => Document is not null && Diagnostics.Count == 0;
}

public sealed class SceneFormatException : FormatException
{
    public IReadOnlyList<SceneDiagnostic> Diagnostics { get; }

    public SceneFormatException(IReadOnlyList<SceneDiagnostic> diagnostics)
        : base(string.Join(Environment.NewLine, diagnostics.Select(d => $"{d.Path}: {d.Message}")))
    {
        Diagnostics = diagnostics;
    }
}
