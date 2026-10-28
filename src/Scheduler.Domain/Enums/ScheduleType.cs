namespace Scheduler.Domain.Enums;

/// <summary>How a schedule decides when it is due (FR-09, FR-10, FR-11).</summary>
public enum ScheduleType
{
    Cron = 1,
    Interval = 2,
    Once = 3
}
