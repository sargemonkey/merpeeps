using System.Text.Json.Nodes;

namespace Merpeeps.Core.Model;

public sealed record VennSet(string Id, string Name, string? Label = null, double? Size = null)
{
    public JsonObject Style { get; init; } = new();
    public double EffectiveSize => Size ?? 10;
    public VennSet DeepCopy() => this with { Style = (JsonObject)Style.DeepClone() };
}
/// <summary>The Mermaid source keyword is "union"; its geometry represents the common overlap of these sets.</summary>
public sealed record VennOverlap(string Id, IReadOnlyList<string> SetIds, string? Label = null, double? Size = null)
{
    public JsonObject Style { get; init; } = new();
    public double EffectiveSize => Size ?? 10d / (SetIds.Count * SetIds.Count);
    public VennOverlap DeepCopy() => this with { SetIds = SetIds.ToArray(), Style = (JsonObject)Style.DeepClone() };
}
public sealed record VennText(string Id, string Name, string AreaId, string? Label = null)
{
    public JsonObject Style { get; init; } = new();
    public VennText DeepCopy() => this with { Style = (JsonObject)Style.DeepClone() };
}
public sealed class VennDiagramDocument
{
    public string Title { get; set; } = "";
    public List<VennSet> Sets { get; } = new();
    public List<VennOverlap> Overlaps { get; } = new();
    public List<VennText> Texts { get; } = new();
    public JsonObject Configuration { get; set; } = new();
    public JsonObject ThemeVariables { get; set; } = new();
    public JsonObject GlobalConfiguration { get; set; } = new();
    public List<string> Comments { get; } = new();
    public VennDiagramDocument Clone()
    {
        var copy = new VennDiagramDocument
        {
            Title = Title, Configuration = (JsonObject)Configuration.DeepClone(), ThemeVariables = (JsonObject)ThemeVariables.DeepClone(),
            GlobalConfiguration = (JsonObject)GlobalConfiguration.DeepClone(),
        };
        copy.Sets.AddRange(Sets.Select(s => s.DeepCopy())); copy.Overlaps.AddRange(Overlaps.Select(s => s.DeepCopy()));
        copy.Texts.AddRange(Texts.Select(t => t.DeepCopy())); copy.Comments.AddRange(Comments); return copy;
    }
}
