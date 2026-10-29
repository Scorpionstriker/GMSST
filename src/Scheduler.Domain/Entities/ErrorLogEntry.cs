namespace Scheduler.Domain.Entities;

/// <summary>An error raised during an execution, with its type, message and structured stack trace (FR-30).</summary>
public sealed class ErrorLogEntry
{
    public const int ErrorTypeMaxLength = 100;

    private ErrorLogEntry()
    {
        ErrorType = string.Empty;
        ErrorMessage = string.Empty;
    }

    public ErrorLogEntry(Guid executionId, string errorType, string errorMessage, string? stackTraceJson, DateTime occurredAtUtc)
    {
        ExecutionId = executionId;
        ErrorType = errorType.Length > ErrorTypeMaxLength ? errorType[..ErrorTypeMaxLength] : errorType;
        ErrorMessage = errorMessage;
        StackTrace = stackTraceJson;
        OccurredAtUtc = occurredAtUtc;
    }

    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid ExecutionId { get; private set; }
    public TaskExecution? Execution { get; private set; }
    public string ErrorType { get; private set; }
    public string ErrorMessage { get; private set; }

    /// <summary>Stack frames and inner exceptions as JSON.</summary>
    public string? StackTrace { get; private set; }

    public DateTime OccurredAtUtc { get; private set; }
}
