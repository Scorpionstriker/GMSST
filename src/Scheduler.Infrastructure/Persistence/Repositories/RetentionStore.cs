using Microsoft.EntityFrameworkCore;
using Scheduler.Application.Common;
using Scheduler.Application.Common.Persistence;
using Scheduler.Domain.Enums;

namespace Scheduler.Infrastructure.Persistence.Repositories;

/// <summary>Bulk deletes for the Log Clean-up task (FR-35, Phase 4 Section 5.7), in one transaction.</summary>
internal sealed class RetentionStore(IDbContextFactory<SchedulerDbContext> contextFactory) : IRetentionStore
{
    public async Task<RetentionResult> PurgeAsync(DateTime queueBeforeUtc, DateTime historyBeforeUtc, DateTime auditBeforeUtc,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // An execution keeps its task, trigger, scheduled time and attempt, so removing its queue entry sets
        // task_execution.queue_id to null and the history stays complete (BR-04, WP-3a).
        var queue = await db.TaskQueue
            .Where(q => (q.Status == QueueStatus.Processed || q.Status == QueueStatus.Cancelled) && q.EnqueuedAtUtc < queueBeforeUtc)
            .ExecuteDeleteAsync(cancellationToken);

        // Security entries are audit records and follow AuditRetentionDays instead (WP-3c).
        var logs = await db.ExecutionLogs.Where(l => l.LoggedAtUtc < historyBeforeUtc && l.Source != LogSources.Security)
            .ExecuteDeleteAsync(cancellationToken);

        // Only finished executions are removed; their errors and log entries go with them (ON DELETE CASCADE).
        var oldExecutions = db.TaskExecutions.Where(e => e.StartTimeUtc < historyBeforeUtc && e.EndTimeUtc != null);
        var errors = await db.ErrorLogs.Where(x => oldExecutions.Any(e => e.Id == x.ExecutionId)).ExecuteDeleteAsync(cancellationToken);
        logs += await db.ExecutionLogs.Where(l => oldExecutions.Any(e => e.Id == l.ExecutionId)).ExecuteDeleteAsync(cancellationToken);
        var executions = await oldExecutions.ExecuteDeleteAsync(cancellationToken);

        var audit = await db.LoginAudits.Where(a => a.AttemptedAtUtc < auditBeforeUtc).ExecuteDeleteAsync(cancellationToken);
        audit += await db.ExecutionLogs.Where(l => l.Source == LogSources.Security && l.LoggedAtUtc < auditBeforeUtc)
            .ExecuteDeleteAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return new RetentionResult(queue, executions, errors, logs, audit);
    }
}
