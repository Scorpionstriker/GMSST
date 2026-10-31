using Microsoft.Extensions.Logging;
using Scheduler.Domain.Enums;
using Scheduler.Extensibility.Contracts;

namespace Scheduler.Application.Common;

// Cross-cutting concerns applied uniformly to every use case by wrapping its handler (Phase 4, Section 3.1).
// The order is: logging (outermost) -> authorisation -> validation -> handler.

/// <summary>Runs every registered validator first and returns all field errors together (FR-74).</summary>
internal sealed class ValidationCommandDecorator<TCommand, TResult>(ICommandHandler<TCommand, TResult> inner, IEnumerable<IValidator<TCommand>> validators)
    : ICommandHandler<TCommand, TResult>
{
    public async Task<Result<TResult>> HandleAsync(TCommand command, CancellationToken cancellationToken = default)
    {
        var errors = await ValidationRunner.RunAsync(validators, command, cancellationToken);
        return errors.Count > 0 ? Result.Failure<TResult>(errors) : await inner.HandleAsync(command, cancellationToken);
    }
}

internal sealed class ValidationQueryDecorator<TQuery, TResult>(IQueryHandler<TQuery, TResult> inner, IEnumerable<IValidator<TQuery>> validators)
    : IQueryHandler<TQuery, TResult>
{
    public async Task<Result<TResult>> HandleAsync(TQuery query, CancellationToken cancellationToken = default)
    {
        var errors = await ValidationRunner.RunAsync(validators, query, cancellationToken);
        return errors.Count > 0 ? Result.Failure<TResult>(errors) : await inner.HandleAsync(query, cancellationToken);
    }
}

/// <summary>Checks the role before any use case marked <see cref="IRequireAdministrator"/> runs (FR-49).</summary>
internal sealed class AuthorizationCommandDecorator<TCommand, TResult>(ICommandHandler<TCommand, TResult> inner, ICurrentUser currentUser)
    : ICommandHandler<TCommand, TResult>
{
    public Task<Result<TResult>> HandleAsync(TCommand command, CancellationToken cancellationToken = default) =>
        command is IRequireDeveloper && !currentUser.IsDeveloper
            ? Task.FromResult(Result.Failure<TResult>(Error.Forbidden))
            : command is IRequireAdministrator && !currentUser.IsAdministrator
            ? Task.FromResult(Result.Failure<TResult>(Error.Forbidden))
            : inner.HandleAsync(command, cancellationToken);
}

internal sealed class AuthorizationQueryDecorator<TQuery, TResult>(IQueryHandler<TQuery, TResult> inner, ICurrentUser currentUser)
    : IQueryHandler<TQuery, TResult>
{
    public Task<Result<TResult>> HandleAsync(TQuery query, CancellationToken cancellationToken = default) =>
        query is IRequireDeveloper && !currentUser.IsDeveloper
            ? Task.FromResult(Result.Failure<TResult>(Error.Forbidden))
            : query is IRequireAdministrator && !currentUser.IsAdministrator
            ? Task.FromResult(Result.Failure<TResult>(Error.Forbidden))
            : inner.HandleAsync(query, cancellationToken);
}

/// <summary>
/// Records every administrative action with the user responsible (NFR-18) and turns unexpected exceptions
/// into a plain-language failure, so the interface never shows technical details (FR-62).
/// </summary>
internal sealed class LoggingCommandDecorator<TCommand, TResult>(
    ICommandHandler<TCommand, TResult> inner,
    ICurrentUser currentUser,
    ISchedulerLogger log,
    ILogger<LoggingCommandDecorator<TCommand, TResult>> logger) : ICommandHandler<TCommand, TResult>
{
    public async Task<Result<TResult>> HandleAsync(TCommand command, CancellationToken cancellationToken = default)
    {
        var name = UseCaseName.Of(typeof(TCommand));
        Result<TResult> result;
        try
        {
            result = await inner.HandleAsync(command, cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            result = Result.Failure<TResult>(Error.ConcurrencyConflict);
        }
        catch (UniqueConstraintException ex)
        {
            result = Result.Failure<TResult>(ex.ConstraintName.Contains("task_name", StringComparison.Ordinal)
                ? Error.Validation("Name", "Name must be unique.")
                : Error.Conflict("Unique", "A record with the same name already exists."));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "{UseCase} failed", name);
            await log.LogAsync(SchedulerLogLevel.Error, LogSources.Admin, $"{name} failed: {ex.Message}", properties: Properties(ex),
                cancellationToken: CancellationToken.None);
            return Result.Failure<TResult>(Error.Unexpected);
        }

        if (result.IsSuccess)
        {
            await log.LogAsync(SchedulerLogLevel.Info, LogSources.Admin, $"{name} by {currentUser.Username ?? "system"}",
                properties: new Dictionary<string, object?> { ["userId"] = currentUser.UserId, ["command"] = SafeDescription(command) },
                cancellationToken: CancellationToken.None);
        }
        else if (result.Error?.Kind is ErrorKind.Forbidden or ErrorKind.Failure)
        {
            await log.LogAsync(SchedulerLogLevel.Warning, LogSources.Admin, $"{name} refused for {currentUser.Username ?? "system"}: {result.Error.Code}",
                properties: new Dictionary<string, object?> { ["userId"] = currentUser.UserId }, cancellationToken: CancellationToken.None);
        }

        return result;
    }

    private static Dictionary<string, object?> Properties(Exception ex) => new()
    {
        ["exceptionType"] = ex.GetType().FullName,
        ["stackTrace"] = ex.ToString()
    };

    // Commands are records, so ToString lists their values. Payloads may hold secrets and are not logged (Section 8.6).
    private static string SafeDescription(TCommand command)
    {
        var text = command?.ToString() ?? string.Empty;
        return text.Contains("Payload", StringComparison.Ordinal) || text.Contains("Password", StringComparison.OrdinalIgnoreCase)
            ? UseCaseName.Of(typeof(TCommand))
            : text.Length > 500 ? text[..500] : text;
    }
}

internal sealed class LoggingQueryDecorator<TQuery, TResult>(
    IQueryHandler<TQuery, TResult> inner,
    ISchedulerLogger log,
    ILogger<LoggingQueryDecorator<TQuery, TResult>> logger) : IQueryHandler<TQuery, TResult>
{
    public async Task<Result<TResult>> HandleAsync(TQuery query, CancellationToken cancellationToken = default)
    {
        try
        {
            return await inner.HandleAsync(query, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var name = UseCaseName.Of(typeof(TQuery));
            logger.LogError(ex, "{UseCase} failed", name);
            await log.LogAsync(SchedulerLogLevel.Error, LogSources.Admin, $"{name} failed: {ex.Message}",
                properties: new Dictionary<string, object?> { ["exceptionType"] = ex.GetType().FullName, ["stackTrace"] = ex.ToString() },
                cancellationToken: CancellationToken.None);
            return Result.Failure<TResult>(Error.Unexpected);
        }
    }
}

internal static class ValidationRunner
{
    public static async Task<IReadOnlyList<Error>> RunAsync<T>(IEnumerable<IValidator<T>> validators, T instance, CancellationToken cancellationToken)
    {
        var errors = new List<Error>();
        foreach (var validator in validators)
        {
            errors.AddRange(await validator.ValidateAsync(instance, cancellationToken));
        }

        return errors;
    }
}

internal static class UseCaseName
{
    public static string Of(Type type)
    {
        var name = type.Name;
        foreach (var suffix in new[] { "Command", "Query" })
        {
            if (name.EndsWith(suffix, StringComparison.Ordinal))
            {
                return name[..^suffix.Length];
            }
        }

        return name;
    }
}
