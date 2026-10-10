using System.Text.Json.Nodes;

namespace Merpeeps.Core.Model;

public enum C4DiagramKind { Context, Container, Component, Dynamic, Deployment }
public enum C4ElementLevel { Person, System, Container, Component, Boundary, DeploymentNode }
public enum C4ElementShape { Box, Database, Queue }
public enum C4BoundaryKind { Generic, Enterprise, System, Container }
public enum C4HorizontalAlignment { Default, Left, Right }
public enum C4RelationshipKind { Rel, Bidirectional, Up, Down, Left, Right, Back, Indexed }
public sealed record C4Argument(string? Name, string Value);
public sealed record C4Element(string Id, string Alias, string Label, C4ElementLevel Level)
{
    public bool External { get; init; }
    public C4ElementShape Shape { get; init; }
    public C4BoundaryKind BoundaryKind { get; init; }
    public C4HorizontalAlignment Alignment { get; init; }
    public string Description { get; init; } = "";
    public string Technology { get; init; } = "";
    public string BoundaryType { get; init; } = "";
    public string? ParentId { get; init; }
    public IReadOnlyList<C4Argument> ExtraArguments { get; init; } = Array.Empty<C4Argument>();
    public JsonObject Style { get; init; } = new();
    public bool IsContainer => Level is C4ElementLevel.Boundary or C4ElementLevel.DeploymentNode;
    public C4Element DeepCopy() => this with { ExtraArguments = ExtraArguments.ToArray(), Style = (JsonObject)Style.DeepClone() };
}
public sealed record C4Relationship(string Id, string From, string To, string Label)
{
    public C4RelationshipKind Kind { get; init; }
    public string Technology { get; init; } = "";
    public string Description { get; init; } = "";
    /// <summary>Preserved source argument; dynamic display numbering follows relation order.</summary>
    public string? IndexArgument { get; init; }
    public IReadOnlyList<C4Argument> ExtraArguments { get; init; } = Array.Empty<C4Argument>();
    public JsonObject Style { get; init; } = new();
    public C4Relationship DeepCopy() => this with { ExtraArguments = ExtraArguments.ToArray(), Style = (JsonObject)Style.DeepClone() };
}
public sealed class C4DiagramDocument
{
    public C4DiagramKind Kind { get; set; }
    public string Title { get; set; } = "";
    public List<C4Element> Elements { get; } = new();
    public List<C4Relationship> Relationships { get; } = new();
    public JsonObject LayoutOptions { get; set; } = new();
    public JsonObject Configuration { get; set; } = new();
    public JsonObject ThemeVariables { get; set; } = new();
    public JsonObject GlobalConfiguration { get; set; } = new();
    public List<string> Comments { get; } = new();
    public C4DiagramDocument Clone()
    {
        var copy = new C4DiagramDocument
        {
            Kind = Kind, Title = Title, LayoutOptions = (JsonObject)LayoutOptions.DeepClone(),
            Configuration = (JsonObject)Configuration.DeepClone(), ThemeVariables = (JsonObject)ThemeVariables.DeepClone(),
            GlobalConfiguration = (JsonObject)GlobalConfiguration.DeepClone(),
        };
        copy.Elements.AddRange(Elements.Select(e => e.DeepCopy()));
        copy.Relationships.AddRange(Relationships.Select(r => r.DeepCopy()));
        copy.Comments.AddRange(Comments); return copy;
    }
    public string Directive => Kind switch
    {
        C4DiagramKind.Context => "C4Context", C4DiagramKind.Container => "C4Container", C4DiagramKind.Component => "C4Component",
        C4DiagramKind.Dynamic => "C4Dynamic", C4DiagramKind.Deployment => "C4Deployment", _ => throw new ArgumentOutOfRangeException(),
    };
}
