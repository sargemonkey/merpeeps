using Merpeeps.Core.Parsing;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Merpeeps.Core.Model;

public static class SwimlaneValidation
{
    public static void Check(SwimlaneDiagramDocument d)
    {
        if (!Enum.IsDefined(d.Direction)) throw new ArgumentException("Unknown lane direction.");
        var ids = new HashSet<string>(); var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var g in d.Groups)
        {
            Identity(g.Id, g.SourceId); var seen = new HashSet<string> { g.Id }; var parent = g.ParentId;
            while (parent is not null)
            {
                if (!seen.Add(parent)) throw new ArgumentException("Lane/group containment must be acyclic.");
                parent = d.Groups.FirstOrDefault(x => x.Id == parent)?.ParentId ?? (d.Groups.Any(x => x.Id == parent) ? null : throw new ArgumentException("Unknown parent group."));
            }
            _ = Appearance(d, g.Id, g.Classes, g.Style);
        }
        foreach (var n in d.Nodes)
        {
            Identity(n.Id, n.SourceId);
            if (!Enum.IsDefined(n.Shape) || n.GroupId is not null && d.Groups.All(g => g.Id != n.GroupId)) throw new ArgumentException("Invalid lane step shape or ownership.");
            _ = Appearance(d, n.Id, n.Classes, n.Style);
        }
        foreach (var link in d.Links)
            if (string.IsNullOrWhiteSpace(link.Id) || !ids.Add(link.Id) || !Enum.IsDefined(link.Kind) || d.Nodes.All(n => n.Id != link.From) || d.Nodes.All(n => n.Id != link.To))
                throw new ArgumentException("Invalid swimlane handoff identity or endpoints.");
        void Identity(string id, string name)
        {
            if (string.IsNullOrWhiteSpace(id) || !ids.Add(id) || !Regex.IsMatch(name, @"^[A-Za-z0-9_.-]+$") || !names.Add(name))
                throw new ArgumentException("Swimlane source and model identities must be unique.");
        }
    }
    public static ClassResolvedStyle Appearance(SwimlaneDiagramDocument d, string id, IReadOnlyList<string> classes, JsonObject direct)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (d.Classes.TryGetValue("default", out var defaults)) Add(defaults);
        foreach (var key in classes)
        {
            if (!d.Classes.TryGetValue(key, out var style)) throw new ArgumentException("Undefined swimlane style class: " + key);
            Add(style);
        }
        Add(direct);
        var result = ClassDiagramStyle.ResolveValues(new DiagramNode { Id = id }, values, false);
        if (result.Diagnostics.Count > 0) throw new DataDiagramNotEditableException(result.Diagnostics.Select(message => new DataDiagramDiagnostic("swimlane-style", id, message, true)).ToArray());
        return result;
        void Add(JsonObject style) { foreach (var pair in style) if (pair.Value is not null) values[pair.Key] = pair.Value.ToString(); }
    }
}
