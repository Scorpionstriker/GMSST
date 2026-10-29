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
        var now = clock.GetUtcNow().UtcDateTime;
        var name = definition.Name;
        if (definition.SystemKey is { } key)
        {
            if (await unitOfWork.Tasks.FindBySystemKeyAsync(key, cancellationToken) is { } builtIn)
            {
                return builtIn.Id;
            }

            // WP-8h: a task created before keys existed is taken over, and restored if it had been deleted.
            var older = await unitOfWork.Tasks.FindByNameAsync(name, includeDeleted: true, cancellationToken);
            if (older is { SystemKey: null } && older.TaskType == definition.TaskType)
            {
                older.AssignSystemKey(key);
                if (older.IsDeleted && !await unitOfWork.Tasks.NameExistsAsync(older.Name, older.Id, cancellationToken))
                {
                    older.Restore(now, null);
                }

                await unitOfWork.SaveChangesAsync(cancellationToken);
                return older.Id;
            }

            if (older is not null)
            {
                name = await FreeNameAsync(unitOfWork, name + " (built-in)", cancellationToken);
            }
        }
        else if (await unitOfWork.Tasks.FindByNameAsync(name, includeDeleted: true, cancellationToken) is { } existing)
        {
            return existing.Id;
        }

        var zone = TimeZones.FindOrUtc(definition.TimeZoneId);
        var input = new TaskInput
        {
            Name = name,
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
        if (definition.SystemKey is { } systemKey)
        {
            task.AssignSystemKey(systemKey);
        }

        unitOfWork.Tasks.Add(task);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await log.LogAsync(SchedulerLogLevel.Info, LogSources.Engine, $"Task '{task.Name}' created by the scheduler client.",
            properties: new Dictionary<string, object?> { ["taskId"] = task.Id, ["taskType"] = task.TaskType }, cancellationToken: cancellationToken);
        engine.Wake();
        return task.Id;
    }

    public async Task<bool> RunNowAsync(string taskNameOrSystemKey, CancellationToken cancellationToken = default)
    {
        await using var unitOfWork = unitOfWorkFactory.Create();
        var task = await unitOfWork.Tasks.FindBySystemKeyAsync(taskNameOrSystemKey, cancellationToken) is { IsDeleted: false } byKey
            ? byKey
            : await unitOfWork.Tasks.FindByNameAsync(taskNameOrSystemKey, includeDeleted: false, cancellationToken);
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

    private static async Task<string> FreeNameAsync(IUnitOfWork unitOfWork, string name, CancellationToken cancellationToken)
    {
        var candidate = name;
        for (var i = 2; await unitOfWork.Tasks.NameExistsAsync(candidate, null, cancellationToken); i++)
        {
            candidate = $"{name} {i}";
        }

        return candidate;
    }
}
