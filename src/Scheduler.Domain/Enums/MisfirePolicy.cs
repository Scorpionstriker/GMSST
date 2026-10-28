namespace Scheduler.Domain.Enums;

/// <summary>What to do with runs missed while the system was not running (FR-15).</summary>
public enum MisfirePolicy
{
    Skip = 1,
    RunOnce = 2,
    RunAll = 3
}
