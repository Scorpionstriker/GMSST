namespace Scheduler.Engine.Execution;

internal enum OutcomeKind
{
    Success,
    Failed,
    TimedOut,
    Cancelled
}

/// <summary>How a handler run ended. <see cref="ErrorType"/> and <see cref="Exception"/> describe a failure.</summary>
internal sealed record RunOutcome(OutcomeKind Kind, string? Message, string? OutputJson = null, string? ErrorType = null, Exception? Exception = null)
{
    public static RunOutcome Succeeded(string? message, string? outputJson) => new(OutcomeKind.Success, message, outputJson);

    public static RunOutcome Failed(string errorType, string message, Exception? exception = null) =>
        new(OutcomeKind.Failed, message, null, errorType, exception);

    public static RunOutcome TimedOut(string message) => new(OutcomeKind.TimedOut, message, null, "TimeoutException");

    public static RunOutcome Cancelled(string message) => new(OutcomeKind.Cancelled, message);
}
