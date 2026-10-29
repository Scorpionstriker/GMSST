using System.Reflection;
using Scheduler.Application.Common;
using Scheduler.Application.Common.Persistence;
using Scheduler.Application.Configuration;
using Scheduler.Application.Dependencies;
using Scheduler.Domain.Entities;
using Scheduler.Domain.Enums;
using Scheduler.Extensibility.Contracts;

namespace Scheduler.UnitTests.InMemory;

/// <summary>
/// An in-memory database behind the application layer's persistence interfaces, so use cases, security rules and
/// scheduling rules can be tested without PostgreSQL. It keeps the rules the tests rely on: unique task names
/// ignoring case (FR-05), optimistic concurrency through row versions (NFR-17) and the queue claim order (FR-20).
/// PostgreSQL-only behaviour (SKIP LOCKED between connections, advisory locks, constraints, migrations, pg_dump) is
/// checked by the manual procedures MC-17 to MC-22 in docs/Test_Cases.md.
/// </summary>
public sealed class InMemoryDatabase : IUnitOfWorkFactory
{
    private readonly object _gate = new();
    private uint _nextVersion = 1;

    internal List<ScheduledTask> Tasks { get; } = [];
    internal List<QueueEntry> Queue { get; } = [];
    internal List<TaskExecution> Executions { get; } = [];
    internal List<ErrorLogEntry> Errors { get; } = [];
    internal List<WorkerThread> Workers { get; } = [];
    internal List<ConfigurationSetting> Settings { get; } = [];
    internal List<LoggingProviderSetting> Providers { get; } = [];

    public IUnitOfWork Create() => new UnitOfWork(this);

    /// <summary>Runs <paramref name="action"/> under the database lock, as one statement would run in PostgreSQL.</summary>
    internal T Locked<T>(Func<T> action)
    {
        lock (_gate)
        {
            return action();
        }
    }

    internal uint NextVersion() => _nextVersion++;

    private long _commits;
    private readonly Dictionary<object, long> _stamps = new(ReferenceEqualityComparer.Instance);

    /// <summary>The commit that last wrote <paramref name="entity"/>; call under the lock.</summary>
    internal long StampOf(object entity) => _stamps.GetValueOrDefault(entity);

    /// <summary>Records that <paramref name="entities"/> were written by a new commit; call under the lock.</summary>
    internal void Stamp(IEnumerable<object> entities)
    {
        var commit = ++_commits;
        foreach (var entity in entities)
        {
            _stamps[entity] = commit;
        }
    }

    /// <summary>Sets a private or protected property, as EF Core does when it materialises an entity.</summary>
    internal static void SetProperty(object entity, string name, object? value)
    {
        for (var type = entity.GetType(); type is not null; type = type.BaseType)
        {
            var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (property?.SetMethod is not null)
            {
                property.SetValue(entity, value);
                return;
            }
        }

        throw new InvalidOperationException($"{entity.GetType().Name}.{name} cannot be set.");
    }

    private sealed class UnitOfWork : IUnitOfWork
    {
        private readonly InMemoryDatabase _db;
        private readonly List<ScheduledTask> _newTasks = [];
        private readonly List<QueueEntry> _newEntries = [];
        private readonly List<TaskExecution> _newExecutions = [];
        private readonly List<ErrorLogEntry> _newErrors = [];
        private readonly List<WorkerThread> _newWorkers = [];
        private readonly List<ConfigurationSetting> _newSettings = [];
        private readonly List<ConfigurationSetting> _removedSettings = [];
        private readonly Dictionary<object, uint> _loaded = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<object, (object Copy, long Stamp)> _snapshots = new(ReferenceEqualityComparer.Instance);

        public UnitOfWork(InMemoryDatabase db)
        {
            _db = db;
            Tasks = new TaskRepository(this);
            Queue = new QueueRepository(this);
            Executions = new ExecutionRepository(this);
            Workers = new WorkerRepository(this);
            Configuration = new ConfigurationRepository(this);
        }

        public ITaskRepository Tasks { get; }
        public IQueueRepository Queue { get; }
        public IExecutionRepository Executions { get; }
        public IWorkerRepository Workers { get; }
        public IConfigurationRepository Configuration { get; }

        private InMemoryDatabase Db => _db;

        /// <summary>Remembers the version an entity had when this unit of work read it.</summary>
        private T? Track<T>(T? entity, Func<T, uint> version) where T : class
        {
            if (entity is not null)
            {
                _loaded.TryAdd(entity, version(entity));
                Load(entity);
            }

            return entity;
        }

        /// <summary>
        /// Keeps a copy of an entity as it was read. Entities are shared objects, so a unit of work that is disposed
        /// without saving puts the copied values back, as a rolled-back transaction would.
        /// </summary>
        private T? Load<T>(T? entity) where T : class
        {
            if (entity is not null && !_snapshots.ContainsKey(entity))
            {
                _db.Locked(() => _snapshots[entity] = (MemberwiseCloneMethod.Invoke(entity, null)!, _db.StampOf(entity)));
            }

            return entity;
        }

        private static readonly MethodInfo MemberwiseCloneMethod =
            typeof(object).GetMethod(nameof(MemberwiseClone), BindingFlags.Instance | BindingFlags.NonPublic)!;

        private static void CopyFields(object from, object to)
        {
            for (var type = to.GetType(); type is not null; type = type.BaseType)
            {
                foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    field.SetValue(to, field.GetValue(from));
                }
            }
        }

        public Task<IAsyncDisposable> BeginTransactionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IAsyncDisposable>(new NoTransaction());

        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            _db.Locked(() =>
            {
                foreach (var (entity, seen) in _loaded)
                {
                    if (CurrentVersion(entity) != seen)
                    {
                        throw new ConcurrencyConflictException($"The {entity.GetType().Name} was changed by someone else.");
                    }
                }

                foreach (var task in _newTasks.Concat(_loaded.Keys.OfType<ScheduledTask>()))
                {
                    var lowered = task.Name.Trim().ToLowerInvariant();
                    if (!task.IsDeleted && _db.Tasks.Concat(_newTasks).Any(t => t != task && !t.IsDeleted && t.Name.Trim().ToLowerInvariant() == lowered))
                    {
                        throw new UniqueConstraintException("ux_task_name_active");
                    }

                    if (task.SystemKey is not null && _db.Tasks.Concat(_newTasks).Any(t => t != task && t.SystemKey == task.SystemKey))
                    {
                        throw new UniqueConstraintException("ux_task_system_key");
                    }
                }

                _db.Tasks.AddRange(_newTasks);
                foreach (var entry in _newEntries)
                {
                    InMemoryDatabase.SetProperty(entry, nameof(QueueEntry.Task), _db.Tasks.Single(t => t.Id == entry.TaskId));
                }

                _db.Queue.AddRange(_newEntries);
                _db.Executions.AddRange(_newExecutions);
                _db.Errors.AddRange(_newErrors);
                _db.Workers.AddRange(_newWorkers);
                _db.Settings.AddRange(_newSettings);
                _db.Settings.RemoveAll(_removedSettings.Contains);

                foreach (var task in _db.Tasks)
                {
                    foreach (var schedule in task.Schedules.Where(s => s.Task is null))
                    {
                        InMemoryDatabase.SetProperty(schedule, nameof(Schedule.Task), task);
                    }
                }

                // Every saved row gets a new version, as PostgreSQL's xmin changes on every update.
                foreach (var entity in _newTasks.Concat(_loaded.Keys.OfType<ScheduledTask>()))
                {
                    InMemoryDatabase.SetProperty(entity, nameof(ScheduledTask.Version), _db.NextVersion());
                }

                foreach (var entity in _newSettings.Concat(_loaded.Keys.OfType<ConfigurationSetting>()))
                {
                    InMemoryDatabase.SetProperty(entity, nameof(ConfigurationSetting.Version), _db.NextVersion());
                }

                foreach (var entity in _loaded.Keys.OfType<LoggingProviderSetting>())
                {
                    InMemoryDatabase.SetProperty(entity, nameof(LoggingProviderSetting.Version), _db.NextVersion());
                }

                _newTasks.Clear();
                _newEntries.Clear();
                _newExecutions.Clear();
                _newErrors.Clear();
                _newWorkers.Clear();
                _newSettings.Clear();
                _removedSettings.Clear();
                foreach (var key in _loaded.Keys.ToList())
                {
                    _loaded[key] = CurrentVersion(key);
                }

                _db.Stamp(_snapshots.Keys);
                foreach (var key in _snapshots.Keys.ToList())
                {
                    _snapshots[key] = (MemberwiseCloneMethod.Invoke(key, null)!, _db.StampOf(key));
                }

                return 0;
            });
            return Task.CompletedTask;
        }

        private static uint CurrentVersion(object entity) => entity switch
        {
            ScheduledTask t => t.Version,
            ConfigurationSetting s => s.Version,
            LoggingProviderSetting p => p.Version,
            _ => 0
        };

        /// <summary>Changes that were not saved are undone.</summary>
        public ValueTask DisposeAsync()
        {
            _db.Locked(() =>
            {
                // Only entities nobody has saved since they were read, so another unit of work's saved change is kept.
                foreach (var (entity, snapshot) in _snapshots.Where(s => _db.StampOf(s.Key) == s.Value.Stamp))
                {
                    CopyFields(snapshot.Copy, entity);
                }

                _snapshots.Clear();
                return 0;
            });
            return ValueTask.CompletedTask;
        }

        private sealed class NoTransaction : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }

        private sealed class TaskRepository(UnitOfWork uow) : ITaskRepository
        {
            private IEnumerable<ScheduledTask> All => uow.Db.Tasks.Concat(uow._newTasks);

            public Task<ScheduledTask?> GetAsync(Guid id, bool includeDeleted = false, CancellationToken cancellationToken = default) =>
                Task.FromResult(uow.Track(uow.Db.Locked(() => All.FirstOrDefault(t => t.Id == id && (includeDeleted || !t.IsDeleted))), t => t.Version));

            public Task<ScheduledTask?> FindByNameAsync(string name, bool includeDeleted, CancellationToken cancellationToken = default)
            {
                var lowered = name.Trim().ToLowerInvariant();
                return Task.FromResult(uow.Track(uow.Db.Locked(() => All
                    .Where(t => t.Name.ToLowerInvariant() == lowered && (includeDeleted || !t.IsDeleted))
                    .OrderBy(t => t.IsDeleted)
                    .FirstOrDefault()), t => t.Version));
            }

            public Task<ScheduledTask?> FindBySystemKeyAsync(string systemKey, CancellationToken cancellationToken = default) =>
                Task.FromResult(uow.Track(uow.Db.Locked(() => All.FirstOrDefault(t => t.SystemKey == systemKey)), t => t.Version));

            public Task<bool> NameExistsAsync(string name, Guid? exceptTaskId, CancellationToken cancellationToken = default)
            {
                var lowered = name.Trim().ToLowerInvariant();
                return Task.FromResult(uow.Db.Locked(() => All.Any(t => !t.IsDeleted && t.Name.ToLowerInvariant() == lowered && t.Id != exceptTaskId)));
            }

            public Task<IReadOnlyList<Guid>> GetExistingIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default) =>
                Task.FromResult<IReadOnlyList<Guid>>(uow.Db.Locked(() => All.Where(t => !t.IsDeleted && ids.Contains(t.Id)).Select(t => t.Id).ToList()));

            public void Add(ScheduledTask task) => uow._newTasks.Add(task);

            public void SetOriginalVersion(ScheduledTask task, uint version) => uow._loaded[task] = version;

            public Task<IReadOnlyList<DependencyEdge>> GetDependencyEdgesAsync(CancellationToken cancellationToken = default) =>
                Task.FromResult<IReadOnlyList<DependencyEdge>>(uow.Db.Locked(() =>
                {
                    var live = All.Where(t => !t.IsDeleted).Select(t => t.Id).ToHashSet();
                    return All.Where(t => !t.IsDeleted)
                        .SelectMany(t => t.Dependencies)
                        .Where(d => live.Contains(d.DependsOnTaskId))
                        .Select(d => new DependencyEdge(d.TaskId, d.DependsOnTaskId))
                        .ToList();
                }));

            public Task<IReadOnlyList<ScheduledTask>> GetActiveDependantsAsync(Guid prerequisiteTaskId, CancellationToken cancellationToken = default) =>
                Task.FromResult<IReadOnlyList<ScheduledTask>>(uow.Db.Locked(() => All
                    .Where(t => !t.IsDeleted && t.Status == ScheduledTaskStatus.Active && t.Dependencies.Any(d => d.DependsOnTaskId == prerequisiteTaskId))
                    .Select(t => uow.Track(t, x => x.Version)!)
                    .ToList()));

            public Task<bool> AllPrerequisitesSucceededAsync(Guid dependantId, Guid succeededTaskId, CancellationToken cancellationToken = default) =>
                Task.FromResult(uow.Db.Locked(() =>
                {
                    var lastQueued = uow.Db.Queue.Where(q => q.TaskId == dependantId && q.TriggerType == TriggerType.Dependency)
                        .Max(q => (DateTime?)q.EnqueuedAtUtc);
                    var lastRun = uow.Db.Executions.Where(e => e.TaskId == dependantId && e.TriggerType == TriggerType.Dependency)
                        .Max(e => (DateTime?)e.ScheduledTimeUtc);
                    var since = lastQueued > lastRun || lastRun is null ? lastQueued : lastRun;
                    var dependant = All.Single(t => t.Id == dependantId);
                    var others = dependant.Dependencies.Select(d => d.DependsOnTaskId)
                        .Where(id => id != succeededTaskId && All.Any(t => t.Id == id && !t.IsDeleted));

                    return others.All(prerequisite => uow.Db.Executions
                        .Where(e => e.TaskId == prerequisite && e.EndTimeUtc != null && (since == null || e.EndTimeUtc > since)
                                    && e.Status is ExecutionStatus.Success or ExecutionStatus.Failed or ExecutionStatus.TimedOut)
                        .OrderByDescending(e => e.EndTimeUtc)
                        .Select(e => (ExecutionStatus?)e.Status)
                        .FirstOrDefault() == ExecutionStatus.Success);
                }));

            public Task<IReadOnlyList<Schedule>> LockDueSchedulesAsync(DateTime nowUtc, int limit, IReadOnlyCollection<string> taskTypes,
                CancellationToken cancellationToken = default) =>
                Task.FromResult<IReadOnlyList<Schedule>>(uow.Db.Locked(() => All
                    .Where(t => t.Status == ScheduledTaskStatus.Active && !t.IsDeleted && taskTypes.Contains(t.TaskType))
                    .SelectMany(t => t.Schedules)
                    .Where(s => s.IsActive && !s.IsDeleted && s.NextRunTimeUtc <= nowUtc)
                    .OrderBy(s => s.NextRunTimeUtc)
                    .Take(limit)
                    .Select(s => { uow.Track(s.Task, t => t.Version); return s; })
                    .ToList()));

            public Task<DateTime?> GetEarliestNextRunAsync(IReadOnlyCollection<string> taskTypes, CancellationToken cancellationToken = default) =>
                Task.FromResult(uow.Db.Locked(() => All
                    .Where(t => t.Status == ScheduledTaskStatus.Active && !t.IsDeleted && taskTypes.Contains(t.TaskType))
                    .SelectMany(t => t.Schedules)
                    .Where(s => s.IsActive && !s.IsDeleted && s.NextRunTimeUtc != null)
                    .Min(s => s.NextRunTimeUtc)));
        }

        private sealed class QueueRepository(UnitOfWork uow) : IQueueRepository
        {
            public void Add(QueueEntry entry) => uow._newEntries.Add(entry);

            public Task<QueueEntry?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
                Task.FromResult(uow.Load(uow.Db.Locked(() => uow.Db.Queue.Concat(uow._newEntries).FirstOrDefault(q => q.Id == id))));

            /// <summary>The claim of Phase 4, Section 5.5, done under the database lock so each entry is claimed once.</summary>
            public Task<Guid?> ClaimNextAsync(Guid workerId, IReadOnlyCollection<string> taskTypes, DateTime nowUtc, int leaseGraceSeconds,
                CancellationToken cancellationToken = default) =>
                Task.FromResult(uow.Db.Locked(() =>
                {
                    var entry = uow.Db.Queue
                        .Where(q => q.Status == QueueStatus.Waiting && q.ScheduledTimeUtc <= nowUtc && taskTypes.Contains(q.Task!.TaskType))
                        .OrderBy(q => q.Priority).ThenBy(q => q.ScheduledTimeUtc).ThenBy(q => q.EnqueuedAtUtc).ThenBy(q => q.Id)
                        .FirstOrDefault();
                    if (entry is null)
                    {
                        return (Guid?)null;
                    }

                    InMemoryDatabase.SetProperty(entry, nameof(QueueEntry.Status), QueueStatus.Claimed);
                    InMemoryDatabase.SetProperty(entry, nameof(QueueEntry.ClaimedBy), workerId);
                    InMemoryDatabase.SetProperty(entry, nameof(QueueEntry.ClaimedAtUtc), nowUtc);
                    InMemoryDatabase.SetProperty(entry, nameof(QueueEntry.ClaimedUntilUtc), nowUtc.AddSeconds(entry.Task!.TimeoutSeconds + leaseGraceSeconds));
                    uow.Db.Stamp([entry]);
                    return entry.Id;
                }));

            public Task<IReadOnlyList<QueueEntry>> LockExpiredLeasesAsync(DateTime nowUtc, int limit, CancellationToken cancellationToken = default) =>
                Task.FromResult<IReadOnlyList<QueueEntry>>(uow.Db.Locked(() => uow.Db.Queue
                    .Where(q => q.Status == QueueStatus.Claimed && q.ClaimedUntilUtc < nowUtc)
                    .OrderBy(q => q.ClaimedUntilUtc)
                    .Take(limit)
                    .ToList()).Select(q => uow.Load(q)!).ToList());

            public Task<DateTime?> GetEarliestWaitingAsync(IReadOnlyCollection<string> taskTypes, CancellationToken cancellationToken = default) =>
                Task.FromResult(uow.Db.Locked(() => uow.Db.Queue
                    .Where(q => q.Status == QueueStatus.Waiting && taskTypes.Contains(q.Task!.TaskType))
                    .Min(q => (DateTime?)q.ScheduledTimeUtc)));

            public Task<IReadOnlyList<QueueEntry>> GetWaitingForTaskAsync(Guid taskId, CancellationToken cancellationToken = default) =>
                Task.FromResult<IReadOnlyList<QueueEntry>>(uow.Db.Locked(() =>
                    uow.Db.Queue.Where(q => q.TaskId == taskId && q.Status == QueueStatus.Waiting).ToList()).Select(q => uow.Load(q)!).ToList());
        }

        private sealed class ExecutionRepository(UnitOfWork uow) : IExecutionRepository
        {
            public void Add(TaskExecution execution) => uow._newExecutions.Add(execution);

            public void AddError(ErrorLogEntry error) => uow._newErrors.Add(error);

            public Task<TaskExecution?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
                Task.FromResult(uow.Load(uow.Db.Locked(() => uow.Db.Executions.Concat(uow._newExecutions).FirstOrDefault(e => e.Id == id))));

            public Task<TaskExecution?> GetForQueueEntryAsync(Guid queueId, CancellationToken cancellationToken = default) =>
                Task.FromResult(uow.Load(uow.Db.Locked(() => uow.Db.Executions.Concat(uow._newExecutions)
                    .Where(e => e.QueueId == queueId).OrderByDescending(e => e.StartTimeUtc).FirstOrDefault())));
        }

        private sealed class WorkerRepository(UnitOfWork uow) : IWorkerRepository
        {
            private IEnumerable<WorkerThread> All => uow.Db.Workers.Concat(uow._newWorkers);

            public Task<IReadOnlyList<WorkerThread>> GetForHostAsync(string hostName, string poolName, CancellationToken cancellationToken = default) =>
                Task.FromResult<IReadOnlyList<WorkerThread>>(uow.Db.Locked(() => All.Where(w => w.HostName == hostName && w.ThreadPoolName == poolName).ToList()));

            public Task<WorkerThread?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
                Task.FromResult(uow.Db.Locked(() => All.FirstOrDefault(w => w.Id == id)));

            public void Add(WorkerThread worker) => uow._newWorkers.Add(worker);

            public Task HeartbeatAsync(string hostName, DateTime nowUtc, CancellationToken cancellationToken = default)
            {
                uow.Db.Locked(() =>
                {
                    foreach (var worker in uow.Db.Workers.Where(w => w.HostName == hostName && w.Status != WorkerStatus.Stopped))
                    {
                        InMemoryDatabase.SetProperty(worker, nameof(WorkerThread.LastHeartbeatUtc), nowUtc);
                    }

                    return 0;
                });
                return Task.CompletedTask;
            }

            public Task<int> MarkStaleAsStoppedAsync(DateTime staleBeforeUtc, DateTime nowUtc, string thisHost, CancellationToken cancellationToken = default) =>
                Task.FromResult(uow.Db.Locked(() =>
                {
                    var stale = uow.Db.Workers.Where(w => w.HostName != thisHost && w.Status != WorkerStatus.Stopped && w.LastHeartbeatUtc < staleBeforeUtc).ToList();
                    stale.ForEach(w => InMemoryDatabase.SetProperty(w, nameof(WorkerThread.Status), WorkerStatus.Stopped));
                    return stale.Count;
                }));

            public Task<int> ReviveStoppedAsync(string hostName, IReadOnlyCollection<Guid> workerIds, DateTime nowUtc, CancellationToken cancellationToken = default) =>
                Task.FromResult(uow.Db.Locked(() =>
                {
                    var stopped = uow.Db.Workers.Where(w => w.HostName == hostName && workerIds.Contains(w.Id) && w.Status == WorkerStatus.Stopped).ToList();
                    foreach (var worker in stopped)
                    {
                        InMemoryDatabase.SetProperty(worker, nameof(WorkerThread.Status), WorkerStatus.Idle);
                        InMemoryDatabase.SetProperty(worker, nameof(WorkerThread.LastHeartbeatUtc), nowUtc);
                    }

                    return stopped.Count;
                }));

            public Task MarkHostStoppedAsync(string hostName, DateTime nowUtc, CancellationToken cancellationToken = default)
            {
                uow.Db.Locked(() =>
                {
                    foreach (var worker in uow.Db.Workers.Where(w => w.HostName == hostName))
                    {
                        InMemoryDatabase.SetProperty(worker, nameof(WorkerThread.Status), WorkerStatus.Stopped);
                    }

                    return 0;
                });
                return Task.CompletedTask;
            }
        }

        private sealed class ConfigurationRepository(UnitOfWork uow) : IConfigurationRepository
        {
            public Task<IReadOnlyList<ConfigurationSetting>> GetAllAsync(CancellationToken cancellationToken = default) =>
                Task.FromResult<IReadOnlyList<ConfigurationSetting>>(uow.Db.Locked(() =>
                    uow.Db.Settings.Select(s => uow.Track(s, x => x.Version)!).ToList()));

            public Task<ConfigurationSetting?> FindAsync(Guid? taskId, string key, CancellationToken cancellationToken = default) =>
                Task.FromResult(uow.Track(uow.Db.Locked(() =>
                    uow.Db.Settings.Concat(uow._newSettings).FirstOrDefault(s => s.TaskId == taskId && s.Key == key)), x => x.Version));

            public void Add(ConfigurationSetting setting) => uow._newSettings.Add(setting);

            public void Remove(ConfigurationSetting setting)
            {
                uow._loaded.Remove(setting);
                uow._removedSettings.Add(setting);
            }

            public Task<IReadOnlyList<LoggingProviderSetting>> GetLoggingProvidersAsync(CancellationToken cancellationToken = default) =>
                Task.FromResult<IReadOnlyList<LoggingProviderSetting>>(uow.Db.Locked(() => uow.Db.Providers.ToList()));

            public Task<LoggingProviderSetting?> GetLoggingProviderAsync(Guid id, CancellationToken cancellationToken = default) =>
                Task.FromResult(uow.Track(uow.Db.Locked(() => uow.Db.Providers.FirstOrDefault(p => p.Id == id)), x => x.Version));
        }
    }
}

/// <summary>Settings read from the in-memory database, falling back to the defaults, as the cached provider does.</summary>
public sealed class InMemorySettingsProvider(InMemoryDatabase db) : ISettingsProvider
{
    public Task<int> GetAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(Read(null, key) ?? SettingDefinitions.DefaultOf(key));

    public Task<int> GetForTaskAsync(string key, Guid taskId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Read(taskId, key) ?? Read(null, key) ?? SettingDefinitions.DefaultOf(key));

    public async Task<int> GetIntAsync(string key, int defaultValue, CancellationToken cancellationToken = default) =>
        Read(null, key) ?? (SettingDefinitions.Find(key) is null ? defaultValue : await GetAsync(key, cancellationToken));

    public async Task<EngineSettings> GetEngineSettingsAsync(CancellationToken cancellationToken = default) => new(
        await GetAsync(SettingKeys.MaxConcurrency, cancellationToken),
        await GetAsync(SettingKeys.MaxPollIntervalSeconds, cancellationToken),
        await GetAsync(SettingKeys.LeaseGraceSeconds, cancellationToken),
        await GetAsync(SettingKeys.RunAllMisfireCap, cancellationToken),
        await GetAsync(SettingKeys.ShutdownGraceSeconds, cancellationToken),
        await GetAsync(SettingKeys.WorkerStaleSeconds, cancellationToken));

    public void Invalidate()
    {
    }

    private int? Read(Guid? taskId, string key) => db.Locked(() =>
        db.Settings.FirstOrDefault(s => s.TaskId == taskId && s.Key == key) is { } setting && int.TryParse(setting.Value, out var value)
            ? value
            : (int?)null);
}

/// <summary>Accounts and the login audit kept in memory for the Login and Security module.</summary>
public sealed class InMemoryUserAccountStore : IUserAccountStore
{
    private readonly object _gate = new();
    private readonly List<UserAccount> _users = [];
    private uint _nextVersion = 1;

    public List<LoginAuditRecord> Audit { get; } = [];

    public Task<bool> AnyUsersAsync(CancellationToken cancellationToken = default) => Task.FromResult(Locked(() => _users.Count > 0));

    public Task<UserAccount?> FindByUsernameAsync(string username, CancellationToken cancellationToken = default) =>
        Task.FromResult(Locked(() => _users.FirstOrDefault(u => u.Username.Equals(username.Trim(), StringComparison.OrdinalIgnoreCase)) is { } u ? Copy(u, u.Version) : null));

    public Task<UserAccount?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Locked(() => _users.FirstOrDefault(u => u.Id == id) is { } u ? Copy(u, u.Version) : null));

    public Task<IReadOnlyList<UserAccount>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<UserAccount>>(Locked(() => _users.OrderBy(u => u.Username).Select(u => Copy(u, u.Version)).ToList()));

    public Task<bool> UsernameExistsAsync(string username, Guid? exceptUserId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Locked(() => _users.Any(u => u.Id != exceptUserId && u.Username.Equals(username.Trim(), StringComparison.OrdinalIgnoreCase))));

    public Task CreateAsync(UserAccount user, CancellationToken cancellationToken = default)
    {
        Locked(() =>
        {
            if (_users.Any(u => u.Username.Equals(user.Username.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                throw new UniqueConstraintException("ux_app_user_username");
            }

            _users.Add(Copy(user, _nextVersion++));
            return 0;
        });
        return Task.CompletedTask;
    }

    public Task UpdateAsync(UserAccount user, CancellationToken cancellationToken = default)
    {
        Locked(() =>
        {
            var index = _users.FindIndex(u => u.Id == user.Id);
            if (index < 0 || _users[index].Version != user.Version)
            {
                throw new UserAccountConflictException("The user was changed by someone else. Open it again and repeat your change.");
            }

            _users[index] = Copy(user, _nextVersion++);
            return 0;
        });
        return Task.CompletedTask;
    }

    public Task AddLoginAuditAsync(LoginAuditRecord record, CancellationToken cancellationToken = default)
    {
        Locked(() => { Audit.Add(record); return 0; });
        return Task.CompletedTask;
    }

    private T Locked<T>(Func<T> action)
    {
        lock (_gate)
        {
            return action();
        }
    }

    private static UserAccount Copy(UserAccount u, uint version) => new()
    {
        Id = u.Id,
        Username = u.Username.Trim(),
        PasswordHash = u.PasswordHash,
        PasswordSalt = u.PasswordSalt,
        Role = u.Role,
        FailedLoginCount = u.FailedLoginCount,
        LockoutEndUtc = u.LockoutEndUtc,
        LastLoginAtUtc = u.LastLoginAtUtc,
        IsActive = u.IsActive,
        CreatedAtUtc = u.CreatedAtUtc,
        Version = version
    };
}

/// <summary>Keeps every log entry in memory, so tests can check what was logged and audited.</summary>
public sealed class InMemorySchedulerLogger : ISchedulerLogger
{
    private readonly object _gate = new();
    private readonly List<(SchedulerLogLevel Level, string Source, string Message, IReadOnlyDictionary<string, object?>? Properties)> _entries = [];

    public IReadOnlyList<(SchedulerLogLevel Level, string Source, string Message, IReadOnlyDictionary<string, object?>? Properties)> Entries
    {
        get
        {
            lock (_gate)
            {
                return _entries.ToList();
            }
        }
    }

    public Task LogAsync(SchedulerLogLevel level, string source, string message, Guid? executionId = null,
        IReadOnlyDictionary<string, object?>? properties = null, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _entries.Add((level, source, message, properties));
        }

        return Task.CompletedTask;
    }
}
