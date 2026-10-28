namespace Scheduler.Extensibility.Events;

/// <summary>
/// Scheduler events for modules and for the live displays of the Admin application (FR-43, FR-56).
/// </summary>
public interface ISchedulerEvents
{
    event EventHandler<TaskLifecycleEventArgs>? TaskQueued;
    event EventHandler<TaskLifecycleEventArgs>? TaskStarted;
    event EventHandler<TaskLifecycleEventArgs>? TaskSucceeded;
    event EventHandler<TaskLifecycleEventArgs>? TaskFailed;
}

public sealed class TaskLifecycleEventArgs(Guid taskId, string taskName, string taskType, Guid? executionId, DateTime occurredAtUtc)
    : EventArgs
{
    public Guid TaskId { get; } = taskId;
    public string TaskName { get; } = taskName;
    public string TaskType { get; } = taskType;
    public Guid? ExecutionId { get; } = executionId;
    public DateTime OccurredAtUtc { get; } = occurredAtUtc;
}
