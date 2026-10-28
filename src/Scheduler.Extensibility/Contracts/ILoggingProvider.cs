using Scheduler.Domain.Enums;

namespace Scheduler.Extensibility.Contracts;

/// <summary>A destination for scheduler logs: Console, File and Database are built in (FR-29).</summary>
public interface ILoggingProvider
{
    string Name { get; }

    Task WriteAsync(SchedulerLogEntry entry, CancellationToken cancellationToken = default);
}

/// <summary>A structured log entry passed to every enabled provider (FR-28).</summary>
public sealed record SchedulerLogEntry(
    DateTime TimestampUtc,
    SchedulerLogLevel Level,
    string Component,
    string Message,
    Guid? ExecutionId = null,
    IReadOnlyDictionary<string, object?>? Properties = null);
