using Microsoft.EntityFrameworkCore;
using Npgsql;
using Scheduler.Application.Common;
using Scheduler.Application.Common.Persistence;

namespace Scheduler.Infrastructure.Persistence.Repositories;

internal sealed class UnitOfWorkFactory(IDbContextFactory<SchedulerDbContext> contextFactory) : IUnitOfWorkFactory
{
    public IUnitOfWork Create() => new UnitOfWork(contextFactory.CreateDbContext());
}

/// <summary>EF Core implementation of <see cref="IUnitOfWork"/>: one DbContext, saved in one transaction (NFR-17).</summary>
internal sealed class UnitOfWork : IUnitOfWork
{
    private readonly SchedulerDbContext _context;

    public UnitOfWork(SchedulerDbContext context)
    {
        _context = context;
        Tasks = new TaskRepository(context);
        Queue = new QueueRepository(context);
        Executions = new ExecutionRepository(context);
        Workers = new WorkerRepository(context);
        Configuration = new ConfigurationRepository(context);
    }

    public ITaskRepository Tasks { get; }
    public IQueueRepository Queue { get; }
    public IExecutionRepository Executions { get; }
    public IWorkerRepository Workers { get; }
    public IConfigurationRepository Configuration { get; }

    public async Task<IAsyncDisposable> BeginTransactionAsync(CancellationToken cancellationToken = default) =>
        await _context.Database.BeginTransactionAsync(cancellationToken);

    public Task CommitTransactionAsync(CancellationToken cancellationToken = default) =>
        _context.Database.CommitTransactionAsync(cancellationToken);

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException("The record was changed by someone else after it was opened.", ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg)
        {
            throw new UniqueConstraintException(pg.ConstraintName ?? "unknown", ex);
        }
    }

    public ValueTask DisposeAsync() => _context.DisposeAsync();
}
