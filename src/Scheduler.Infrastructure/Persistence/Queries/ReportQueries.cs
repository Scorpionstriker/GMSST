using Microsoft.EntityFrameworkCore;
using Scheduler.Application.Reports;
using Scheduler.Domain.Entities;
using Scheduler.Domain.Enums;

namespace Scheduler.Infrastructure.Persistence.Queries;

/// <summary>Dashboard and report aggregates, grouped and counted inside PostgreSQL (NFR-11).</summary>
internal sealed class ReportQueries(IDbContextFactory<SchedulerDbContext> contextFactory) : IReportQueries
{
    public async Task<DashboardData> GetDashboardAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        var next = await db.Schedules.AsNoTracking()
            .Where(s => s.IsActive && !s.IsDeleted && s.NextRunTimeUtc != null && s.Task!.Status == ScheduledTaskStatus.Active && !s.Task.IsDeleted)
            .OrderBy(s => s.NextRunTimeUtc)
            .Select(s => new { s.Task!.Name, s.NextRunTimeUtc })
            .FirstOrDefaultAsync(cancellationToken);

        var taskCounts = await db.Tasks.AsNoTracking().Where(t => !t.IsDeleted)
            .GroupBy(t => t.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken);

        var dayStart = nowUtc.AddHours(-24);
        var lastDay = db.TaskExecutions.AsNoTracking().Where(e => e.StartTimeUtc >= dayStart);
        var succeeded = await lastDay.CountAsync(e => e.Status == ExecutionStatus.Success, cancellationToken);
        var failed = await lastDay.CountAsync(e => e.Status == ExecutionStatus.Failed || e.Status == ExecutionStatus.TimedOut
                                                   || e.Status == ExecutionStatus.Retrying, cancellationToken);
        var average = await lastDay.Where(e => e.DurationMs != null).AverageAsync(e => (double?)e.DurationMs, cancellationToken);

        var workers = await db.WorkerThreads.AsNoTracking().Where(w => w.Status != WorkerStatus.Stopped)
            .GroupBy(w => w.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
        var waiting = await db.TaskQueue.AsNoTracking().CountAsync(q => q.Status == QueueStatus.Waiting, cancellationToken);

        var firstHour = new DateTime(nowUtc.Year, nowUtc.Month, nowUtc.Day, nowUtc.Hour, 0, 0, DateTimeKind.Utc).AddHours(-23);
        var hourly = await db.TaskExecutions.AsNoTracking()
            .Where(e => e.StartTimeUtc >= firstHour)
            .GroupBy(e => new { e.StartTimeUtc.Year, e.StartTimeUtc.Month, e.StartTimeUtc.Day, e.StartTimeUtc.Hour })
            .Select(g => new
            {
                g.Key,
                Succeeded = g.Count(e => e.Status == ExecutionStatus.Success),
                Failed = g.Count(e => e.Status == ExecutionStatus.Failed || e.Status == ExecutionStatus.TimedOut || e.Status == ExecutionStatus.Retrying),
                Average = g.Average(e => (double?)e.DurationMs)
            })
            .ToListAsync(cancellationToken);
        var buckets = Enumerable.Range(0, 24).Select(i =>
        {
            var hour = firstHour.AddHours(i);
            var row = hourly.FirstOrDefault(h => h.Key.Year == hour.Year && h.Key.Month == hour.Month && h.Key.Day == hour.Day && h.Key.Hour == hour.Hour);
            return new HourBucket(hour, row?.Succeeded ?? 0, row?.Failed ?? 0, row?.Average);
        }).ToList();

        var recent = await ExecutionQueries.Project(db, db.TaskExecutions.AsNoTracking().OrderByDescending(e => e.StartTimeUtc).Take(10))
            .ToListAsync(cancellationToken);

        int TaskCount(ScheduledTaskStatus status) => taskCounts.FirstOrDefault(c => c.Key == status)?.Count ?? 0;
        return new DashboardData(
            next?.Name,
            next?.NextRunTimeUtc,
            taskCounts.Sum(c => c.Count),
            TaskCount(ScheduledTaskStatus.Active),
            TaskCount(ScheduledTaskStatus.Paused),
            succeeded,
            failed,
            average,
            workers.FirstOrDefault(w => w.Key == WorkerStatus.Busy)?.Count ?? 0,
            workers.Sum(w => w.Count),
            waiting,
            buckets,
            recent);
    }

    public async Task<IReadOnlyList<ExecutionSummaryRow>> GetExecutionSummaryAsync(ReportFilter filter, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await Filter(db, filter)
            .GroupBy(e => new { e.TaskId, e.Task!.Name })
            .Select(g => new ExecutionSummaryRow(
                g.Key.TaskId,
                g.Key.Name,
                g.Count(),
                g.Count(e => e.Status == ExecutionStatus.Success),
                g.Count(e => e.Status == ExecutionStatus.Failed),
                g.Count(e => e.Status == ExecutionStatus.TimedOut),
                g.Count(e => e.Status == ExecutionStatus.Cancelled),
                g.Count(e => e.Status == ExecutionStatus.Retrying)))
            .ToListAsync(cancellationToken);
        return rows.OrderBy(r => r.TaskName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<IReadOnlyList<FailureRow>> GetFailuresAsync(ReportFilter filter, int maxRows, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var failures = Filter(db, filter with { Status = null })
            .Where(e => e.Status == ExecutionStatus.Failed || e.Status == ExecutionStatus.TimedOut || e.Status == ExecutionStatus.Retrying);
        if (filter.Status is { } status)
        {
            failures = failures.Where(e => e.Status == status);
        }

        return await failures
            .OrderByDescending(e => e.StartTimeUtc)
            .Take(maxRows)
            .Select(e => new
            {
                e.Id,
                e.Task!.Name,
                e.StartTimeUtc,
                e.Status,
                e.RetryCount,
                Error = db.ErrorLogs.Where(x => x.ExecutionId == e.Id).OrderBy(x => x.OccurredAtUtc).Select(x => new { x.ErrorType, x.ErrorMessage }).FirstOrDefault()
            })
            .Select(r => new FailureRow(r.Id, r.Name, r.StartTimeUtc, r.Status, r.RetryCount,
                r.Error == null ? null : r.Error.ErrorType, r.Error == null ? null : r.Error.ErrorMessage))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PerformanceRow>> GetPerformanceAsync(ReportFilter filter, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await Filter(db, filter)
            .Where(e => e.DurationMs != null)
            .GroupBy(e => new { e.TaskId, e.Task!.Name })
            .Select(g => new
            {
                g.Key.TaskId,
                g.Key.Name,
                Runs = g.Count(),
                Average = g.Average(e => (double)e.DurationMs!.Value),
                Minimum = g.Min(e => e.DurationMs!.Value),
                Maximum = g.Max(e => e.DurationMs!.Value),
                Delay = g.Average(e => (e.StartTimeUtc - e.ScheduledTimeUtc).TotalMilliseconds)
            })
            .ToListAsync(cancellationToken);
        return rows
            .Select(r => new PerformanceRow(r.TaskId, r.Name, r.Runs, r.Average, r.Minimum, r.Maximum, Math.Max(0, r.Delay)))
            .OrderByDescending(r => r.AverageMs)
            .ToList();
    }

    public async Task<IReadOnlyList<LoginAuditRow>> GetLoginAuditAsync(ReportFilter filter, int maxRows, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.LoginAudits.AsNoTracking()
            .Where(a => a.AttemptedAtUtc >= filter.FromUtc && a.AttemptedAtUtc < filter.ToUtc)
            .OrderByDescending(a => a.AttemptedAtUtc)
            .Take(maxRows)
            .Select(a => new LoginAuditRow(a.AttemptedAtUtc, a.UsernameAttempted, a.Succeeded, a.Reason))
            .ToListAsync(cancellationToken);
    }

    private static IQueryable<TaskExecution> Filter(SchedulerDbContext db, ReportFilter filter)
    {
        var query = db.TaskExecutions.AsNoTracking().Where(e => e.StartTimeUtc >= filter.FromUtc && e.StartTimeUtc < filter.ToUtc);
        if (filter.TaskId is { } taskId)
        {
            query = query.Where(e => e.TaskId == taskId);
        }

        if (filter.Status is { } status)
        {
            query = query.Where(e => e.Status == status);
        }

        return query;
    }
}
