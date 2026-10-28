namespace Scheduler.Domain.Enums;

/// <summary>Status of a worker in the bounded worker pool (FR-21).</summary>
public enum WorkerStatus
{
    Idle = 1,
    Busy = 2,
    Stopped = 3
}
