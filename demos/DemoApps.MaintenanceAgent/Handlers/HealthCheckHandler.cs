using Scheduler.Extensibility.Contracts;

namespace DemoApps.MaintenanceAgent.Handlers;

public sealed class HealthCheckHandler : ITaskHandler
{
    public string TaskType => "HealthCheck";

    public Task<TaskResult> ExecuteAsync(ITaskContext context)
    {
        // Demonstration logic goes here.
        return Task.FromResult(TaskResult.Success());
    }
}
