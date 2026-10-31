namespace Scheduler.Extensibility.Contracts;

/// <summary>
/// Defines a task type. The <see cref="TaskType"/> name is what a task stores, so modules add
/// new task types without changing the core (FR-06, FR-40).
/// </summary>
/// <remarks>
/// Return <see cref="TaskResult.Failure"/> for expected problems, such as a missing payload value, so the
/// run is recorded as failed rather than silently succeeding. Unexpected exceptions are caught by the
/// engine and recorded with their stack trace (FR-30, FR-32). Honour <see cref="ITaskContext.CancellationToken"/>:
/// it is cancelled when the task's time-out expires or the host shuts down.
/// <para>
/// Delivery is at-least-once (NFR-01): work is never lost, but after a crash, or when the result of a finished run
/// could not be saved, the same work may run again. Write handlers so that running them twice does no harm.
/// <see cref="ITaskContext.ExecutionId"/> identifies one attempt and can be stored with the handler's own data as an
/// idempotency key, as the Inventory Monitor's stock report does; together with <see cref="ITaskContext.RetryNumber"/>
/// it tells a handler which attempt it is. A run queued again by recovery gets a new execution id, so work that must
/// never repeat should also check its own data, for example "has today's report already been written?".
/// </para>
/// </remarks>
public interface ITaskHandler
{
    /// <summary>Unique task type name, for example "LogEntry" or "LowStockCheck".</summary>
    string TaskType { get; }

    /// <summary>Friendly name for the task type drop-down. Defaults to <see cref="TaskType"/>.</summary>
    string DisplayName => TaskType;

    /// <summary>One sentence shown under the task type in the task editor.</summary>
    string? Description => null;

    /// <summary>Example JSON payload shown in the task editor, so users know what the handler reads.</summary>
    string? PayloadExample => null;

    Task<TaskResult> ExecuteAsync(ITaskContext context);
}
