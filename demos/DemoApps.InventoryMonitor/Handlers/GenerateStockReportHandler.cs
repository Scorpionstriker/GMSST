using Scheduler.Extensibility.Contracts;

namespace DemoApps.InventoryMonitor.Handlers;

public sealed class GenerateStockReportHandler : ITaskHandler
{
    public string TaskType => "GenerateStockReport";

    public Task<TaskResult> ExecuteAsync(ITaskContext context)
    {
        // Demonstration logic goes here.
        return Task.FromResult(TaskResult.Success());
    }
}
