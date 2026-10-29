using Microsoft.EntityFrameworkCore;
using Scheduler.Application.Common;
using Scheduler.Application.Executions;
using Scheduler.Domain.Entities;

namespace Scheduler.Infrastructure.Persistence.Queries;

internal sealed class ExecutionQueries(IDbContextFactory<SchedulerDbContext> contextFactory) : IExecutionQueries
{
    public async Task<PagedResult<ExecutionListItem>> SearchAsync(ExecutionFilter filter, PageRequest page,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = Filter(db, filter);
        var total = await query.CountAsync(cancellationToken);
        var items = await Project(db, query.OrderByDescending(e => e.StartTimeUtc).ThenByDescending(e => e.Id).Skip(page.Skip).Take(page.SafePageSize))
            .ToListAsync(cancellationToken);
        return new PagedResult<ExecutionListItem>(items, total, page.SafePage, page.SafePageSize);
    }

    public async Task<IReadOnlyList<ExecutionListItem>> ExportAsync(ExecutionFilter filter, int maxRows, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await Project(db, Filter(db, filter).OrderByDescending(e => e.StartTimeUtc).Take(maxRows)).ToListAsync(cancellationToken);
    }

    public async Task<ExecutionDetail?> GetDetailAsync(Guid executionId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var item = await Project(db, db.TaskExecutions.AsNoTracking().Where(e => e.Id == executionId)).FirstOrDefaultAsync(cancellationToken);
        if (item is null)
        {
            return null;
        }

        var extra = await db.TaskExecutions.AsNoTracking().Where(e => e.Id == executionId)
            .Select(e => new { e.QueueId, e.Output }).FirstAsync(cancellationToken);
        var errors = await db.ErrorLogs.AsNoTracking().Where(e => e.ExecutionId == executionId).OrderBy(e => e.OccurredAtUtc)
            .Select(e => new ErrorItem(e.Id, e.ErrorType, e.ErrorMessage, e.StackTrace, e.OccurredAtUtc))
            .ToListAsync(cancellationToken);
        var logs = await db.ExecutionLogs.AsNoTracking().Where(l => l.ExecutionId == executionId).OrderBy(l => l.LoggedAtUtc)
            .Take(500)
            .Select(l => new LogItem(l.Id, l.Level, l.Source, l.Message, l.LoggedAtUtc))
            .ToListAsync(cancellationToken);
        return new ExecutionDetail(item, extra.QueueId, extra.Output, errors, logs);
    }

    public async Task<PagedResult<ErrorListItem>> SearchErrorsAsync(ErrorFilter filter, PageRequest page, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = db.ErrorLogs.AsNoTracking();
        if (filter.FromUtc is { } from)
        {
            query = query.Where(e => e.OccurredAtUtc >= from);
        }

        if (filter.ToUtc is { } to)
        {
            query = query.Where(e => e.OccurredAtUtc < to);
        }

        if (filter.TaskId is { } taskId)
        {
            query = query.Where(e => e.Execution!.TaskId == taskId);
        }

        if (!string.IsNullOrWhiteSpace(filter.Text))
        {
            var pattern = LikePattern.Contains(filter.Text);
            query = query.Where(e => EF.Functions.ILike(e.ErrorMessage, pattern, LikePattern.Escape)
                                     || EF.Functions.ILike(e.ErrorType, pattern, LikePattern.Escape));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(e => e.OccurredAtUtc)
            .Skip(page.Skip).Take(page.SafePageSize)
            .Select(e => new ErrorListItem(e.Id, e.ExecutionId, e.Execution!.TaskId, e.Execution.Task!.Name, e.ErrorType, e.ErrorMessage,
                e.StackTrace, e.OccurredAtUtc, e.Execution.Status, e.Execution.RetryCount))
            .ToListAsync(cancellationToken);
        return new PagedResult<ErrorListItem>(items, total, page.SafePage, page.SafePageSize);
    }

    private static IQueryable<TaskExecution> Filter(SchedulerDbContext db, ExecutionFilter filter)
    {
        var query = db.TaskExecutions.AsNoTracking();
        if (filter.FromUtc is { } from)
        {
            query = query.Where(e => e.StartTimeUtc >= from);
        }

        if (filter.ToUtc is { } to)
        {
            query = query.Where(e => e.StartTimeUtc < to);
        }

        if (filter.TaskId is { } taskId)
        {
            query = query.Where(e => e.TaskId == taskId);
        }

        if (filter.Status is { } status)
        {
            query = query.Where(e => e.Status == status);
        }

        if (filter.TriggerType is { } trigger)
        {
            query = query.Where(e => e.TriggerType == trigger);
        }

        return query;
    }

    internal static IQueryable<ExecutionListItem> Project(SchedulerDbContext db, IQueryable<TaskExecution> query) =>
        query.Select(e => new ExecutionListItem(e.Id, e.TaskId, e.Task!.Name, e.Task.TaskType, e.TriggerType, e.Status, e.ScheduledTimeUtc,
            e.StartTimeUtc, e.EndTimeUtc, e.DurationMs, e.RetryCount,
            db.WorkerThreads.Where(w => w.Id == e.ThreadId).Select(w => w.HostName).FirstOrDefault()));
}
