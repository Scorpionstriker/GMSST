using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using Scheduler.Application.Common.Persistence;
using Scheduler.Domain.Entities;
using Scheduler.Domain.Enums;

namespace Scheduler.Infrastructure.Persistence.Repositories;

internal sealed class QueueRepository(SchedulerDbContext context) : IQueueRepository
{
    // The claim query of Phase 4, Section 5.5: one short statement that locks the best eligible row,
    // skipping rows other workers have locked, and records the lease on it (FR-20).
    private const string ClaimSql =
        """
        UPDATE scheduler.task_queue q
        SET queue_status = 'Claimed', claimed_by = @workerId,
            claimed_at = @now, claimed_until = @now + make_interval(secs => t.timeout_seconds + @graceSeconds)
        FROM scheduler.task t
        WHERE q.queue_id = (
            SELECT q2.queue_id
            FROM scheduler.task_queue q2
            JOIN scheduler.task t2 ON t2.task_id = q2.task_id
            WHERE q2.queue_status = 'Waiting'
              AND q2.scheduled_time <= @now
              AND t2.task_type = ANY(@registeredTypes)
            ORDER BY q2.priority, q2.scheduled_time
            LIMIT 1
            FOR UPDATE OF q2 SKIP LOCKED)
          AND t.task_id = q.task_id
        RETURNING q.queue_id;
        """;

    public void Add(QueueEntry entry) => context.TaskQueue.Add(entry);

    public Task<QueueEntry?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.TaskQueue.Include(q => q.Task).FirstOrDefaultAsync(q => q.Id == id, cancellationToken);

    public async Task<Guid?> ClaimNextAsync(Guid workerId, IReadOnlyCollection<string> taskTypes, DateTime nowUtc, int leaseGraceSeconds,
        CancellationToken cancellationToken = default)
    {
        if (taskTypes.Count == 0)
        {
            return null;
        }

        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        var opened = connection.State != System.Data.ConnectionState.Open;
        if (opened)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = new NpgsqlCommand(ClaimSql, connection);
            if (context.Database.CurrentTransaction?.GetDbTransaction() is NpgsqlTransaction transaction)
            {
                command.Transaction = transaction;
            }

            command.Parameters.AddWithValue("workerId", workerId);
            command.Parameters.AddWithValue("now", nowUtc);
            command.Parameters.AddWithValue("graceSeconds", (double)leaseGraceSeconds);
            command.Parameters.Add(new NpgsqlParameter("registeredTypes", NpgsqlDbType.Array | NpgsqlDbType.Varchar) { Value = taskTypes.ToArray() });
            var result = await command.ExecuteScalarAsync(cancellationToken);
            return result is Guid id ? id : null;
        }
        finally
        {
            if (opened)
            {
                await connection.CloseAsync();
            }
        }
    }

    public async Task<IReadOnlyList<QueueEntry>> LockExpiredLeasesAsync(DateTime nowUtc, int limit, CancellationToken cancellationToken = default) =>
        await context.TaskQueue
            .FromSqlRaw(
                """
                SELECT * FROM scheduler.task_queue
                WHERE queue_status = 'Claimed' AND claimed_until < @now
                ORDER BY claimed_until
                LIMIT @limit
                FOR UPDATE SKIP LOCKED
                """,
                new NpgsqlParameter("now", nowUtc),
                new NpgsqlParameter("limit", limit))
            .ToListAsync(cancellationToken);

    public Task<DateTime?> GetEarliestWaitingAsync(IReadOnlyCollection<string> taskTypes, CancellationToken cancellationToken = default)
    {
        var types = taskTypes.ToList();
        return context.TaskQueue
            .Where(q => q.Status == QueueStatus.Waiting && types.Contains(q.Task!.TaskType))
            .MinAsync(q => (DateTime?)q.ScheduledTimeUtc, cancellationToken);
    }

    public async Task<IReadOnlyList<QueueEntry>> GetWaitingForTaskAsync(Guid taskId, CancellationToken cancellationToken = default) =>
        await context.TaskQueue.Where(q => q.TaskId == taskId && q.Status == QueueStatus.Waiting).ToListAsync(cancellationToken);
}
