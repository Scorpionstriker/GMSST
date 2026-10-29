using Scheduler.Extensibility.Events;

namespace Scheduler.Application.Common;

/// <summary>
/// Wakes the scheduling engine after a task is created, changed or run manually (FR-18). Implemented by
/// the engine; when no engine is registered, a no-op is used.
/// </summary>
public interface IEngineSignal
{
    void Wake();
}

/// <summary>Lets use cases pause and resume the engine, for example around a restore (FR-72).</summary>
public interface IEngineControl
{
    EngineState State { get; }

    /// <summary>Stops claiming new work and waits up to the shutdown grace period for running executions.</summary>
    Task PauseAsync(CancellationToken cancellationToken = default);

    /// <summary>Resumes scheduling, running start-up recovery first.</summary>
    Task ResumeAsync(CancellationToken cancellationToken = default);
}

/// <summary>Publishes scheduler events to subscribers of <see cref="ISchedulerEvents"/> (FR-43).</summary>
public interface ISchedulerEventPublisher
{
    void Publish(SchedulerEvent schedulerEvent);
}

/// <summary>Kinds of events the engine and the use cases publish.</summary>
public enum SchedulerEventKind
{
    TaskQueued,
    TaskStarted,
    TaskSucceeded,
    TaskFailed,
    EngineStateChanged
}

public sealed record SchedulerEvent(SchedulerEventKind Kind, EventArgs Args);

internal sealed class NullEngineSignal : IEngineSignal
{
    public void Wake()
    {
    }
}

internal sealed class NullEventPublisher : ISchedulerEventPublisher
{
    public void Publish(SchedulerEvent schedulerEvent)
    {
    }
}
