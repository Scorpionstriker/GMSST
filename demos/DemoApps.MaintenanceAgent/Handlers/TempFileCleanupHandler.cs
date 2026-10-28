using Scheduler.Extensibility.Contracts;

namespace DemoApps.MaintenanceAgent.Handlers;

public sealed class TempFileCleanupHandler : ITaskHandler
{
    public string TaskType => "TempFileCleanup";

    public Task<TaskResult> ExecuteAsync(ITaskContext context)
    {
        // Demonstration logic goes here.
        return Task.FromResult(TaskResult.Success());
    }
}
