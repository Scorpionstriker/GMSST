using Microsoft.EntityFrameworkCore;
using Scheduler.Application.Common;
using Scheduler.Application.Logging;
using Scheduler.Domain.Entities;
using Scheduler.Domain.Enums;
using System.Text.Json;

namespace Scheduler.Infrastructure.Persistence.Queries;

internal sealed class LogHistoryQueries(IDbContextFactory<SchedulerDbContext> contextFactory) : ILogHistoryQueries
{
    public async Task<PagedResult<LogHistoryItem>> SearchLogsAsync(LogHistoryFilter filter, PageRequest page,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = db.ExecutionLogs.AsNoTracking().Where(log => filter.AuditOnly
            ? log.Source == LogSources.Security
            : log.Source != LogSources.Security);
        if (filter.FromUtc is { } from)
        {
            query = query.Where(log => log.LoggedAtUtc >= from);
        }

        if (filter.ToUtc is { } to)
        {
            query = query.Where(log => log.LoggedAtUtc < to);
        }

        if (filter.Level is { } level)
        {
            query = query.Where(log => log.Level == level);
        }

        if (!string.IsNullOrWhiteSpace(filter.Text))
        {
            var pattern = LikePattern.Contains(filter.Text.Trim());
            query = query.Where(log => EF.Functions.ILike(log.Message, pattern, LikePattern.Escape)
                                       || EF.Functions.ILike(log.Source, pattern, LikePattern.Escape));
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(log => log.LoggedAtUtc).ThenByDescending(log => log.Id)
            .Skip(page.Skip).Take(page.SafePageSize)
            .Select(log => new { log.Id, log.LoggedAtUtc, log.Level, log.Source, log.Message, log.ExecutionId })
            .ToListAsync(cancellationToken);
        return new PagedResult<LogHistoryItem>(
            rows.Select(row => ToLogHistoryItem(row.Id, row.LoggedAtUtc, row.Level, row.Source, row.Message, row.ExecutionId)).ToList(),
            total, page.SafePage, page.SafePageSize);
    }

    public async Task<PagedResult<LoginAttemptItem>> SearchLoginAttemptsAsync(LoginAttemptFilter filter, PageRequest page,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = db.LoginAudits.AsNoTracking();
        if (filter.FromUtc is { } from)
        {
            query = query.Where(audit => audit.AttemptedAtUtc >= from);
        }

        if (filter.ToUtc is { } to)
        {
            query = query.Where(audit => audit.AttemptedAtUtc < to);
        }

        if (!string.IsNullOrWhiteSpace(filter.Text))
        {
            var pattern = LikePattern.Contains(filter.Text.Trim());
            query = query.Where(audit => EF.Functions.ILike(audit.UsernameAttempted, pattern, LikePattern.Escape)
                                         || audit.Reason != null && EF.Functions.ILike(audit.Reason, pattern, LikePattern.Escape));
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(audit => audit.AttemptedAtUtc).ThenByDescending(audit => audit.Id)
            .Skip(page.Skip).Take(page.SafePageSize)
            .Select(audit => new LoginAttemptItem(audit.Id, audit.AttemptedAtUtc, audit.UsernameAttempted, audit.Succeeded, audit.Reason))
            .ToListAsync(cancellationToken);
        return new PagedResult<LoginAttemptItem>(rows, total, page.SafePage, page.SafePageSize);
    }

    private static LogHistoryItem ToLogHistoryItem(Guid id, DateTime loggedAtUtc, SchedulerLogLevel level, string source,
        string json, Guid? executionId)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var message = root.TryGetProperty("message", out var messageProperty) ? messageProperty.ToString() : json;
            var properties = root.TryGetProperty("properties", out var value) && value.ValueKind == JsonValueKind.Object
                ? value
                : default;
            return new LogHistoryItem(id, loggedAtUtc, level, source,
                Property(properties, "action"), Property(properties, "user"), Property(properties, "application"), message, executionId);
        }
        catch (JsonException)
        {
            return new LogHistoryItem(id, loggedAtUtc, level, source, "", "", "", json, executionId);
        }
    }

    private static string Property(JsonElement properties, string name) =>
        properties.ValueKind == JsonValueKind.Object && properties.TryGetProperty(name, out var value) ? value.ToString() : "";
}
