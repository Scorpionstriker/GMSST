namespace Scheduler.Domain.Enums;

/// <summary>Status of a task queue entry (FR-19).</summary>
public enum QueueStatus
{
    Waiting = 1,
    Claimed = 2,
    Processed = 3,
    Cancelled = 4
}
