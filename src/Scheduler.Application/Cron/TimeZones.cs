namespace Scheduler.Application.Cron;

/// <summary>
/// IANA time zone handling (FR-14). Schedules store IANA ids such as "Africa/Johannesburg" so the same
/// database works on Windows, macOS and Linux; .NET converts to and from Windows ids where needed.
/// </summary>
public static class TimeZones
{
    public const string Utc = "UTC";

    /// <summary>Finds a zone by IANA (or Windows) id.</summary>
    public static bool TryFind(string? id, out TimeZoneInfo zone)
    {
        zone = TimeZoneInfo.Utc;
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        if (string.Equals(id, Utc, StringComparison.OrdinalIgnoreCase) || string.Equals(id, "Etc/UTC", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            if (TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out var windowsId))
            {
                try
                {
                    zone = TimeZoneInfo.FindSystemTimeZoneById(windowsId);
                    return true;
                }
                catch (Exception inner) when (inner is TimeZoneNotFoundException or InvalidTimeZoneException)
                {
                }
            }

            return false;
        }
    }

    /// <summary>Returns the zone, or UTC when the id is unknown.</summary>
    public static TimeZoneInfo FindOrUtc(string? id) => TryFind(id, out var zone) ? zone : TimeZoneInfo.Utc;

    /// <summary>The IANA id of this computer's zone, for the default of new schedules.</summary>
    public static string LocalId()
    {
        var local = TimeZoneInfo.Local;
        var id = local.HasIanaId ? local.Id : TimeZoneInfo.TryConvertWindowsIdToIanaId(local.Id, out var iana) ? iana : null;

        // Zones such as "Etc/UTC" are not offered in the drop-down, so they map to UTC.
        return id is null || !id.Contains('/') || id.StartsWith("Etc/", StringComparison.Ordinal) ? Utc : id;
    }

    /// <summary>Every known IANA zone with its current UTC offset, for the time zone drop-down (FR-76).</summary>
    public static IReadOnlyList<TimeZoneOption> All(DateTime nowUtc)
    {
        var options = new Dictionary<string, TimeZoneOption>(StringComparer.OrdinalIgnoreCase)
        {
            [Utc] = new(Utc, "(UTC+00:00) UTC", TimeSpan.Zero)
        };

        foreach (var zone in TimeZoneInfo.GetSystemTimeZones())
        {
            string? iana = zone.HasIanaId ? zone.Id : TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out var converted) ? converted : null;
            if (iana is null || !iana.Contains('/') || iana.StartsWith("Etc/", StringComparison.Ordinal))
            {
                continue;
            }

            var offset = zone.GetUtcOffset(nowUtc);
            var sign = offset < TimeSpan.Zero ? "-" : "+";
            options.TryAdd(iana, new TimeZoneOption(iana, $"(UTC{sign}{offset:hh\\:mm}) {iana.Replace('_', ' ')}", offset));
        }

        return options.Values.OrderBy(o => o.Offset).ThenBy(o => o.Id, StringComparer.Ordinal).ToList();
    }

    /// <summary>Converts a wall-clock time in <paramref name="zone"/> to UTC, moving past a clock-change gap.</summary>
    public static DateTime ToUtc(DateTime localTime, TimeZoneInfo zone)
    {
        var unspecified = DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(unspecified))
        {
            unspecified = unspecified.AddMinutes(1);
        }

        return DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeToUtc(unspecified, zone), DateTimeKind.Utc);
    }

    /// <summary>Converts a UTC instant to wall-clock time in <paramref name="zone"/>.</summary>
    public static DateTime FromUtc(DateTime utc, TimeZoneInfo zone) =>
        DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone), DateTimeKind.Unspecified);
}

public sealed record TimeZoneOption(string Id, string Label, TimeSpan Offset);
