using Scheduler.Application.Common;
using Scheduler.Application.Common.Persistence;
using Scheduler.Extensibility.Contracts;

namespace Scheduler.Application.Tasks;

/// <summary>Creates a task with its schedule and dependencies (FR-01, Algorithm 6.12).</summary>
public sealed record CreateTaskCommand(TaskInput Input) : IRequireAdministrator;

internal sealed class CreateTaskValidator(ITaskTypeRegistry registry, IUnitOfWorkFactory unitOfWorkFactory, TimeProvider clock)
    : IValidator<CreateTaskCommand>
{
    public async Task<IReadOnlyList<Error>> ValidateAsync(CreateTaskCommand command, CancellationToken cancellationToken = default)
    {
        var errors = TaskInputRules.ValidateFields(command.Input, registry, clock.GetUtcNow().UtcDateTime);
        await using var unitOfWork = unitOfWorkFactory.Create();
        await TaskInputRules.ValidateAgainstStoreAsync(command.Input, Guid.Empty, unitOfWork, errors, cancellationToken);
        return errors.Errors;
    }
}

internal sealed class CreateTaskHandler(IUnitOfWorkFactory unitOfWorkFactory, ICurrentUser currentUser, TimeProvider clock, IEngineSignal engine)
    : ICommandHandler<CreateTaskCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreateTaskCommand command, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var task = TaskWriter.CreateTask(command.Input, now, currentUser.UserId);

        await using var unitOfWork = unitOfWorkFactory.Create();
        unitOfWork.Tasks.Add(task);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        engine.Wake();
        return Result.Success(task.Id);
    }
}
