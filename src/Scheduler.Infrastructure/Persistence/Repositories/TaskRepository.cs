using Microsoft.EntityFrameworkCore;
using Npgsql;
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

    public async Task<IReadOnlyList<ScheduledTask>> GetActiveDependantsAsync(Guid prerequisiteTaskId, CancellationToken cancellationToken = default) =>
        await context.Tasks
            .Where(t => !t.IsDeleted && t.Status == ScheduledTaskStatus.Active
                        && context.TaskDependencies.Any(d => d.TaskId == t.Id && d.DependsOnTaskId == prerequisiteTaskId))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Schedule>> LockDueSchedulesAsync(DateTime nowUtc, int limit, CancellationToken cancellationToken = default)
    {
        // Not composed further by EF, so the row locks apply exactly to the rows returned.
        var schedules = await context.Schedules
            .FromSqlRaw(
                """
                SELECT s.* FROM scheduler.schedule s
                JOIN scheduler.task t ON t.task_id = s.task_id
                WHERE s.is_active AND NOT s.is_deleted AND s.next_run_time <= @now
                  AND t.status = 'Active' AND NOT t.is_deleted
                ORDER BY s.next_run_time
                LIMIT @limit
                FOR UPDATE OF s SKIP LOCKED
                """,
                new NpgsqlParameter("now", nowUtc),
                new NpgsqlParameter("limit", limit))
            .ToListAsync(cancellationToken);

        if (schedules.Count > 0)
        {
            // Loads the owning tasks so Schedule.Task is set by EF's relationship fix-up.
            var taskIds = schedules.Select(s => s.TaskId).Distinct().ToList();
            await context.Tasks.Where(t => taskIds.Contains(t.Id)).LoadAsync(cancellationToken);
        }

        return schedules;
    }

    public Task<DateTime?> GetEarliestNextRunAsync(CancellationToken cancellationToken = default) =>
        context.Schedules
            .Where(s => s.IsActive && !s.IsDeleted && s.NextRunTimeUtc != null && s.Task!.Status == ScheduledTaskStatus.Active && !s.Task.IsDeleted)
            .MinAsync(s => s.NextRunTimeUtc, cancellationToken);
}
