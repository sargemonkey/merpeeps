using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Merpeeps.Core.Model;

namespace Merpeeps.Core.Parsing;

public static class SequenceText
{
    public static string Decode(string text)
    {
        text = Regex.Replace(text, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        return Regex.Replace(text, @"#(?<value>[A-Za-z0-9]+);", match =>
        {
            var value = match.Groups["value"].Value;
            if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var code) &&
                System.Text.Rune.IsValid(code)) return char.ConvertFromUtf32(code);
            var decoded = WebUtility.HtmlDecode("&" + value + ";");
            return decoded == "&" + value + ";" ? match.Value : decoded;
        });
    }

    public static string Encode(string text)
    {
        var result = new System.Text.StringBuilder();
        foreach (var character in text.Replace("\r\n", "\n"))
            result.Append(character switch
            {
                '#' => "#35;", ';' => "#59;", '<' => "#60;", '>' => "#62;", '&' => "#38;",
                '\n' => "<br/>", _ => character.ToString(),
            });
        return result.ToString();
    }

    public static string Identifier(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (id.Trim() != id || id.IndexOfAny(['\r', '\n', ':', ';', ',', '@', '#', '<', '>', '(', ')', '+', '\\', '/']) >= 0 || id == "end")
            throw new ArgumentException("Use a simple sequence participant identity and put descriptive text in its alias.", nameof(id));
        return id;
    }
}

public static class SequenceColors
{
    public static bool TryParse(string input, out ClassColor color)
    {
        color = default;
        input = input.Trim();
        if (input.Equals("transparent", StringComparison.OrdinalIgnoreCase)) { color = new(0, 0, 0, 0); return true; }
        if (input.StartsWith('#'))
        {
            var hex = input[1..];
            if (hex.Length is 3 or 4) hex = string.Concat(hex.Select(c => new string(c, 2)));
            if (hex.Length is not (6 or 8) || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)) return false;
            color = hex.Length == 6 ? new((byte)(value >> 16), (byte)(value >> 8), (byte)value)
                : new((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value);
            return true;
        }
        var function = Regex.Match(input, @"^(?<type>rgba?|hsla?)\s*\((?<parts>[^)]+)\)$", RegexOptions.IgnoreCase);
        if (function.Success)
        {
            var parts = function.Groups["parts"].Value.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length is not (3 or 4)) return false;
            var numbers = new double[parts.Length];
            for (var i = 0; i < parts.Length; i++)
                if (!double.TryParse(parts[i].TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[i]) || !double.IsFinite(numbers[i]))
                    return false;
            var alpha = parts.Length == 4 ? numbers[3] : 1;
            if (alpha is < 0 or > 1) return false;
            if (function.Groups["type"].Value.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
            {
                for (var i = 0; i < 3; i++)
                {
                    if (parts[i].EndsWith('%')) numbers[i] *= 2.55;
                    if (numbers[i] is < 0 or > 255) return false;
                }
                color = new((byte)Math.Round(numbers[0]), (byte)Math.Round(numbers[1]), (byte)Math.Round(numbers[2]), (byte)Math.Round(alpha * 255));
                return true;
            }
            if (!parts[1].EndsWith('%') || !parts[2].EndsWith('%') || numbers[1] is < 0 or > 100 || numbers[2] is < 0 or > 100) return false;
            var hue = ((numbers[0] % 360) + 360) % 360 / 60;
            var saturation = numbers[1] / 100;
            var light = numbers[2] / 100;
            var chroma = (1 - Math.Abs(2 * light - 1)) * saturation;
            var x = chroma * (1 - Math.Abs(hue % 2 - 1));
            var m = light - chroma / 2;
            var rgb = (int)hue switch
            {
                0 => (chroma, x, 0d), 1 => (x, chroma, 0d), 2 => (0d, chroma, x),
                3 => (0d, x, chroma), 4 => (x, 0d, chroma), _ => (chroma, 0d, x),
            };
            color = new((byte)Math.Round((rgb.Item1 + m) * 255), (byte)Math.Round((rgb.Item2 + m) * 255),
                (byte)Math.Round((rgb.Item3 + m) * 255), (byte)Math.Round(alpha * 255));
            return true;
        }
        var named = System.Drawing.Color.FromName(input.Replace("grey", "gray", StringComparison.OrdinalIgnoreCase));
        if (!named.IsKnownColor || named.IsSystemColor) return false;
        color = new(named.R, named.G, named.B, named.A);
        return true;
    }

    public static string Source(string color)
    {
        if (!TryParse(color, out var parsed)) throw new ArgumentException($"Unknown sequence color '{color}'.");
        if (!color.TrimStart().StartsWith('#')) return color.Trim();
        return parsed.Alpha == 255 ? $"rgb({parsed.Red},{parsed.Green},{parsed.Blue})" :
            $"rgba({parsed.Red},{parsed.Green},{parsed.Blue},{(parsed.Alpha / 255m).ToString("0.####", CultureInfo.InvariantCulture)})";
    }
}
