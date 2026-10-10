using System.Text.Json.Nodes;
using Merpeeps.Core.Parsing;

namespace Merpeeps.Core.Model;

public sealed record C4ElementAppearance(ClassColor Fill, ClassColor Stroke, ClassColor TextColor, string FontFamily, double FontSize, int FontWeight);
public sealed record C4RelationshipAppearance(ClassColor Stroke, ClassColor TextColor, double OffsetX, double OffsetY);
public static class C4Appearance
{
    public static C4ElementAppearance Element(C4DiagramDocument document, C4Element element)
    {
        var prefix = (element.External ? "external_" : "") + element.Level.ToString().ToLowerInvariant() +
            (element.Shape == C4ElementShape.Database ? "_db" : element.Shape == C4ElementShape.Queue ? "_queue" : "");
        var colors = element.Level switch
        {
            C4ElementLevel.Person => element.External ? ("#686868", "#8A8A8A") : ("#08427B", "#073B6F"),
            C4ElementLevel.System => element.External ? ("#999999", "#8A8A8A") : ("#1168BD", "#3C7FC0"),
            C4ElementLevel.Container => element.External ? ("#B3B3B3", "#A6A6A6") : ("#438DD5", "#3C7FC0"),
            C4ElementLevel.Component => element.External ? ("#CCCCCC", "#BFBFBF") : ("#85BBF0", "#78A8D8"),
            _ => ("#FFFFFF", "#444444"),
        };
        var size = Number(document.Configuration, prefix + "FontSize", 14);
        var family = document.Configuration[prefix + "FontFamily"]?.ToString() ?? document.GlobalConfiguration["fontFamily"]?.ToString() ?? "Open Sans, sans-serif";
        var rawWeight = document.Configuration[prefix + "FontWeight"]?.ToString() ?? "normal";
        var numericWeight = rawWeight switch { "normal" => 400, "bold" => 700, _ => ChartConfiguration.ParseNumber(rawWeight) };
        if (size <= 0 || numericWeight is < 1 or > 1000 || numericWeight != Math.Truncate(numericWeight)) throw new ArgumentException("C4 font size/weight is invalid.");
        var weight = (int)numericWeight;
        return new(Color(element.Style, "bgColor", Color(document.Configuration, prefix + "_bg_color", Parse(colors.Item1))),
            Color(element.Style, "borderColor", Color(document.Configuration, prefix + "_border_color", Parse(colors.Item2))),
            Color(element.Style, "fontColor", element.IsContainer ? Parse("#444444") : Parse("#FFFFFF")), family, size, weight);
    }
    public static C4RelationshipAppearance Relationship(C4Relationship relationship) =>
        new(Color(relationship.Style, "lineColor", Parse("#666666")), Color(relationship.Style, "textColor", Parse("#333333")),
            Math.Truncate(Number(relationship.Style, "offsetX", 0)), Math.Truncate(Number(relationship.Style, "offsetY", 0)));
    internal static double Number(JsonObject values, string key, double fallback) =>
        values[key] is { } value && value.ToString().Length > 0 ? ChartConfiguration.ParseNumber(ChartConfiguration.Raw(value)) : fallback;
    private static ClassColor Color(JsonObject values, string key, ClassColor fallback) => values[key] is { } value && value.ToString().Length > 0 ? Parse(ChartConfiguration.Raw(value)) : fallback;
    private static ClassColor Parse(string value) => SequenceColors.TryParse(value, out var color) ? color : throw new ArgumentException("Invalid C4 color: " + value);
}
