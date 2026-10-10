using System.Text.Json.Nodes;

namespace Merpeeps.Core.Model;

public static class RequirementAppearance
{
    public static ClassResolvedStyle Resolve(RequirementDocument document, RequirementItem item)
    {
        var styles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (document.Classes.TryGetValue("default", out var defaults)) Merge(defaults);
        foreach (var key in item.Classes)
        {
            if (!document.Classes.TryGetValue(key, out var style))
                throw new DataDiagramNotEditableException([new("requirement-class", item.Id, "Undefined requirement style class: " + key, true)]);
            Merge(style);
        }
        Merge(item.Style);
        var appearance = ClassDiagramStyle.ResolveValues(new DiagramNode { Id = item.Id }, styles, false);
        if (appearance.Diagnostics.Count > 0)
            throw new DataDiagramNotEditableException(appearance.Diagnostics.Select(message => new DataDiagramDiagnostic("requirement-style", item.Id, message, true)).ToArray());
        return appearance;
        void Merge(JsonObject values)
        {
            foreach (var pair in values) if (pair.Value is not null) styles[pair.Key] = ChartConfiguration.Raw(pair.Value);
        }
    }
}
