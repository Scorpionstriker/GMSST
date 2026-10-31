using System.Globalization;

namespace Scheduler.Application.Cron;

/// <summary>
/// A standard five-field cron expression: minute, hour, day of month, month and day of week (FR-09).
/// Supports wildcards (*), lists (1,15), ranges (1-5), steps (*/10, 0-30/5), month and weekday names
/// (JAN, MON) and 7 for Sunday. Hand-written so the template needs no scheduling library (NFR-13).
/// </summary>
/// <remarks>
/// Fixes carried over from the reference analysis: values are range-checked with a specific message for
/// each field, a step of zero is rejected instead of looping forever, and the next occurrence is
/// calculated in the schedule's own time zone with unspecified-kind local times, so non-UTC zones work.
/// </remarks>
public sealed class CronExpression
{
    private static readonly FieldSpec[] Fields =
    [
        new("minute", 0, 59, null),
        new("hour", 0, 23, null),
        new("day-of-month", 1, 31, null),
        new("month", 1, 12, ["JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC"]),
        new("day-of-week", 0, 7, ["SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT"])
    ];

    private readonly bool[] _minutes;
    private readonly bool[] _hours;
    private readonly bool[] _days;
    private readonly bool[] _months;
    private readonly bool[] _weekdays;

    private CronExpression(string text, bool[][] sets, bool dayStartsWithStar, bool weekdayStartsWithStar)
    {
        Text = text;
        _minutes = sets[0];
        _hours = sets[1];
        _days = sets[2];
        _months = sets[3];
        _weekdays = sets[4];
        DayStartsWithStar = dayStartsWithStar;
        WeekdayStartsWithStar = weekdayStartsWithStar;
    }

    /// <summary>The expression as entered, with single spaces between fields.</summary>
    public string Text { get; }

    /// <summary>The day-of-month field starts with *, for example "*" or "*/2" (WP-8g).</summary>
    public bool DayStartsWithStar { get; }

    /// <summary>The day-of-week field starts with *, for example "*" or "*/2" (WP-8g).</summary>
    public bool WeekdayStartsWithStar { get; }

    /// <summary>Parses <paramref name="text"/>; throws <see cref="FormatException"/> with a specific message when it is invalid.</summary>
    public static CronExpression Parse(string text) =>
        TryParse(text, out var expression, out var error) ? expression! : throw new FormatException(error);

    /// <summary>Parses <paramref name="text"/> (Algorithm 6.6). On failure, <paramref name="error"/> names the field and the bad value.</summary>
    public static bool TryParse(string? text, out CronExpression? expression, out string? error)
    {
        expression = null;
        error = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Enter a cron expression with five fields: minute hour day-of-month month day-of-week.";
            return false;
        }

        var parts = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 5)
        {
            error = $"A cron expression must have five fields (minute hour day-of-month month day-of-week); this one has {parts.Length}.";
            return false;
        }

        var sets = new bool[5][];
        for (var i = 0; i < 5; i++)
        {
            if (!TryParseField(parts[i], Fields[i], out sets[i], out error))
            {
                return false;
            }
        }

        // 7 is another name for Sunday.
        if (sets[4][7])
        {
            sets[4][0] = true;
            sets[4][7] = false;
        }

        expression = new CronExpression(string.Join(' ', parts), sets, parts[2].StartsWith('*'), parts[4].StartsWith('*'));
        return true;
    }

    /// <summary>
    /// The first occurrence strictly after <paramref name="afterUtc"/>, calculated in <paramref name="timeZone"/>
    /// and returned in UTC (Algorithm 6.7). Returns null when the expression never matches within five years,
    /// for example "0 0 30 2 *".
    /// </summary>
    public DateTime? GetNextOccurrence(DateTime afterUtc, TimeZoneInfo timeZone)
    {
        var after = DateTime.SpecifyKind(afterUtc, DateTimeKind.Utc);

        // Local wall-clock time in the schedule's zone. Kind must be Unspecified: ConvertTimeToUtc rejects a
        // Utc-kind value for a non-UTC zone, which is the bug that broke every non-UTC schedule in the reference.
        var local = DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(after, timeZone), DateTimeKind.Unspecified);
        var t = new DateTime(local.Year, local.Month, local.Day, local.Hour, local.Minute, 0, DateTimeKind.Unspecified).AddMinutes(1);
        var limit = t.AddYears(5);

        while (t < limit)
        {
            if (!_months[t.Month])
            {
                t = new DateTime(t.Year, t.Month, 1, 0, 0, 0, DateTimeKind.Unspecified).AddMonths(1);
                continue;
            }

            if (!DayMatches(t))
            {
                t = t.Date.AddDays(1);
                continue;
            }

            if (!_hours[t.Hour])
            {
                t = t.Date.AddHours(t.Hour + 1);
                continue;
            }

            var minute = NextSet(_minutes, t.Minute);
            if (minute < 0)
            {
                t = t.Date.AddHours(t.Hour + 1);
                continue;
            }

            t = t.Date.AddHours(t.Hour).AddMinutes(minute);

            // WP-8g: a local time that does not exist because the clocks went forward runs at the first valid minute
            // after the gap (02:30 on a night when 02:00 becomes 03:00 runs at 03:00), so the run is not lost.
            var wallClock = t;
            while (timeZone.IsInvalidTime(wallClock))
            {
                wallClock = wallClock.AddMinutes(1);
            }

            var utc = TimeZoneInfo.ConvertTimeToUtc(wallClock, timeZone);
            if (utc <= after)
            {
                // A repeated local hour after the clocks went back maps to an earlier instant; move on.
                t = t.AddMinutes(1);
                continue;
            }

            return DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        }

        return null;
    }

    /// <summary>
    /// Standard cron rule: when both day fields are restricted, a day matches if either matches. A field that starts
    /// with * (including "*/2") counts as unrestricted for this rule, so the other field and its own step both apply
    /// (WP-8g, as in Vixie cron).
    /// </summary>
    private bool DayMatches(DateTime t)
    {
        var dayOk = _days[t.Day];
        var weekdayOk = _weekdays[(int)t.DayOfWeek];
        return DayStartsWithStar || WeekdayStartsWithStar ? dayOk && weekdayOk : dayOk || weekdayOk;
    }

    private static int NextSet(bool[] set, int from)
    {
        for (var i = from; i < set.Length; i++)
        {
            if (set[i])
            {
                return i;
            }
        }

        return -1;
    }

    private static bool TryParseField(string field, FieldSpec spec, out bool[] set, out string? error)
    {
        set = new bool[spec.Max + 1];
        error = null;

        foreach (var part in field.Split(','))
        {
            if (part.Length == 0)
            {
                error = $"The {spec.Name} field has an empty list item in '{field}'.";
                return false;
            }

            var rangePart = part;
            var step = 1;
            var slash = part.IndexOf('/');
            if (slash >= 0)
            {
                var stepText = part[(slash + 1)..];
                if (!int.TryParse(stepText, NumberStyles.None, CultureInfo.InvariantCulture, out step) || step < 1)
                {
                    error = $"Invalid step '{stepText}' in the {spec.Name} field: a step must be a whole number of at least 1.";
                    return false;
                }

                rangePart = part[..slash];
            }

            int low;
            int high;
            if (rangePart == "*")
            {
                low = spec.Min;
                high = spec.Name == "day-of-week" ? 6 : spec.Max;
            }
            else
            {
                var dash = rangePart.IndexOf('-');
                if (dash > 0)
                {
                    if (!TryValue(rangePart[..dash], spec, out low, out error) || !TryValue(rangePart[(dash + 1)..], spec, out high, out error))
                    {
                        return false;
                    }

                    if (low > high)
                    {
                        error = $"Invalid range '{rangePart}' in the {spec.Name} field: the start must not be after the end.";
                        return false;
                    }
                }
                else
                {
                    if (!TryValue(rangePart, spec, out low, out error))
                    {
                        return false;
                    }

                    // "5/15" means from 5 to the end of the field in steps of 15.
                    high = slash >= 0 ? (spec.Name == "day-of-week" ? 6 : spec.Max) : low;
                }
            }

            for (var value = low; value <= high; value += step)
            {
                set[value] = true;
            }
        }

        return true;
    }

    private static bool TryValue(string text, FieldSpec spec, out int value, out string? error)
    {
        error = null;
        if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value))
        {
            if (value < spec.Min || value > spec.Max)
            {
                error = $"Invalid value '{text}' in the {spec.Name} field (allowed {spec.Min}-{spec.Max}).";
                return false;
            }

            return true;
        }

        if (spec.Names is not null)
        {
            var index = Array.FindIndex(spec.Names, n => string.Equals(n, text, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                value = spec.Name == "month" ? index + 1 : index;
                return true;
            }
        }

        error = $"Invalid value '{text}' in the {spec.Name} field (allowed {spec.Min}-{spec.Max}{(spec.Names is null ? "" : " or names such as " + spec.Names[1])}).";
        return false;
    }

    public override string ToString() => Text;

    private sealed record FieldSpec(string Name, int Min, int Max, string[]? Names);
}
