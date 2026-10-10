using System.Text.Json.Nodes;

namespace Merpeeps.Core.Model;

public sealed record IshikawaCause(string Id, string Label, IReadOnlyList<IshikawaCause> Children)
{
    public IshikawaCause DeepCopy() => this with { Children = Children.Select(c => c.DeepCopy()).ToArray() };
}
public sealed class IshikawaDocument
{
    public string Problem { get; set; } = "";
    public List<IshikawaCause> Causes { get; } = new();
    public JsonObject Configuration { get; set; } = new();
    public JsonObject ThemeVariables { get; set; } = new();
    public JsonObject GlobalConfiguration { get; set; } = new();
    public List<string> Comments { get; } = new();
    public IEnumerable<IshikawaCause> AllCauses => Walk(Causes);
    public static IEnumerable<IshikawaCause> Walk(IEnumerable<IshikawaCause> causes)
    {
        foreach (var cause in causes) { yield return cause; foreach (var child in Walk(cause.Children)) yield return child; }
    }
    public IshikawaDocument Clone()
    {
        var copy = new IshikawaDocument
        {
            Problem = Problem, Configuration = (JsonObject)Configuration.DeepClone(), ThemeVariables = (JsonObject)ThemeVariables.DeepClone(),
            GlobalConfiguration = (JsonObject)GlobalConfiguration.DeepClone(),
        };
        copy.Causes.AddRange(Causes.Select(c => c.DeepCopy())); copy.Comments.AddRange(Comments); return copy;
    }
}
public enum CynefinDomain { Complex, Complicated, Clear, Chaotic, Confusion }
public sealed record CynefinItem(string Id, CynefinDomain Domain, string Label);
public sealed record CynefinTransition(string Id, CynefinDomain From, CynefinDomain To, string Label);
public sealed class CynefinDocument
{
    public string Title { get; set; } = "";
    public string AccessibilityTitle { get; set; } = "";
    public string AccessibilityDescription { get; set; } = "";
    public List<CynefinDomain> DeclaredDomains { get; } = new();
    public List<CynefinItem> Items { get; } = new();
    public List<CynefinTransition> Transitions { get; } = new();
    public JsonObject Configuration { get; set; } = new();
    public JsonObject ThemeVariables { get; set; } = new();
    public JsonObject GlobalConfiguration { get; set; } = new();
    public List<string> Comments { get; } = new();
    public CynefinDocument Clone()
    {
        var copy = new CynefinDocument
        {
            Title = Title, AccessibilityTitle = AccessibilityTitle, AccessibilityDescription = AccessibilityDescription,
            Configuration = (JsonObject)Configuration.DeepClone(), ThemeVariables = (JsonObject)ThemeVariables.DeepClone(),
            GlobalConfiguration = (JsonObject)GlobalConfiguration.DeepClone(),
        };
        copy.DeclaredDomains.AddRange(DeclaredDomains); copy.Items.AddRange(Items); copy.Transitions.AddRange(Transitions); copy.Comments.AddRange(Comments);
        return copy;
    }
}
