using System.Globalization;
using System.Text.RegularExpressions;

namespace CodeCaller.Core.Mail;

/// <summary>Reads the dates found in mail headers, which come in more shapes than the RFC allows.</summary>
public static class MailDate
{
    static readonly Regex Comment = new(@"\([^()]*\)", RegexOptions.CultureInvariant);

    // "Tue, 27 Oct 2026 09:59:58 -0700", "27 Oct 26 9:59 GMT", "Tue, 27 Oct 2026 16:59:58 +0000 (UTC)"
    static readonly Regex Rfc5322 = new(
        @"(?:[A-Za-z]{3,9},?\s*)?(?<day>\d{1,2})[\s-]+(?<month>[A-Za-z]{3})[A-Za-z]*\.?[\s-]+(?<year>\d{2,4})\s+(?<hour>\d{1,2}):(?<minute>\d{2})(?::(?<second>\d{2}))?(?:\.\d+)?\s*(?<zone>[+-]\d{4}|[+-]\d{2}:\d{2}|[A-Za-z]{1,5})?",
        RegexOptions.CultureInvariant);

    // The asctime() form in mbox "From " lines: "Tue Oct 27 09:59:58 2026"
    static readonly Regex AscTime = new(
        @"[A-Za-z]{3}\s+(?<month>[A-Za-z]{3})\s+(?<day>\d{1,2})\s+(?<hour>\d{1,2}):(?<minute>\d{2}):(?<second>\d{2})\s+(?<year>\d{4})",
        RegexOptions.CultureInvariant);

    static readonly string[] Months = ["jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"];

    /// <summary>Reads an RFC 5322 date, as found in Date and Received headers.</summary>
    public static DateTimeOffset? Parse(string text)
    {
        var match = Rfc5322.Match(Comment.Replace(text, " "));
        if (!match.Success) return null;
        var year = Number(match, "year");
        // RFC 5322 section 4.3: two-digit years below 50 are 20xx, three-digit years add 1900.
        year = match.Groups["year"].Length switch
        {
            2 => year < 50 ? 2000 + year : 1900 + year,
            3 => 1900 + year,
            _ => year,
        };
        var offset = Zone(match.Groups["zone"].Value);
        return Build(year, match, offset);
    }

    /// <summary>Reads the date in an mbox "From " line, which is in the computer's local time.</summary>
    public static DateTimeOffset? ParseEnvelope(string line, TimeZoneInfo zone)
    {
        var match = AscTime.Match(line);
        if (!match.Success) return null;
        var month = Month(match.Groups["month"].Value);
        if (month < 0) return null;
        try
        {
            var local = new DateTime(Number(match, "year"), month, Number(match, "day"), Number(match, "hour"), Number(match, "minute"), Number(match, "second"), DateTimeKind.Unspecified);
            return new DateTimeOffset(local, zone.GetUtcOffset(local));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    static DateTimeOffset? Build(int year, Match match, TimeSpan offset)
    {
        var month = Month(match.Groups["month"].Value);
        if (month < 0) return null;
        try
        {
            var second = match.Groups["second"].Success ? Number(match, "second") : 0;
            // A leap second, or a sloppy "60", still means the same minute.
            second = Math.Min(second, 59);
            return new DateTimeOffset(year, month, Number(match, "day"), Number(match, "hour"), Number(match, "minute"), second, offset);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    static int Number(Match match, string group) => int.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture);

    static int Month(string name)
    {
        var index = Array.IndexOf(Months, name.ToLowerInvariant());
        return index < 0 ? -1 : index + 1;
    }

    static TimeSpan Zone(string zone)
    {
        if (zone.Length == 0) return TimeSpan.Zero;
        if (zone[0] is '+' or '-')
        {
            var digits = zone.Replace(":", "", StringComparison.Ordinal);
            var hours = int.Parse(digits.AsSpan(1, 2), CultureInfo.InvariantCulture);
            var minutes = int.Parse(digits.AsSpan(3, 2), CultureInfo.InvariantCulture);
            if (hours > 14 || minutes > 59) return TimeSpan.Zero;
            var span = new TimeSpan(hours, minutes, 0);
            return zone[0] == '-' ? -span : span;
        }
        return zone.ToUpperInvariant() switch
        {
            "EDT" => TimeSpan.FromHours(-4),
            "EST" or "CDT" => TimeSpan.FromHours(-5),
            "CST" or "MDT" => TimeSpan.FromHours(-6),
            "MST" or "PDT" => TimeSpan.FromHours(-7),
            "PST" => TimeSpan.FromHours(-8),
            // UT, GMT, Z and anything unknown, which RFC 5322 says to treat as UTC.
            _ => TimeSpan.Zero,
        };
    }
}
