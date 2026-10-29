using Scheduler.Application.Common;
using Scheduler.Application.Common.Persistence;
using Scheduler.Domain.Enums;
using Scheduler.Extensibility.Contracts;

namespace Scheduler.Application.Tasks;

/// <summary>
/// Pauses, resumes, disables or enables a task (FR-02, FR-03) following the state table of WP-6. Only Active tasks are
/// scheduled; a paused task can still be run with Run now, a disabled task cannot.
/// </summary>
public sealed record ChangeTaskStatusCommand(Guid TaskId, ScheduledTaskStatus NewStatus) : IRequireAdministrator;

internal sealed class ChangeTaskStatusHandler(IUnitOfWorkFactory unitOfWorkFactory, ICurrentUser currentUser, TimeProvider clock, IEngineSignal engine)
    : ICommandHandler<ChangeTaskStatusCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(ChangeTaskStatusCommand command, CancellationToken cancellationToken = default)
    {
        await using var unitOfWork = unitOfWorkFactory.Create();
        var task = await unitOfWork.Tasks.GetAsync(command.TaskId, cancellationToken: cancellationToken);
        if (task is null)
        {
            return Error.NotFound("Task");
        }

        if (task.Status == command.NewStatus)
        {
            return Result.Success(Unit.Value);
        }

        if (!task.CanChangeStatusTo(command.NewStatus))
        {
            return Error.Conflict("Task.InvalidStatusChange",
                $"A {task.Status.ToString().ToLowerInvariant()} task cannot be set to {command.NewStatus.ToString().ToLowerInvariant()}. {(task.Status == ScheduledTaskStatus.Disabled ? "Enable" : "Resume")} it first.");
        }

        var now = clock.GetUtcNow().UtcDateTime;
        switch (command.NewStatus)
        {
            case ScheduledTaskStatus.Active:
                task.Resume(now, currentUser.UserId);
                break;
            case ScheduledTaskStatus.Paused:
                task.Pause(now, currentUser.UserId);
                break;
            case ScheduledTaskStatus.Disabled:
                task.Disable(now, currentUser.UserId);
                break;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        engine.Wake();
        return Result.Success(Unit.Value);
    }
}
