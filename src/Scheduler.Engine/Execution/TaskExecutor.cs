using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Scheduler.Application.Common;
using Scheduler.Application.Common.Persistence;
using Scheduler.Application.Tasks;
using Scheduler.Domain.Entities;
using Scheduler.Domain.Enums;
using Scheduler.Extensibility.Contracts;
using Scheduler.Extensibility.Events;

namespace Scheduler.Engine.Execution;

/// <summary>
/// Runs one claimed queue entry (Algorithm 6.5): records the execution, resolves the handler for the task type,
/// applies the time-out, records the outcome, retries failures and queues dependants (FR-22, FR-23, FR-26, FR-31).
/// Every run is isolated, so a failing or hanging handler cannot stop the engine (FR-32).
/// </summary>
public sealed class TaskExecutor
{
    /// <summary>A handler that ignores its cancellation token is abandoned this long after its time-out.</summary>
    internal static TimeSpan AbandonGrace { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Waits between attempts to save a result (WP-2): three more attempts after 1, 2 and 4 seconds.</summary>
    internal static TimeSpan[] SaveRetryDelays { get; set; } = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)];

    /// <summary>Test hook, called with the task id just before a result is saved; it may throw to simulate a failed save.</summary>
    internal static Func<Guid, Task>? BeforeResultSaved { get; set; }

    private readonly IUnitOfWorkFactory _unitOfWorkFactory;
    private readonly ITaskTypeRegistry _registry;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly FailureRecorder _failures;
    private readonly ISchedulerLogger _log;
    private readonly ISchedulerEventPublisher _events;
    private readonly IEngineSignal _signal;
    private readonly TimeProvider _clock;
    private readonly ILogger<TaskExecutor> _logger;

    internal TaskExecutor(IUnitOfWorkFactory unitOfWorkFactory, ITaskTypeRegistry registry, IServiceScopeFactory scopeFactory,
        FailureRecorder failures, ISchedulerLogger log, ISchedulerEventPublisher events, IEngineSignal signal, TimeProvider clock,
        ILogger<TaskExecutor> logger)
    {
        _unitOfWorkFactory = unitOfWorkFactory;
        _registry = registry;
        _scopeFactory = scopeFactory;
        _failures = failures;
        _log = log;
        _events = events;
        _signal = signal;
        _clock = clock;
        _logger = logger;
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    /// <summary>Runs the entry. <paramref name="stopToken"/> is cancelled when the engine pauses or stops.</summary>
    public async Task ExecuteAsync(Guid queueEntryId, Guid workerId, CancellationToken stopToken)
    {
        StartedRun? run;
        try
        {
            run = await StartAsync(queueEntryId, workerId);
        }
        catch (Exception ex)
        {
            // The entry stays claimed; recovery returns it to the queue when its lease expires (Algorithm 6.11).
            _logger.LogError(ex, "Queue entry {QueueEntryId} could not be started.", queueEntryId);
            return;
        }

        if (run is null)
        {
            return;
        }

        _events.Publish(new SchedulerEvent(SchedulerEventKind.TaskStarted,
            new TaskLifecycleEventArgs(run.TaskId, run.TaskName, run.TaskType, run.ExecutionId, Now, ExecutionStatus.Running)));

        var outcome = await RunHandlerAsync(run, stopToken);

        // WP-2: a short database problem should not cost a run, so saving the result is tried four times. If it still
        // fails, the entry stays claimed and recovery queues the task again when the lease expires: delivery is
        // at-least-once, so a handler may run again after it finished (handlers must be idempotent, see ITaskHandler).
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await CompleteAsync(run, workerId, outcome);
                return;
            }
            catch (Exception ex) when (attempt < SaveRetryDelays.Length)
            {
                _logger.LogWarning(ex, "Saving the result of execution {ExecutionId} failed; trying again.", run.ExecutionId);
                await Task.Delay(SaveRetryDelays[attempt], CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "The result of execution {ExecutionId} could not be saved.", run.ExecutionId);
                await _log.LogAsync(SchedulerLogLevel.Error, LogSources.Executor, "The result of an execution could not be saved; recovery will retry it.",
                    run.ExecutionId, new Dictionary<string, object?> { ["error"] = ex.Message });
                return;
            }
        }
    }

    private async Task<StartedRun?> StartAsync(Guid queueEntryId, Guid workerId)
    {
        await using var unitOfWork = _unitOfWorkFactory.Create();
        var entry = await unitOfWork.Queue.GetAsync(queueEntryId);
        if (entry?.Task is not { } task || entry.Status != QueueStatus.Claimed)
        {
            return null;
        }

        // Only Active tasks run; a manual run of a paused task, and its retries, are allowed (FR-02, FR-03, WP-6).
        var runnable = entry.MayRun(task);
        if (!runnable)
        {
            entry.Cancel();
            await unitOfWork.SaveChangesAsync();
            await _log.LogAsync(SchedulerLogLevel.Info, LogSources.Executor,
                $"Queued run of '{task.Name}' was cancelled because the task is {(task.IsDeleted ? "deleted" : task.Status.ToString().ToLowerInvariant())}.");
            return null;
        }

        var now = Now;
        var worker = await unitOfWork.Workers.GetAsync(workerId);
        if (worker is { Status: WorkerStatus.Stopped })
        {
            // Safety net (WP-1): the worker row was marked Stopped, so it cannot become Busy. The claim goes back to the
            // queue and the engine revives the worker in its next cycle.
            entry.ReleaseClaim();
            await unitOfWork.SaveChangesAsync();
            await _log.LogAsync(SchedulerLogLevel.Warning, LogSources.Executor,
                $"The run of '{task.Name}' was returned to the queue because its worker was marked as stopped.");
            return null;
        }

        var execution = TaskExecution.Start(entry, workerId, now);
        unitOfWork.Executions.Add(execution);
        worker?.MarkBusy(execution.Id, now);
        await unitOfWork.SaveChangesAsync();

        return new StartedRun(execution.Id, entry.Id, task.Id, task.Name, task.TaskType, task.Payload, task.TimeoutSeconds, entry.Attempt,
            entry.TriggerType, entry.ScheduledTimeUtc);
    }

    private async Task<RunOutcome> RunHandlerAsync(StartedRun run, CancellationToken stopToken)
    {
        var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(run.TimeoutSeconds));
        var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, stopToken);
        var scope = _scopeFactory.CreateAsyncScope();
        var logger = new ExecutionLogger(_log, run.ExecutionId, run.TaskName);
        Task<TaskResult>? handlerTask = null;

        try
        {
            ITaskHandler? handler;
            try
            {
                handler = _registry.Resolve(scope.ServiceProvider, run.TaskType);
            }
            catch (Exception ex)
            {
                return RunOutcome.Failed("HandlerConfigurationError", $"The handler for task type '{run.TaskType}' could not be created: {ex.Message}", ex);
            }

            if (handler is null)
            {
                return RunOutcome.Failed("HandlerNotFound", $"No handler is registered for task type '{run.TaskType}' on this host.");
            }

            JsonElement payload;
            try
            {
                using var document = JsonDocument.Parse(run.Payload);
                payload = document.RootElement.Clone();
            }
            catch (JsonException ex)
            {
                return RunOutcome.Failed("InvalidPayload", "The task's payload is not valid JSON.", ex);
            }

            var context = new TaskContext(run.TaskId, run.ExecutionId, run.TaskName, run.Attempt, run.TriggerType, run.ScheduledTimeUtc,
                payload, logger, scope.ServiceProvider, linked.Token);
            handlerTask = Task.Run(() => handler.ExecuteAsync(context), CancellationToken.None);
        }
        finally
        {
            if (handlerTask is null)
            {
                await DisposeAsync(scope, linked, timeout);
            }
        }

        var cancelled = new TaskCompletionSource();
        await using (linked.Token.Register(() => cancelled.TrySetResult()))
        {
            if (await Task.WhenAny(handlerTask, cancelled.Task) != handlerTask
                && await Task.WhenAny(handlerTask, Task.Delay(AbandonGrace)) != handlerTask)
            {
                // The handler ignored its cancellation token. Its worker slot is released and the run is recorded;
                // the handler is left to finish in the background and its scope is disposed when it does (D11).
                _ = handlerTask.ContinueWith(async t =>
                {
                    _ = t.Exception;
                    await DisposeAsync(scope, linked, timeout);
                }, TaskScheduler.Default);
                await logger.FlushAsync();
                _logger.LogWarning("Handler for task {Task} did not stop after cancellation and was abandoned.", run.TaskName);
                return stopToken.IsCancellationRequested
                    ? RunOutcome.Cancelled("The engine stopped while the task was running; it will run again.")
                    : RunOutcome.TimedOut($"The task exceeded its time-out of {run.TimeoutSeconds} seconds and did not stop when asked.");
            }
        }

        try
        {
            var result = await handlerTask;
            if (result.IsSuccess)
            {
                return RunOutcome.Succeeded(result.Message, SerializeOutput(result));
            }

            return Interrupted(timeout, stopToken, run) ?? RunOutcome.Failed("TaskFailed", result.Message ?? "The handler reported a failure.");
        }
        catch (OperationCanceledException ex)
        {
            return Interrupted(timeout, stopToken, run) ?? RunOutcome.Failed(nameof(OperationCanceledException), ex.Message, ex);
        }
        catch (Exception ex)
        {
            return RunOutcome.Failed(ErrorDetails.TypeName(ex), ex.Message, ex);
        }
        finally
        {
            await logger.FlushAsync();
            await DisposeAsync(scope, linked, timeout);
        }
    }

    private static RunOutcome? Interrupted(CancellationTokenSource timeout, CancellationToken stopToken, StartedRun run)
    {
        if (stopToken.IsCancellationRequested)
        {
            return RunOutcome.Cancelled("The engine stopped while the task was running; it will run again.");
        }

        return timeout.IsCancellationRequested ? RunOutcome.TimedOut($"The task exceeded its time-out of {run.TimeoutSeconds} seconds.") : null;
    }

    private static string? SerializeOutput(TaskResult result)
    {
        if (result.Output is null)
        {
            return result.Message is null ? null : JsonSerializer.Serialize(new { message = result.Message });
        }

        try
        {
            return result.Output is string text && IsJsonObject(text) ? text : JsonSerializer.Serialize(new { message = result.Message, data = result.Output });
        }
        catch (Exception ex) when (ex is NotSupportedException or JsonException or InvalidOperationException)
        {
            return JsonSerializer.Serialize(new { message = result.Message, data = result.Output.ToString() });
        }
    }

    private static bool IsJsonObject(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private async Task CompleteAsync(StartedRun run, Guid workerId, RunOutcome outcome)
    {
        await using var unitOfWork = _unitOfWorkFactory.Create();
        var execution = await unitOfWork.Executions.GetAsync(run.ExecutionId) ?? throw new InvalidOperationException("The execution record is missing.");
        if (execution.Status != ExecutionStatus.Running)
        {
            // An earlier attempt saved the result (or recovery already handled the run).
            return;
        }

        var entry = await unitOfWork.Queue.GetAsync(run.QueueEntryId) ?? throw new InvalidOperationException("The queue entry is missing.");
        var task = entry.Task!;
        var now = Now;
        var queued = new List<ScheduledTask>();
        var willRetry = false;

        switch (outcome.Kind)
        {
            case OutcomeKind.Success:
                execution.Complete(ExecutionStatus.Success, now, outcome.OutputJson);
                entry.MarkProcessed();
                foreach (var dependant in await unitOfWork.Tasks.GetActiveDependantsAsync(task.Id))
                {
                    // WP-6 "All" rule (FR-26): queued only once every prerequisite has succeeded since the last
                    // dependency run, and one waiting dependency run is enough (Algorithm 6.10).
                    if (!await unitOfWork.Tasks.AllPrerequisitesSucceededAsync(dependant.Id, task.Id))
                    {
                        continue;
                    }

                    var waiting = await unitOfWork.Queue.GetWaitingForTaskAsync(dependant.Id);
                    if (waiting.All(w => w.TriggerType != TriggerType.Dependency))
                    {
                        unitOfWork.Queue.Add(QueueEntry.Create(dependant, null, TriggerType.Dependency, now, 0, now));
                        queued.Add(dependant);
                    }
                }

                break;

            case OutcomeKind.Cancelled:
                // Stopped by shutdown or pause: the run is recorded as cancelled and queued again, so no work is lost (NFR-01).
                execution.Complete(ExecutionStatus.Cancelled, now, null);
                entry.MarkProcessed();
                unitOfWork.Queue.Add(entry.FollowUp(task, entry.TriggerType, now, entry.Attempt, now));
                break;

            default:
                willRetry = await _failures.RecordAsync(unitOfWork, execution, entry, task, outcome.Kind == OutcomeKind.TimedOut,
                    outcome.ErrorType ?? "TaskFailed", outcome.Message ?? "The task failed.", ErrorDetails.ToJson(outcome.Exception), now,
                    CancellationToken.None);
                break;
        }

        var worker = await unitOfWork.Workers.GetAsync(workerId);
        if (worker is { Status: WorkerStatus.Busy })
        {
            worker.MarkIdle(now);
        }

        if (BeforeResultSaved is { } hook)
        {
            await hook(task.Id);
        }

        await unitOfWork.SaveChangesAsync();
        await PublishOutcomeAsync(run, outcome, execution.Status, willRetry, queued, now);
        _signal.Wake();
    }

    private async Task PublishOutcomeAsync(StartedRun run, RunOutcome outcome, ExecutionStatus status, bool willRetry,
        IReadOnlyList<ScheduledTask> queued, DateTime now)
    {
        var args = new TaskLifecycleEventArgs(run.TaskId, run.TaskName, run.TaskType, run.ExecutionId, now, status, outcome.Message);
        if (outcome.Kind == OutcomeKind.Success)
        {
            _events.Publish(new SchedulerEvent(SchedulerEventKind.TaskSucceeded, args));
        }
        else
        {
            _events.Publish(new SchedulerEvent(SchedulerEventKind.TaskFailed, args));
            var level = outcome.Kind == OutcomeKind.Cancelled || willRetry ? SchedulerLogLevel.Warning : SchedulerLogLevel.Error;
            var text = outcome.Kind switch
            {
                OutcomeKind.Cancelled => $"Task '{run.TaskName}' was interrupted and has been queued again.",
                _ when willRetry => $"Task '{run.TaskName}' failed (attempt {run.Attempt + 1}); a retry has been queued.",
                _ => $"Task '{run.TaskName}' failed after its final attempt."
            };
            await _log.LogAsync(level, LogSources.Executor, text, run.ExecutionId,
                new Dictionary<string, object?> { ["status"] = status.ToString(), ["error"] = outcome.Message });
        }

        foreach (var dependant in queued)
        {
            _events.Publish(new SchedulerEvent(SchedulerEventKind.TaskQueued,
                new TaskLifecycleEventArgs(dependant.Id, dependant.Name, dependant.TaskType, null, now)));
        }
    }

    private static async Task DisposeAsync(AsyncServiceScope scope, CancellationTokenSource linked, CancellationTokenSource timeout)
    {
        await scope.DisposeAsync();
        linked.Dispose();
        timeout.Dispose();
    }

    private sealed record StartedRun(
        Guid ExecutionId,
        Guid QueueEntryId,
        Guid TaskId,
        string TaskName,
        string TaskType,
        string Payload,
        int TimeoutSeconds,
        int Attempt,
        TriggerType TriggerType,
        DateTime ScheduledTimeUtc);
}
