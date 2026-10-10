using System.Text.Json.Nodes;
using Merpeeps.Core.Model;
using Merpeeps.Core.Parsing;

namespace Merpeeps.Core.Editing;

public sealed class VennEditCommand : IEditCommand
{
    private readonly Action<VennDiagramDocument> _edit;
    private VennDiagramDocument? _before;
    public string Description { get; }
    public string? ResultId { get; }
    internal VennEditCommand(string description, string? id, Action<VennDiagramDocument> edit) => (Description, ResultId, _edit) = (description, id, edit);
    public void Do(DiagramModel model)
    {
        var before = model.Venn ?? throw new ArgumentException("Choose a Venn diagram.");
        var next = before.Clone(); _edit(next); VennValidation.Check(next); _ = VennDiagramAdapter.Write(next);
        _ = Layout.VennDiagramLayout.Create(new DiagramModel { Kind = "venn-beta", Venn = next });
        _before = before; model.Venn = next;
    }
    public void Undo(DiagramModel model) => model.Venn = _before ?? throw new InvalidOperationException("Venn edit is not applied.");
}
public static class VennEditing
{
    public static VennEditCommand AddSet(VennSet set)
    { var copy = set.DeepCopy(); return new("Add Venn set", set.Id, d => d.Sets.Add(copy.DeepCopy())); }
    public static VennEditCommand UpdateSet(VennSet set)
    { var copy = set.DeepCopy(); return new("Edit Venn set", set.Id, d => d.Sets[Index(d.Sets, s => s.Id == set.Id)] = copy.DeepCopy()); }
    public static VennEditCommand RemoveSet(string id) => new("Remove Venn set and dependent regions", null, d =>
    {
        d.Sets.RemoveAt(Index(d.Sets, s => s.Id == id));
        var overlaps = d.Overlaps.Where(o => o.SetIds.Contains(id)).Select(o => o.Id).ToHashSet();
        d.Overlaps.RemoveAll(o => overlaps.Contains(o.Id)); d.Texts.RemoveAll(t => t.AreaId == id || overlaps.Contains(t.AreaId));
    });
    public static VennEditCommand AddOverlap(VennOverlap overlap)
    { var copy = overlap.DeepCopy(); return new("Add Venn overlap", overlap.Id, d => d.Overlaps.Add(copy.DeepCopy())); }
    public static VennEditCommand UpdateOverlap(VennOverlap overlap)
    { var copy = overlap.DeepCopy(); return new("Edit Venn overlap", overlap.Id, d => d.Overlaps[Index(d.Overlaps, o => o.Id == overlap.Id)] = copy.DeepCopy()); }
    public static VennEditCommand RemoveOverlap(string id) => new("Remove Venn overlap and its text", null, d =>
    { d.Overlaps.RemoveAt(Index(d.Overlaps, o => o.Id == id)); d.Texts.RemoveAll(t => t.AreaId == id); });
    public static VennEditCommand AddText(VennText text)
    { var copy = text.DeepCopy(); return new("Add Venn text", text.Id, d => d.Texts.Add(copy.DeepCopy())); }
    public static VennEditCommand UpdateText(VennText text)
    { var copy = text.DeepCopy(); return new("Edit Venn text", text.Id, d => d.Texts[Index(d.Texts, t => t.Id == text.Id)] = copy.DeepCopy()); }
    public static VennEditCommand RemoveText(string id) => new("Remove Venn text", null, d => d.Texts.RemoveAt(Index(d.Texts, t => t.Id == id)));
    public static VennEditCommand SetTitle(string title) => new("Edit Venn title", null, d => d.Title = title);
    public static VennEditCommand SetOption(string key, JsonNode? value)
    {
        var copy = value?.DeepClone();
        return new("Edit Venn option", null, d => { if (copy is null) d.Configuration.Remove(key); else d.Configuration[key] = copy.DeepClone(); });
    }
    private static int Index<T>(List<T> values, Predicate<T> predicate) => values.FindIndex(predicate) is var index && index >= 0 ? index : throw new ArgumentException("Venn element no longer exists.");
}
public sealed record RailroadLocation(string RuleId, string? ParentId, int Index, RailroadExpression Expression);
public sealed class RailroadEditCommand : IEditCommand
{
    private readonly Action<RailroadDiagramDocument> _edit;
    private RailroadDiagramDocument? _before;
    public string Description { get; }
    public string? ResultId { get; }
    internal RailroadEditCommand(string description, string? id, Action<RailroadDiagramDocument> edit) => (Description, ResultId, _edit) = (description, id, edit);
    public void Do(DiagramModel model)
    {
        var before = model.Railroad ?? throw new ArgumentException("Choose a Railroad diagram.");
        var next = before.Clone(); _edit(next); RailroadValidation.Check(next); _ = RailroadDiagramAdapter.Write(next);
        _ = Layout.RailroadDiagramLayout.Create(new DiagramModel { Kind = next.Directive, Railroad = next });
        _before = before; model.Railroad = next;
    }
    public void Undo(DiagramModel model) => model.Railroad = _before ?? throw new InvalidOperationException("Railroad edit is not applied.");
}
public static class RailroadEditing
{
    public static RailroadLocation Locate(RailroadDiagramDocument d, string id)
    {
        foreach (var rule in d.Rules)
            if (Find(rule.Expression, rule.Id, null, 0) is { } result) return result;
        throw new ArgumentException("Grammar expression no longer exists.");
        RailroadLocation? Find(RailroadExpression expression, string ruleId, string? parent, int index)
        {
            if (expression.Id == id) return new(ruleId, parent, index, expression);
            for (var i = 0; i < expression.Children.Count; i++) if (Find(expression.Children[i], ruleId, expression.Id, i) is { } child) return child;
            return null;
        }
    }
    public static RailroadEditCommand AddRule(RailroadRule rule)
    { var copy = rule.DeepCopy(); return new("Add grammar rule", rule.Id, d => d.Rules.Add(copy.DeepCopy())); }
    public static RailroadEditCommand RenameRule(string id, string name) => new("Rename grammar rule and references", id, d =>
    {
        var index = Index(d.Rules, r => r.Id == id); var old = d.Rules[index].Name;
        d.Rules[index] = d.Rules[index] with { Name = name };
        var comparison = d.Notation == RailroadNotation.Abnf ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        for (var i = 0; i < d.Rules.Count; i++) d.Rules[i] = d.Rules[i] with { Expression = Rename(d.Rules[i].Expression) };
        RailroadExpression Rename(RailroadExpression expression) => expression with
        {
            Text = expression.Kind == RailroadExpressionKind.Nonterminal && expression.Text.Equals(old, comparison) ? name : expression.Text,
            Children = expression.Children.Select(Rename).ToArray(),
        };
    });
    public static RailroadEditCommand RemoveRule(string id) => new("Remove grammar rule", null, d => d.Rules.RemoveAt(Index(d.Rules, r => r.Id == id)));
    public static RailroadEditCommand MoveRule(string id, int index) => new("Move grammar rule", id, d =>
    { var old = Index(d.Rules, r => r.Id == id); var rule = d.Rules[old]; d.Rules.RemoveAt(old); d.Rules.Insert(index, rule); });
    public static RailroadEditCommand UpdateExpression(RailroadExpression expression)
    {
        var copy = expression.DeepCopy();
        return new("Edit grammar expression", expression.Id, d => Replace(d, expression.Id, _ => copy.DeepCopy()));
    }
    public static RailroadEditCommand InsertChild(string parentId, int index, RailroadExpression child)
    {
        var copy = child.DeepCopy();
        return new("Insert grammar expression", child.Id, d => Replace(d, parentId, parent =>
        { var children = parent.Children.ToList(); children.Insert(index, copy.DeepCopy()); return parent with { Children = children }; }));
    }
    public static RailroadEditCommand RemoveChild(string parentId, int index) => new("Remove grammar child", parentId, d => Replace(d, parentId, parent =>
    { var children = parent.Children.ToList(); children.RemoveAt(index); return parent with { Children = children }; }));
    public static RailroadEditCommand MoveChild(string parentId, int from, int to) => new("Reorder grammar child", parentId, d => Replace(d, parentId, parent =>
    { var children = parent.Children.ToList(); var child = children[from]; children.RemoveAt(from); children.Insert(to, child); return parent with { Children = children }; }));
    public static RailroadEditCommand KeepChild(string parentId, int index) => new("Replace operator with chosen child", null, d => Replace(d, parentId, parent => parent.Children[index]));
    public static RailroadEditCommand Wrap(string id, RailroadExpressionKind kind, string wrapperId) => new("Wrap grammar expression", wrapperId, d =>
        Replace(d, id, expression => new RailroadExpression(wrapperId, kind, "", [expression])));
    public static RailroadEditCommand WrapWithSibling(string id, RailroadExpressionKind kind, string wrapperId, RailroadExpression sibling)
    {
        var copy = sibling.DeepCopy();
        return new("Combine grammar expressions", wrapperId, d =>
        {
            if (kind is not (RailroadExpressionKind.Sequence or RailroadExpressionKind.Choice or RailroadExpressionKind.OrderedChoice or RailroadExpressionKind.Exception))
                throw new ArgumentException("Choose a two-child grammar operator.");
            Replace(d, id, expression => new(wrapperId, kind, "", [expression, copy.DeepCopy()]));
        });
    }
    public static RailroadEditCommand SetTitle(string title) => new("Edit Railroad title", null, d => d.Title = title);
    public static RailroadEditCommand SetOption(string key, JsonNode? value)
    {
        var copy = value?.DeepClone();
        return new("Edit Railroad option", null, d => { if (copy is null) d.Configuration.Remove(key); else d.Configuration[key] = copy.DeepClone(); });
    }
    private static void Replace(RailroadDiagramDocument d, string id, Func<RailroadExpression, RailroadExpression> replace)
    {
        var found = false;
        for (var i = 0; i < d.Rules.Count; i++) d.Rules[i] = d.Rules[i] with { Expression = Change(d.Rules[i].Expression) };
        if (!found) throw new ArgumentException("Grammar expression no longer exists.");
        RailroadExpression Change(RailroadExpression expression)
        {
            if (expression.Id == id) { found = true; return replace(expression); }
            return expression with { Children = expression.Children.Select(Change).ToArray() };
        }
    }
    private static int Index<T>(List<T> values, Predicate<T> predicate) => values.FindIndex(predicate) is var index && index >= 0 ? index : throw new ArgumentException("Grammar rule no longer exists.");
}
