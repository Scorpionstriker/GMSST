namespace Scheduler.Extensibility.Contracts;

/// <summary>Outcome returned by a task handler. Expected failures are returned, not thrown.</summary>
public sealed record TaskResult
{
    public bool IsSuccess { get; init; }
    public string? Message { get; init; }

    /// <summary>Optional structured output, stored as JSONB with the execution (FR-22).</summary>
    public object? Output { get; init; }

    public static TaskResult Success(string? message = null, object? output = null) =>
        new() { IsSuccess = true, Message = message, Output = output };

    public static TaskResult Failure(string message, object? output = null) =>
        new() { IsSuccess = false, Message = message, Output = output };
}
