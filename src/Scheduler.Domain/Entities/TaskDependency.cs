namespace Scheduler.Domain.Entities;

/// <summary>
/// The dependent task runs only after the prerequisite task (<see cref="DependsOnTaskId"/>) has
/// completed successfully (FR-26). Self-dependencies and cycles are rejected (FR-27, BR-05).
/// </summary>
public sealed class TaskDependency
{
    private TaskDependency()
    {
    }

    internal TaskDependency(Guid taskId, Guid dependsOnTaskId, DateTime nowUtc)
    {
        TaskId = taskId;
        DependsOnTaskId = dependsOnTaskId;
        CreatedAtUtc = nowUtc;
    }

    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid TaskId { get; private set; }
    public Guid DependsOnTaskId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
}
