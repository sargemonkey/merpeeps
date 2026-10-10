using System.Text.Json.Nodes;

namespace Merpeeps.Core.Model;

public sealed record SankeyNode(string Id, string Label);
public sealed record SankeyLink(string Id, string From, string To, double Value);
public sealed class SankeyDocument
{
    public string Directive { get; set; } = "sankey";
    public List<SankeyNode> Nodes { get; } = new();
    public List<SankeyLink> Links { get; } = new();
    public JsonObject Configuration { get; set; } = new();
    public JsonObject ThemeVariables { get; set; } = new();
    public JsonObject GlobalConfiguration { get; set; } = new();
    public List<string> Comments { get; } = new();
    public SankeyDocument Clone()
    {
        var copy = new SankeyDocument { Directive = Directive, Configuration = (JsonObject)Configuration.DeepClone(), ThemeVariables = (JsonObject)ThemeVariables.DeepClone(), GlobalConfiguration = (JsonObject)GlobalConfiguration.DeepClone() };
        copy.Nodes.AddRange(Nodes); copy.Links.AddRange(Links); copy.Comments.AddRange(Comments); return copy;
    }
}
public sealed record TreemapNode(string Id, string Label, double? Value, IReadOnlyList<TreemapNode> Children)
{
    public IReadOnlyList<string> Classes { get; init; } = Array.Empty<string>();
    public TreemapNode DeepCopy() => this with { Classes = Classes.ToArray(), Children = Children.Select(n => n.DeepCopy()).ToArray() };
    public double Total => Value ?? Children.Sum(n => n.Total);
}
public sealed class TreemapDocument
{
    public List<TreemapNode> Roots { get; } = new();
    public Dictionary<string, JsonObject> Classes { get; } = new(StringComparer.Ordinal);
    public JsonObject Configuration { get; set; } = new();
    public JsonObject ThemeVariables { get; set; } = new();
    public JsonObject GlobalConfiguration { get; set; } = new();
    public List<string> Comments { get; } = new();
    public IEnumerable<TreemapNode> Nodes => Walk(Roots);
    public static IEnumerable<TreemapNode> Walk(IEnumerable<TreemapNode> nodes)
    {
        foreach (var node in nodes) { yield return node; foreach (var child in Walk(node.Children)) yield return child; }
    }
    public TreemapDocument Clone()
    {
        var copy = new TreemapDocument { Configuration = (JsonObject)Configuration.DeepClone(), ThemeVariables = (JsonObject)ThemeVariables.DeepClone(), GlobalConfiguration = (JsonObject)GlobalConfiguration.DeepClone() };
        copy.Roots.AddRange(Roots.Select(n => n.DeepCopy())); copy.Comments.AddRange(Comments);
        foreach (var pair in Classes) copy.Classes[pair.Key] = (JsonObject)pair.Value.DeepClone();
        return copy;
    }
}
