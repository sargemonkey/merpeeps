using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Merpeeps.Core.Model;

namespace Merpeeps.Core.Parsing;

public static class GanttDateCodec
{
    private static readonly string[] Tokens = ["YYYY", "MMMM", "MMM", "DDDD", "DDD", "SSS", "YY", "MM", "DD", "Do", "HH", "hh", "mm", "ss", "SS", "ZZ", "Q", "M", "D", "H", "h", "m", "s", "S", "Z", "A", "a"];
    public static string Format(DateTimeOffset instant, string format, GanttEvaluationContext context, bool start = false)
    {
        ArgumentNullException.ThrowIfNull(context);
        format = format.Trim();
        if (format is "X" or "x")
        {
            var milliseconds = instant.ToUnixTimeMilliseconds();
            return (format == "x" || start && milliseconds >= 0 ? milliseconds : instant.ToUnixTimeSeconds()).ToString(CultureInfo.InvariantCulture);
        }
        var date = TimeZoneInfo.ConvertTime(instant, context.TimeZone);
        var output = new StringBuilder();
        for (var index = 0; index < format.Length;)
        {
            if (format[index] == '[')
            {
                var close = format.IndexOf(']', index + 1);
                if (close < 0) throw new FormatException("Unclosed Gantt date-format literal.");
                output.Append(format[(index + 1)..close]); index = close + 1; continue;
            }
            var token = Tokens.FirstOrDefault(token => format.AsSpan(index).StartsWith(token, StringComparison.Ordinal));
            if (token is null)
            {
                if (char.IsLetter(format[index])) throw new NotSupportedException("Unsupported Gantt date-format token near " + format[index..]);
                output.Append(format[index++]); continue;
            }
            output.Append(token switch
            {
                "YYYY" => date.ToString("yyyy", CultureInfo.InvariantCulture), "YY" => date.ToString("yy", CultureInfo.InvariantCulture),
                "MMMM" => context.DateCulture.DateTimeFormat.GetMonthName(date.Month),
                "MMM" => context.DateCulture.DateTimeFormat.GetAbbreviatedMonthName(date.Month),
                "M" => Number(date.Month), "MM" => date.ToString("MM", CultureInfo.InvariantCulture),
                "D" => Number(date.Day), "DD" => date.ToString("dd", CultureInfo.InvariantCulture),
                "Do" => Number(date.Day) + (date.Day is 11 or 12 or 13 ? "th" : (date.Day % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" }),
                "DDD" => Number(date.DayOfYear), "DDDD" => date.DayOfYear.ToString("000", CultureInfo.InvariantCulture),
                "Q" => Number((date.Month - 1) / 3 + 1),
                "H" => Number(date.Hour), "HH" => date.ToString("HH", CultureInfo.InvariantCulture),
                "h" => Number(date.Hour % 12 == 0 ? 12 : date.Hour % 12), "hh" => date.ToString("hh", CultureInfo.InvariantCulture),
                "m" => Number(date.Minute), "mm" => date.ToString("mm", CultureInfo.InvariantCulture),
                "s" => Number(date.Second), "ss" => date.ToString("ss", CultureInfo.InvariantCulture),
                "S" => date.ToString("%f", CultureInfo.InvariantCulture), "SS" => date.ToString("ff", CultureInfo.InvariantCulture),
                "SSS" => date.ToString("fff", CultureInfo.InvariantCulture),
                "Z" => date.ToString("zzz", CultureInfo.InvariantCulture), "ZZ" => date.ToString("zzz", CultureInfo.InvariantCulture).Replace(":", ""),
                "A" => date.Hour < 12 ? "AM" : "PM", "a" => date.Hour < 12 ? "am" : "pm",
                _ => throw new NotSupportedException("Unsupported Gantt date format."),
            });
            index += token.Length;
        }
        return output.ToString();
        static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
    }
    public static DateTimeOffset Parse(string text, string format, GanttEvaluationContext context, bool start = false)
    {
        ArgumentNullException.ThrowIfNull(context);
        text = text.Trim(); format = format.Trim();
        if (format is "X" or "x")
        {
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var numeric) || !double.IsFinite(numeric))
                throw new FormatException("Invalid Gantt timestamp.");
            var milliseconds = format == "x" || start && Regex.IsMatch(text, @"^\d+$");
            return milliseconds ? DateTimeOffset.UnixEpoch.AddMilliseconds(numeric) : DateTimeOffset.UnixEpoch.AddSeconds(numeric);
        }
        var expression = new StringBuilder("^");
        for (var index = 0; index < format.Length;)
        {
            if (format[index] == '[')
            {
                var close = format.IndexOf(']', index + 1);
                if (close < 0) throw new FormatException("Unclosed Gantt date-format literal.");
                expression.Append(Regex.Escape(format[(index + 1)..close])); index = close + 1; continue;
            }
            var token = Tokens.FirstOrDefault(token => format.AsSpan(index).StartsWith(token, StringComparison.Ordinal));
            if (token is null)
            {
                if (char.IsLetter(format[index])) throw new NotSupportedException("Unsupported Gantt date-format token near " + format[index..]);
                expression.Append(Regex.Escape(format[index++].ToString())); continue;
            }
            expression.Append(token switch
            {
                "YYYY" => @"(?<year>\d{4})", "YY" => @"(?<year2>\d{2})", "Q" => @"(?<quarter>[1-4])",
                "M" or "MM" => @"(?<month>\d{1,2})", "MMM" or "MMMM" => @"(?<monthName>[\p{L}.]+)",
                "D" or "DD" => @"(?<day>\d{1,2})", "Do" => @"(?<day>\d{1,2})(?:st|nd|rd|th)", "DDD" or "DDDD" => @"(?<dayOfYear>\d{1,3})",
                "H" or "HH" => @"(?<hour>\d{1,2})", "h" or "hh" => @"(?<hour12>\d{1,2})",
                "m" or "mm" => @"(?<minute>\d{1,2})", "s" or "ss" => @"(?<second>\d{1,2})",
                "S" => @"(?<fraction>\d)", "SS" => @"(?<fraction>\d{2})", "SSS" => @"(?<fraction>\d{3})",
                "Z" or "ZZ" => @"(?<zone>Z|[+-]\d{2}:?\d{2})", "A" or "a" => @"(?<ampm>am|pm)",
                _ => throw new NotSupportedException("Unsupported Gantt date format."),
            });
            index += token.Length;
        }
        expression.Append('$');
        var match = Regex.Match(text, expression.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success) throw new FormatException("Date '" + text + "' does not match Gantt dateFormat '" + format + "'.");
        var reference = context.ReferenceLocal;
        var year = Number("year", Number("year2", -1) is var shortYear && shortYear >= 0 ? shortYear > 68 ? 1900 + shortYear : 2000 + shortYear : reference.Year);
        var hasYear = match.Groups["year"].Success || match.Groups["year2"].Success;
        var hasMonth = match.Groups["month"].Success || match.Groups["monthName"].Success || match.Groups["quarter"].Success;
        var month = Number("month", hasYear ? 1 : reference.Month);
        if (match.Groups["quarter"].Success) month = Number("quarter", 1) * 3 - 2;
        if (match.Groups["monthName"].Success)
        {
            var names = context.DateCulture.DateTimeFormat;
            var value = match.Groups["monthName"].Value.TrimEnd('.');
            month = Enumerable.Range(1, 12).FirstOrDefault(i => new[] { names.GetMonthName(i), names.GetAbbreviatedMonthName(i) }
                .Any(name => string.Equals(name.TrimEnd('.'), value, StringComparison.OrdinalIgnoreCase)));
            if (month == 0) throw new FormatException("Unknown month name for the selected Gantt culture.");
        }
        var day = Number("day", hasYear || hasMonth ? 1 : reference.Day);
        var hour = Number("hour", 0);
        if (match.Groups["hour12"].Success)
        {
            hour = Number("hour12", 0);
            if (hour is < 1 or > 12 || !match.Groups["ampm"].Success) throw new FormatException("A 12-hour Gantt time needs a valid hour and am/pm.");
            hour = hour % 12 + (match.Groups["ampm"].Value.Equals("pm", StringComparison.OrdinalIgnoreCase) ? 12 : 0);
        }
        var fraction = match.Groups["fraction"].Success ? int.Parse(match.Groups["fraction"].Value.PadRight(3, '0'), CultureInfo.InvariantCulture) : 0;
        var local = new DateTime(year, month, day, hour, Number("minute", 0), Number("second", 0), fraction, DateTimeKind.Unspecified);
        if (match.Groups["dayOfYear"].Success)
        {
            var dayOfYear = Number("dayOfYear", 0);
            if (dayOfYear < 1 || dayOfYear > (DateTime.IsLeapYear(year) ? 366 : 365)) throw new FormatException("Invalid day of year.");
            local = new DateTime(year, 1, 1).AddDays(dayOfYear - 1).Add(local.TimeOfDay);
        }
        if (match.Groups["zone"].Success)
        {
            var zone = match.Groups["zone"].Value;
            if (zone.Equals("Z", StringComparison.OrdinalIgnoreCase)) return new(local, TimeSpan.Zero);
            var digits = zone[1..].Replace(":", "");
            var offset = new TimeSpan(int.Parse(digits[..2], CultureInfo.InvariantCulture), int.Parse(digits[2..], CultureInfo.InvariantCulture), 0);
            return new(local, zone[0] == '-' ? -offset : offset);
        }
        return InZone(local, context);
        int Number(string name, int fallback) => match.Groups[name].Success ? int.Parse(match.Groups[name].Value, CultureInfo.InvariantCulture) : fallback;
    }
    public static DateTimeOffset InZone(DateTime local, GanttEvaluationContext context)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (context.TimeZone.IsInvalidTime(local) || context.TimeZone.IsAmbiguousTime(local))
            throw new ArgumentException("The Gantt local time is skipped or ambiguous in this timezone; use an explicit offset.");
        return new(local, context.TimeZone.GetUtcOffset(local));
    }
    public static DateTimeOffset AddDuration(DateTimeOffset start, GanttEndSpec.Duration duration, GanttEvaluationContext context)
    {
        if (!double.IsFinite(duration.Amount) || duration.Amount < 0 || !Enum.IsDefined(duration.Unit))
            throw new ArgumentException("Gantt duration must be finite, nonnegative and have a known unit.");
        var local = TimeZoneInfo.ConvertTime(start, context.TimeZone).DateTime;
        return duration.Unit switch
        {
            GanttDurationUnit.Millisecond => start.AddMilliseconds(duration.Amount),
            GanttDurationUnit.Second => start.AddSeconds(duration.Amount),
            GanttDurationUnit.Minute => start.AddMinutes(duration.Amount),
            GanttDurationUnit.Hour => start.AddHours(duration.Amount),
            GanttDurationUnit.Day => InZone(local.AddDays(Math.Floor(duration.Amount + .5)), context),
            GanttDurationUnit.Week => InZone(local.AddDays(Math.Floor(duration.Amount * 7 + .5)), context),
            GanttDurationUnit.Month => InZone(local.AddMonths(checked((int)Math.Truncate(duration.Amount))), context),
            GanttDurationUnit.Year => InZone(local.AddYears(checked((int)Math.Truncate(duration.Amount))), context),
            _ => throw new ArgumentOutOfRangeException(nameof(duration)),
        };
    }
}
