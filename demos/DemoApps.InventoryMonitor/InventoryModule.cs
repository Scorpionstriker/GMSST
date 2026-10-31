using DemoApps.InventoryMonitor.Data;
using DemoApps.InventoryMonitor.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Scheduler.Domain.Enums;
using Scheduler.Extensibility.Contracts;

namespace DemoApps.InventoryMonitor;

/// <summary>
/// Demonstration app 1 (FR-78): a module loaded into the Admin application. It adds the Inventory menu, three task
/// types, its own tables in the "inventory" schema of its own database inventory_demo, and its three scheduled tasks,
/// using only the extensibility contracts (WP-1).
/// </summary>
public sealed class InventoryModule : ISchedulerModule
{
    public const string ModuleName = "Inventory Monitor";

    // Stable keys, so the Launch and Stop buttons find the tasks after an administrator renames them.
    public const string StockReportKey = "inventory.stock-report";
    public const string LowStockCheckKey = "inventory.low-stock-check";
    public const string WeeklySummaryKey = "inventory.weekly-summary";

    public static readonly string[] TaskKeys = [StockReportKey, LowStockCheckKey, WeeklySummaryKey];

    public string Name => ModuleName;

    public string Description => "Demonstration module: products and stock levels with scheduled stock reports and low-stock checks.";

    public IReadOnlyList<ModuleMenuItem> MenuItems { get; } =
    [
        new("Products", "/inventory/products", "Inventory"),
        new("Stock levels", "/inventory/stock", "Inventory"),
        new("Low-stock list", "/inventory/low-stock", "Inventory")
    ];

    public void Register(IServiceCollection services)
    {
        services.AddSingleton<InventoryStore>();
        services.AddTransient<ITaskHandler, GenerateStockReportHandler>();
        services.AddTransient<ITaskHandler, LowStockCheckHandler>();
        services.AddTransient<ITaskHandler, WeeklySummaryHandler>();
        services.AddSingleton<IManagedApplication, InventoryTasksControl>();
    }

    /// <summary>Creates the inventory schema and sample data, and the module's tasks through the scheduler client (FR-42).</summary>
    public async Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var store = services.GetRequiredService<InventoryStore>();
        await store.EnsureSchemaAsync(cancellationToken);
        await store.SeedAsync(cancellationToken);

        var client = services.GetRequiredService<ISchedulerClient>();
        var zone = TimeZoneInfo.Local.HasIanaId ? TimeZoneInfo.Local.Id
            : TimeZoneInfo.TryConvertWindowsIdToIanaId(TimeZoneInfo.Local.Id, out var iana) ? iana : "UTC";

        await client.EnsureTaskAsync(new TaskDefinition
        {
            Name = "Inventory: hourly stock report",
            SystemKey = StockReportKey,
            TaskType = GenerateStockReportHandler.Type,
            Description = "Records the stock value every hour.",
            CronExpression = "0 * * * *",
            TimeZoneId = zone,
            MisfirePolicy = MisfirePolicy.Skip
        }, cancellationToken);
        await client.EnsureTaskAsync(new TaskDefinition
        {
            Name = "Inventory: low-stock check",
            SystemKey = LowStockCheckKey,
            TaskType = LowStockCheckHandler.Type,
            Description = "Refreshes the low-stock list every 15 minutes.",
            ScheduleType = ScheduleType.Interval,
            IntervalSeconds = 900,
            Priority = 3,
            MisfirePolicy = MisfirePolicy.RunOnce
        }, cancellationToken);
        await client.EnsureTaskAsync(new TaskDefinition
        {
            Name = "Inventory: weekly summary",
            SystemKey = WeeklySummaryKey,
            TaskType = WeeklySummaryHandler.Type,
            Description = "Summarises the week's stock reports every Monday at 08:00.",
            CronExpression = "0 8 * * MON",
            TimeZoneId = zone,
            MisfirePolicy = MisfirePolicy.RunOnce
        }, cancellationToken);
    }
}
