using Scheduler.Domain.Enums;

namespace Scheduler.Extensibility.Contracts;

/// <summary>
/// In-process integration point (FR-42): lets a module or an application that embeds the scheduler
/// create its tasks and trigger runs without depending on the core's internals. There is deliberately no
/// network API; an adopting developer can expose one by calling this interface.
/// </summary>
public interface ISchedulerClient
{
    /// <summary>
    /// Creates the task with its schedule unless it already exists, and returns the task's id. With a
    /// <see cref="TaskDefinition.SystemKey"/> the task is found by that key, so the administrator may rename it; an
    /// older task with the same name and type is taken over and given the key. Without a key the task is found by
    /// name (deleted tasks included, so a task the administrator deleted is not recreated).
    /// </summary>
    Task<Guid> EnsureTaskAsync(TaskDefinition definition, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queues an event-triggered run for the task with this stable system key. Event data must be a JSON object no
    /// larger than 64 KiB; the task's configured payload remains unchanged and is supplied separately to the handler.
    /// Returns false when no active task has the key.
    /// </summary>
    Task<bool> TriggerEventAsync(string taskSystemKey, string eventName, string eventSource, string eventDataJson,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Queues an immediate run of the task with this system key or, if none has it, this name. Returns false when no
    /// runnable task matches.
    /// </summary>
    Task<bool> RunNowAsync(string taskNameOrSystemKey, CancellationToken cancellationToken = default);

    /// <summary>The status of the task with this system key or, if none has it, this name; null when there is no such task.</summary>
    Task<ScheduledTaskStatus?> GetTaskStatusAsync(string taskNameOrSystemKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pauses (<paramref name="active"/> false) or resumes the task with this system key or, if none has it, this
    /// name. A disabled task is left disabled. Returns false when no task matches or the change is not allowed.
    /// </summary>
    Task<bool> SetTaskActiveAsync(string taskNameOrSystemKey, bool active, CancellationToken cancellationToken = default);
}

/// <summary>A task and its schedule, as created through <see cref="ISchedulerClient"/>.</summary>
public sealed record TaskDefinition
{
    public required string Name { get; init; }
    public required string TaskType { get; init; }

    /// <summary>
    /// Optional stable key, for example "builtin.log-cleanup" or "maintenance.health-check" (WP-8h). A task with a
    /// key is built in: it can be paused, edited and renamed but not deleted.
    /// </summary>
    public string? SystemKey { get; init; }
    public string? Description { get; init; }
    public string Payload { get; init; } = "{}";
    public int Priority { get; init; } = 5;
    public int? MaxRetries { get; init; }
    public int TimeoutSeconds { get; init; } = 300;
    public int? MaxExecutions { get; init; }

    public ScheduleType ScheduleType { get; init; } = ScheduleType.Cron;
    public string? CronExpression { get; init; }
    public int? IntervalSeconds { get; init; }
    public string TimeZoneId { get; init; } = "UTC";
    public MisfirePolicy MisfirePolicy { get; init; } = MisfirePolicy.RunOnce;

    /// <summary>Start of the schedule window; defaults to now.</summary>
    public DateTime? StartTimeUtc { get; init; }
}
