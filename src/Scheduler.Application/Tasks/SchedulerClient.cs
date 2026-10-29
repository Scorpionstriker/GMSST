using Scheduler.Application.Common;
using Scheduler.Application.Common.Persistence;
using Scheduler.Application.Cron;
using Scheduler.Domain.Entities;
using Scheduler.Domain.Enums;
using Scheduler.Extensibility.Contracts;
using Scheduler.Extensibility.Events;

namespace Scheduler.Application.Tasks;

/// <summary>
/// The in-process integration API for modules and embedding applications (FR-42). It applies the same
/// validation as the task editor but runs as the system, not as the signed-in user.
/// </summary>
internal sealed class SchedulerClient(
    IUnitOfWorkFactory unitOfWorkFactory,
    ITaskTypeRegistry registry,
    TimeProvider clock,
    IEngineSignal engine,
    ISchedulerEventPublisher events,
    ISchedulerLogger log) : ISchedulerClient
{
    public async Task<Guid> EnsureTaskAsync(TaskDefinition definition, CancellationToken cancellationToken = default)
    {
        await using var unitOfWork = unitOfWorkFactory.Create();
        var existing = await unitOfWork.Tasks.FindByNameAsync(definition.Name, includeDeleted: true, cancellationToken);
        if (existing is not null)
        {
            return existing.Id;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var zone = TimeZones.FindOrUtc(definition.TimeZoneId);
        var input = new TaskInput
        {
            Name = definition.Name,
            Description = definition.Description,
            TaskType = definition.TaskType,
            Payload = definition.Payload,
            Priority = definition.Priority,
            MaxRetries = definition.MaxRetries,
            TimeoutSeconds = definition.TimeoutSeconds,
            Schedule = new ScheduleInput
            {
                Type = definition.ScheduleType,
                CronExpression = definition.CronExpression,
                IntervalSeconds = definition.IntervalSeconds,
                TimeZoneId = definition.TimeZoneId,
                MisfirePolicy = definition.MisfirePolicy,
                StartTime = TimeZones.FromUtc(definition.StartTimeUtc ?? now, zone)
            }
        };

        var errors = TaskInputRules.ValidateFields(input, registry, now);
        if (!errors.IsValid)
        {
            throw new ArgumentException($"Task '{definition.Name}' is not valid: " + string.Join(" ", errors.Errors.Select(e => e.Message)),
                nameof(definition));
        }

        var task = TaskWriter.CreateTask(input, now, userId: null);
        unitOfWork.Tasks.Add(task);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await log.LogAsync(SchedulerLogLevel.Info, LogSources.Engine, $"Task '{task.Name}' created by the scheduler client.",
            properties: new Dictionary<string, object?> { ["taskId"] = task.Id, ["taskType"] = task.TaskType }, cancellationToken: cancellationToken);
        engine.Wake();
        return task.Id;
    }

    public async Task<bool> RunNowAsync(string taskName, CancellationToken cancellationToken = default)
    {
        await using var unitOfWork = unitOfWorkFactory.Create();
        var task = await unitOfWork.Tasks.FindByNameAsync(taskName, includeDeleted: false, cancellationToken);
        if (task is null || task.Status == ScheduledTaskStatus.Disabled)
        {
            return false;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        unitOfWork.Queue.Add(QueueEntry.Create(task, null, TriggerType.Manual, now, 0, now));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        events.Publish(new SchedulerEvent(SchedulerEventKind.TaskQueued, new TaskLifecycleEventArgs(task.Id, task.Name, task.TaskType, null, now)));
        engine.Wake();
        return true;
    }
}
