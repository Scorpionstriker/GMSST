using Microsoft.Extensions.Logging;
using Scheduler.Application.Common.Persistence;
using Scheduler.Application.Configuration;
using Scheduler.Extensibility.Contracts;

namespace Scheduler.Engine.Handlers;

/// <summary>
/// Removes processed queue entries after seven days and executions, errors and log entries older than
/// LogRetentionDays (FR-35, Phase 4 Section 5.7).
/// </summary>
public sealed class LogCleanupHandler(IRetentionStore retention, ISettingsProvider settings, TimeProvider clock) : ITaskHandler
{
    public const string Type = "LogCleanup";
    private static readonly TimeSpan QueueRetention = TimeSpan.FromDays(7);

    public string TaskType => Type;
    public string DisplayName => "Log clean-up";
    public string Description => "Deletes old queue entries, executions, errors and log entries.";
    public string PayloadExample => "{}";

    public async Task<TaskResult> ExecuteAsync(ITaskContext context)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var days = await settings.GetAsync(SettingKeys.LogRetentionDays, context.CancellationToken);
        var result = await retention.PurgeAsync(now - QueueRetention, now.AddDays(-days), context.CancellationToken);
        context.Logger.LogInformation(
            "Removed {Queue} queue entries, {Executions} executions, {Errors} errors and {Logs} log entries older than {Days} days.",
            result.QueueEntries, result.Executions, result.Errors, result.LogEntries, days);
        return TaskResult.Success("Old records removed.", result);
    }
}
