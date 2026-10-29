using Microsoft.EntityFrameworkCore;
using Scheduler.Application.Common.Persistence;
using Scheduler.Domain.Entities;
using Scheduler.Domain.Enums;

namespace Scheduler.Infrastructure.Persistence.Repositories;

internal sealed class ExecutionRepository(SchedulerDbContext context) : IExecutionRepository
{
    public void Add(TaskExecution execution) => context.TaskExecutions.Add(execution);

    public void AddError(ErrorLogEntry error) => context.ErrorLogs.Add(error);

    public Task<TaskExecution?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.TaskExecutions.Include(e => e.Task).FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

    public Task<TaskExecution?> GetForQueueEntryAsync(Guid queueId, CancellationToken cancellationToken = default) =>
        context.TaskExecutions.FirstOrDefaultAsync(e => e.QueueId == queueId, cancellationToken);
}

internal sealed class WorkerRepository(SchedulerDbContext context) : IWorkerRepository
{
    public async Task<IReadOnlyList<WorkerThread>> GetForHostAsync(string hostName, string poolName, CancellationToken cancellationToken = default) =>
        await context.WorkerThreads.Where(w => w.HostName == hostName && w.ThreadPoolName == poolName)
            .OrderBy(w => w.Id).ToListAsync(cancellationToken);

    public Task<WorkerThread?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.WorkerThreads.FirstOrDefaultAsync(w => w.Id == id, cancellationToken);

    public void Add(WorkerThread worker) => context.WorkerThreads.Add(worker);

    public Task HeartbeatAsync(string hostName, DateTime nowUtc, CancellationToken cancellationToken = default) =>
        context.WorkerThreads.Where(w => w.HostName == hostName && w.Status != WorkerStatus.Stopped)
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.LastHeartbeatUtc, nowUtc), cancellationToken);

    public Task<int> MarkStaleAsStoppedAsync(DateTime staleBeforeUtc, DateTime nowUtc, CancellationToken cancellationToken = default) =>
        context.WorkerThreads.Where(w => w.Status != WorkerStatus.Stopped && w.LastHeartbeatUtc < staleBeforeUtc)
            .ExecuteUpdateAsync(s => s
                .SetProperty(w => w.Status, WorkerStatus.Stopped)
                .SetProperty(w => w.CurrentExecutionId, (Guid?)null), cancellationToken);

    public Task MarkHostStoppedAsync(string hostName, DateTime nowUtc, CancellationToken cancellationToken = default) =>
        context.WorkerThreads.Where(w => w.HostName == hostName && w.Status != WorkerStatus.Stopped)
            .ExecuteUpdateAsync(s => s
                .SetProperty(w => w.Status, WorkerStatus.Stopped)
                .SetProperty(w => w.CurrentExecutionId, (Guid?)null)
                .SetProperty(w => w.LastHeartbeatUtc, nowUtc), cancellationToken);
}

internal sealed class ConfigurationRepository(SchedulerDbContext context) : IConfigurationRepository
{
    public async Task<IReadOnlyList<ConfigurationSetting>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await context.Configuration.OrderBy(c => c.TaskId).ThenBy(c => c.Key).ToListAsync(cancellationToken);

    public Task<ConfigurationSetting?> FindAsync(Guid? taskId, string key, CancellationToken cancellationToken = default) =>
        context.Configuration.FirstOrDefaultAsync(c => c.TaskId == taskId && c.Key == key, cancellationToken);

    public void Add(ConfigurationSetting setting) => context.Configuration.Add(setting);

    public void Remove(ConfigurationSetting setting) => context.Configuration.Remove(setting);

    public async Task<IReadOnlyList<LoggingProviderSetting>> GetLoggingProvidersAsync(CancellationToken cancellationToken = default) =>
        await context.LoggingProviders.OrderBy(p => p.ProviderName).ToListAsync(cancellationToken);

    public Task<LoggingProviderSetting?> GetLoggingProviderAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.LoggingProviders.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
}
