using Scheduler.Application.Executions;
using Scheduler.Domain.Enums;

namespace Scheduler.Application.Reports;

/// <summary>Aggregates for the dashboard and the reports, calculated inside PostgreSQL (FR-56, FR-64, NFR-11).</summary>
public interface IReportQueries
{
    Task<DashboardData> GetDashboardAsync(DateTime nowUtc, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ExecutionSummaryRow>> GetExecutionSummaryAsync(ReportFilter filter, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FailureRow>> GetFailuresAsync(ReportFilter filter, int maxRows, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PerformanceRow>> GetPerformanceAsync(ReportFilter filter, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LoginAuditRow>> GetLoginAuditAsync(ReportFilter filter, int maxRows, CancellationToken cancellationToken = default);

    /// <summary>Account and security setting changes: log entries with source Security (WP-4c).</summary>
    Task<IReadOnlyList<SecurityEventRow>> GetSecurityEventsAsync(ReportFilter filter, int maxRows, CancellationToken cancellationToken = default);
}

public sealed record ReportFilter(DateTime FromUtc, DateTime ToUtc, Guid? TaskId, ExecutionStatus? Status, bool? Succeeded = null);

public sealed record ExecutionSummaryRow(Guid TaskId, string TaskName, int Total, int Succeeded, int Failed, int TimedOut, int Cancelled, int Retrying)
{
    public double SuccessRate => Total == 0 ? 0 : Succeeded * 100.0 / Total;
}

public sealed record FailureRow(Guid ExecutionId, string TaskName, DateTime StartTimeUtc, ExecutionStatus Status, int RetryCount,
    string? ErrorType, string? ErrorMessage);

public sealed record PerformanceRow(Guid TaskId, string TaskName, int Runs, double AverageMs, int MinimumMs, int MaximumMs, double AverageDelayMs);

public sealed record LoginAuditRow(DateTime AttemptedAtUtc, string Username, bool Succeeded, string? Reason);

public sealed record SecurityEventRow(DateTime LoggedAtUtc, string User, string Action, string Message);

public sealed record DashboardData(
    string? NextRunTaskName,
    DateTime? NextRunUtc,
    int TotalTasks,
    int ActiveTasks,
    int PausedTasks,
    int Succeeded24h,
    int Failed24h,
    double? AverageDurationMs24h,
    int BusyWorkers,
    int RunningWorkers,
    int WaitingInQueue,
    IReadOnlyList<HourBucket> Hourly,
    IReadOnlyList<ExecutionListItem> Recent);

/// <summary>Executions that started in one hour of the last 24.</summary>
public sealed record HourBucket(DateTime HourStartUtc, int Succeeded, int Failed, double? AverageDurationMs);
