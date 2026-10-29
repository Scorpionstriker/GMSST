using Scheduler.Domain.Entities;
using Scheduler.Domain.Enums;

namespace Scheduler.Application.Cron;

/// <summary>The parts of a schedule the calculator needs, with the cron expression and time zone already resolved.</summary>
public sealed record ScheduleSpec(
    ScheduleType Type,
    CronExpression? Cron,
    int? IntervalSeconds,
    TimeZoneInfo TimeZone,
    DateTime StartUtc,
    DateTime? EndUtc,
    DateTime? LastRunUtc)
{
    /// <summary>Builds the spec of a stored schedule. Throws <see cref="FormatException"/> if its cron expression is invalid.</summary>
    public static ScheduleSpec From(Schedule schedule) => new(
        schedule.Type,
        schedule.Type == ScheduleType.Cron ? CronExpression.Parse(schedule.CronExpression ?? string.Empty) : null,
        schedule.IntervalSeconds,
        TimeZones.FindOrUtc(schedule.TimeZoneId),
        schedule.StartTimeUtc,
        schedule.EndTimeUtc,
        schedule.LastRunTimeUtc);
}
