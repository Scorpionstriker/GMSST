using Scheduler.Application.Common;
using Scheduler.Domain.Enums;

namespace Scheduler.Application.Logging;

public interface ILogHistoryQueries
{
    Task<PagedResult<LogHistoryItem>> SearchLogsAsync(LogHistoryFilter filter, PageRequest page,
        CancellationToken cancellationToken = default);

    Task<PagedResult<LoginAttemptItem>> SearchLoginAttemptsAsync(LoginAttemptFilter filter, PageRequest page,
        CancellationToken cancellationToken = default);
}

public sealed record LogHistoryFilter(
    bool AuditOnly,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    SchedulerLogLevel? Level = null,
    string? Text = null);

public sealed record LoginAttemptFilter(DateTime? FromUtc = null, DateTime? ToUtc = null, string? Text = null);

public sealed record LogHistoryItem(
    Guid Id,
    DateTime LoggedAtUtc,
    SchedulerLogLevel Level,
    string Source,
    string Action,
    string User,
    string Application,
    string Message,
    Guid? ExecutionId);

public sealed record LoginAttemptItem(
    Guid Id,
    DateTime AttemptedAtUtc,
    string Username,
    bool Succeeded,
    string? Reason);

public sealed record SearchLogHistoryQuery(LogHistoryFilter Filter, PageRequest Page) : IRequireAdministrator;

public sealed record SearchLoginAttemptsQuery(LoginAttemptFilter Filter, PageRequest Page) : IRequireAdministrator;

internal sealed class SearchLogHistoryHandler(ILogHistoryQueries queries)
    : IQueryHandler<SearchLogHistoryQuery, PagedResult<LogHistoryItem>>
{
    public async Task<Result<PagedResult<LogHistoryItem>>> HandleAsync(SearchLogHistoryQuery query,
        CancellationToken cancellationToken = default)
    {
        var errors = new ValidationErrors()
            .AddIf(query.Filter.FromUtc > query.Filter.ToUtc, "To", "The end of the date range must not be before its start.")
            .Errors;
        if (errors.Count > 0)
        {
            return Result.Failure<PagedResult<LogHistoryItem>>(errors);
        }

        return Result.Success(await queries.SearchLogsAsync(query.Filter, query.Page, cancellationToken));
    }
}

internal sealed class SearchLoginAttemptsHandler(ILogHistoryQueries queries)
    : IQueryHandler<SearchLoginAttemptsQuery, PagedResult<LoginAttemptItem>>
{
    public async Task<Result<PagedResult<LoginAttemptItem>>> HandleAsync(SearchLoginAttemptsQuery query,
        CancellationToken cancellationToken = default)
    {
        var errors = new ValidationErrors()
            .AddIf(query.Filter.FromUtc > query.Filter.ToUtc, "To", "The end of the date range must not be before its start.")
            .Errors;
        if (errors.Count > 0)
        {
            return Result.Failure<PagedResult<LoginAttemptItem>>(errors);
        }

        return Result.Success(await queries.SearchLoginAttemptsAsync(query.Filter, query.Page, cancellationToken));
    }
}
