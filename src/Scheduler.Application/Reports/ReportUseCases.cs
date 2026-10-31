using System.Globalization;
using Scheduler.Application.Common;
using Scheduler.Application.Cron;
using Scheduler.Application.Tasks;
using Scheduler.Domain.Enums;

namespace Scheduler.Application.Reports;

/// <summary>The five reports (FR-64).</summary>
public enum ReportType
{
    ExecutionSummary = 1,
    FailuresAndErrors = 2,
    Performance = 3,
    UpcomingSchedule = 4,
    SecurityAudit = 5
}

/// <summary>Report parameters: date range of at most 366 days, optional task and status (FR-65).</summary>
/// <param name="Type">The report.</param>
/// <param name="FromUtc">Start of the range (inclusive).</param>
/// <param name="ToUtc">End of the range (exclusive).</param>
/// <param name="TaskId">Optional task filter (not used by the Security and audit report).</param>
/// <param name="Status">Status filter for the Execution summary, Failures and errors and Performance reports (WP-8c).</param>
/// <param name="Succeeded">Outcome filter for the Security and audit report: true, false or null for both (WP-8c).</param>
/// <param name="FromDate">The local From date the user picked, for the header and file name (WP-8e).</param>
/// <param name="ToDate">The local To date the user picked, inclusive (WP-8e).</param>
public sealed record GenerateReportQuery(ReportType Type, DateTime FromUtc, DateTime ToUtc, Guid? TaskId = null, ExecutionStatus? Status = null,
    bool? Succeeded = null, DateOnly? FromDate = null, DateOnly? ToDate = null);

/// <summary>Dashboard indicators, chart data and recent executions (FR-56).</summary>
public sealed record GetDashboardQuery;

/// <summary>A generated report: summary figures, one chart and a table, ready to show, export or print (FR-65, FR-66).</summary>
public sealed record ReportResult(
    ReportType Type,
    string Title,
    string Question,
    string Parameters,
    DateTime GeneratedAtUtc,
    IReadOnlyList<ReportFigure> Summary,
    ReportChart Chart,
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<string?>> Rows,
    bool Truncated)
{
    /// <summary>The CSV file, named after the local From and To dates the user picked, both inclusive (WP-8e).</summary>
    public CsvFile ToCsv(DateOnly fromDate, DateOnly toDate) =>
        new($"{FileStem(Type)}_{fromDate:yyyyMMdd}_{toDate:yyyyMMdd}.csv", CsvWriter.Write(Columns, Rows));

    private static string FileStem(ReportType type) => type switch
    {
        ReportType.ExecutionSummary => "execution_summary",
        ReportType.FailuresAndErrors => "failures_and_errors",
        ReportType.Performance => "performance",
        ReportType.UpcomingSchedule => "upcoming_schedule",
        _ => "security_audit"
    };
}

public sealed record ReportFigure(string Label, string Value);

/// <summary>A bar chart: one label per bar group and one or more series.</summary>
public sealed record ReportChart(string Title, IReadOnlyList<string> Labels, IReadOnlyList<ChartSeries> Series);

public sealed record ChartSeries(string Name, IReadOnlyList<double> Values, string CssClass);

internal sealed class GenerateReportValidator : IValidator<GenerateReportQuery>
{
    public const int MaxRangeDays = 366;

    public Task<IReadOnlyList<Error>> ValidateAsync(GenerateReportQuery query, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ValidationErrors()
            .AddIf(!Enum.IsDefined(query.Type), "Type", "Select a report.")
            .AddIf(query.FromUtc > query.ToUtc, "To", "'From' must not be after 'To'.")
            .AddIf(query.ToUtc - query.FromUtc > TimeSpan.FromDays(MaxRangeDays), "To", $"The date range can be at most {MaxRangeDays} days.")
            .Errors);
}

internal sealed class GetDashboardHandler(IReportQueries queries, TimeProvider clock) : IQueryHandler<GetDashboardQuery, DashboardData>
{
    public async Task<Result<DashboardData>> HandleAsync(GetDashboardQuery query, CancellationToken cancellationToken = default) =>
        Result.Success(await queries.GetDashboardAsync(clock.GetUtcNow().UtcDateTime, cancellationToken));
}

/// <summary>Builds each report from data aggregated in the database (Algorithm 6.15).</summary>
internal sealed class GenerateReportHandler(IReportQueries queries, ITaskQueries taskQueries, TimeProvider clock)
    : IQueryHandler<GenerateReportQuery, ReportResult>
{
    internal const int MaxDetailRows = 5_000;
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public async Task<Result<ReportResult>> HandleAsync(GenerateReportQuery query, CancellationToken cancellationToken = default)
    {
        var filter = new ReportFilter(query.FromUtc, query.ToUtc, query.TaskId, query.Status, query.Succeeded);
        var parameters = (query.FromDate is { } fromDate && query.ToDate is { } toDate
                             ? $"{fromDate:yyyy-MM-dd} to {toDate:yyyy-MM-dd} (local dates, inclusive)"
                             : $"{query.FromUtc:yyyy-MM-dd HH:mm} to {query.ToUtc:yyyy-MM-dd HH:mm} UTC") +
                         (query.Status is null || query.Type is ReportType.UpcomingSchedule or ReportType.SecurityAudit ? string.Empty : $", status {query.Status}") +
                         (query.Succeeded is not { } outcome || query.Type != ReportType.SecurityAudit ? string.Empty : outcome ? ", succeeded only" : ", failed only");
        var now = clock.GetUtcNow().UtcDateTime;

        var report = query.Type switch
        {
            ReportType.ExecutionSummary => Summary(await queries.GetExecutionSummaryAsync(filter, cancellationToken), parameters, now),
            ReportType.FailuresAndErrors => Failures(await queries.GetFailuresAsync(filter, MaxDetailRows + 1, cancellationToken), parameters, now),
            ReportType.Performance => Performance(await queries.GetPerformanceAsync(filter, cancellationToken), parameters, now),
            ReportType.UpcomingSchedule => Upcoming(await taskQueries.GetActiveSchedulesAsync(query.TaskId, cancellationToken), query, parameters, now),
            _ => Security(
                await queries.GetLoginAuditAsync(filter, MaxDetailRows + 1, cancellationToken),
                query.Succeeded == false ? [] : await queries.GetSecurityEventsAsync(filter, MaxDetailRows + 1, cancellationToken), parameters, now)
        };

        return Result.Success(report);
    }

    private static ReportResult Summary(IReadOnlyList<ExecutionSummaryRow> rows, string parameters, DateTime now)
    {
        var total = rows.Sum(r => r.Total);
        var succeeded = rows.Sum(r => r.Succeeded);
        var top = rows.OrderByDescending(r => r.Total).Take(12).ToList();

        return new ReportResult(ReportType.ExecutionSummary, "Execution summary", "How reliably is each task running?", parameters, now,
            [
                new("Executions", total.ToString("N0", Invariant)),
                new("Succeeded", succeeded.ToString("N0", Invariant)),
                new("Failed or timed out", rows.Sum(r => r.Failed + r.TimedOut).ToString("N0", Invariant)),
                new("Success rate", total == 0 ? "-" : (succeeded * 100.0 / total).ToString("0.0", Invariant) + " %")
            ],
            new ReportChart("Executions per task", top.Select(r => r.TaskName).ToList(),
            [
                new("Succeeded", top.Select(r => (double)r.Succeeded).ToList(), "series-success"),
                new("Failed / timed out", top.Select(r => (double)(r.Failed + r.TimedOut)).ToList(), "series-failed")
            ]),
            ["Task", "Total", "Succeeded", "Failed", "Timed out", "Cancelled", "Retried", "Success rate (%)"],
            rows.Select(r => (IReadOnlyList<string?>)
            [
                r.TaskName, Number(r.Total), Number(r.Succeeded), Number(r.Failed), Number(r.TimedOut), Number(r.Cancelled), Number(r.Retrying),
                r.SuccessRate.ToString("0.0", Invariant)
            ]).ToList(),
            false);
    }

    private static ReportResult Failures(IReadOnlyList<FailureRow> rows, string parameters, DateTime now)
    {
        var truncated = rows.Count > MaxDetailRows;
        var shown = rows.Take(MaxDetailRows).ToList();
        var byType = shown.GroupBy(r => r.ErrorType ?? "Unknown").OrderByDescending(g => g.Count()).Take(10).ToList();

        return new ReportResult(ReportType.FailuresAndErrors, "Failure and error report", "What is failing, and why?", parameters, now,
            [
                new("Failed attempts", shown.Count.ToString("N0", Invariant) + (truncated ? "+" : string.Empty)),
                new("Final failures", shown.Count(r => r.Status is ExecutionStatus.Failed or ExecutionStatus.TimedOut).ToString("N0", Invariant)),
                new("Retried", shown.Count(r => r.Status == ExecutionStatus.Retrying).ToString("N0", Invariant)),
                new("Tasks affected", shown.Select(r => r.TaskName).Distinct().Count().ToString(Invariant))
            ],
            new ReportChart("Failures by error type", byType.Select(g => g.Key).ToList(),
                [new("Failures", byType.Select(g => (double)g.Count()).ToList(), "series-failed")]),
            ["Started (UTC)", "Task", "Outcome", "Retry", "Error type", "Error message", "Execution id"],
            shown.Select(r => (IReadOnlyList<string?>)
            [
                r.StartTimeUtc.ToString("yyyy-MM-dd HH:mm:ss", Invariant), r.TaskName, Outcome(r.Status), Number(r.RetryCount), r.ErrorType,
                r.ErrorMessage, r.ExecutionId.ToString()
            ]).ToList(),
            truncated);
    }

    private static ReportResult Performance(IReadOnlyList<PerformanceRow> rows, string parameters, DateTime now)
    {
        var slowest = rows.OrderByDescending(r => r.AverageMs).Take(12).ToList();
        var runs = rows.Sum(r => r.Runs);

        return new ReportResult(ReportType.Performance, "Performance report", "Which tasks are slow, and is the scheduler keeping up?", parameters, now,
            [
                new("Completed runs", runs.ToString("N0", Invariant)),
                new("Average duration", runs == 0 ? "-" : Seconds(rows.Sum(r => r.AverageMs * r.Runs) / runs)),
                new("Slowest task", slowest.FirstOrDefault()?.TaskName ?? "-"),
                new("Average start delay", runs == 0 ? "-" : Seconds(rows.Sum(r => r.AverageDelayMs * r.Runs) / runs))
            ],
            new ReportChart("Average duration per task (seconds)", slowest.Select(r => r.TaskName).ToList(),
                [new("Average duration", slowest.Select(r => Math.Round(r.AverageMs / 1000, 2)).ToList(), "series-info")]),
            ["Task", "Runs", "Average (s)", "Minimum (s)", "Maximum (s)", "Average start delay (s)"],
            rows.Select(r => (IReadOnlyList<string?>)
                [r.TaskName, Number(r.Runs), Seconds(r.AverageMs), Seconds(r.MinimumMs), Seconds(r.MaximumMs), Seconds(r.AverageDelayMs)]).ToList(),
            false);
    }

    private static ReportResult Upcoming(IReadOnlyList<ActiveScheduleRow> schedules, GenerateReportQuery query, string parameters, DateTime now)
    {
        var from = query.FromUtc > now ? query.FromUtc : now;
        var rows = new List<(DateTime Run, ActiveScheduleRow Schedule)>();

        foreach (var schedule in schedules)
        {
            var spec = new ScheduleSpec(schedule.Type,
                schedule.Type == ScheduleType.Cron && CronExpression.TryParse(schedule.CronExpression, out var cron, out _) ? cron : null,
                schedule.IntervalSeconds, TimeZones.FindOrUtc(schedule.TimeZoneId), schedule.StartTimeUtc, schedule.EndTimeUtc, schedule.LastRunUtc);

            // Start from the stored next run, so the report shows what the engine will actually queue.
            DateTime? next = schedule.NextRunUtc is { } stored && stored >= from ? stored : ScheduleCalculator.GetNextRun(spec, from.AddTicks(-1));
            var perSchedule = 0;
            while (next is not null && next <= query.ToUtc && perSchedule < 500 && rows.Count <= MaxDetailRows)
            {
                rows.Add((next.Value, schedule));
                perSchedule++;
                spec = spec with { LastRunUtc = next.Value };
                next = ScheduleCalculator.GetNextRun(spec, next.Value);
            }
        }

        var ordered = rows.OrderBy(r => r.Run).ToList();
        var truncated = ordered.Count > MaxDetailRows;
        ordered = ordered.Take(MaxDetailRows).ToList();
        var perDay = ordered.GroupBy(r => r.Run.Date).OrderBy(g => g.Key).Take(31).ToList();

        return new ReportResult(ReportType.UpcomingSchedule, "Upcoming schedule", "Have the schedules been defined correctly?", parameters, now,
            [
                new("Planned runs", ordered.Count.ToString("N0", Invariant) + (truncated ? "+" : string.Empty)),
                new("Tasks", ordered.Select(r => r.Schedule.TaskId).Distinct().Count().ToString(Invariant)),
                new("First run", ordered.Count == 0 ? "-" : ordered[0].Run.ToString("yyyy-MM-dd HH:mm", Invariant) + " UTC"),
                new("Active schedules", schedules.Count.ToString(Invariant))
            ],
            new ReportChart("Planned runs per day (UTC)", perDay.Select(g => g.Key.ToString("MM-dd", Invariant)).ToList(),
                [new("Runs", perDay.Select(g => (double)g.Count()).ToList(), "series-info")]),
            ["Run time (UTC)", "Run time (schedule zone)", "Task", "Task type", "Schedule"],
            ordered.Select(r => (IReadOnlyList<string?>)
            [
                r.Run.ToString("yyyy-MM-dd HH:mm", Invariant),
                TimeZones.FromUtc(r.Run, TimeZones.FindOrUtc(r.Schedule.TimeZoneId)).ToString("yyyy-MM-dd HH:mm", Invariant) + " " + r.Schedule.TimeZoneId,
                r.Schedule.TaskName, r.Schedule.TaskType,
                ScheduleDescriber.Describe(r.Schedule.Type, r.Schedule.CronExpression, r.Schedule.IntervalSeconds, r.Schedule.TimeZoneId, r.Schedule.StartTimeUtc)
            ]).ToList(),
            truncated);
    }

    // WP-4c: sign-in attempts and the account and security setting changes, newest first, in one table.
    private static ReportResult Security(IReadOnlyList<LoginAuditRow> rows, IReadOnlyList<SecurityEventRow> events, string parameters, DateTime now)
    {
        var truncated = rows.Count > MaxDetailRows || events.Count > MaxDetailRows;
        var shown = rows.Take(MaxDetailRows).ToList();
        var changes = events.Take(MaxDetailRows).ToList();
        var perDay = shown.GroupBy(r => r.AttemptedAtUtc.Date).OrderBy(g => g.Key).TakeLast(31).ToList();
        var table = shown
            .Select(r => (Time: r.AttemptedAtUtc, Row: (IReadOnlyList<string?>)
                [r.AttemptedAtUtc.ToString("yyyy-MM-dd HH:mm:ss", Invariant), r.Username, r.Succeeded ? "Sign-in succeeded" : "Sign-in failed", r.Reason]))
            .Concat(changes.Select(e => (Time: e.LoggedAtUtc, Row: (IReadOnlyList<string?>)
                [e.LoggedAtUtc.ToString("yyyy-MM-dd HH:mm:ss", Invariant), e.User, e.Action, e.Message])))
            .OrderByDescending(r => r.Time)
            .Take(MaxDetailRows)
            .Select(r => r.Row)
            .ToList();

        return new ReportResult(ReportType.SecurityAudit, "Security and audit report", "Has anyone attempted to access the system improperly?", parameters, now,
            [
                new("Sign-in attempts", shown.Count.ToString("N0", Invariant) + (rows.Count > MaxDetailRows ? "+" : string.Empty)),
                new("Successful", shown.Count(r => r.Succeeded).ToString("N0", Invariant)),
                new("Failed", shown.Count(r => !r.Succeeded).ToString("N0", Invariant)),
                new("Lockouts", shown.Count(r => r.Reason == "LockedOut").ToString("N0", Invariant)),
                new("Account and setting changes", changes.Count.ToString("N0", Invariant) + (events.Count > MaxDetailRows ? "+" : string.Empty))
            ],
            new ReportChart("Sign-in attempts per day (UTC)", perDay.Select(g => g.Key.ToString("MM-dd", Invariant)).ToList(),
            [
                new("Successful", perDay.Select(g => (double)g.Count(r => r.Succeeded)).ToList(), "series-success"),
                new("Failed", perDay.Select(g => (double)g.Count(r => !r.Succeeded)).ToList(), "series-failed")
            ]),
            ["Time (UTC)", "User", "Event", "Details"],
            table,
            truncated);
    }

    private static string Outcome(ExecutionStatus status) => status switch
    {
        ExecutionStatus.Retrying => "Failed, retried",
        ExecutionStatus.TimedOut => "Timed out (final)",
        ExecutionStatus.Failed => "Failed (final)",
        _ => status.ToString()
    };

    private static string Number(int value) => value.ToString(Invariant);

    private static string Seconds(double milliseconds) => (milliseconds / 1000).ToString("0.0", Invariant);
}
