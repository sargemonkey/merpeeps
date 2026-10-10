using Merpeeps.Core.Parsing;

namespace Merpeeps.Core.Model;

public static class VennValidation
{
    public static void Check(VennDiagramDocument d)
    {
        var ids = new HashSet<string>(); var names = new HashSet<string>(StringComparer.Ordinal); var combinations = new HashSet<string>();
        foreach (var set in d.Sets)
        {
            Identity(set.Id); Name(set.Name); Size(set.EffectiveSize); Style(set.Style);
        }
        foreach (var overlap in d.Overlaps)
        {
            Identity(overlap.Id); Size(overlap.EffectiveSize); Style(overlap.Style);
            if (overlap.SetIds.Count < 2 || overlap.SetIds.Distinct().Count() != overlap.SetIds.Count || overlap.SetIds.Any(id => d.Sets.All(s => s.Id != id)))
                throw new ArgumentException("Venn overlaps need two or more distinct existing sets.");
            if (!combinations.Add(string.Join("\0", overlap.SetIds.Order(StringComparer.Ordinal))))
                throw new DataDiagramNotEditableException([new("venn-duplicate-overlap", overlap.Id, "Duplicate overlap definitions are retained source-only, not silently merged.", true)]);
            if (overlap.EffectiveSize > overlap.SetIds.Min(id => d.Sets.Single(s => s.Id == id).EffectiveSize))
                throw new ArgumentException("An overlap cannot be larger than any of its sets.");
        }
        foreach (var text in d.Texts)
        {
            Identity(text.Id); Name(text.Name); Style(text.Style);
            if (d.Sets.All(s => s.Id != text.AreaId) && d.Overlaps.All(o => o.Id != text.AreaId)) throw new ArgumentException("A Venn text node needs an existing set or overlap.");
        }
        if (d.Title.IndexOfAny(['\r', '\n']) >= 0) throw new ArgumentException("Venn titles must be a single line.");
        foreach (var key in new[] { "width", "height", "padding" })
            if (d.Configuration[key] is { } value && ChartConfiguration.ParseNumber(value.ToString()) < 0) throw new ArgumentException("Venn dimensions must be nonnegative.");
        void Identity(string id) { if (string.IsNullOrWhiteSpace(id) || !ids.Add(id)) throw new ArgumentException("Venn identities must be unique."); }
        void Name(string name) { if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(['\r', '\n', '"']) >= 0 || !names.Add(name)) throw new ArgumentException("Venn set/text names must be unique, nonempty single-line identifiers."); }
        static void Size(double value) { if (!double.IsFinite(value) || value < 0) throw new ArgumentException("Venn sizes must be finite and nonnegative."); }
        static void Style(System.Text.Json.Nodes.JsonObject style)
        {
            foreach (var pair in style)
            {
                if (pair.Value is null) continue;
                if (pair.Key is "fill" or "color" or "stroke")
                {
                    if (!SequenceColors.TryParse(pair.Value.ToString(), out _)) throw new ArgumentException("Invalid Venn color: " + pair.Key);
                }
                else if (pair.Key is "stroke-width" or "fill-opacity")
                {
                    var number = ChartConfiguration.ParseNumber(pair.Value.ToString());
                    if (number < 0 || pair.Key == "fill-opacity" && number > 1) throw new ArgumentException("Venn stroke width/opacity is outside its range.");
                }
                else throw new NotSupportedException("Unsupported native Venn style: " + pair.Key);
            }
        }
    }
}
