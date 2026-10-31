using Scheduler.Application.Common.Persistence;
using Scheduler.Application.Configuration;
using Scheduler.Application.Executions;
using Scheduler.Domain.Entities;
using Scheduler.Domain.Enums;

namespace Scheduler.Engine.Execution;

/// <summary>
/// Records a failed or timed-out attempt and queues a retry with back-off when the retry limit allows it
/// (Algorithm 6.9, FR-30, FR-31). Used by the executor and by recovery, inside the caller's unit of work.
/// </summary>
internal sealed class FailureRecorder(ISettingsProvider settings)
{
    /// <summary>Returns true when a retry was queued.</summary>
    public async Task<bool> RecordAsync(IUnitOfWork unitOfWork, TaskExecution execution, QueueEntry entry, ScheduledTask task,
        bool timedOut, string errorType, string message, string? stackTraceJson, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var maxRetries = task.MaxRetries ?? await settings.GetForTaskAsync(SettingKeys.MaxRetryLimit, task.Id, cancellationToken);
        var willRetry = RetryPolicy.ShouldRetry(entry.Attempt, maxRetries, entry.MayRun(task));

        var status = willRetry ? ExecutionStatus.Retrying : timedOut ? ExecutionStatus.TimedOut : ExecutionStatus.Failed;
        execution.Complete(status, nowUtc, null);
        unitOfWork.Executions.AddError(new ErrorLogEntry(execution.Id, errorType, message, stackTraceJson, nowUtc));
        if (entry.Status == QueueStatus.Claimed)
        {
            entry.MarkProcessed();
        }

        if (willRetry)
        {
            var delay = RetryPolicy.GetDelay(
                entry.Attempt,
                await settings.GetForTaskAsync(SettingKeys.RetryBaseDelaySeconds, task.Id, cancellationToken),
                await settings.GetForTaskAsync(SettingKeys.RetryMaxDelaySeconds, task.Id, cancellationToken));
            unitOfWork.Queue.Add(entry.FollowUp(task, TriggerType.Retry, nowUtc + delay, entry.Attempt + 1, nowUtc));
        }

        return willRetry;
    }
}
