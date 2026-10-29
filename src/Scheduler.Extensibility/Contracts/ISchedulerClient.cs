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
    /// Creates the task with its schedule unless a task with the same name already exists (deleted tasks
    /// included, so a task the administrator deleted is not recreated). Returns the task's id.
    /// </summary>
    Task<Guid> EnsureTaskAsync(TaskDefinition definition, CancellationToken cancellationToken = default);

    /// <summary>Queues an immediate run of the named task. Returns false when no runnable task has that name.</summary>
    Task<bool> RunNowAsync(string taskName, CancellationToken cancellationToken = default);
}

/// <summary>A task and its schedule, as created through <see cref="ISchedulerClient"/>.</summary>
public sealed record TaskDefinition
{
    public required string Name { get; init; }
    public required string TaskType { get; init; }
    public string? Description { get; init; }
    public string Payload { get; init; } = "{}";
    public int Priority { get; init; } = 5;
    public int? MaxRetries { get; init; }
    public int TimeoutSeconds { get; init; } = 300;

    public ScheduleType ScheduleType { get; init; } = ScheduleType.Cron;
    public string? CronExpression { get; init; }
    public int? IntervalSeconds { get; init; }
    public string TimeZoneId { get; init; } = "UTC";
    public MisfirePolicy MisfirePolicy { get; init; } = MisfirePolicy.RunOnce;

    /// <summary>Start of the schedule window; defaults to now.</summary>
    public DateTime? StartTimeUtc { get; init; }
}
