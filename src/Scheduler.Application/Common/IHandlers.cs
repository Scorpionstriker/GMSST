namespace Scheduler.Application.Common;

/// <summary>A use case that changes state, for example CreateTask or PauseTask.</summary>
public interface ICommandHandler<in TCommand, TResult>
{
    Task<Result<TResult>> HandleAsync(TCommand command, CancellationToken cancellationToken = default);
}

/// <summary>A use case that only reads, for example GetDashboard or GetExecutionSummaryReport.</summary>
public interface IQueryHandler<in TQuery, TResult>
{
    Task<Result<TResult>> HandleAsync(TQuery query, CancellationToken cancellationToken = default);
}
