using System.Globalization;
using System.Text.RegularExpressions;

namespace Merpeeps.Core.Model;

public readonly record struct ClassColor(byte Red, byte Green, byte Blue, byte Alpha = 255)
{
    public string RgbHex => $"{Red:X2}{Green:X2}{Blue:X2}";
    public string Css => Alpha == 255 ? "#" + RgbHex
        : $"rgba({Red},{Green},{Blue},{(Alpha / 255d).ToString("0.###", CultureInfo.InvariantCulture)})";
}

public sealed record ClassResolvedStyle(
    ClassColor? Fill, ClassColor? Stroke, ClassColor? TextColor,
    double StrokeWidth, IReadOnlyList<double> DashPattern,
    double FontSize, string FontFamily, bool Bold, bool Italic,
    IReadOnlyList<string> Diagnostics);

/// <summary>CSS lengths are logical 96-DPI pixels; dashes are absolute lengths, not pen-width multiples.</summary>
public static class ClassDiagramStyle
{
    private static readonly HashSet<string> Supported = new(StringComparer.Ordinal)
    {
        "fill", "stroke", "color", "stroke-width", "stroke-dasharray",
        "font-size", "font-family", "font-weight", "font-style",
    };

    public static ClassResolvedStyle Resolve(DiagramNode node, bool showMerpeepsStyling = true)
    {
        var style = node.Class?.ResolvedStyle
            ?? throw new ArgumentException("The node has no class-family style.", nameof(node));
        return ResolveValues(node, style, showMerpeepsStyling);
    }

    internal static ClassResolvedStyle ResolveValues(DiagramNode node, Dictionary<string, string> style, bool showMerpeepsStyling)
    {
        var diagnostics = style.Keys.Where(k => !Supported.Contains(k))
            .Select(k => $"Diagram style '{k}' has no native interpretation.").ToList();
        ClassColor? Paint(string key, string? custom)
        {
            var value = showMerpeepsStyling && !string.IsNullOrWhiteSpace(custom)
                ? custom : style.GetValueOrDefault(key);
            if (value is null) return null;
            if (TryColor(value, out var paint)) return paint;
            diagnostics.Add($"Unsupported {key} color '{value}'.");
            return null;
        }
        double Length(string key, double fallback, double? custom = null)
        {
            if (showMerpeepsStyling && custom is { } authored && double.IsFinite(authored) && authored > 0)
                return authored;
            var value = style.GetValueOrDefault(key);
            if (value is null) return fallback;
            if (TryLength(value, out var length) && length > 0) return length;
            diagnostics.Add($"Unsupported {key} length '{value}'; use positive px or pt.");
            return fallback;
        }
        var dashes = new List<double>();
        if (showMerpeepsStyling && node.Dashed) dashes.AddRange([5, 3]);
        else if (style.GetValueOrDefault("stroke-dasharray") is { } dash && dash != "none")
        {
            foreach (var part in Regex.Split(dash.Trim(), @"[,\s]+"))
            {
                if (TryLength(part, out var length) && length >= 0) dashes.Add(length);
                else diagnostics.Add($"Unsupported dash length '{part}'.");
            }
            if (dashes.Count > 0 && dashes.All(d => d == 0))
                diagnostics.Add("A dash pattern must contain a positive length.");
        }
        var weight = style.GetValueOrDefault("font-weight");
        if (weight is not (null or "normal" or "bold" or "400" or "700"))
            diagnostics.Add($"Unsupported font weight '{weight}'.");
        var fontStyle = style.GetValueOrDefault("font-style");
        if (fontStyle is not (null or "normal" or "italic"))
            diagnostics.Add($"Unsupported font style '{fontStyle}'.");
        var family = style.GetValueOrDefault("font-family")?.Trim(' ', '\'', '"') ?? "Segoe UI";
        var fill = Paint("fill", node.Fill);
        var stroke = Paint("stroke", node.Stroke);
        var text = Paint("color", node.TextColor);
        var width = Length("stroke-width", 1.6, node.StrokeWidth);
        var size = Length("font-size", 14);
        return new(fill, stroke, text, width, dashes.AsReadOnly(), size, family,
            weight is "bold" or "700", fontStyle == "italic", diagnostics.AsReadOnly());
    }

    private static bool TryLength(string text, out double value)
    {
        var multiplier = text.EndsWith("pt", StringComparison.OrdinalIgnoreCase) ? 96d / 72 : 1;
        if (text.EndsWith("px", StringComparison.OrdinalIgnoreCase) || text.EndsWith("pt", StringComparison.OrdinalIgnoreCase))
            text = text[..^2];
        return double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            && double.IsFinite(value *= multiplier);
    }

    private static bool TryColor(string text, out ClassColor color)
    {
        text = text.Trim();
        color = default;
        if (text is "none" or "transparent") { color = new(0, 0, 0, 0); return true; }
        if (text.StartsWith('#'))
        {
            var hex = text[1..];
            if (hex.Length is 3 or 4) hex = string.Concat(hex.Select(c => new string(c, 2)));
            if (hex.Length is not (6 or 8) || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
                return false;
            color = hex.Length == 6
                ? new((byte)(value >> 16), (byte)(value >> 8), (byte)value)
                : new((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value);
            return true;
        }
        var rgb = Regex.Match(text, @"^rgba?\(\s*(?<r>\d+)\s*,\s*(?<g>\d+)\s*,\s*(?<b>\d+)(?:\s*,\s*(?<a>[\d.]+))?\s*\)$",
            RegexOptions.IgnoreCase);
        if (rgb.Success && byte.TryParse(rgb.Groups["r"].Value, out var red) &&
            byte.TryParse(rgb.Groups["g"].Value, out var green) && byte.TryParse(rgb.Groups["b"].Value, out var blue))
        {
            var alpha = 1d;
            if (rgb.Groups["a"].Success &&
                (!double.TryParse(rgb.Groups["a"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out alpha) || alpha is < 0 or > 1))
                return false;
            color = new(red, green, blue, (byte)Math.Round(alpha * 255));
            return true;
        }
        var named = System.Drawing.Color.FromName(text);
        if (!named.IsKnownColor || named.IsSystemColor) return false;
        color = new(named.R, named.G, named.B, named.A);
        return true;
    }
}
