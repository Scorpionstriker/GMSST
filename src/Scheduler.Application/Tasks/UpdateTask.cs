using Scheduler.Application.Common;
using Scheduler.Application.Common.Persistence;
using Scheduler.Extensibility.Contracts;

namespace Scheduler.Application.Tasks;

/// <summary>
/// Saves the task editor for an existing task (FR-02). <paramref name="Version"/> is the row version the
/// editor loaded, so a change someone else saved in the meantime is not overwritten (NFR-17).
/// </summary>
public sealed record UpdateTaskCommand(Guid TaskId, uint Version, TaskInput Input) : IRequireAdministrator;

internal sealed class UpdateTaskValidator(ITaskTypeRegistry registry, IUnitOfWorkFactory unitOfWorkFactory, TimeProvider clock)
    : IValidator<UpdateTaskCommand>
{
    public async Task<IReadOnlyList<Error>> ValidateAsync(UpdateTaskCommand command, CancellationToken cancellationToken = default)
    {
        await using var unitOfWork = unitOfWorkFactory.Create();
        var existing = await unitOfWork.Tasks.GetAsync(command.TaskId, cancellationToken: cancellationToken);
        if (existing is null)
        {
            return [Error.NotFound("Task")];
        }

        var errors = TaskInputRules.ValidateFields(command.Input, registry, clock.GetUtcNow().UtcDateTime, existing.TaskType);

        // An unchanged schedule is kept as it is, even when its start time (for example of a Once run) has passed.
        if (existing.CurrentSchedule is not null && TaskWriter.ToInput(existing.CurrentSchedule) == command.Input.Schedule)
        {
            var kept = new ValidationErrors();
            foreach (var error in errors.Errors.Where(e => e.Field != TaskFields.StartTime))
            {
                kept.Add(error.Field!, error.Message);
            }

            errors = kept;
        }

        await TaskInputRules.ValidateAgainstStoreAsync(command.Input, command.TaskId, unitOfWork, errors, cancellationToken);
        return errors.Errors;
    }
}

internal sealed class UpdateTaskHandler(IUnitOfWorkFactory unitOfWorkFactory, ICurrentUser currentUser, TimeProvider clock, IEngineSignal engine)
    : ICommandHandler<UpdateTaskCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(UpdateTaskCommand command, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var input = command.Input;

        await using var unitOfWork = unitOfWorkFactory.Create();
        var task = await unitOfWork.Tasks.GetAsync(command.TaskId, cancellationToken: cancellationToken);
        if (task is null)
        {
            return Error.NotFound("Task");
        }

        unitOfWork.Tasks.SetOriginalVersion(task, command.Version);

        task.UpdateDetails(input.Name, input.Description, input.TaskType, input.Payload, input.Priority, input.MaxRetries,
            input.TimeoutSeconds, now, currentUser.UserId);

        if (task.CurrentSchedule is null || TaskWriter.ToInput(task.CurrentSchedule) != input.Schedule)
        {
            task.SetSchedule(TaskWriter.BuildSchedule(task.Id, input.Schedule, now, currentUser.UserId), now, currentUser.UserId);
        }

        task.SetDependencies(input.DependsOn, now);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        engine.Wake();
        return Result.Success(Unit.Value);
    }
}
