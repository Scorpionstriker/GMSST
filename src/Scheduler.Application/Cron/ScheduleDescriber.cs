using System.Globalization;
using Scheduler.Domain.Enums;

namespace Scheduler.Application.Cron;

/// <summary>Short, readable form of a schedule for the task list (Phase 4, Section 7.3.2).</summary>
public static class ScheduleDescriber
{
    public static string Describe(ScheduleType? type, string? cron, int? intervalSeconds, string? timeZoneId, DateTime? startUtc)
    {
        var zone = string.IsNullOrEmpty(timeZoneId) || timeZoneId == TimeZones.Utc ? "UTC" : timeZoneId;
        return type switch
        {
            ScheduleType.Cron => $"Cron {cron} ({zone})",
            ScheduleType.Interval => $"Every {DescribeInterval(intervalSeconds ?? 0)}",
            ScheduleType.Once when startUtc is not null =>
                $"Once at {TimeZones.FromUtc(startUtc.Value, TimeZones.FindOrUtc(timeZoneId)).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} ({zone})",
            ScheduleType.Once => "Once",
            _ => "No schedule"
        };
    }

    public static string DescribeInterval(int seconds)
    {
        if (seconds <= 0)
        {
            return "-";
        }

        if (seconds % 86_400 == 0)
        {
            return Plural(seconds / 86_400, "day");
        }

        if (seconds % 3_600 == 0)
        {
            return Plural(seconds / 3_600, "hour");
        }

        return seconds % 60 == 0 ? Plural(seconds / 60, "minute") : Plural(seconds, "second");
    }

    private static string Plural(int value, string unit) => value == 1 ? $"1 {unit}" : $"{value} {unit}s";
}
