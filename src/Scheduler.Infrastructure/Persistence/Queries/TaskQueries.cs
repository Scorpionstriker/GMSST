using Microsoft.EntityFrameworkCore;
using Scheduler.Application.Common;
using Scheduler.Application.Tasks;
using Scheduler.Domain.Enums;

namespace Scheduler.Infrastructure.Persistence.Queries;

internal sealed class TaskQueries(IDbContextFactory<SchedulerDbContext> contextFactory) : ITaskQueries
{
    public async Task<PagedResult<TaskListItem>> SearchAsync(TaskSearchCriteria criteria, PageRequest page,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var tasks = db.Tasks.AsNoTracking().Where(t => criteria.ShowDeleted ? t.IsDeleted : !t.IsDeleted);
        if (!string.IsNullOrWhiteSpace(criteria.Text))
        {
            var pattern = LikePattern.Contains(criteria.Text);
            tasks = tasks.Where(t => EF.Functions.ILike(t.Name, pattern, LikePattern.Escape)
                                     || (t.Description != null && EF.Functions.ILike(t.Description, pattern, LikePattern.Escape)));
        }

        if (criteria.Status is { } status)
        {
            tasks = tasks.Where(t => t.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(criteria.TaskType))
        {
            tasks = tasks.Where(t => t.TaskType == criteria.TaskType);
        }

        var rows = tasks.Select(t => new
        {
            Task = t,
            Schedule = t.Schedules.Where(s => !s.IsDeleted).OrderByDescending(s => s.CreatedAtUtc).FirstOrDefault(),
            Last = db.TaskExecutions.Where(e => e.TaskId == t.Id).OrderByDescending(e => e.StartTimeUtc).FirstOrDefault()
        });

        var d = criteria.Descending;
        var ordered = criteria.SortBy switch
        {
            TaskSortColumn.TaskType => d ? rows.OrderByDescending(r => r.Task.TaskType) : rows.OrderBy(r => r.Task.TaskType),
            TaskSortColumn.Status => d ? rows.OrderByDescending(r => r.Task.Status) : rows.OrderBy(r => r.Task.Status),
            TaskSortColumn.NextRun => d ? rows.OrderByDescending(r => r.Schedule!.NextRunTimeUtc) : rows.OrderBy(r => r.Schedule!.NextRunTimeUtc),
            TaskSortColumn.LastRun => d ? rows.OrderByDescending(r => r.Last!.StartTimeUtc) : rows.OrderBy(r => r.Last!.StartTimeUtc),
            TaskSortColumn.Priority => d ? rows.OrderByDescending(r => r.Task.Priority) : rows.OrderBy(r => r.Task.Priority),
            _ => d ? rows.OrderByDescending(r => r.Task.Name) : rows.OrderBy(r => r.Task.Name)
        };

        var total = await tasks.CountAsync(cancellationToken);
        var items = await ordered
            .ThenBy(r => r.Task.Id)
            .Skip(page.Skip)
            .Take(page.SafePageSize)
            .Select(r => new TaskListItem(
                r.Task.Id, r.Task.Name, r.Task.Description, r.Task.TaskType, r.Task.Status, r.Task.IsDeleted, r.Task.Priority,
                r.Schedule == null ? null : r.Schedule.Type,
                r.Schedule == null ? null : r.Schedule.CronExpression,
                r.Schedule == null ? null : r.Schedule.IntervalSeconds,
                r.Schedule == null ? null : r.Schedule.TimeZoneId,
                r.Schedule == null ? null : r.Schedule.StartTimeUtc,
                r.Schedule == null || !r.Schedule.IsActive ? null : r.Schedule.NextRunTimeUtc,
                r.Last == null ? null : r.Last.Status,
                r.Last == null ? null : r.Last.StartTimeUtc))
            .ToListAsync(cancellationToken);

        return new PagedResult<TaskListItem>(items, total, page.SafePage, page.SafePageSize);
    }

    public async Task<IReadOnlyList<TaskLookup>> GetLookupsAsync(bool includeDeleted, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Tasks.AsNoTracking()
            .Where(t => includeDeleted || !t.IsDeleted)
            .OrderBy(t => t.Name)
            .Select(t => new TaskLookup(t.Id, t.Name, t.TaskType, t.Status, t.IsDeleted))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ActiveScheduleRow>> GetActiveSchedulesAsync(Guid? taskId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Schedules.AsNoTracking()
            .Where(s => s.IsActive && !s.IsDeleted && !s.Task!.IsDeleted && s.Task.Status == ScheduledTaskStatus.Active
                        && (taskId == null || s.TaskId == taskId))
            .OrderBy(s => s.NextRunTimeUtc)
            .Select(s => new ActiveScheduleRow(s.TaskId, s.Task!.Name, s.Task.TaskType, s.Type, s.CronExpression, s.IntervalSeconds,
                s.TimeZoneId, s.StartTimeUtc, s.EndTimeUtc, s.NextRunTimeUtc, s.LastRunTimeUtc))
            .ToListAsync(cancellationToken);
    }
}
