using Scheduler.Extensibility.Contracts;

namespace DemoApps.InventoryMonitor.Handlers;

public sealed class LowStockCheckHandler : ITaskHandler
{
    public string TaskType => "LowStockCheck";

    public Task<TaskResult> ExecuteAsync(ITaskContext context)
    {
        // Demonstration logic goes here.
        return Task.FromResult(TaskResult.Success());
    }
}
