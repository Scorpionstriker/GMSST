using Microsoft.Extensions.Logging;
using Scheduler.Application.Common.Persistence;
using Scheduler.Application.Configuration;
using Scheduler.Extensibility.Contracts;

namespace Scheduler.Engine.Handlers;

/// <summary>
/// Removes processed queue entries after seven days, executions, errors, log entries and log files older than
/// LogRetentionDays, and sign-in and user management audit records older than AuditRetentionDays (FR-35, Phase 4
/// Section 5.7, WP-3).
/// </summary>
public sealed class LogCleanupHandler(IRetentionStore retention, ISettingsProvider settings, TimeProvider clock,
    ILogFileCleaner? logFiles = null) : ITaskHandler
{
    public const string Type = "LogCleanup";
    private static readonly TimeSpan QueueRetention = TimeSpan.FromDays(7);

    public string TaskType => Type;
    public string DisplayName => "Log clean-up";
    public string Description => "Deletes old queue entries, executions, errors, log entries, log files and audit records.";
    public string PayloadExample => "{}";

    public async Task<TaskResult> ExecuteAsync(ITaskContext context)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var days = await settings.GetAsync(SettingKeys.LogRetentionDays, context.CancellationToken);
        var auditDays = await settings.GetAsync(SettingKeys.AuditRetentionDays, context.CancellationToken);
        var result = await retention.PurgeAsync(now - QueueRetention, now.AddDays(-days), now.AddDays(-auditDays), context.CancellationToken);
        result = result with { LogFiles = logFiles?.DeleteOlderThan(now.AddDays(-days)) ?? 0 };
        context.Logger.LogInformation(
            "Removed {Queue} queue entries, {Executions} executions, {Errors} errors, {Logs} log entries and {Files} log files older than {Days} days, " +
            "and {Audit} audit records older than {AuditDays} days.",
            result.QueueEntries, result.Executions, result.Errors, result.LogEntries, result.LogFiles, days, result.AuditEntries, auditDays);
        return TaskResult.Success("Old records removed.", result);
    }
}
