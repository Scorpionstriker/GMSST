using DemoApps.InventoryMonitor.Data;
using Microsoft.Extensions.Logging;
using Scheduler.Extensibility.Contracts;

namespace DemoApps.InventoryMonitor.Handlers;

/// <summary>Summarises the last seven days of stock reports: the lowest and highest stock value and the low-stock trend.</summary>
public sealed class WeeklySummaryHandler(InventoryStore store) : ITaskHandler
{
    public const string Type = "WeeklySummary";

    public string TaskType => Type;
    public string DisplayName => "Inventory: weekly summary";
    public string Description => "Summarises the stock reports of the past seven days.";
    public string PayloadExample => "{}";

    public async Task<TaskResult> ExecuteAsync(ITaskContext context)
    {
        var since = DateTime.UtcNow.AddDays(-7);
        var reports = await store.GetReportsAsync(InventoryStore.ReportStock, since, 1000, context.CancellationToken);
        var products = await store.GetProductsAsync(context.CancellationToken);
        var summary = new
        {
            fromUtc = since,
            reports = reports.Count,
            lowestValue = reports.Count == 0 ? (decimal?)null : reports.Min(r => r.TotalValue),
            highestValue = reports.Count == 0 ? (decimal?)null : reports.Max(r => r.TotalValue),
            averageLowStock = reports.Count == 0 ? (double?)null : reports.Average(r => r.LowStockCount),
            currentValue = products.Sum(p => p.StockValue)
        };
        await store.AddReportAsync(InventoryStore.ReportWeekly, products, summary, context.ExecutionId, context.CancellationToken);
        context.Logger.LogInformation("Weekly summary written from {Reports} stock reports.", reports.Count);
        return TaskResult.Success($"Weekly summary of {reports.Count} stock reports.", summary);
    }
}
