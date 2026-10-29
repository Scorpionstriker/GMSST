using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Scheduler.Application.Common;
using Scheduler.Application.Common.Persistence;
using Scheduler.Application.Configuration;
using Scheduler.Application.Cron;
using Scheduler.Application.Tasks;
using Scheduler.Domain.Entities;
using Scheduler.Domain.Enums;
using Scheduler.Engine.Execution;
using Scheduler.Extensibility.Events;

namespace Scheduler.Engine;

/// <summary>
/// The scheduling loop (Algorithm 6.2; FR-18 to FR-21, FR-32 to FR-34). Each cycle sends a heartbeat, recovers
/// expired leases, queues due schedules, claims waiting entries while a worker is free, and then sleeps until the
/// next run or waiting entry is due, at most MaxPollIntervalSeconds and at least <see cref="MinimumDelay"/>. A wake
/// signal (a change in the Admin application or a worker becoming free) ends the sleep early.
/// </summary>
/// <remarks>
/// Fix for the busy loop of D3: the sleep time only considers entries this host can claim (Waiting, a registered
/// task type) and only when a worker is free, and it is never shorter than <see cref="MinimumDelay"/>.
/// </remarks>
public sealed class SchedulerEngine : IEngineControl
{
    internal static TimeSpan MinimumDelay { get; set; } = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ErrorDelay = TimeSpan.FromSeconds(5);
    private const int BatchSize = 100;

    private readonly IUnitOfWorkFactory _unitOfWorkFactory;
    private readonly ISettingsProvider _settings;
    private readonly ITaskTypeRegistry _registry;
    private readonly TaskExecutor _executor;
    private readonly FailureRecorder _failures;
    private readonly EngineSignal _signal;
    private readonly ISchedulerEventPublisher _events;
    private readonly ISchedulerLogger _log;
    private readonly TimeProvider _clock;
    private readonly ILogger<SchedulerEngine> _logger;
    private readonly IEngineInstanceLock _instanceLock;
    private readonly string _hostName;

    private readonly ConcurrentQueue<Guid> _idleWorkers = new();
    private readonly ConcurrentDictionary<Guid, (Task Run, CancellationTokenSource Stop)> _running = new();
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private CancellationTokenSource? _loopStop;
    private Task? _loop;
    private volatile EngineState _state = EngineState.Stopped;
    private volatile string? _blockedMessage;

    internal SchedulerEngine(IUnitOfWorkFactory unitOfWorkFactory, ISettingsProvider settings, ITaskTypeRegistry registry, TaskExecutor executor,
        FailureRecorder failures, EngineSignal signal, ISchedulerEventPublisher events, ISchedulerLogger log, TimeProvider clock,
        EngineOptions options, IEngineInstanceLock instanceLock, ILogger<SchedulerEngine> logger)
    {
        _unitOfWorkFactory = unitOfWorkFactory;
        _settings = settings;
        _registry = registry;
        _executor = executor;
        _failures = failures;
        _signal = signal;
        _events = events;
        _log = log;
        _clock = clock;
        _logger = logger;
        _instanceLock = instanceLock;
        _hostName = options.ResolveHostName();
    }

    public EngineState State => _state;

    public string HostName => _hostName;

    /// <summary>
    /// Set when another engine already runs on this database, so this one stays Stopped (WP-1). The Admin application
    /// shows it as a banner; null when the engine holds the single-engine lock or has not tried to start.
    /// </summary>
    public string? BlockedMessage => _blockedMessage;

    /// <summary>Number of executions running in this host now.</summary>
    public int RunningCount => _running.Count;

    /// <summary>Number of completed scheduling cycles since start, used by tests of the idle behaviour.</summary>
    public long CycleCount => Interlocked.Read(ref _cycles);

    private long _cycles;

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    /// <summary>Registers the worker pool, recovers interrupted work and starts the loop (FR-33).</summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken);
        try
        {
            if (_state != EngineState.Stopped)
            {
                return;
            }

            // Only one engine may run per database (WP-1); a second one stays Stopped and says who holds the lock.
            var instance = await _instanceLock.TryAcquireAsync(_hostName, cancellationToken);
            if (!instance.Acquired)
            {
                _blockedMessage = instance.BlockedMessage;
                _events.Publish(new SchedulerEvent(SchedulerEventKind.EngineStateChanged, new EngineStateEventArgs(EngineState.Stopped)));
                await _log.LogAsync(SchedulerLogLevel.Warning, LogSources.Engine, _blockedMessage, cancellationToken: cancellationToken);
                return;
            }

            _blockedMessage = null;
            var settings = await _settings.GetEngineSettingsAsync(cancellationToken);
            await RegisterWorkersAsync(settings.MaxConcurrency, cancellationToken);
            await RecoverAsync(settings, cancellationToken);

            _loopStop = new CancellationTokenSource();
            var token = _loopStop.Token;
            _loop = Task.Run(() => LoopAsync(token), CancellationToken.None);
            SetState(EngineState.Running);
            await _log.LogAsync(SchedulerLogLevel.Info, LogSources.Engine, $"Scheduler engine started on {_hostName} with {settings.MaxConcurrency} workers.",
                cancellationToken: cancellationToken);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    /// <summary>
    /// Stops claiming work, waits up to ShutdownGraceSeconds for running tasks, then cancels the rest, which are
    /// recorded as Cancelled and queued again (FR-34).
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken);
        try
        {
            if (_state == EngineState.Stopped)
            {
                return;
            }

            _loopStop?.Cancel();
            if (_loop is not null)
            {
                await _loop;
            }

            await DrainAsync(cancellationToken);
            try
            {
                await using var unitOfWork = _unitOfWorkFactory.Create();
                await unitOfWork.Workers.MarkHostStoppedAsync(_hostName, Now, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Workers of {Host} could not be marked as stopped.", _hostName);
            }

            await _instanceLock.ReleaseAsync(CancellationToken.None);
            _idleWorkers.Clear();
            _loopStop?.Dispose();
            _loopStop = null;
            _loop = null;
            SetState(EngineState.Stopped);
            await _log.LogAsync(SchedulerLogLevel.Info, LogSources.Engine, $"Scheduler engine stopped on {_hostName}.", cancellationToken: CancellationToken.None);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    /// <summary>Stops queuing and claiming and waits for running tasks, for example before a restore (FR-72).</summary>
    public async Task PauseAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken);
        try
        {
            if (_state != EngineState.Running)
            {
                return;
            }

            SetState(EngineState.Paused);
            await DrainAsync(cancellationToken);
            await _log.LogAsync(SchedulerLogLevel.Info, LogSources.Engine, "Scheduler engine paused.", cancellationToken: cancellationToken);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async Task ResumeAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken);
        try
        {
            if (_state != EngineState.Paused)
            {
                return;
            }

            await RecoverAsync(await _settings.GetEngineSettingsAsync(cancellationToken), cancellationToken);
            SetState(EngineState.Running);
            _signal.Wake();
            await _log.LogAsync(SchedulerLogLevel.Info, LogSources.Engine, "Scheduler engine resumed.", cancellationToken: cancellationToken);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async Task RefreshAfterRestoreAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken);
        try
        {
            if (_state == EngineState.Stopped)
            {
                return;
            }

            // Paused, so nothing runs; the restored worker rows are replaced by this host's pool.
            await RegisterWorkersAsync((await _settings.GetEngineSettingsAsync(cancellationToken)).MaxConcurrency, cancellationToken);
            await _instanceLock.HeartbeatAsync(cancellationToken);
            await _log.LogAsync(SchedulerLogLevel.Info, LogSources.Engine, $"Workers of {_hostName} were registered again after a restore.",
                cancellationToken: cancellationToken);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    private async Task LoopAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            TimeSpan delay;
            try
            {
                delay = await RunCycleAsync(stop);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // The loop never ends on an error, for example while the database is unavailable (FR-32).
                _logger.LogError(ex, "Scheduler cycle failed.");
                await _log.LogAsync(SchedulerLogLevel.Error, LogSources.Engine, "Scheduler cycle failed: " + ex.Message, cancellationToken: CancellationToken.None);
                delay = ErrorDelay;
            }

            Interlocked.Increment(ref _cycles);
            try
            {
                await _signal.WaitAsync(delay, stop);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>One cycle. Returns how long to sleep before the next one.</summary>
    internal async Task<TimeSpan> RunCycleAsync(CancellationToken stop)
    {
        var settings = await _settings.GetEngineSettingsAsync(stop);
        var maxSleep = TimeSpan.FromSeconds(Math.Min(settings.MaxPollIntervalSeconds, Math.Max(10, settings.WorkerStaleSeconds / 3)));
        if (!await HoldsInstanceLockAsync(stop))
        {
            return maxSleep;
        }

        await using (var unitOfWork = _unitOfWorkFactory.Create())
        {
            // Safety net (WP-1): idle workers of this host that something marked Stopped are set back to Idle.
            var idle = _idleWorkers.ToArray();
            if (idle.Length > 0 && await unitOfWork.Workers.ReviveStoppedAsync(_hostName, idle, Now, stop) is var revived and > 0)
            {
                await _log.LogAsync(SchedulerLogLevel.Warning, LogSources.Engine,
                    $"{revived} worker{(revived == 1 ? " was" : "s were")} marked as stopped while the engine was running and have been restarted.",
                    cancellationToken: stop);
            }

            await unitOfWork.Workers.HeartbeatAsync(_hostName, Now, stop);
        }

        await RecoverAsync(settings, stop);
        if (_state != EngineState.Running)
        {
            return maxSleep;
        }

        await EnqueueDueAsync(settings, stop);
        await DispatchAsync(settings, stop);
        return await NextDelayAsync(maxSleep, stop);
    }

    /// <summary>
    /// Heartbeats the single-engine lock. When its connection was lost (for example while the database restarted),
    /// takes the lock again; if another engine took it meanwhile, this engine does no work until it gets it back.
    /// </summary>
    private async Task<bool> HoldsInstanceLockAsync(CancellationToken stop)
    {
        if (await _instanceLock.HeartbeatAsync(stop))
        {
            return true;
        }

        var instance = await _instanceLock.TryAcquireAsync(_hostName, stop);
        var message = instance.Acquired ? null : instance.BlockedMessage;
        if (message != _blockedMessage)
        {
            _blockedMessage = message;
            _events.Publish(new SchedulerEvent(SchedulerEventKind.EngineStateChanged, new EngineStateEventArgs(_state)));
            if (message is not null)
            {
                await _log.LogAsync(SchedulerLogLevel.Warning, LogSources.Engine, message, cancellationToken: stop);
            }
        }

        return instance.Acquired;
    }

    private async Task RegisterWorkersAsync(int count, CancellationToken cancellationToken)
    {
        await using var unitOfWork = _unitOfWorkFactory.Create();
        var now = Now;
        var workers = (await unitOfWork.Workers.GetForHostAsync(_hostName, WorkerThread.DefaultPoolName, cancellationToken)).ToList();
        while (workers.Count < count)
        {
            var worker = new WorkerThread(_hostName, WorkerThread.DefaultPoolName, now);
            unitOfWork.Workers.Add(worker);
            workers.Add(worker);
        }

        _idleWorkers.Clear();
        for (var i = 0; i < workers.Count; i++)
        {
            if (i < count)
            {
                workers[i].Start(now);
                _idleWorkers.Enqueue(workers[i].Id);
            }
            else
            {
                workers[i].MarkStopped(now);
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Algorithm 6.11: marks workers without a recent heartbeat as Stopped and handles claimed entries whose lease
    /// expired. An entry with a running execution is recorded as failed and retried under the normal rules; an
    /// entry that never started is returned to the queue.
    /// </summary>
    private async Task RecoverAsync(EngineSettings settings, CancellationToken cancellationToken)
    {
        var now = Now;
        await using var unitOfWork = _unitOfWorkFactory.Create();
        await unitOfWork.Workers.MarkStaleAsStoppedAsync(now.AddSeconds(-settings.WorkerStaleSeconds), now, _hostName, cancellationToken);

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var expired = await unitOfWork.Queue.LockExpiredLeasesAsync(now, BatchSize, cancellationToken);
        if (expired.Count == 0)
        {
            return;
        }

        foreach (var entry in expired)
        {
            var execution = await unitOfWork.Executions.GetForQueueEntryAsync(entry.Id, cancellationToken);
            var task = await unitOfWork.Tasks.GetAsync(entry.TaskId, includeDeleted: true, cancellationToken);
            if (execution is { Status: ExecutionStatus.Running } && task is not null)
            {
                await _failures.RecordAsync(unitOfWork, execution, entry, task, timedOut: false, "Interrupted",
                    "The run was interrupted because its host stopped unexpectedly.", null, now, cancellationToken);
            }
            else if (execution is null)
            {
                entry.ReleaseClaim();
            }
            else
            {
                entry.MarkProcessed();
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await unitOfWork.CommitTransactionAsync(cancellationToken);
        await _log.LogAsync(SchedulerLogLevel.Warning, LogSources.Engine, $"Recovered {expired.Count} interrupted queue entr{(expired.Count == 1 ? "y" : "ies")}.",
            cancellationToken: cancellationToken);
        _signal.Wake();
    }

    /// <summary>
    /// Algorithm 6.3: queues the runs of due schedules, applying each schedule's misfire policy, and moves the
    /// schedules on. Only tasks with an installed handler are queued (WP-1); the others keep their next run time.
    /// </summary>
    private async Task EnqueueDueAsync(EngineSettings settings, CancellationToken cancellationToken)
    {
        var queued = new List<(ScheduledTask Task, int Count)>();
        await using (var unitOfWork = _unitOfWorkFactory.Create())
        {
            await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
            var now = Now;
            var due = await unitOfWork.Tasks.LockDueSchedulesAsync(now, BatchSize, _registry.TaskTypeNames, cancellationToken);
            if (due.Count == 0)
            {
                return;
            }

            foreach (var schedule in due)
            {
                var task = schedule.Task!;
                ScheduleSpec spec;
                try
                {
                    spec = ScheduleSpec.From(schedule);
                }
                catch (FormatException ex)
                {
                    // A schedule changed directly in the database to something invalid is switched off, not retried forever.
                    schedule.Advance(schedule.NextRunTimeUtc!.Value, null);
                    await _log.LogAsync(SchedulerLogLevel.Error, LogSources.Engine,
                        $"The schedule of task '{task.Name}' is invalid and has been deactivated: {ex.Message}", cancellationToken: cancellationToken);
                    continue;
                }

                var plan = ScheduleCalculator.PlanDueRuns(spec, schedule.MisfirePolicy, schedule.NextRunTimeUtc!.Value, now, settings.RunAllMisfireCap);
                foreach (var run in plan)
                {
                    unitOfWork.Queue.Add(QueueEntry.Create(task, schedule.Id, run.TriggerType, run.ScheduledTimeUtc, 0, now));
                }

                var lastRun = LastDueRun(spec, schedule.NextRunTimeUtc.Value, now);
                schedule.Advance(lastRun, ScheduleCalculator.GetNextRun(spec with { LastRunUtc = lastRun }, now));

                if (plan.Count > 0)
                {
                    queued.Add((task, plan.Count));
                }
                else
                {
                    await _log.LogAsync(SchedulerLogLevel.Warning, LogSources.Engine,
                        $"Missed runs of task '{task.Name}' were skipped by its misfire policy.", cancellationToken: cancellationToken);
                }
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            await unitOfWork.CommitTransactionAsync(cancellationToken);
        }

        var at = Now;
        foreach (var (task, _) in queued)
        {
            _events.Publish(new SchedulerEvent(SchedulerEventKind.TaskQueued, new TaskLifecycleEventArgs(task.Id, task.Name, task.TaskType, null, at)));
        }
    }

    /// <summary>The latest run time that is due by <paramref name="nowUtc"/>; it becomes the schedule's last run time.</summary>
    private static DateTime LastDueRun(ScheduleSpec spec, DateTime nextRunUtc, DateTime nowUtc)
    {
        if (spec.Type == ScheduleType.Interval && spec.IntervalSeconds > 0 && nowUtc > nextRunUtc)
        {
            var interval = TimeSpan.FromSeconds(spec.IntervalSeconds.Value).Ticks;
            return nextRunUtc.AddTicks((nowUtc - nextRunUtc).Ticks / interval * interval);
        }

        var runs = ScheduleCalculator.GetDueRuns(spec, nextRunUtc, nowUtc, 1000);
        return runs.Count == 0 ? nextRunUtc : runs[^1];
    }

    /// <summary>Algorithm 6.4: claims waiting entries while a worker is free and runs each on its own task.</summary>
    private async Task DispatchAsync(EngineSettings settings, CancellationToken stop)
    {
        var types = _registry.TaskTypeNames;
        while (!stop.IsCancellationRequested && _state == EngineState.Running && _idleWorkers.TryDequeue(out var workerId))
        {
            Guid? claimed;
            try
            {
                await using var unitOfWork = _unitOfWorkFactory.Create();
                claimed = await unitOfWork.Queue.ClaimNextAsync(workerId, types, Now, settings.LeaseGraceSeconds, stop);
            }
            catch
            {
                _idleWorkers.Enqueue(workerId);
                throw;
            }

            if (claimed is not { } entryId)
            {
                _idleWorkers.Enqueue(workerId);
                return;
            }

            var runStop = new CancellationTokenSource();
            var run = Task.Run(async () =>
            {
                try
                {
                    await _executor.ExecuteAsync(entryId, workerId, runStop.Token);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Execution of queue entry {QueueEntryId} failed unexpectedly.", entryId);
                }
                finally
                {
                    _running.TryRemove(entryId, out _);
                    runStop.Dispose();
                    _idleWorkers.Enqueue(workerId);
                    _signal.Wake();
                }
            }, CancellationToken.None);
            _running.TryAdd(entryId, (run, runStop));
        }
    }

    private async Task<TimeSpan> NextDelayAsync(TimeSpan maxSleep, CancellationToken cancellationToken)
    {
        var now = Now;
        var next = now + maxSleep;
        await using var unitOfWork = _unitOfWorkFactory.Create();

        if (await unitOfWork.Tasks.GetEarliestNextRunAsync(_registry.TaskTypeNames, cancellationToken) is { } nextRun && nextRun < next)
        {
            next = nextRun;
        }

        // Waiting entries only matter while a worker is free; a finishing worker wakes the engine itself.
        if (!_idleWorkers.IsEmpty
            && await unitOfWork.Queue.GetEarliestWaitingAsync(_registry.TaskTypeNames, cancellationToken) is { } waiting
            && waiting < next)
        {
            next = waiting;
        }

        var delay = next - now;
        return delay < MinimumDelay ? MinimumDelay : delay > maxSleep ? maxSleep : delay;
    }

    /// <summary>Waits for running executions up to the grace period, then cancels the rest and waits for them to record their outcome.</summary>
    private async Task DrainAsync(CancellationToken cancellationToken)
    {
        if (_running.IsEmpty)
        {
            return;
        }

        var grace = (await _settings.GetEngineSettingsAsync(cancellationToken)).ShutdownGraceSeconds;
        var runs = _running.Values.ToList();
        await Task.WhenAny(Task.WhenAll(runs.Select(r => r.Run)), Task.Delay(TimeSpan.FromSeconds(grace), CancellationToken.None));

        foreach (var (_, stop) in _running.Values)
        {
            try
            {
                stop.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Finished meanwhile.
            }
        }

        await Task.WhenAny(Task.WhenAll(runs.Select(r => r.Run)), Task.Delay(TaskExecutor.AbandonGrace + TimeSpan.FromSeconds(10), CancellationToken.None));
    }

    private void SetState(EngineState state)
    {
        _state = state;
        _events.Publish(new SchedulerEvent(SchedulerEventKind.EngineStateChanged, new EngineStateEventArgs(state)));
    }
}
