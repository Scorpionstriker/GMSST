using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using Scheduler.Application.Common.Persistence;
using Scheduler.Application.Dependencies;
using Scheduler.Domain.Entities;
using Scheduler.Domain.Enums;

namespace Scheduler.Infrastructure.Persistence.Repositories;

internal sealed class TaskRepository(SchedulerDbContext context) : ITaskRepository
{
    public Task<ScheduledTask?> GetAsync(Guid id, bool includeDeleted = false, CancellationToken cancellationToken = default) =>
        context.Tasks
            .Include(t => t.Schedules)
            .Include(t => t.Dependencies)
            .AsSplitQuery()
            .FirstOrDefaultAsync(t => t.Id == id && (includeDeleted || !t.IsDeleted), cancellationToken);

    public Task<ScheduledTask?> FindByNameAsync(string name, bool includeDeleted, CancellationToken cancellationToken = default)
    {
        var lowered = name.Trim().ToLowerInvariant();
        return context.Tasks
            .Include(t => t.Schedules)
            .Include(t => t.Dependencies)
            .AsSplitQuery()
            .Where(t => t.Name.ToLower() == lowered && (includeDeleted || !t.IsDeleted))
            .OrderBy(t => t.IsDeleted)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<bool> NameExistsAsync(string name, Guid? exceptTaskId, CancellationToken cancellationToken = default)
    {
        var lowered = name.Trim().ToLowerInvariant();
        return context.Tasks.AnyAsync(t => !t.IsDeleted && t.Name.ToLower() == lowered && t.Id != exceptTaskId, cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> GetExistingIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var list = ids.ToList();
        return await context.Tasks.Where(t => !t.IsDeleted && list.Contains(t.Id)).Select(t => t.Id).ToListAsync(cancellationToken);
    }

    public void Add(ScheduledTask task) => context.Tasks.Add(task);

    public void SetOriginalVersion(ScheduledTask task, uint version) =>
        context.Entry(task).Property(t => t.Version).OriginalValue = version;

    public async Task<IReadOnlyList<DependencyEdge>> GetDependencyEdgesAsync(CancellationToken cancellationToken = default) =>
        await (from d in context.TaskDependencies
               join t in context.Tasks on d.TaskId equals t.Id
               join p in context.Tasks on d.DependsOnTaskId equals p.Id
               where !t.IsDeleted && !p.IsDeleted
               select new DependencyEdge(d.TaskId, d.DependsOnTaskId)).ToListAsync(cancellationToken);

    public Task<ScheduledTask?> FindBySystemKeyAsync(string systemKey, CancellationToken cancellationToken = default) =>
        context.Tasks.Include(t => t.Schedules).FirstOrDefaultAsync(t => t.SystemKey == systemKey, cancellationToken);

    public async Task<IReadOnlyList<ScheduledTask>> GetActiveDependantsAsync(Guid prerequisiteTaskId, CancellationToken cancellationToken = default) =>
        await context.Tasks
            .Where(t => !t.IsDeleted && t.Status == ScheduledTaskStatus.Active
                        && context.TaskDependencies.Any(d => d.TaskId == t.Id && d.DependsOnTaskId == prerequisiteTaskId))
            .ToListAsync(cancellationToken);

    public async Task<bool> AllPrerequisitesSucceededAsync(Guid dependantId, Guid succeededTaskId, CancellationToken cancellationToken = default)
    {
        // The last time the dependant was triggered by its dependencies, from the queue or, after the queue was
        // cleaned up, from its history.
        var lastQueued = await context.TaskQueue
            .Where(q => q.TaskId == dependantId && q.TriggerType == TriggerType.Dependency)
            .MaxAsync(q => (DateTime?)q.EnqueuedAtUtc, cancellationToken);
        var lastRun = await context.TaskExecutions
            .Where(e => e.TaskId == dependantId && e.TriggerType == TriggerType.Dependency)
            .MaxAsync(e => (DateTime?)e.ScheduledTimeUtc, cancellationToken);
        var since = lastQueued > lastRun || lastRun is null ? lastQueued : lastRun;

        var others = await context.TaskDependencies
            .Where(d => d.TaskId == dependantId && d.DependsOnTaskId != succeededTaskId)
            .Where(d => context.Tasks.Any(t => t.Id == d.DependsOnTaskId && !t.IsDeleted))
            .Select(d => d.DependsOnTaskId)
            .ToListAsync(cancellationToken);

        foreach (var prerequisite in others)
        {
            var latestFinal = await context.TaskExecutions
                .Where(e => e.TaskId == prerequisite && e.EndTimeUtc != null && (since == null || e.EndTimeUtc > since)
                            && (e.Status == ExecutionStatus.Success || e.Status == ExecutionStatus.Failed || e.Status == ExecutionStatus.TimedOut))
                .OrderByDescending(e => e.EndTimeUtc)
                .Select(e => (ExecutionStatus?)e.Status)
                .FirstOrDefaultAsync(cancellationToken);
            if (latestFinal != ExecutionStatus.Success)
            {
                return false;
            }
        }

        return true;
    }

    public async Task<IReadOnlyList<Schedule>> LockDueSchedulesAsync(DateTime nowUtc, int limit, IReadOnlyCollection<string> taskTypes,
        CancellationToken cancellationToken = default)
    {
        if (taskTypes.Count == 0)
        {
            return [];
        }

        // Not composed further by EF, so the row locks apply exactly to the rows returned.
        var schedules = await context.Schedules
            .FromSqlRaw(
                """
                SELECT s.* FROM scheduler.schedule s
                JOIN scheduler.task t ON t.task_id = s.task_id
                WHERE s.is_active AND NOT s.is_deleted AND s.next_run_time <= @now
                  AND t.status = 'Active' AND NOT t.is_deleted
                  AND t.task_type = ANY(@registeredTypes)
                ORDER BY s.next_run_time
                LIMIT @limit
                FOR UPDATE OF s SKIP LOCKED
                """,
                new NpgsqlParameter("now", nowUtc),
                new NpgsqlParameter("limit", limit),
                new NpgsqlParameter("registeredTypes", NpgsqlDbType.Array | NpgsqlDbType.Varchar) { Value = taskTypes.ToArray() })
            .ToListAsync(cancellationToken);

        if (schedules.Count > 0)
        {
            // Loads the owning tasks so Schedule.Task is set by EF's relationship fix-up.
            var taskIds = schedules.Select(s => s.TaskId).Distinct().ToList();
            await context.Tasks.Where(t => taskIds.Contains(t.Id)).LoadAsync(cancellationToken);
        }

        return schedules;
    }

    public Task<DateTime?> GetEarliestNextRunAsync(IReadOnlyCollection<string> taskTypes, CancellationToken cancellationToken = default)
    {
        var types = taskTypes.ToList();
        return context.Schedules
            .Where(s => s.IsActive && !s.IsDeleted && s.NextRunTimeUtc != null && s.Task!.Status == ScheduledTaskStatus.Active && !s.Task.IsDeleted
                && types.Contains(s.Task.TaskType))
            .MinAsync(s => s.NextRunTimeUtc, cancellationToken);
    }
}
