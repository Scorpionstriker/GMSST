using DemoApps.InventoryMonitor.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Scheduler.Extensibility.Contracts;

namespace DemoApps.InventoryMonitor;

/// <summary>Registers the inventory task handlers and the Inventory menu (FR-78).</summary>
public sealed class InventoryModule : ISchedulerModule
{
    public string Name => "Inventory Monitor";

    public IReadOnlyList<ModuleMenuItem> MenuItems { get; } =
    [
        new("Products", "/inventory/products", "Inventory"),
        new("Stock levels", "/inventory/stock", "Inventory"),
        new("Low-stock list", "/inventory/low-stock", "Inventory")
    ];

    public void Register(IServiceCollection services)
    {
        services.AddSingleton<ITaskHandler, GenerateStockReportHandler>();
        services.AddSingleton<ITaskHandler, LowStockCheckHandler>();
        services.AddSingleton<ITaskHandler, WeeklySummaryHandler>();
    }
}
