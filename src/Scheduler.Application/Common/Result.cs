namespace Scheduler.Application.Common;

/// <summary>
/// Explicit success or failure returned by every use-case handler, so expected failures
/// (validation, not found, conflicts) are values rather than exceptions.
/// </summary>
public class Result
{
    protected Result(bool isSuccess, Error? error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error? Error { get; }

    public static Result Success() => new(true, null);
    public static Result Failure(Error error) => new(false, error);
    public static Result<T> Success<T>(T value) => new(value, true, null);
    public static Result<T> Failure<T>(Error error) => new(default, false, error);
}

public sealed class Result<T> : Result
{
    internal Result(T? value, bool isSuccess, Error? error) : base(isSuccess, error) => Value = value;

    public T? Value { get; }
}

/// <summary>A failure with a stable code and a plain-language message for the user (FR-62).</summary>
/// <param name="Code">For example "Task.NameNotUnique".</param>
/// <param name="Message">Text that is safe to show in the Admin application.</param>
/// <param name="Field">The form field the failure belongs to, when there is one (FR-74).</param>
public sealed record Error(string Code, string Message, string? Field = null);
