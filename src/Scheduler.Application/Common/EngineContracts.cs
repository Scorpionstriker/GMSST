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

    /// <summary>
    /// After the scheduler database was restored while the engine was paused: registers this host's workers again,
    /// because the restored worker rows are those of the backup (WP-5g).
    /// </summary>
    Task RefreshAfterRestoreAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Makes sure only one scheduler engine runs per database (WP-1). The engine takes the lock when it starts and holds
/// it on a dedicated connection until it stops; a second engine finds the lock taken and stays Stopped.
/// </summary>
public interface IEngineInstanceLock
{
    /// <summary>Takes the lock for <paramref name="hostName"/>, or reports the host that already holds it.</summary>
    Task<EngineLockResult> TryAcquireAsync(string hostName, CancellationToken cancellationToken = default);

    /// <summary>Records a heartbeat. Returns false when the lock connection was lost, so the lock must be taken again.</summary>
    Task<bool> HeartbeatAsync(CancellationToken cancellationToken = default);

    /// <summary>Releases the lock and removes the instance row.</summary>
    Task ReleaseAsync(CancellationToken cancellationToken = default);
}

/// <param name="Acquired">True when this engine now holds the lock.</param>
/// <param name="RunningHost">The host that holds the lock when it was not acquired, if known.</param>
/// <param name="RunningSinceUtc">When that engine started, if known.</param>
public sealed record EngineLockResult(bool Acquired, string? RunningHost = null, DateTime? RunningSinceUtc = null)
{
    public static EngineLockResult Taken { get; } = new(true);

    /// <summary>The banner text shown when another engine already runs on this database.</summary>
    public string BlockedMessage =>
        $"The scheduler engine is already running on {RunningHost ?? "another computer"}. Only one scheduler may run per database.";
}

/// <summary>Used when no database lock is registered, for example in unit tests: the lock is always granted.</summary>
public sealed class NoEngineInstanceLock : IEngineInstanceLock
{
    public Task<EngineLockResult> TryAcquireAsync(string hostName, CancellationToken cancellationToken = default) =>
        Task.FromResult(EngineLockResult.Taken);

    public Task<bool> HeartbeatAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

    public Task ReleaseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
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
