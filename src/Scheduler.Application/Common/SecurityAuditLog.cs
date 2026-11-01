using Scheduler.Domain.Enums;
using Scheduler.Extensibility.Contracts;

namespace Scheduler.Application.Common;

/// <summary>Writes audit entries as Info log entries with source Security, naming the current user (WP-4c).</summary>
internal sealed class SecurityAuditLog(ISchedulerLogger log, ICurrentUser currentUser) : IAuditLog
{
    public Task RecordAsync(string action, string message, CancellationToken cancellationToken = default) =>
        RecordAsync(action, message, "Scheduler Admin", cancellationToken);

    public Task RecordAsync(string action, string message, string application, CancellationToken cancellationToken = default) =>
        log.LogAsync(SchedulerLogLevel.Info, LogSources.Security, message, properties: new Dictionary<string, object?>
        {
            ["action"] = action,
            ["user"] = currentUser.Username ?? "unknown",
            ["application"] = string.IsNullOrWhiteSpace(application) ? "Unknown" : application.Trim()
        }, cancellationToken: cancellationToken);
}
