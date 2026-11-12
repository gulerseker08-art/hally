using System.Globalization;
using System.Text.RegularExpressions;

namespace Hally.Domain.Common;

public record ResolvedDate(DateOnly Date, TimeOnly? Time);

/// <summary>
/// Deterministic resolver for the date phrases parents actually write (Turkish and English):
/// "yarın sabah", "haftaya cuma", "ayın 15'i", "15 Kasım", "next Friday", "on the 15th", "12.11".
/// The AI returns the phrase it read; the server resolves it here against the family's local "today",
/// so a model can never shift a date on its own. Returns null when the phrase is not understood.
/// </summary>
public static partial class RelativeDateResolver
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    private static readonly (string Stem, DayOfWeek Day)[] Weekdays =
    [
        ("pazartesi", DayOfWeek.Monday),
        ("salı", DayOfWeek.Tuesday),
        ("çarşamba", DayOfWeek.Wednesday),
        ("perşembe", DayOfWeek.Thursday),
        ("cumartesi", DayOfWeek.Saturday),
        ("cuma", DayOfWeek.Friday),
        ("pazar", DayOfWeek.Sunday),
        ("monday", DayOfWeek.Monday),
        ("tuesday", DayOfWeek.Tuesday),
        ("wednesday", DayOfWeek.Wednesday),
        ("thursday", DayOfWeek.Thursday),
        ("friday", DayOfWeek.Friday),
        ("saturday", DayOfWeek.Saturday),
        ("sunday", DayOfWeek.Sunday),
    ];

    private static readonly (string Stem, int Month)[] Months =
    [
        ("ocak", 1),
        ("şubat", 2),
        ("mart", 3),
        ("nisan", 4),
        ("mayıs", 5),
        ("haziran", 6),
        ("temmuz", 7),
        ("ağustos", 8),
        ("eylül", 9),
        ("ekim", 10),
        ("kasım", 11),
        ("aralık", 12),
        ("january", 1),
        ("february", 2),
        ("march", 3),
        ("april", 4),
        ("may", 5),
        ("june", 6),
        ("july", 7),
        ("august", 8),
        ("september", 9),
        ("october", 10),
        ("november", 11),
        ("december", 12),
        ("jan", 1),
        ("feb", 2),
        ("mar", 3),
        ("apr", 4),
        ("jun", 6),
        ("jul", 7),
        ("aug", 8),
        ("sep", 9),
        ("oct", 10),
        ("nov", 11),
        ("dec", 12),
    ];

    public static ResolvedDate? Resolve(string? phrase, DateOnly today)
    {
        if (string.IsNullOrWhiteSpace(phrase))
        {
            return null;
        }

        var text = Normalize(phrase);
        var date = ResolveDate(text, today);
        if (date is null)
        {
            return null;
        }

        // "12.11" is the date itself; do not read it again as 12:11.
        var withoutDates = NumericDate().Replace(text, m => IsNumericDate(m, today) ? " " : m.Value);
        return new ResolvedDate(date.Value, ResolveTime(withoutDates));
    }

    private static string Normalize(string phrase) =>
        Spaces().Replace(phrase.ToLower(Tr).Replace('\'', ' ').Replace('’', ' ').Replace(',', ' '), " ").Trim();

    private static DateOnly? ResolveDate(string t, DateOnly today)
    {
        // Explicit numeric date: 12.11, 12/11/2026, 2026-11-12
        if (IsoDate().Match(t) is { Success: true } iso &&
            DateOnly.TryParseExact(iso.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var isoDate))
        {
            return isoDate;
        }

        foreach (Match num in NumericDate().Matches(t))
        {
            var day = int.Parse(num.Groups["d"].Value, CultureInfo.InvariantCulture);
            var month = int.Parse(num.Groups["m"].Value, CultureInfo.InvariantCulture);
            int? year = num.Groups["y"].Success ? int.Parse(num.Groups["y"].Value, CultureInfo.InvariantCulture) : null;
            if (year < 100)
            {
                year += 2000;
            }

            if (Build(day, month, year, today) is { } numeric)
            {
                return numeric; // "14.30" is a time, not a date: keep looking
            }
        }

        // "15 kasım", "15th november", "november 15"
        foreach (var (stem, month) in Months)
        {
            var m = Regex.Match(t, $@"(?<!\p{{L}})(?:(?<d>\d{{1,2}})(?:st|nd|rd|th)?\s+{stem}\p{{L}}*|{stem}\p{{L}}*\s+(?<d>\d{{1,2}})(?:st|nd|rd|th)?)(?:\s+(?<y>\d{{4}}))?");
            if (m.Success)
            {
                int? year = m.Groups["y"].Success ? int.Parse(m.Groups["y"].Value, CultureInfo.InvariantCulture) : null;
                return Build(int.Parse(m.Groups["d"].Value, CultureInfo.InvariantCulture), month, year, today);
            }
        }

        // "ayın 15'i", "ayın 15inde", "on the 15th", "the 15th"
        if (DayOfMonth().Match(t) is { Success: true } dom)
        {
            var day = int.Parse(dom.Groups["d"].Value, CultureInfo.InvariantCulture);
            var candidate = SafeDate(today.Year, today.Month, day);
            if (candidate is null)
            {
                return null;
            }

            return candidate >= today ? candidate : SafeDate(today.AddMonths(1).Year, today.AddMonths(1).Month, day);
        }

        if (Has(t, "öbür gün", "yarından sonra", "day after tomorrow"))
        {
            return today.AddDays(2);
        }

        if (Has(t, "yarın", "tomorrow"))
        {
            return today.AddDays(1);
        }

        if (Has(t, "bugün", "today", "tonight", "bu akşam"))
        {
            return today;
        }

        foreach (var (stem, day) in Weekdays)
        {
            if (!Regex.IsMatch(t, $@"(?<!\p{{L}}){stem}"))
            {
                continue;
            }

            // Week starts on Monday in both TR and UK; "next/haftaya" means the following week.
            var mondayThisWeek = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
            var offset = ((int)day + 6) % 7;
            if (Has(t, "haftaya", "gelecek hafta", "önümüzdeki hafta", "next week", "next "))
            {
                return mondayThisWeek.AddDays(7 + offset);
            }

            if (Has(t, "bu ", "this "))
            {
                return mondayThisWeek.AddDays(offset);
            }

            var ahead = ((int)day - (int)today.DayOfWeek + 7) % 7;
            return today.AddDays(ahead);
        }

        if (Has(t, "haftaya", "next week", "gelecek hafta"))
        {
            return null; // a week without a day is ambiguous: ask instead of guessing
        }

        return null;
    }

    private static bool IsNumericDate(Match m, DateOnly today)
    {
        var day = int.Parse(m.Groups["d"].Value, CultureInfo.InvariantCulture);
        var month = int.Parse(m.Groups["m"].Value, CultureInfo.InvariantCulture);
        int? year = m.Groups["y"].Success ? int.Parse(m.Groups["y"].Value, CultureInfo.InvariantCulture) : null;
        return Build(day, month, year < 100 ? year + 2000 : year, today) is not null;
    }

    private static TimeOnly? ResolveTime(string t)
    {
        if (ClockTime().Match(t) is { Success: true } m)
        {
            var hour = int.Parse(m.Groups["h"].Value, CultureInfo.InvariantCulture);
            var minute = m.Groups["min"].Success ? int.Parse(m.Groups["min"].Value, CultureInfo.InvariantCulture) : 0;
            if (m.Groups["pm"].Success && hour < 12)
            {
                hour += 12;
            }

            if (hour is >= 0 and < 24 && minute is >= 0 and < 60)
            {
                return new TimeOnly(hour, minute);
            }
        }

        // Part-of-day words are hints, not exact times: callers may surface them as "needs info".
        return null;
    }

    private static DateOnly? Build(int day, int month, int? year, DateOnly today)
    {
        if (year is { } y)
        {
            return SafeDate(y, month, day);
        }

        var candidate = SafeDate(today.Year, month, day);
        if (candidate is null)
        {
            return null;
        }

        // A date without a year that already passed more than a month ago means next year.
        return candidate < today.AddMonths(-1) ? SafeDate(today.Year + 1, month, day) : candidate;
    }

    private static DateOnly? SafeDate(int year, int month, int day) =>
        month is >= 1 and <= 12 && day >= 1 && day <= DateTime.DaysInMonth(year, month) ? new DateOnly(year, month, day) : null;

    private static bool Has(string text, params string[] needles) =>
        needles.Any(n => text.Contains(n, StringComparison.Ordinal));

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"\b\d{4}-\d{2}-\d{2}\b")]
    private static partial Regex IsoDate();

    [GeneratedRegex(@"(?<!\d)(?<d>\d{1,2})[./](?<m>\d{1,2})(?:[./](?<y>\d{2,4}))?(?!\d)")]
    private static partial Regex NumericDate();

    [GeneratedRegex(@"(?:ayın\s+(?<d>\d{1,2})|(?:on\s+)?the\s+(?<d>\d{1,2})(?:st|nd|rd|th))")]
    private static partial Regex DayOfMonth();

    [GeneratedRegex(@"(?:saat\s*(?<h>\d{1,2})(?:[:.](?<min>\d{2}))?|(?<!\d)(?<h>\d{1,2})[:.](?<min>\d{2})(?!\d)|(?<!\d)(?<h>\d{1,2})\s*(?<pm>pm)|(?<!\d)(?<h>\d{1,2})\s*am)")]
    private static partial Regex ClockTime();
}
