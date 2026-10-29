using Scheduler.Application.Common;
using Scheduler.Application.Common.Persistence;
using Scheduler.Extensibility.Contracts;

namespace Scheduler.Application.Tasks;

/// <summary>Soft-deletes a task and cancels its waiting queue entries; its history is kept (FR-04).</summary>
public sealed record DeleteTaskCommand(Guid TaskId) : IRequireAdministrator;

/// <summary>Brings a soft-deleted task back, if its name is still free (FR-04, FR-05).</summary>
public sealed record RestoreTaskCommand(Guid TaskId) : IRequireAdministrator;

internal sealed class DeleteTaskHandler(IUnitOfWorkFactory unitOfWorkFactory, ICurrentUser currentUser, TimeProvider clock, IEngineSignal engine)
    : ICommandHandler<DeleteTaskCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(DeleteTaskCommand command, CancellationToken cancellationToken = default)
    {
        await using var unitOfWork = unitOfWorkFactory.Create();
        var task = await unitOfWork.Tasks.GetAsync(command.TaskId, cancellationToken: cancellationToken);
        if (task is null)
        {
            return Error.NotFound("Task");
        }

        task.Delete(clock.GetUtcNow().UtcDateTime, currentUser.UserId);
        foreach (var entry in await unitOfWork.Queue.GetWaitingForTaskAsync(task.Id, cancellationToken))
        {
            entry.Cancel();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        engine.Wake();
        return Result.Success(Unit.Value);
    }
}

internal sealed class RestoreTaskHandler(IUnitOfWorkFactory unitOfWorkFactory, ICurrentUser currentUser, TimeProvider clock, IEngineSignal engine)
    : ICommandHandler<RestoreTaskCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(RestoreTaskCommand command, CancellationToken cancellationToken = default)
    {
        await using var unitOfWork = unitOfWorkFactory.Create();
        var task = await unitOfWork.Tasks.GetAsync(command.TaskId, includeDeleted: true, cancellationToken);
        if (task is null)
        {
            return Error.NotFound("Task");
        }

        if (!task.IsDeleted)
        {
            return Result.Success(Unit.Value);
        }

        if (await unitOfWork.Tasks.NameExistsAsync(task.Name, task.Id, cancellationToken))
        {
            return Error.Conflict("Task.NameNotUnique", $"Another task is already called '{task.Name}'. Rename that task first.");
        }

        task.Restore(clock.GetUtcNow().UtcDateTime, currentUser.UserId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        engine.Wake();
        return Result.Success(Unit.Value);
    }
}
