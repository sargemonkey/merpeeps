using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Merpeeps.Core.Model;

public static class AgentFlowSemantics
{
    public static NodeShape Shape(string shape) => shape switch
    {
        "task" or "roundedRect" or "rounded" or "round-rect" => NodeShape.RoundedRect,
        "tool" or "subroutine" or "subproc" or "fr-rect" => NodeShape.Subroutine,
        "input" or "lean-right" or "lean-r" => NodeShape.Parallelogram,
        "decision" or "diamond" or "diam" => NodeShape.Rhombus,
        "action" or "hex" or "hexagon" => NodeShape.Hexagon,
        "refdoc" or "lin-doc" or "rect" or "rectangle" => NodeShape.Rectangle,
        "circle" or "circ" => NodeShape.Circle, "stadium" => NodeShape.Stadium, "cyl" or "cylinder" => NodeShape.Cylinder,
        _ => throw new NotSupportedException("This Agentflow canonical shape is not yet available in the native renderer: " + shape),
    };
    public static void Check(AgentFlowDocument d)
    {
        if (!Enum.IsDefined(d.Direction)) throw new ArgumentException("Invalid Agentflow direction.");
        var ids = new HashSet<string>(); var names = new HashSet<string>();
        foreach (var element in d.Elements)
        {
            if (string.IsNullOrWhiteSpace(element.Id) || !ids.Add(element.Id) || !Regex.IsMatch(element.SourceId, @"^[A-Za-z0-9_]+$") || !names.Add(element.SourceId))
                throw new ArgumentException("Agentflow identities must be unique.");
            if (!Enum.IsDefined(element.Kind) || element.Global && element.ParentId is not null) throw new ArgumentException("Invalid Agentflow kind or global ownership.");
            if (element.Kind == AgentFlowElementKind.Node) _ = Shape(element.Shape);
            var visited = new HashSet<string> { element.Id }; var parent = element.ParentId;
            while (parent is not null)
            {
                if (!visited.Add(parent)) throw new ArgumentException("Agentflow containment must be acyclic.");
                var group = d.Elements.FirstOrDefault(e => e.Id == parent && e.Kind == AgentFlowElementKind.Flow) ?? throw new ArgumentException("Agentflow parent must be a flow.");
                parent = group.ParentId;
            }
            Metadata(element.Metadata);
        }
        foreach (var link in d.Links)
        {
            if (string.IsNullOrWhiteSpace(link.Id) || !ids.Add(link.Id) || link.SourceId is { } sourceId && (!names.Add(sourceId) || !Regex.IsMatch(sourceId, @"^[A-Za-z0-9_]+$")))
                throw new ArgumentException("Agentflow link identity must be unique.");
            if (!Enum.IsDefined(link.Kind) || d.Elements.All(e => e.Id != link.From) || d.Elements.All(e => e.Id != link.To)) throw new ArgumentException("Unknown Agentflow endpoint.");
            if (link.Metadata.Count > 0 && link.SourceId is null) throw new ArgumentException("Link metadata requires an explicit link ID.");
            Metadata(link.Metadata);
        }
        static void Metadata(JsonNode? node)
        {
            if (node is JsonObject map)
            {
                if (map.Any(p => p.Key is "__proto__" or "constructor" or "prototype")) throw new ArgumentException("Prototype-shaped metadata keys are not retained.");
                foreach (var pair in map) Metadata(pair.Value);
            }
            else if (node is JsonArray array) foreach (var child in array) Metadata(child);
        }
    }
    public static string VisibleEndpoint(AgentFlowDocument d, string id)
    {
        var element = d.Elements.Single(e => e.Id == id); var result = id; var parent = element.ParentId;
        while (parent is not null)
        {
            var flow = d.Elements.Single(e => e.Id == parent);
            if (flow.Collapsed) result = flow.Id; parent = flow.ParentId;
        }
        return result;
    }
}
