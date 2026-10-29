using Scheduler.Application.Common;
using Scheduler.Domain.Enums;

namespace Scheduler.Application.Executions;

/// <summary>Execution history and Error Log queries (FR-58, FR-59), executed in the database.</summary>
public interface IExecutionQueries
{
    Task<PagedResult<ExecutionListItem>> SearchAsync(ExecutionFilter filter, PageRequest page, CancellationToken cancellationToken = default);

    /// <summary>Every matching execution, newest first, at most <paramref name="maxRows"/>, for CSV export.</summary>
    Task<IReadOnlyList<ExecutionListItem>> ExportAsync(ExecutionFilter filter, int maxRows, CancellationToken cancellationToken = default);

    Task<ExecutionDetail?> GetDetailAsync(Guid executionId, CancellationToken cancellationToken = default);

    Task<PagedResult<ErrorListItem>> SearchErrorsAsync(ErrorFilter filter, PageRequest page, CancellationToken cancellationToken = default);
}

public sealed record ExecutionFilter(
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    Guid? TaskId = null,
    ExecutionStatus? Status = null,
    TriggerType? TriggerType = null);

public sealed record ExecutionListItem(
    Guid Id,
    Guid TaskId,
    string TaskName,
    string TaskType,
    TriggerType TriggerType,
    ExecutionStatus Status,
    DateTime ScheduledTimeUtc,
    DateTime StartTimeUtc,
    DateTime? EndTimeUtc,
    int? DurationMs,
    int RetryCount,
    string? HostName)
{
    /// <summary>How late the run started, in milliseconds.</summary>
    public double DelayMs => Math.Max(0, (StartTimeUtc - ScheduledTimeUtc).TotalMilliseconds);
}

public sealed record ExecutionDetail(
    ExecutionListItem Execution,
    Guid? QueueId,
    string? Output,
    IReadOnlyList<ErrorItem> Errors,
    IReadOnlyList<LogItem> Logs);

public sealed record ErrorItem(Guid Id, string ErrorType, string ErrorMessage, string? StackTrace, DateTime OccurredAtUtc);

public sealed record LogItem(Guid Id, SchedulerLogLevel Level, string Source, string Message, DateTime LoggedAtUtc);

public sealed record ErrorFilter(DateTime? FromUtc = null, DateTime? ToUtc = null, Guid? TaskId = null, string? Text = null);

public sealed record ErrorListItem(
    Guid Id,
    Guid ExecutionId,
    Guid TaskId,
    string TaskName,
    string ErrorType,
    string ErrorMessage,
    string? StackTrace,
    DateTime OccurredAtUtc,
    ExecutionStatus ExecutionStatus,
    int RetryCount);
