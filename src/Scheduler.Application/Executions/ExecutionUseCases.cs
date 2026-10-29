using System.Globalization;
using Scheduler.Application.Common;
using Scheduler.Application.Reports;

namespace Scheduler.Application.Executions;

/// <summary>Execution history with filters (FR-58).</summary>
public sealed record SearchExecutionsQuery(ExecutionFilter Filter, PageRequest Page);

/// <summary>One run with its error, stack trace, output and log entries on one screen (FR-58).</summary>
public sealed record GetExecutionDetailQuery(Guid ExecutionId);

/// <summary>Error Log page (FR-59).</summary>
public sealed record SearchErrorsQuery(ErrorFilter Filter, PageRequest Page);

/// <summary>Execution history as CSV (FR-66).</summary>
public sealed record ExportExecutionsQuery(ExecutionFilter Filter);

internal sealed class ExecutionFilterValidator : IValidator<SearchExecutionsQuery>, IValidator<ExportExecutionsQuery>
{
    public Task<IReadOnlyList<Error>> ValidateAsync(SearchExecutionsQuery query, CancellationToken cancellationToken = default) =>
        Task.FromResult(Validate(query.Filter));

    public Task<IReadOnlyList<Error>> ValidateAsync(ExportExecutionsQuery query, CancellationToken cancellationToken = default) =>
        Task.FromResult(Validate(query.Filter));

    private static IReadOnlyList<Error> Validate(ExecutionFilter filter) =>
        new ValidationErrors()
            .AddIf(filter.FromUtc > filter.ToUtc, "To", "The end of the date range must not be before its start.")
            .Errors;
}

internal sealed class SearchExecutionsHandler(IExecutionQueries queries) : IQueryHandler<SearchExecutionsQuery, PagedResult<ExecutionListItem>>
{
    public async Task<Result<PagedResult<ExecutionListItem>>> HandleAsync(SearchExecutionsQuery query, CancellationToken cancellationToken = default) =>
        Result.Success(await queries.SearchAsync(query.Filter, query.Page, cancellationToken));
}

internal sealed class GetExecutionDetailHandler(IExecutionQueries queries) : IQueryHandler<GetExecutionDetailQuery, ExecutionDetail>
{
    public async Task<Result<ExecutionDetail>> HandleAsync(GetExecutionDetailQuery query, CancellationToken cancellationToken = default)
    {
        var detail = await queries.GetDetailAsync(query.ExecutionId, cancellationToken);
        return detail is null ? Error.NotFound("Execution") : Result.Success(detail);
    }
}

internal sealed class SearchErrorsHandler(IExecutionQueries queries) : IQueryHandler<SearchErrorsQuery, PagedResult<ErrorListItem>>
{
    public async Task<Result<PagedResult<ErrorListItem>>> HandleAsync(SearchErrorsQuery query, CancellationToken cancellationToken = default) =>
        Result.Success(await queries.SearchErrorsAsync(query.Filter, query.Page, cancellationToken));
}

internal sealed class ExportExecutionsHandler(IExecutionQueries queries, TimeProvider clock) : IQueryHandler<ExportExecutionsQuery, CsvFile>
{
    public const int MaxRows = 100_000;

    public async Task<Result<CsvFile>> HandleAsync(ExportExecutionsQuery query, CancellationToken cancellationToken = default)
    {
        var rows = await queries.ExportAsync(query.Filter, MaxRows, cancellationToken);
        var csv = CsvWriter.Write(
            ["Execution id", "Task", "Task type", "Trigger", "Status", "Scheduled (UTC)", "Started (UTC)", "Ended (UTC)", "Delay (s)",
                "Duration (s)", "Retry", "Host"],
            rows.Select(r => (IReadOnlyList<string?>)
            [
                r.Id.ToString(), r.TaskName, r.TaskType, r.TriggerType.ToString(), r.Status.ToString(), Format(r.ScheduledTimeUtc),
                Format(r.StartTimeUtc), r.EndTimeUtc is null ? null : Format(r.EndTimeUtc.Value), Seconds(r.DelayMs), Seconds(r.DurationMs),
                r.RetryCount.ToString(CultureInfo.InvariantCulture), r.HostName
            ]));

        var now = clock.GetUtcNow().UtcDateTime;
        var from = query.Filter.FromUtc ?? rows.LastOrDefault()?.StartTimeUtc ?? now;
        var to = query.Filter.ToUtc ?? now;
        return Result.Success(new CsvFile($"execution_history_{from:yyyyMMdd}_{to:yyyyMMdd}.csv", csv));
    }

    internal static string Format(DateTime utc) => utc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    internal static string? Seconds(double? milliseconds) =>
        milliseconds is null ? null : (milliseconds.Value / 1000).ToString("0.0", CultureInfo.InvariantCulture);
}
