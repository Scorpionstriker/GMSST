using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Scheduler.Extensibility.Contracts;

/// <summary>Everything a running handler is given by the engine (FR-22, FR-40).</summary>
public interface ITaskContext
{
    Guid TaskId { get; }
    Guid ExecutionId { get; }
    string TaskName { get; }
    int RetryNumber { get; }

    /// <summary>The task's JSON payload (FR-01).</summary>
    JsonElement Payload { get; }

    ILogger Logger { get; }

    /// <summary>Signalled on time-out, cancellation or shutdown.</summary>
    CancellationToken CancellationToken { get; }
}
