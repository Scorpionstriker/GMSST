using System.Text.Json;
using Microsoft.Extensions.Logging;
using Scheduler.Domain.Enums;
using Scheduler.Extensibility.Contracts;

namespace Scheduler.Engine.Execution;

internal sealed class TaskContext(
    Guid taskId,
    Guid executionId,
    string taskName,
    int retryNumber,
    TriggerType triggerType,
    DateTime scheduledTimeUtc,
    JsonElement payload,
    ILogger logger,
    IServiceProvider services,
    CancellationToken cancellationToken) : ITaskContext
{
    public Guid TaskId { get; } = taskId;
    public Guid ExecutionId { get; } = executionId;
    public string TaskName { get; } = taskName;
    public int RetryNumber { get; } = retryNumber;
    public TriggerType TriggerType { get; } = triggerType;
    public DateTime ScheduledTimeUtc { get; } = scheduledTimeUtc;
    public JsonElement Payload { get; } = payload;
    public ILogger Logger { get; } = logger;
    public IServiceProvider Services { get; } = services;
    public CancellationToken CancellationToken { get; } = cancellationToken;
}
