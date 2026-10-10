using System.Text.RegularExpressions;
using Merpeeps.Core.Parsing;

namespace Merpeeps.Core.Model;

public static class RailroadValidation
{
    public static void Check(RailroadDiagramDocument document)
    {
        if (!Enum.IsDefined(document.Notation)) throw new ArgumentException("Unknown Railroad notation.");
        var names = new HashSet<string>(document.Notation == RailroadNotation.Abnf ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var namePattern = document.Notation == RailroadNotation.Abnf ? @"^[A-Za-z][A-Za-z0-9-]*$" : @"^[\p{L}_][\p{L}\p{N}_-]*$";
        var identities = new HashSet<string>();
        foreach (var rule in document.Rules)
        {
            Id(rule.Id);
            if (!Regex.IsMatch(rule.Name, namePattern) || !names.Add(rule.Name))
                throw new ArgumentException("Railroad rule names must be unique identifiers.");
            var pending = new Stack<RailroadExpression>(); pending.Push(rule.Expression);
            while (pending.TryPop(out var node))
            {
                Id(node.Id);
                if (!Enum.IsDefined(node.Kind)) throw new ArgumentException("Unknown Railroad expression kind.");
                var count = node.Children.Count;
                switch (node.Kind)
                {
                    case RailroadExpressionKind.Terminal:
                    case RailroadExpressionKind.Nonterminal:
                    case RailroadExpressionKind.Special:
                    case RailroadExpressionKind.AnyCharacter:
                    case RailroadExpressionKind.NumericLiteral:
                        if (count != 0) throw new ArgumentException("Leaf expressions cannot own children."); break;
                    case RailroadExpressionKind.Sequence:
                    case RailroadExpressionKind.Choice:
                    case RailroadExpressionKind.OrderedChoice:
                        if (count < 2) throw new ArgumentException("Sequences and choices need at least two children."); break;
                    case RailroadExpressionKind.Exception:
                        if (count != 2) throw new ArgumentException("An exception has a matched and excluded expression."); break;
                    case RailroadExpressionKind.Group:
                        throw new NotSupportedException("Grouping punctuation is derived from the expression tree; use a sequence or choice.");
                    default:
                        if (count != 1) throw new ArgumentException("This Railroad operator needs one child."); break;
                }
                if (node.Kind == RailroadExpressionKind.Nonterminal && !Regex.IsMatch(node.Text, namePattern))
                    throw new ArgumentException("Nonterminal references must be identifiers.");
                if (node.Kind == RailroadExpressionKind.Repeat && (node.Minimum < 0 || node.Maximum < node.Minimum))
                    throw new ArgumentException("Repetition bounds must be nonnegative and ordered.");
                if (node.Kind == RailroadExpressionKind.Repeat && document.Notation != RailroadNotation.Abnf && (node.Maximum is not null || node.Minimum > 1))
                    throw new NotSupportedException("Finite/exact repetition is expressed by the ABNF notation.");
                if (node.Kind is RailroadExpressionKind.Exception && document.Notation != RailroadNotation.Ebnf ||
                    node.Kind is RailroadExpressionKind.AnyCharacter or RailroadExpressionKind.AndPredicate or RailroadExpressionKind.NotPredicate or RailroadExpressionKind.OrderedChoice && document.Notation != RailroadNotation.Peg ||
                    node.Kind == RailroadExpressionKind.NumericLiteral && document.Notation != RailroadNotation.Abnf ||
                    node.Kind == RailroadExpressionKind.Special && document.Notation is not (RailroadNotation.Ebnf or RailroadNotation.Primitives))
                    throw new NotSupportedException("The selected Railroad notation does not support this construct.");
                if (node.Kind == RailroadExpressionKind.Choice && document.Notation == RailroadNotation.Peg)
                    throw new ArgumentException("PEG alternatives are ordered choices.");
                if (node.Kind == RailroadExpressionKind.NumericLiteral && !Regex.IsMatch(node.Text, @"^%[xX][\da-fA-F]+(?:[.-][\da-fA-F]+)*$|^%[dD]\d+(?:[.-]\d+)*$|^%[bB][01]+(?:[.-][01]+)*$"))
                    throw new ArgumentException("Invalid ABNF numeric literal.");
                if (node.Kind == RailroadExpressionKind.Special && node.Text.Contains('?')) throw new ArgumentException("Special-sequence text cannot contain its '?' delimiter.");
                foreach (var child in node.Children.Reverse()) pending.Push(child);
            }
        }
        if (document.GlobalConfiguration["look"]?.ToString() == "handDrawn")
            throw new DataDiagramNotEditableException([new("railroad-look", "", "Railroad diagrams do not support hand-drawn rendering.", true)]);
        if (document.Title.IndexOfAny(['\r', '\n']) >= 0 || document.AccessibilityTitle.IndexOfAny(['\r', '\n']) >= 0 || document.AccessibilityDescription.IndexOfAny(['\r', '\n']) >= 0)
            throw new ArgumentException("Railroad directives must be single-line text.");
        foreach (var pair in document.Configuration)
        {
            if (pair.Value is null) continue;
            if (pair.Key.EndsWith("Color", StringComparison.Ordinal) || pair.Key.EndsWith("Fill", StringComparison.Ordinal) || pair.Key.EndsWith("Stroke", StringComparison.Ordinal))
                if (!SequenceColors.TryParse(pair.Value.ToString(), out _)) throw new ArgumentException("Invalid Railroad color: " + pair.Key);
            if (pair.Key is "padding" or "verticalSeparation" or "horizontalSeparation" or "arcRadius" or "fontSize" or "strokeWidth" or "markerRadius")
                if (ChartConfiguration.ParseNumber(pair.Value.ToString()) < 0) throw new ArgumentException("Railroad sizes must be nonnegative.");
        }
        void Id(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || !identities.Add(id)) throw new ArgumentException("Railroad rule/expression identities must be unique and the expression tree acyclic.");
        }
    }
}
