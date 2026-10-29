using Microsoft.EntityFrameworkCore;
using Scheduler.Application.Common;
using Scheduler.Application.Queue;
using Scheduler.Domain.Enums;

namespace Scheduler.Infrastructure.Persistence.Queries;

internal sealed class QueueQueries(IDbContextFactory<SchedulerDbContext> contextFactory) : IQueueQueries
{
    public async Task<PagedResult<QueueListItem>> GetEntriesAsync(IReadOnlyCollection<QueueStatus> statuses, PageRequest page,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var wanted = statuses.ToList();
        var query = db.TaskQueue.AsNoTracking().Where(q => wanted.Contains(q.Status));
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(q => q.Status == QueueStatus.Claimed ? 0 : 1)
            .ThenBy(q => q.Priority)
            .ThenBy(q => q.ScheduledTimeUtc)
            .Skip(page.Skip)
            .Take(page.SafePageSize)
            .Select(q => new QueueListItem(q.Id, q.TaskId, q.Task!.Name, q.Task.TaskType, q.TriggerType, q.Priority, q.ScheduledTimeUtc,
                q.EnqueuedAtUtc, q.Status,
                db.WorkerThreads.Where(w => w.Id == q.ClaimedBy).Select(w => w.HostName).FirstOrDefault(),
                q.ClaimedUntilUtc, q.Attempt))
            .ToListAsync(cancellationToken);
        return new PagedResult<QueueListItem>(items, total, page.SafePage, page.SafePageSize);
    }

    public async Task<QueueCounts> GetCountsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var counts = await db.TaskQueue.AsNoTracking()
            .Where(q => q.Status == QueueStatus.Waiting || q.Status == QueueStatus.Claimed)
            .GroupBy(q => q.Status)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        return new QueueCounts(
            counts.FirstOrDefault(c => c.Key == QueueStatus.Waiting)?.Count ?? 0,
            counts.FirstOrDefault(c => c.Key == QueueStatus.Claimed)?.Count ?? 0);
    }
}
