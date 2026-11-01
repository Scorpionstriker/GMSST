using Scheduler.Application.Common;
using Scheduler.Application.Common.Persistence;
using Scheduler.Domain.Entities;
using Scheduler.Domain.Enums;
using Scheduler.Extensibility.Events;

namespace Scheduler.Application.Tasks;

/// <summary>
/// Queues an immediate manual run (FR-02). A paused task may be run manually and stays paused; a disabled
/// task may not (Table 7.3).
/// </summary>
public sealed record RunTaskNowCommand(Guid TaskId) : IRequireAdministrator;

internal sealed class RunTaskNowHandler(IUnitOfWorkFactory unitOfWorkFactory, TimeProvider clock, IEngineSignal engine, ISchedulerEventPublisher events)
    : ICommandHandler<RunTaskNowCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(RunTaskNowCommand command, CancellationToken cancellationToken = default)
    {
        await using var unitOfWork = unitOfWorkFactory.Create();
        var task = await unitOfWork.Tasks.GetAsync(command.TaskId, cancellationToken: cancellationToken);
        if (task is null)
        {
            return Error.NotFound("Task");
        }

        if (task.Status is ScheduledTaskStatus.Disabled or ScheduledTaskStatus.Completed)
        {
            return Error.Conflict("Task.NotRunnable",
                task.Status == ScheduledTaskStatus.Completed
                    ? "This task has completed because it reached its execution limit."
                    : "A disabled task cannot be run. Enable it first.");
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var entry = QueueEntry.Create(task, null, TriggerType.Manual, now, 0, now);
        unitOfWork.Queue.Add(entry);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        events.Publish(new SchedulerEvent(SchedulerEventKind.TaskQueued,
            new TaskLifecycleEventArgs(task.Id, task.Name, task.TaskType, null, now)));
        engine.Wake();
        return Result.Success(entry.Id);
    }
}
