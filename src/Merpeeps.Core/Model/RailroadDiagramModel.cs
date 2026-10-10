using System.Text.Json.Nodes;

namespace Merpeeps.Core.Model;

public enum RailroadNotation { Ebnf, Abnf, Peg, Primitives }
public enum RailroadExpressionKind { Terminal, Nonterminal, Sequence, Choice, OrderedChoice, Optional, Repeat, Group, Special, Exception, AndPredicate, NotPredicate, AnyCharacter, NumericLiteral }
public sealed record RailroadExpression(string Id, RailroadExpressionKind Kind, string Text, IReadOnlyList<RailroadExpression> Children)
{
    public int Minimum { get; init; }
    public int? Maximum { get; init; }
    public RailroadExpression DeepCopy() => this with { Children = Children.Select(c => c.DeepCopy()).ToArray() };
}
public sealed record RailroadRule(string Id, string Name, RailroadExpression Expression)
{
    public RailroadRule DeepCopy() => this with { Expression = Expression.DeepCopy() };
}
public sealed class RailroadDiagramDocument
{
    public RailroadNotation Notation { get; set; }
    public string Title { get; set; } = "";
    public string AccessibilityTitle { get; set; } = "";
    public string AccessibilityDescription { get; set; } = "";
    public List<RailroadRule> Rules { get; } = new();
    public List<string> Comments { get; } = new();
    public JsonObject Configuration { get; set; } = new();
    public JsonObject ThemeVariables { get; set; } = new();
    public JsonObject GlobalConfiguration { get; set; } = new();
    public string Directive => Notation switch
    {
        RailroadNotation.Ebnf => "railroad-ebnf-beta", RailroadNotation.Abnf => "railroad-abnf-beta",
        RailroadNotation.Peg => "railroad-peg-beta", RailroadNotation.Primitives => "railroad-beta", _ => throw new ArgumentOutOfRangeException(),
    };
    public RailroadDiagramDocument Clone()
    {
        var copy = new RailroadDiagramDocument
        {
            Notation = Notation, Title = Title, AccessibilityTitle = AccessibilityTitle, AccessibilityDescription = AccessibilityDescription,
            Configuration = (JsonObject)Configuration.DeepClone(), ThemeVariables = (JsonObject)ThemeVariables.DeepClone(),
            GlobalConfiguration = (JsonObject)GlobalConfiguration.DeepClone(),
        };
        copy.Rules.AddRange(Rules.Select(r => r.DeepCopy())); copy.Comments.AddRange(Comments); return copy;
    }
    public static IEnumerable<RailroadExpression> Walk(RailroadExpression expression)
    {
        yield return expression;
        foreach (var child in expression.Children) foreach (var nested in Walk(child)) yield return nested;
    }
}
