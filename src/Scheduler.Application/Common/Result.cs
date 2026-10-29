namespace Scheduler.Application.Common;

/// <summary>
/// Explicit success or failure returned by every use-case handler, so expected failures
/// (validation, not found, conflicts) are values rather than exceptions.
/// </summary>
public class Result
{
    private static readonly IReadOnlyList<Error> NoErrors = [];

    protected Result(bool isSuccess, IReadOnlyList<Error> errors)
    {
        IsSuccess = isSuccess;
        Errors = errors;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;

    /// <summary>The first error, or null on success.</summary>
    public Error? Error => Errors.Count > 0 ? Errors[0] : null;

    /// <summary>Every error, for example one per invalid form field (FR-62, FR-74).</summary>
    public IReadOnlyList<Error> Errors { get; }

    public static Result Success() => new(true, NoErrors);
    public static Result Failure(Error error) => new(false, [error]);
    public static Result<T> Success<T>(T value) => new(value, true, NoErrors);
    public static Result<T> Failure<T>(Error error) => new(default, false, [error]);

    public static Result<T> Failure<T>(IEnumerable<Error> errors)
    {
        var list = errors.ToList();
        if (list.Count == 0)
        {
            throw new ArgumentException("A failure needs at least one error.", nameof(errors));
        }

        return new Result<T>(default, false, list);
    }

    /// <summary>The message of the error for <paramref name="field"/>, for showing beneath a form field.</summary>
    public string? MessageFor(string field) =>
        Errors.FirstOrDefault(e => string.Equals(e.Field, field, StringComparison.OrdinalIgnoreCase))?.Message;
}

public sealed class Result<T> : Result
{
    internal Result(T? value, bool isSuccess, IReadOnlyList<Error> errors) : base(isSuccess, errors) => Value = value;

    public T? Value { get; }

    public static implicit operator Result<T>(Error error) => Failure<T>(error);
}

/// <summary>What kind of failure an <see cref="Error"/> describes.</summary>
public enum ErrorKind
{
    Validation = 1,
    NotFound = 2,
    Forbidden = 3,
    Conflict = 4,
    Failure = 5
}

/// <summary>A failure with a stable code and a plain-language message for the user (FR-62).</summary>
/// <param name="Code">For example "Task.NameNotUnique".</param>
/// <param name="Message">Text that is safe to show in the Admin application: never a stack trace or SQL.</param>
/// <param name="Field">The form field the failure belongs to, when there is one (FR-74).</param>
/// <param name="Kind">The kind of failure.</param>
public sealed record Error(string Code, string Message, string? Field = null, ErrorKind Kind = ErrorKind.Validation)
{
    public static Error Validation(string field, string message) => new($"{field}.Invalid", message, field);

    public static Error NotFound(string what) => new($"{what}.NotFound", $"The {what.ToLowerInvariant()} could not be found. It may have been deleted.", Kind: ErrorKind.NotFound);

    public static readonly Error Forbidden = new("Auth.Forbidden", "You do not have permission to do this. Ask an administrator.", Kind: ErrorKind.Forbidden);

    public static readonly Error ConcurrencyConflict = new("Concurrency.Conflict",
        "Someone else changed this record after you opened it. Reload the page and make your change again.", Kind: ErrorKind.Conflict);

    public static readonly Error Unexpected = new("Unexpected",
        "The action could not be completed. The details have been written to the scheduler log.", Kind: ErrorKind.Failure);

    public static Error Conflict(string code, string message) => new(code, message, Kind: ErrorKind.Conflict);

    public static Error Failure(string code, string message) => new(code, message, Kind: ErrorKind.Failure);
}
