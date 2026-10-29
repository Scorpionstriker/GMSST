namespace Scheduler.Application.Common;

/// <summary>A use case that changes state, for example CreateTask or PauseTask.</summary>
public interface ICommandHandler<in TCommand, TResult>
{
    Task<Result<TResult>> HandleAsync(TCommand command, CancellationToken cancellationToken = default);
}

/// <summary>A use case that only reads, for example GetDashboard or GenerateReport.</summary>
public interface IQueryHandler<in TQuery, TResult>
{
    Task<Result<TResult>> HandleAsync(TQuery query, CancellationToken cancellationToken = default);
}

/// <summary>
/// Marks a command or query that only an Administrator may run. The authorisation decorator checks the
/// role before the handler runs, so the rule holds however the use case is reached (FR-49).
/// </summary>
public interface IRequireAdministrator;

/// <summary>Use cases that return no value return <see cref="Unit"/>.</summary>
public readonly record struct Unit
{
    public static readonly Unit Value = default;
}
