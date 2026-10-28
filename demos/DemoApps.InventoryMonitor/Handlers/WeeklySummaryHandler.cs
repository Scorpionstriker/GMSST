using Scheduler.Extensibility.Contracts;

namespace DemoApps.InventoryMonitor.Handlers;

public sealed class WeeklySummaryHandler : ITaskHandler
{
    public string TaskType => "WeeklySummary";

    public Task<TaskResult> ExecuteAsync(ITaskContext context)
    {
        // Demonstration logic goes here.
        return Task.FromResult(TaskResult.Success());
    }
}
