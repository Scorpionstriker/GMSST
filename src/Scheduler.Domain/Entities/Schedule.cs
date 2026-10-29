using Scheduler.Domain.Enums;

namespace Scheduler.Domain.Entities;

/// <summary>
/// When a task runs: a cron expression, a fixed interval or a single run, interpreted in an IANA time
/// zone, within a start and optional end window (FR-09 to FR-15). All times are stored in UTC (BR-02).
/// </summary>
public sealed class Schedule : AuditableEntity
{
    public const int CronMaxLength = 100;
    public const int TimeZoneMaxLength = 64;
    public const string DefaultTimeZone = "UTC";

    private Schedule()
    {
        TimeZoneId = DefaultTimeZone;
    }

    private Schedule(Guid taskId, ScheduleType type, string? cronExpression, int? intervalSeconds, string timeZoneId,
        MisfirePolicy misfirePolicy, DateTime startTimeUtc, DateTime? endTimeUtc, DateTime nowUtc, Guid? userId)
    {
        TaskId = taskId;
        Type = type;
        CronExpression = type == ScheduleType.Cron ? cronExpression?.Trim() : null;
        IntervalSeconds = type == ScheduleType.Interval ? intervalSeconds : null;
        TimeZoneId = string.IsNullOrWhiteSpace(timeZoneId) ? DefaultTimeZone : timeZoneId;
        MisfirePolicy = misfirePolicy;
        StartTimeUtc = DateTime.SpecifyKind(startTimeUtc, DateTimeKind.Utc);
        EndTimeUtc = endTimeUtc is null ? null : DateTime.SpecifyKind(endTimeUtc.Value, DateTimeKind.Utc);
        IsActive = true;
        MarkCreated(nowUtc, userId);
    }

    public static Schedule Create(Guid taskId, ScheduleType type, string? cronExpression, int? intervalSeconds,
        string timeZoneId, MisfirePolicy misfirePolicy, DateTime startTimeUtc, DateTime? endTimeUtc,
        DateTime nowUtc, Guid? userId) =>
        new(taskId, type, cronExpression, intervalSeconds, timeZoneId, misfirePolicy, startTimeUtc, endTimeUtc, nowUtc, userId);

    public Guid TaskId { get; private set; }
    public ScheduledTask? Task { get; private set; }

    public ScheduleType Type { get; private set; }
    public string? CronExpression { get; private set; }
    public int? IntervalSeconds { get; private set; }
    public string TimeZoneId { get; private set; }
    public MisfirePolicy MisfirePolicy { get; private set; }

    /// <summary>Start of the active window; the run time of a Once schedule.</summary>
    public DateTime StartTimeUtc { get; private set; }

    public DateTime? EndTimeUtc { get; private set; }

    /// <summary>Next calculated run in UTC; null when no further run will happen (FR-13).</summary>
    public DateTime? NextRunTimeUtc { get; private set; }

    /// <summary>The most recent scheduled run time that was queued.</summary>
    public DateTime? LastRunTimeUtc { get; private set; }

    public bool IsActive { get; private set; }

    public void SetNextRun(DateTime? nextRunUtc) =>
        NextRunTimeUtc = nextRunUtc is null ? null : DateTime.SpecifyKind(nextRunUtc.Value, DateTimeKind.Utc);

    /// <summary>
    /// Records that the schedule became due and moves it on (Algorithm 6.3). A Once schedule, or a
    /// schedule with no further run, becomes inactive.
    /// </summary>
    public void Advance(DateTime dueRunUtc, DateTime? nextRunUtc)
    {
        LastRunTimeUtc = DateTime.SpecifyKind(dueRunUtc, DateTimeKind.Utc);
        SetNextRun(Type == ScheduleType.Once ? null : nextRunUtc);
        if (NextRunTimeUtc is null)
        {
            IsActive = false;
        }
    }

    /// <summary>Takes the schedule out of use when it is replaced (FR-17).</summary>
    internal void Retire(DateTime nowUtc, Guid? userId)
    {
        IsActive = false;
        IsDeleted = true;
        NextRunTimeUtc = null;
        MarkUpdated(nowUtc, userId);
    }
}
