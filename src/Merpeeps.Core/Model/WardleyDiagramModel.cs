using System.Text.Json.Nodes;

namespace Merpeeps.Core.Model;

public enum WardleyStrategy { None, Build, Buy, Outsource, Market }
public enum WardleyLinkKind { Dependency, Dashed, Flow, ReverseFlow, BidirectionalFlow }
public enum WardleyAnnotationKind { Note, Numbered, Accelerator, Deaccelerator }
public sealed record WardleyComponent(string Id, string Name, double Evolution)
{
    /// <summary>Null only for pipeline variants, which inherit visibility from PipelineParentId.</summary>
    public double? Visibility { get; init; }
    public string? PipelineParentId { get; init; }
    public bool Anchor { get; init; }
    public bool Inertia { get; init; }
    public WardleyStrategy Strategy { get; init; }
    public DiagramPoint LabelOffset { get; init; }
    public double? EvolveTo { get; init; }
    /// <summary>Trends use standard x=evolution, y=visibility, unlike component source coordinates.</summary>
    public DiagramPoint? Trend { get; init; }
}
public sealed record WardleyLink(string Id, string From, string To, WardleyLinkKind Kind, string Label = "");
public sealed record WardleyAnnotation(string Id, WardleyAnnotationKind Kind, string Text, double Visibility, double Evolution)
{
    public int? Number { get; init; }
}
public sealed record WardleyStage(string Label, string? SecondaryLabel, double End, bool ExplicitEnd = true);
public sealed class WardleyDiagramDocument
{
    public string Title { get; set; } = "";
    public double Width { get; set; } = 1100;
    public double Height { get; set; } = 600;
    public bool HasExplicitSize { get; set; }
    public List<WardleyComponent> Components { get; } = new();
    public List<WardleyLink> Links { get; } = new();
    public List<WardleyAnnotation> Annotations { get; } = new();
    public List<WardleyStage> Stages { get; } = new();
    /// <summary>Canonical X=evolution, Y=visibility. The pinned parser reads source [visibility,evolution], despite the docs' x/y wording.</summary>
    public DiagramPoint? AnnotationBox { get; set; }
    public JsonObject Configuration { get; set; } = new();
    public JsonObject ThemeVariables { get; set; } = new();
    public JsonObject GlobalConfiguration { get; set; } = new();
    public List<string> Comments { get; } = new();
    public WardleyDiagramDocument Clone()
    {
        var copy = new WardleyDiagramDocument
        {
            Title = Title, Width = Width, Height = Height, HasExplicitSize = HasExplicitSize, AnnotationBox = AnnotationBox,
            Configuration = (JsonObject)Configuration.DeepClone(), ThemeVariables = (JsonObject)ThemeVariables.DeepClone(),
            GlobalConfiguration = (JsonObject)GlobalConfiguration.DeepClone(),
        };
        copy.Components.AddRange(Components); copy.Links.AddRange(Links); copy.Annotations.AddRange(Annotations); copy.Stages.AddRange(Stages);
        copy.Comments.AddRange(Comments); return copy;
    }
    public double VisibilityOf(string id)
    {
        var seen = new HashSet<string>(); var component = Components.Single(c => c.Id == id);
        while (component.PipelineParentId is { } parent)
        {
            if (!seen.Add(component.Id)) throw new ArgumentException("Wardley pipelines cannot be cyclic.");
            component = Components.Single(c => c.Id == parent);
        }
        return component.Visibility ?? throw new ArgumentException("Wardley component visibility is missing.");
    }
}
