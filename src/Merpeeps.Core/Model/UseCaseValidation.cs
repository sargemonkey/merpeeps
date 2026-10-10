using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Merpeeps.Core.Parsing;

namespace Merpeeps.Core.Model;

public static class UseCaseValidation
{
    public static void Check(UseCaseDiagramDocument d)
    {
        if (!Enum.IsDefined(d.Direction)) throw new ArgumentException("Invalid use-case direction.");
        foreach (var key in new[] { "rankSpacing", "nodeSpacing", "diagramPadding" })
            if (d.Configuration[key] is { } value && ChartConfiguration.ParseNumber(value.ToString()) < 0) throw new ArgumentException("Use-case spacing must be nonnegative.");
        if (d.Configuration["colorScheme"]?.ToString() is { } scheme && scheme is not ("role" or "rotate")) throw new ArgumentException("Unknown use-case color scheme.");
        var ids = new HashSet<string>(); var sources = new HashSet<string>(StringComparer.Ordinal);
        foreach (var boundary in d.Boundaries)
        {
            Identity(boundary.Id, boundary.SourceId);
            Text(boundary.Title); _ = Appearance(d, boundary.Id, boundary.Classes, boundary.Style);
        }
        foreach (var node in d.Nodes)
        {
            Identity(node.Id, node.Kind == UseCaseNodeKind.Note ? null : node.SourceId); Text(node.Label);
            if (!Enum.IsDefined(node.Kind) || !Enum.IsDefined(node.ActorKind) || !Enum.IsDefined(node.Shape)) throw new ArgumentException("Unknown use-case element type.");
            if (node.Business && (node.Kind == UseCaseNodeKind.Actor && node.ActorKind is UseCaseActorKind.Awesome or UseCaseActorKind.Icon ||
                node.Kind == UseCaseNodeKind.UseCase && node.Shape != UseCaseShape.Ellipse || node.Kind is UseCaseNodeKind.JsonTable or UseCaseNodeKind.Note))
                throw new ArgumentException("Business markings require normal/hollow actors or ellipse use cases.");
            if (node.Kind != UseCaseNodeKind.Actor && (node.ActorKind != UseCaseActorKind.Normal || node.Icon is not null))
                throw new ArgumentException("Actor variants cannot be applied to use cases or tables.");
            if (node.Kind == UseCaseNodeKind.JsonTable && node.Data is null || node.Kind != UseCaseNodeKind.JsonTable && node.Data is not null)
                throw new ArgumentException("JSON data belongs only to JSON table nodes.");
            if (node.BoundaryId is { } boundary && (d.Boundaries.All(b => b.Id != boundary) || node.Kind is not (UseCaseNodeKind.Actor or UseCaseNodeKind.UseCase)))
                throw new ArgumentException("Only actors/use cases can belong to an existing system boundary.");
            if (node.Kind == UseCaseNodeKind.Note && d.Nodes.All(n => n.Id != node.NoteTargetId || n.Kind is not (UseCaseNodeKind.Actor or UseCaseNodeKind.UseCase)))
                throw new ArgumentException("Notes must target one actor or use case.");
            if (node.Stereotype is { } stereotype && (stereotype.IndexOfAny(['\r', '\n', '<', '>']) >= 0 || node.Kind is not (UseCaseNodeKind.Actor or UseCaseNodeKind.UseCase)))
                throw new ArgumentException("Invalid use-case stereotype.");
            _ = Appearance(d, node.Id, node.Classes, node.Style);
        }
        foreach (var edge in d.Relationships)
        {
            Identity(edge.Id, edge.SourceId); Text(edge.Label);
            var from = d.Nodes.SingleOrDefault(n => n.Id == edge.From); var to = d.Nodes.SingleOrDefault(n => n.Id == edge.To);
            if (from is null || to is null || from.Kind == UseCaseNodeKind.Note || to.Kind == UseCaseNodeKind.Note)
                throw new ArgumentException("Relationships connect actors, use cases or allowed JSON tables.");
            if (!Enum.IsDefined(edge.Kind) || !Enum.IsDefined(edge.FromEnd) || !Enum.IsDefined(edge.ToEnd) || edge.MinimumLength < 1)
                throw new ArgumentException("Unknown UML relationship kind or marker.");
            if (edge.Kind is UseCaseRelationshipKind.Include or UseCaseRelationshipKind.Extend &&
                (from.Kind != UseCaseNodeKind.UseCase || to.Kind != UseCaseNodeKind.UseCase))
                throw new ArgumentException("Include/extend require two use cases.");
            if (edge.Kind == UseCaseRelationshipKind.Generalization &&
                (from.Kind != to.Kind || from.Kind is not (UseCaseNodeKind.Actor or UseCaseNodeKind.UseCase)))
                throw new ArgumentException("Generalization requires two actors or two use cases.");
            if (edge.Kind == UseCaseRelationshipKind.Association)
            {
                var allowed = (edge.FromEnd, edge.ToEnd) is (UseCaseEnd.None, UseCaseEnd.None) or (UseCaseEnd.None, UseCaseEnd.Arrow) or
                    (UseCaseEnd.Arrow, UseCaseEnd.None) or (UseCaseEnd.None, UseCaseEnd.Circle) or (UseCaseEnd.Circle, UseCaseEnd.None) or
                    (UseCaseEnd.None, UseCaseEnd.Cross) or (UseCaseEnd.Cross, UseCaseEnd.None);
                if (!allowed) throw new ArgumentException("Unsupported association marker combination.");
                if ((from.Kind == UseCaseNodeKind.JsonTable || to.Kind == UseCaseNodeKind.JsonTable) &&
                    (edge.FromEnd is UseCaseEnd.Circle or UseCaseEnd.Cross || edge.ToEnd is UseCaseEnd.Circle or UseCaseEnd.Cross))
                    throw new ArgumentException("JSON associations cannot use circle/cross markers.");
            }
            if (edge.MinimumLength > 1 && (edge.Kind != UseCaseRelationshipKind.Association || edge.FromEnd is UseCaseEnd.Circle or UseCaseEnd.Cross || edge.ToEnd is UseCaseEnd.Circle or UseCaseEnd.Cross))
                throw new ArgumentException("Only plain/point associations accept extra minimum length.");
            if (edge.SourceId is null && (edge.Classes.Count > 0 || edge.Style.Count > 0 || edge.Animated || edge.AnimationSpeed is not null))
                throw new ArgumentException("Styles and animation require an explicit relationship source ID.");
            if (edge.AnimationSpeed is not (null or "fast" or "slow")) throw new ArgumentException("Unknown animation speed.");
            _ = Appearance(d, edge.Id, edge.Classes, edge.Style);
        }
        void Identity(string id, string? source)
        {
            if (string.IsNullOrWhiteSpace(id) || !ids.Add(id)) throw new ArgumentException("Use-case model identities must be unique.");
            if (source is not null && (!Regex.IsMatch(source, @"^[A-Za-z0-9_]+$") || !sources.Add(source))) throw new ArgumentException("Use-case source IDs share one unique namespace.");
        }
        static void Text(UseCaseText text)
        {
            if (!text.Markdown && text.Text.IndexOfAny(['\r', '\n']) >= 0) throw new ArgumentException("Plain labels cannot contain physical newlines; use Markdown labels.");
        }
    }
    public static ClassResolvedStyle Appearance(UseCaseDiagramDocument d, string id, IReadOnlyList<string> classes, JsonObject direct)
    {
        var values = new Dictionary<string, string>();
        var node = d.Nodes.FirstOrDefault(n => n.Id == id);
        if (node is { Kind: UseCaseNodeKind.Actor or UseCaseNodeKind.UseCase })
        {
            var prefix = node.Kind == UseCaseNodeKind.Actor ? "actor" : "usecase";
            values["font-size"] = d.Configuration[prefix + "FontSize"]?.ToString() ?? (prefix == "actor" ? "14" : "12");
            values["font-family"] = d.Configuration[prefix + "FontFamily"]?.ToString() ?? d.GlobalConfiguration["fontFamily"]?.ToString() ?? "Open Sans, sans-serif";
            values["font-weight"] = d.Configuration[prefix + "FontWeight"]?.ToString() ?? "normal";
        }
        if (d.Classes.TryGetValue("default", out var defaults)) Add(defaults);
        foreach (var name in classes)
        {
            if (!d.Classes.TryGetValue(name, out var style)) throw new DataDiagramNotEditableException([new("usecase-class", id, "Undefined use-case class: " + name, true)]);
            Add(style);
        }
        Add(direct); var result = ClassDiagramStyle.ResolveValues(new DiagramNode { Id = id }, values, false);
        if (result.Diagnostics.Count > 0) throw new DataDiagramNotEditableException(result.Diagnostics.Select(message => new DataDiagramDiagnostic("usecase-style", id, message, true)).ToArray());
        return result;
        void Add(JsonObject style) { foreach (var pair in style) if (pair.Value is not null) values[pair.Key] = pair.Value.ToString(); }
    }
}
