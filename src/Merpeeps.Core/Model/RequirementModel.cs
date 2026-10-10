using System.Text.Json.Nodes;

namespace Merpeeps.Core.Model;

public enum RequirementKind { Requirement, FunctionalRequirement, InterfaceRequirement, PerformanceRequirement, PhysicalRequirement, DesignConstraint, Element }
public enum RequirementRisk { Unspecified, Low, Medium, High }
public enum RequirementVerification { Unspecified, Analysis, Inspection, Test, Demonstration }
public enum RequirementRelationKind { Contains, Copies, Derives, Satisfies, Verifies, Refines, Traces }
public sealed record RequirementItem(string Id, string Name, RequirementKind Kind)
{
    public string DisplayId { get; init; } = "";
    public string Text { get; init; } = "";
    public RequirementRisk Risk { get; init; }
    public RequirementVerification Verification { get; init; }
    public string ElementType { get; init; } = "";
    public string DocumentReference { get; init; } = "";
    public JsonObject Style { get; init; } = new();
    public IReadOnlyList<string> Classes { get; init; } = Array.Empty<string>();
    public RequirementItem DeepCopy() => this with { Style = (JsonObject)Style.DeepClone(), Classes = Classes.ToArray() };
}
public sealed record RequirementRelation(string Id, string From, string To, RequirementRelationKind Kind);
public sealed class RequirementDocument
{
    public Direction Direction { get; set; } = Direction.TB;
    public List<RequirementItem> Items { get; } = new();
    public List<RequirementRelation> Relations { get; } = new();
    public Dictionary<string, JsonObject> Classes { get; } = new(StringComparer.Ordinal);
    public JsonObject Configuration { get; set; } = new();
    public JsonObject ThemeVariables { get; set; } = new();
    public JsonObject GlobalConfiguration { get; set; } = new();
    public List<string> Comments { get; } = new();
    public RequirementDocument Clone()
    {
        var result = new RequirementDocument { Direction = Direction, Configuration = (JsonObject)Configuration.DeepClone(),
            ThemeVariables = (JsonObject)ThemeVariables.DeepClone(), GlobalConfiguration = (JsonObject)GlobalConfiguration.DeepClone() };
        result.Items.AddRange(Items.Select(i => i.DeepCopy())); result.Relations.AddRange(Relations);
        foreach (var pair in Classes) result.Classes[pair.Key] = (JsonObject)pair.Value.DeepClone();
        result.Comments.AddRange(Comments); return result;
    }
}
