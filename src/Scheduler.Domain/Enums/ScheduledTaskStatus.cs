namespace Scheduler.Domain.Enums;

/// <summary>Status of a task definition. Only Active tasks are scheduled (FR-03).</summary>
public enum ScheduledTaskStatus
{
    Active = 1,
    Paused = 2,
    Disabled = 3
}
