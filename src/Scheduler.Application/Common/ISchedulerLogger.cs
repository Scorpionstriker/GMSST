using Scheduler.Domain.Enums;

namespace Scheduler.Application.Common;

/// <summary>
/// Writes a structured scheduler log entry to every enabled logging provider whose minimum level allows it
/// (FR-28, FR-29). Implementations never throw: a failing provider must not stop the engine.
/// </summary>
public interface ISchedulerLogger
{
    Task LogAsync(
        SchedulerLogLevel level,
        string source,
        string message,
        Guid? executionId = null,
        IReadOnlyDictionary<string, object?>? properties = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Well-known log sources, so entries can be filtered by component.</summary>
public static class LogSources
{
    public const string Engine = "SchedulerEngine";
    public const string Executor = "TaskExecutor";
    public const string Admin = "AdminApplication";
    public const string Backup = "BackupService";
    public const string Database = "Database";
    public const string Security = "Security";
}
