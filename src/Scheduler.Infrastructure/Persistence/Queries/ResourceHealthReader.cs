using Microsoft.EntityFrameworkCore;
using Scheduler.Application.Common.Persistence;
using Scheduler.Domain.Enums;

namespace Scheduler.Infrastructure.Persistence.Queries;

internal sealed class ResourceHealthReader(IDbContextFactory<SchedulerDbContext> contextFactory) : IResourceHealthReader
{
    public async Task<ResourceHealthSnapshot> GetSnapshotAsync(DateTime executionsSinceUtc, DateTime staleBeforeUtc,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var queueCounts = await db.TaskQueue.AsNoTracking()
            .GroupBy(entry => entry.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);
        var workerRows = await db.WorkerThreads.AsNoTracking()
            .Select(worker => new { worker.Status, worker.LastHeartbeatUtc })
            .ToListAsync(cancellationToken);
        var failures = await db.TaskExecutions.AsNoTracking()
            .Where(execution => execution.StartTimeUtc >= executionsSinceUtc
                && (execution.Status == ExecutionStatus.Failed || execution.Status == ExecutionStatus.Retrying))
            .GroupBy(execution => new { execution.TaskId, TaskName = execution.Task!.Name })
            .Select(group => new { group.Key.TaskId, group.Key.TaskName, Count = group.Count() })
            .ToListAsync(cancellationToken);
        var timeouts = await db.ErrorLogs.AsNoTracking()
            .Where(error => error.OccurredAtUtc >= executionsSinceUtc && error.ErrorType == "TimeoutException")
            .Join(db.TaskExecutions.AsNoTracking(),
                error => error.ExecutionId,
                execution => execution.Id,
                (error, execution) => new { error, execution })
            .Where(row => row.execution.StartTimeUtc >= executionsSinceUtc)
            .GroupBy(row => new { row.execution.TaskId, TaskName = row.execution.Task!.Name })
            .Select(group => new { group.Key.TaskId, group.Key.TaskName, Count = group.Count() })
            .ToListAsync(cancellationToken);
        var recentTaskExecutions = failures.Select(failure =>
        {
            var timeoutCount = timeouts.FirstOrDefault(timeout => timeout.TaskId == failure.TaskId)?.Count ?? 0;
            return new TaskExecutionHealth(failure.TaskId, failure.TaskName, failure.Count, timeoutCount);
        }).Concat(timeouts.Where(timeout => failures.All(failure => failure.TaskId != timeout.TaskId))
            .Select(timeout => new TaskExecutionHealth(timeout.TaskId, timeout.TaskName, 0, timeout.Count)))
            .ToList();

        return new ResourceHealthSnapshot(
            queueCounts.Where(group => group.Status == QueueStatus.Waiting).Select(group => group.Count).SingleOrDefault(),
            queueCounts.Where(group => group.Status == QueueStatus.Claimed).Select(group => group.Count).SingleOrDefault(),
            workerRows.Count(worker => worker.Status == WorkerStatus.Busy),
            workerRows.Count(worker => worker.Status != WorkerStatus.Stopped),
            workerRows.Count(worker => worker.LastHeartbeatUtc < staleBeforeUtc && worker.Status != WorkerStatus.Stopped),
            recentTaskExecutions);
    }
}
