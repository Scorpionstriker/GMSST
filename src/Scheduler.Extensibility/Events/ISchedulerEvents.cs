using Scheduler.Domain.Enums;

namespace Scheduler.Extensibility.Events;

/// <summary>
/// Scheduler events for modules and for the live displays of the Admin application (FR-43, FR-56).
/// Events are raised on a background thread, one at a time and never on the engine's own thread, so a
/// slow subscriber cannot hold up scheduling. Blazor components should call InvokeAsync(StateHasChanged)
/// and unsubscribe in Dispose.
/// </summary>
public interface ISchedulerEvents
{
    event EventHandler<TaskLifecycleEventArgs>? TaskQueued;
    event EventHandler<TaskLifecycleEventArgs>? TaskStarted;
    event EventHandler<TaskLifecycleEventArgs>? TaskSucceeded;
    event EventHandler<TaskLifecycleEventArgs>? TaskFailed;

    /// <summary>Raised when the engine starts, pauses, resumes or stops (FR-57).</summary>
    event EventHandler<EngineStateEventArgs>? EngineStateChanged;
}

public sealed class TaskLifecycleEventArgs(
    Guid taskId,
    string taskName,
    string taskType,
    Guid? executionId,
    DateTime occurredAtUtc,
    ExecutionStatus? status = null,
    string? message = null) : EventArgs
{
    public Guid TaskId { get; } = taskId;
    public string TaskName { get; } = taskName;
    public string TaskType { get; } = taskType;
    public Guid? ExecutionId { get; } = executionId;
    public DateTime OccurredAtUtc { get; } = occurredAtUtc;

    /// <summary>The execution status for Started, Succeeded and Failed events.</summary>
    public ExecutionStatus? Status { get; } = status;

    /// <summary>The handler's message or the error message.</summary>
    public string? Message { get; } = message;
}

/// <summary>State of a host's scheduling engine.</summary>
public enum EngineState
{
    Stopped = 0,
    Running = 1,
    Paused = 2
}

public sealed class EngineStateEventArgs(EngineState state) : EventArgs
{
    public EngineState State { get; } = state;
}
