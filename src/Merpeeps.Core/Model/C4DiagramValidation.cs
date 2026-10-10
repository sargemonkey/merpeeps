using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Merpeeps.Core.Parsing;

namespace Merpeeps.Core.Model;

public static class C4DiagramValidation
{
    public static IReadOnlyList<DataDiagramDiagnostic> Validate(C4DiagramDocument document)
    {
        var issues = new List<DataDiagramDiagnostic>();
        var ids = new HashSet<string>(StringComparer.Ordinal); var aliases = new HashSet<string>(StringComparer.Ordinal);
        if (!Enum.IsDefined(document.Kind)) issues.Add(new("c4-kind", "", "Unknown C4 diagram kind.", true));
        foreach (var element in document.Elements)
        {
            if (string.IsNullOrWhiteSpace(element.Id) || !ids.Add(element.Id))
                issues.Add(new("c4-identity", element.Id, "C4 identities must be unique.", true));
            if (!Regex.IsMatch(element.Alias, @"^[\p{L}_][\p{L}\p{N}_.-]*$") || !aliases.Add(element.Alias))
                issues.Add(new("c4-alias", element.Id, "C4 aliases must be unique source identifiers.", true));
            if (!Enum.IsDefined(element.Level) || !Enum.IsDefined(element.Shape) || !Enum.IsDefined(element.BoundaryKind) || !Enum.IsDefined(element.Alignment))
                issues.Add(new("c4-element-kind", element.Id, "Unknown C4 element declaration.", true));
            if (element.IsContainer && (element.External || element.Shape != C4ElementShape.Box) ||
                element.Level == C4ElementLevel.Person && element.Shape != C4ElementShape.Box ||
                element.Level != C4ElementLevel.DeploymentNode && element.Alignment != C4HorizontalAlignment.Default)
                issues.Add(new("c4-element-kind", element.Id, "These C4 declaration options cannot be combined.", true));
            if (element.Label.Length == 0) issues.Add(new("c4-label", element.Id, "C4 elements need a label.", true));
            CheckStyle(element.Id, element.Style, ["bgColor", "fontColor", "borderColor"], []);
            CheckExtras(element.Id, element.ExtraArguments);
            try { _ = C4Appearance.Element(document, element); }
            catch (Exception error) when (error is ArgumentException or FormatException)
            { issues.Add(new("c4-appearance", element.Id, error.Message, true)); }
        }
        var containers = document.Elements.Where(e => e.IsContainer).Select(e => e.Id).ToHashSet();
        foreach (var element in document.Elements)
        {
            var seen = new HashSet<string> { element.Id }; var parent = element.ParentId;
            while (parent is not null)
            {
                if (!containers.Contains(parent)) { issues.Add(new("c4-parent", element.Id, "C4 parent must be an existing boundary or deployment node.", true)); break; }
                if (!seen.Add(parent)) { issues.Add(new("c4-parent-cycle", element.Id, "C4 containment cannot be cyclic.", true)); break; }
                parent = document.Elements.First(e => e.Id == parent).ParentId;
            }
        }
        var pairs = new HashSet<(string From, string To)>();
        foreach (var relation in document.Relationships)
        {
            if (string.IsNullOrWhiteSpace(relation.Id) || !ids.Add(relation.Id)) issues.Add(new("c4-relation-identity", relation.Id, "C4 relationship identities must be unique.", true));
            if (!document.Elements.Any(e => e.Id == relation.From) || !document.Elements.Any(e => e.Id == relation.To))
                issues.Add(new("c4-relation-endpoint", relation.Id, "C4 relationships require existing endpoints.", true));
            if (!Enum.IsDefined(relation.Kind)) issues.Add(new("c4-relation-kind", relation.Id, "Unknown C4 relationship kind.", true));
            if (!pairs.Add((relation.From, relation.To)))
                issues.Add(new("c4-duplicate-pair", relation.Id, "Mermaid coalesces repeated C4 endpoint pairs. Native authoring preserves this source without inventing parallel relationships; edit the existing pair instead.", true));
            CheckStyle(relation.Id, relation.Style, ["textColor", "lineColor"], ["offsetX", "offsetY"]);
            CheckExtras(relation.Id, relation.ExtraArguments);
        }
        foreach (var option in document.LayoutOptions)
        {
            if (option.Key is not ("c4ShapeInRow" or "c4BoundaryInRow"))
            { issues.Add(new("c4-layout-option", "", "Unsupported C4 layout option: " + option.Key, true)); continue; }
            try
            {
                var number = option.Value is null ? 0 : ChartConfiguration.ParseNumber(ChartConfiguration.Raw(option.Value));
                if (number < 1 || number != Math.Truncate(number)) issues.Add(new("c4-layout-option", "", option.Key + " must be a positive integer.", true));
            }
            catch (Exception error) when (error is FormatException or ArgumentException)
            { issues.Add(new("c4-layout-option", "", option.Key + ": " + error.Message, true)); }
        }
        foreach (var value in new[] { document.GlobalConfiguration["wrap"], document.Configuration["wrap"] })
            if (value is not null && (value is not JsonValue flag || !flag.TryGetValue<bool>(out _)))
                issues.Add(new("c4-wrap", "", "C4 wrap must be true or false.", true));
        foreach (var key in new[] { "width", "height", "c4ShapeMargin", "c4ShapePadding", "c4ShapeInRow", "c4BoundaryInRow" })
            if (document.Configuration[key] is { } value)
            {
                try
                {
                    var number = ChartConfiguration.ParseNumber(ChartConfiguration.Raw(value));
                    if (number < 0 || key is "width" or "c4ShapeInRow" or "c4BoundaryInRow" && number < 1)
                        issues.Add(new("c4-layout-config", "", key + " is outside its native range.", true));
                }
                catch (Exception error) when (error is ArgumentException or FormatException)
                { issues.Add(new("c4-layout-config", "", key + ": " + error.Message, true)); }
            }
        return issues;

        void CheckStyle(string id, JsonObject style, string[] colors, string[] numbers)
        {
            foreach (var pair in style)
            {
                if (pair.Value is null || pair.Value.ToString().Length == 0) continue;
                var raw = ChartConfiguration.Raw(pair.Value);
                if (colors.Contains(pair.Key, StringComparer.OrdinalIgnoreCase))
                {
                    if (!SequenceColors.TryParse(raw, out _)) issues.Add(new("c4-color", id, "Invalid C4 color for " + pair.Key, true));
                }
                else if (numbers.Contains(pair.Key, StringComparer.OrdinalIgnoreCase))
                {
                    try { _ = ChartConfiguration.ParseNumber(raw); }
                    catch (Exception error) when (error is ArgumentException or FormatException) { issues.Add(new("c4-style-number", id, pair.Key + ": " + error.Message, true)); }
                }
                else issues.Add(new("c4-style-extra", id, "Style argument '" + pair.Key + "' is preserved but not applied by the native renderer."));
            }
        }
        void CheckExtras(string id, IReadOnlyList<C4Argument> extras)
        {
            foreach (var extra in extras)
            {
                if (extra.Name is not null && !Regex.IsMatch(extra.Name, @"^\w+$"))
                    issues.Add(new("c4-argument-name", id, "Invalid C4 named argument.", true));
                if (extra.Value.Length > 0) issues.Add(new("c4-inert-argument", id, "Additional C4 argument '" + extra.Name + "' is preserved as inert source data."));
            }
        }
    }
    public static void Check(C4DiagramDocument document)
    {
        var issues = Validate(document);
        if (issues.Any(i => i.Blocking)) throw new DataDiagramNotEditableException(issues);
    }
}
