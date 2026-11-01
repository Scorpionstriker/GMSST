using Scheduler.Domain.Entities;
using Scheduler.Domain.Enums;

namespace Scheduler.Application.Tasks;

/// <summary>The fields of the task editor (Table 6.1). Defaults let a task be created from its name, type and schedule (NFR-04).</summary>
public sealed record TaskInput
{
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string TaskType { get; init; } = string.Empty;
    public string Payload { get; init; } = ScheduledTask.EmptyPayload;
    public int Priority { get; init; } = ScheduledTask.DefaultPriority;

    /// <summary>Null uses the global MaxRetryLimit.</summary>
    public int? MaxRetries { get; init; }

    public int TimeoutSeconds { get; init; } = ScheduledTask.DefaultTimeoutSeconds;

    /// <summary>Maximum number of started executions; null means no limit.</summary>
    public int? MaxExecutions { get; init; }
    public ScheduleInput Schedule { get; init; } = new();

    /// <summary>Tasks that must succeed before this one is queued (FR-26).</summary>
    public IReadOnlyList<Guid> DependsOn { get; init; } = [];
}

/// <summary>
/// The schedule part of the editor. Start and end are wall-clock times in the selected time zone, which is
/// how the administrator thinks about them; they are converted to UTC before they are stored (FR-14).
/// </summary>
public sealed record ScheduleInput
{
    public ScheduleType Type { get; init; } = ScheduleType.Cron;
    public string? CronExpression { get; init; }
    public int? IntervalSeconds { get; init; }
    public string TimeZoneId { get; init; } = Domain.Entities.Schedule.DefaultTimeZone;
    public MisfirePolicy MisfirePolicy { get; init; } = MisfirePolicy.RunOnce;
    public DateTime? StartTime { get; init; }
    public DateTime? EndTime { get; init; }
}

/// <summary>Form field names used in validation errors, so each message appears beneath its field.</summary>
public static class TaskFields
{
    public const string Name = nameof(TaskInput.Name);
    public const string Description = nameof(TaskInput.Description);
    public const string TaskType = nameof(TaskInput.TaskType);
    public const string Priority = nameof(TaskInput.Priority);
    public const string MaxRetries = nameof(TaskInput.MaxRetries);
    public const string TimeoutSeconds = nameof(TaskInput.TimeoutSeconds);
    public const string MaxExecutions = nameof(TaskInput.MaxExecutions);
    public const string Payload = nameof(TaskInput.Payload);
    public const string ScheduleType = "Schedule.Type";
    public const string CronExpression = "Schedule.CronExpression";
    public const string IntervalSeconds = "Schedule.IntervalSeconds";
    public const string TimeZoneId = "Schedule.TimeZoneId";
    public const string StartTime = "Schedule.StartTime";
    public const string EndTime = "Schedule.EndTime";
    public const string MisfirePolicy = "Schedule.MisfirePolicy";
    public const string DependsOn = nameof(TaskInput.DependsOn);
}
