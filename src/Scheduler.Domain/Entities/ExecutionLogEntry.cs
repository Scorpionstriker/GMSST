using Scheduler.Domain.Enums;

namespace Scheduler.Domain.Entities;

/// <summary>A structured log entry written by the Database logging provider (FR-28).</summary>
public sealed class ExecutionLogEntry
{
    public const int SourceMaxLength = 50;

    private ExecutionLogEntry()
    {
        Source = string.Empty;
        Message = "{}";
    }

    public ExecutionLogEntry(Guid? executionId, Guid providerId, SchedulerLogLevel level, string source, string messageJson,
        DateTime loggedAtUtc)
    {
        ExecutionId = executionId;
        ProviderId = providerId;
        Level = level;
        Source = source.Length > SourceMaxLength ? source[..SourceMaxLength] : source;
        Message = messageJson;
        LoggedAtUtc = loggedAtUtc;
    }

    public Guid Id { get; private set; } = Guid.CreateVersion7();

    /// <summary>The related execution; null for engine-level messages.</summary>
    public Guid? ExecutionId { get; private set; }

    public Guid ProviderId { get; private set; }
    public SchedulerLogLevel Level { get; private set; }
    public string Source { get; private set; }

    /// <summary>Structured message content as JSON.</summary>
    public string Message { get; private set; }

    public DateTime LoggedAtUtc { get; private set; }
}
