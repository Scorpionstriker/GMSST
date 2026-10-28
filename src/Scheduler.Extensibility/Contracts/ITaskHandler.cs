namespace Scheduler.Extensibility.Contracts;

/// <summary>
/// Defines a task type. The <see cref="TaskType"/> name is what a task stores, so modules add
/// new task types without changing the core (FR-06, FR-40).
/// </summary>
public interface ITaskHandler
{
    /// <summary>Unique task type name, for example "LogEntry" or "LowStockCheck".</summary>
    string TaskType { get; }

    Task<TaskResult> ExecuteAsync(ITaskContext context);
}
