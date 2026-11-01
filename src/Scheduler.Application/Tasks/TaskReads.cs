using Scheduler.Application.Common;
using Scheduler.Application.Common.Persistence;
using Scheduler.Application.Cron;
using Scheduler.Domain.Enums;

namespace Scheduler.Application.Tasks;

/// <summary>Searchable, filterable task list (FR-58).</summary>
public sealed record SearchTasksQuery(TaskSearchCriteria Criteria, PageRequest Page);

/// <summary>A task as the editor shows it.</summary>
public sealed record GetTaskQuery(Guid TaskId);

public sealed record TaskDetails(
    Guid Id,
    uint Version,
    TaskInput Input,
    ScheduledTaskStatus Status,
    bool IsDeleted,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    DateTime? NextRunUtc,
    DateTime? LastRunUtc,
    bool IsBuiltIn = false,
    int ExecutionCount = 0);

/// <summary>The registered task types for the task type drop-down (FR-06, FR-76).</summary>
public sealed record GetTaskTypesQuery;

/// <summary>Tasks for drop-downs and the dependency picker.</summary>
public sealed record GetTaskLookupsQuery(bool IncludeDeleted = false);

/// <summary>The next run times of a schedule before it is saved (FR-16).</summary>
public sealed record PreviewScheduleQuery(ScheduleInput Schedule, int Count = 5);

/// <summary>A previewed run, in UTC and as wall-clock time in the schedule's zone.</summary>
public sealed record PreviewRun(DateTime Utc, DateTime Local);

internal sealed class SearchTasksHandler(ITaskQueries queries) : IQueryHandler<SearchTasksQuery, PagedResult<TaskListItem>>
{
    public async Task<Result<PagedResult<TaskListItem>>> HandleAsync(SearchTasksQuery query, CancellationToken cancellationToken = default) =>
        Result.Success(await queries.SearchAsync(query.Criteria, query.Page, cancellationToken));
}

internal sealed class GetTaskHandler(IUnitOfWorkFactory unitOfWorkFactory) : IQueryHandler<GetTaskQuery, TaskDetails>
{
    public async Task<Result<TaskDetails>> HandleAsync(GetTaskQuery query, CancellationToken cancellationToken = default)
    {
        await using var unitOfWork = unitOfWorkFactory.Create();
        var task = await unitOfWork.Tasks.GetAsync(query.TaskId, includeDeleted: true, cancellationToken);
        if (task is null)
        {
            return Error.NotFound("Task");
        }

        var schedule = task.CurrentSchedule;
        var input = new TaskInput
        {
            Name = task.Name,
            Description = task.Description,
            TaskType = task.TaskType,
            Payload = task.Payload,
            Priority = task.Priority,
            MaxRetries = task.MaxRetries,
            TimeoutSeconds = task.TimeoutSeconds,
            MaxExecutions = task.MaxExecutions,
            Schedule = TaskWriter.ToInput(schedule),
            DependsOn = task.Dependencies.Select(d => d.DependsOnTaskId).ToList()
        };

        return Result.Success(new TaskDetails(task.Id, task.Version, input, task.Status, task.IsDeleted, task.CreatedAtUtc, task.UpdatedAtUtc,
            schedule?.NextRunTimeUtc, schedule?.LastRunTimeUtc, task.IsBuiltIn, task.ExecutionCount));
    }
}

internal sealed class GetTaskTypesHandler(ITaskTypeRegistry registry) : IQueryHandler<GetTaskTypesQuery, IReadOnlyList<TaskTypeInfo>>
{
    public Task<Result<IReadOnlyList<TaskTypeInfo>>> HandleAsync(GetTaskTypesQuery query, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success(registry.TaskTypes));
}

internal sealed class GetTaskLookupsHandler(ITaskQueries queries) : IQueryHandler<GetTaskLookupsQuery, IReadOnlyList<TaskLookup>>
{
    public async Task<Result<IReadOnlyList<TaskLookup>>> HandleAsync(GetTaskLookupsQuery query, CancellationToken cancellationToken = default) =>
        Result.Success(await queries.GetLookupsAsync(query.IncludeDeleted, cancellationToken));
}

internal sealed class PreviewScheduleValidator : IValidator<PreviewScheduleQuery>
{
    public Task<IReadOnlyList<Error>> ValidateAsync(PreviewScheduleQuery query, CancellationToken cancellationToken = default)
    {
        // The preview is shown while the user types, so a Once time in the past is not an error here.
        var errors = new ValidationErrors();
        TaskInputRules.ValidateSchedule(query.Schedule, DateTime.UtcNow, errors, requireFutureOnce: false);
        return Task.FromResult(errors.Errors);
    }
}

internal sealed class PreviewScheduleHandler(TimeProvider clock) : IQueryHandler<PreviewScheduleQuery, IReadOnlyList<PreviewRun>>
{
    public Task<Result<IReadOnlyList<PreviewRun>>> HandleAsync(PreviewScheduleQuery query, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var schedule = TaskWriter.BuildSchedule(Guid.Empty, query.Schedule, now, null);
        var spec = ScheduleSpec.From(schedule);
        var zone = spec.TimeZone;

        IReadOnlyList<PreviewRun> runs = ScheduleCalculator.Preview(spec, now, Math.Clamp(query.Count, 1, 50))
            .Where(r => r > now)
            .Select(r => new PreviewRun(r, TimeZones.FromUtc(r, zone)))
            .ToList();
        return Task.FromResult(Result.Success(runs));
    }
}
