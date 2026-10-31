using Scheduler.Application.Dependencies;
using Scheduler.Domain.Entities;

namespace Scheduler.Application.Common.Persistence;

/// <summary>Creates a short-lived unit of work. The engine and the use cases create one per operation.</summary>
public interface IUnitOfWorkFactory
{
    IUnitOfWork Create();
}

/// <summary>
/// A set of changes saved together in one transaction (NFR-17). Implemented with EF Core in the
/// infrastructure layer, so this layer has no dependency on EF Core or PostgreSQL.
/// </summary>
public interface IUnitOfWork : IAsyncDisposable
{
    ITaskRepository Tasks { get; }
    IQueueRepository Queue { get; }
    IExecutionRepository Executions { get; }
    IWorkerRepository Workers { get; }
    IConfigurationRepository Configuration { get; }

    /// <summary>Starts an explicit transaction, needed when rows are locked with FOR UPDATE.</summary>
    Task<IAsyncDisposable> BeginTransactionAsync(CancellationToken cancellationToken = default);

    Task CommitTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves every change in one transaction. Throws <see cref="ConcurrencyConflictException"/> when a row changed
    /// since it was read and <see cref="UniqueConstraintException"/> when a unique rule is broken.
    /// </summary>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface ITaskRepository
{
    /// <summary>Loads a task with all its schedules and dependencies.</summary>
    Task<ScheduledTask?> GetAsync(Guid id, bool includeDeleted = false, CancellationToken cancellationToken = default);

    Task<ScheduledTask?> FindByNameAsync(string name, bool includeDeleted, CancellationToken cancellationToken = default);

    /// <summary>Finds a built-in task by its stable key, deleted tasks included (WP-8h).</summary>
    Task<ScheduledTask?> FindBySystemKeyAsync(string systemKey, CancellationToken cancellationToken = default);

    /// <summary>True when a task that is not deleted has this name, ignoring letter case (FR-05).</summary>
    Task<bool> NameExistsAsync(string name, Guid? exceptTaskId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Guid>> GetExistingIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);

    void Add(ScheduledTask task);

    /// <summary>Sets the row version the user saw, so a save fails if someone else changed the task since (NFR-17).</summary>
    void SetOriginalVersion(ScheduledTask task, uint version);

    /// <summary>Dependencies between tasks that are not deleted.</summary>
    Task<IReadOnlyList<DependencyEdge>> GetDependencyEdgesAsync(CancellationToken cancellationToken = default);

    /// <summary>Active, non-deleted tasks that depend on <paramref name="prerequisiteTaskId"/>.</summary>
    Task<IReadOnlyList<ScheduledTask>> GetActiveDependantsAsync(Guid prerequisiteTaskId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The "All" rule (WP-6, FR-26): true when every other prerequisite of <paramref name="dependantId"/> has finished
    /// with Success since the dependant was last triggered by its dependencies. A prerequisite whose latest final run
    /// since then failed or timed out does not count. <paramref name="succeededTaskId"/> has just succeeded.
    /// </summary>
    Task<bool> AllPrerequisitesSucceededAsync(Guid dependantId, Guid succeededTaskId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Locks and returns active schedules of active tasks whose next run is due, skipping schedules another host
    /// has locked (FOR UPDATE SKIP LOCKED), so two hosts never queue the same run. Only tasks whose type is in
    /// <paramref name="taskTypes"/> are returned, so a task without an installed handler is never queued (WP-1).
    /// Call inside a transaction.
    /// </summary>
    Task<IReadOnlyList<Schedule>> LockDueSchedulesAsync(DateTime nowUtc, int limit, IReadOnlyCollection<string> taskTypes,
        CancellationToken cancellationToken = default);

    /// <summary>Earliest next run of an active schedule of an active task of a registered type, for the engine's sleep time.</summary>
    Task<DateTime?> GetEarliestNextRunAsync(IReadOnlyCollection<string> taskTypes, CancellationToken cancellationToken = default);
}

public interface IQueueRepository
{
    void Add(QueueEntry entry);

    Task<QueueEntry?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Claims the highest-priority waiting entry that is due and whose task type this host can run, with
    /// SELECT ... FOR UPDATE SKIP LOCKED and a lease of the task's time-out plus the grace period (FR-20, FR-25).
    /// Runs as its own statement. Returns the claimed entry's id, or null when nothing is eligible.
    /// </summary>
    Task<Guid?> ClaimNextAsync(Guid workerId, IReadOnlyCollection<string> taskTypes, DateTime nowUtc, int leaseGraceSeconds,
        CancellationToken cancellationToken = default);

    /// <summary>Locks and returns claimed entries whose lease has expired (Algorithm 6.11). Call inside a transaction.</summary>
    Task<IReadOnlyList<QueueEntry>> LockExpiredLeasesAsync(DateTime nowUtc, int limit, CancellationToken cancellationToken = default);

    /// <summary>Earliest scheduled time of a waiting entry this host could run, for the engine's sleep time.</summary>
    Task<DateTime?> GetEarliestWaitingAsync(IReadOnlyCollection<string> taskTypes, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<QueueEntry>> GetWaitingForTaskAsync(Guid taskId, CancellationToken cancellationToken = default);
}

public interface IExecutionRepository
{
    void Add(TaskExecution execution);
    void AddError(ErrorLogEntry error);
    Task<TaskExecution?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<TaskExecution?> GetForQueueEntryAsync(Guid queueId, CancellationToken cancellationToken = default);
}

public interface IWorkerRepository
{
    Task<IReadOnlyList<WorkerThread>> GetForHostAsync(string hostName, string poolName, CancellationToken cancellationToken = default);
    Task<WorkerThread?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    void Add(WorkerThread worker);

    /// <summary>Updates the heartbeat of this host's running workers.</summary>
    Task HeartbeatAsync(string hostName, DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks workers of other hosts whose heartbeat is older than <paramref name="staleBeforeUtc"/> as Stopped.
    /// The workers of <paramref name="thisHost"/> are skipped: the running engine knows they are alive (WP-1).
    /// </summary>
    Task<int> MarkStaleAsStoppedAsync(DateTime staleBeforeUtc, DateTime nowUtc, string thisHost, CancellationToken cancellationToken = default);

    /// <summary>Sets the listed workers of this host back to Idle when something marked them Stopped. Returns how many changed.</summary>
    Task<int> ReviveStoppedAsync(string hostName, IReadOnlyCollection<Guid> workerIds, DateTime nowUtc, CancellationToken cancellationToken = default);

    Task MarkHostStoppedAsync(string hostName, DateTime nowUtc, CancellationToken cancellationToken = default);
}

public interface IConfigurationRepository
{
    Task<IReadOnlyList<ConfigurationSetting>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<ConfigurationSetting?> FindAsync(Guid? taskId, string key, CancellationToken cancellationToken = default);
    void Add(ConfigurationSetting setting);
    void Remove(ConfigurationSetting setting);
    Task<IReadOnlyList<LoggingProviderSetting>> GetLoggingProvidersAsync(CancellationToken cancellationToken = default);
    Task<LoggingProviderSetting?> GetLoggingProviderAsync(Guid id, CancellationToken cancellationToken = default);
}

/// <summary>Deletes old history in bulk for the Log Clean-up task (FR-35).</summary>
public interface IRetentionStore
{
    /// <summary>
    /// Deletes processed and cancelled queue entries before <paramref name="queueBeforeUtc"/>; finished executions, their
    /// errors and log entries before <paramref name="historyBeforeUtc"/>; and the sign-in audit and the Security log
    /// entries (user management, login bypass) before <paramref name="auditBeforeUtc"/> (FR-35, WP-3c).
    /// </summary>
    Task<RetentionResult> PurgeAsync(DateTime queueBeforeUtc, DateTime historyBeforeUtc, DateTime auditBeforeUtc,
        CancellationToken cancellationToken = default);
}

public sealed record RetentionResult(int QueueEntries, int Executions, int Errors, int LogEntries, int AuditEntries = 0, int LogFiles = 0);

/// <summary>Deletes the File logging provider's files older than the retention period (WP-3b).</summary>
public interface ILogFileCleaner
{
    /// <summary>Returns the number of files deleted.</summary>
    int DeleteOlderThan(DateTime beforeUtc);
}
