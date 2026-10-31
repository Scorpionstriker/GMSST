using Scheduler.Domain.Enums;
using Scheduler.Extensibility.Contracts;

namespace DemoApps.InventoryMonitor;

/// <summary>
/// The Launch and Stop buttons of the Inventory Monitor on the Modules page. The module runs inside the Admin
/// application, so launching it means starting its three scheduled tasks (and running a stock report and a low-stock
/// check straight away, so the Inventory pages show fresh figures), and stopping it means pausing them.
/// </summary>
internal sealed class InventoryTasksControl(ISchedulerClient scheduler) : IManagedApplication
{
    public string ModuleName => InventoryModule.ModuleName;

    public string DisplayName => "Inventory Monitor";

    public async Task<ManagedApplicationStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var statuses = new List<ScheduledTaskStatus>();
        foreach (var key in InventoryModule.TaskKeys)
        {
            if (await scheduler.GetTaskStatusAsync(key, cancellationToken) is { } status)
            {
                statuses.Add(status);
            }
        }

        var active = statuses.Count(s => s == ScheduledTaskStatus.Active);
        return statuses.Count == 0 ? new ManagedApplicationStatus(ManagedApplicationState.Stopped, "Its tasks have not been created yet.")
            : active == 0 ? new ManagedApplicationStatus(ManagedApplicationState.Stopped, "Its scheduled tasks are paused.")
            : active == statuses.Count ? new ManagedApplicationStatus(ManagedApplicationState.Running, "Its scheduled tasks are running.")
            : new ManagedApplicationStatus(ManagedApplicationState.Running, $"{active} of its {statuses.Count} scheduled tasks are running.");
    }

    public async Task<ManagedApplicationResult> StartAsync(CancellationToken cancellationToken = default)
    {
        var started = 0;
        foreach (var key in InventoryModule.TaskKeys)
        {
            started += await scheduler.SetTaskActiveAsync(key, true, cancellationToken) ? 1 : 0;
        }

        if (started == 0)
        {
            return ManagedApplicationResult.Failure("The Inventory Monitor's tasks were not found or are disabled on the Tasks page.");
        }

        await scheduler.RunNowAsync(InventoryModule.StockReportKey, cancellationToken);
        await scheduler.RunNowAsync(InventoryModule.LowStockCheckKey, cancellationToken);
        return ManagedApplicationResult.Success("The Inventory Monitor is running: its tasks are active and a stock report and low-stock check have been queued.");
    }

    public async Task<ManagedApplicationResult> StopAsync(CancellationToken cancellationToken = default)
    {
        foreach (var key in InventoryModule.TaskKeys)
        {
            await scheduler.SetTaskActiveAsync(key, false, cancellationToken);
        }

        return ManagedApplicationResult.Success("The Inventory Monitor is stopped: its scheduled tasks are paused. Its pages still show the last figures.");
    }
}
