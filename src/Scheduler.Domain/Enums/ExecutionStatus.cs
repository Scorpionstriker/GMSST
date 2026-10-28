namespace Scheduler.Domain.Enums;

/// <summary>Status of a single execution attempt (FR-23).</summary>
public enum ExecutionStatus
{
    Running = 1,
    Success = 2,
    Failed = 3,
    Retrying = 4,
    TimedOut = 5,
    Cancelled = 6
}
