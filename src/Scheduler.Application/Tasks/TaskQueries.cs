using Scheduler.Application.Common;
using Scheduler.Domain.Enums;

namespace Scheduler.Application.Tasks;

/// <summary>Read-only task queries, executed in the database by the infrastructure layer (NFR-11).</summary>
public interface ITaskQueries
{
    Task<PagedResult<TaskListItem>> SearchAsync(TaskSearchCriteria criteria, PageRequest page, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TaskLookup>> GetLookupsAsync(bool includeDeleted, CancellationToken cancellationToken = default);

    /// <summary>Active schedules of active tasks, for the Upcoming schedule report.</summary>
    Task<IReadOnlyList<ActiveScheduleRow>> GetActiveSchedulesAsync(Guid? taskId, CancellationToken cancellationToken = default);
}

public sealed record TaskSearchCriteria(
    string? Text = null,
    ScheduledTaskStatus? Status = null,
    string? TaskType = null,
    bool ShowDeleted = false,
    TaskSortColumn SortBy = TaskSortColumn.Name,
    bool Descending = false);

public enum TaskSortColumn
{
    Name,
    TaskType,
    Status,
    NextRun,
    LastRun,
    Priority
}

public sealed record TaskListItem(
    Guid Id,
    string Name,
    string? Description,
    string TaskType,
    ScheduledTaskStatus Status,
    bool IsDeleted,
    int Priority,
    ScheduleType? ScheduleType,
    string? CronExpression,
    int? IntervalSeconds,
    string? TimeZoneId,
    DateTime? StartTimeUtc,
    DateTime? NextRunUtc,
    ExecutionStatus? LastRunStatus,
    DateTime? LastRunStartUtc,
    bool IsBuiltIn = false,
    int ExecutionCount = 0,
    int? MaxExecutions = null);

public sealed record TaskLookup(Guid Id, string Name, string TaskType, ScheduledTaskStatus Status, bool IsDeleted);

public sealed record ActiveScheduleRow(
    Guid TaskId,
    string TaskName,
    string TaskType,
    ScheduleType Type,
    string? CronExpression,
    int? IntervalSeconds,
    string TimeZoneId,
    DateTime StartTimeUtc,
    DateTime? EndTimeUtc,
    DateTime? NextRunUtc,
    DateTime? LastRunUtc);
