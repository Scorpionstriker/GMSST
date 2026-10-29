using DemoApps.InventoryMonitor.Data;
using Microsoft.Extensions.Logging;
using Scheduler.Extensibility.Contracts;

namespace DemoApps.InventoryMonitor.Handlers;

/// <summary>Records the current stock value and the number of products at or below their reorder level.</summary>
public sealed class GenerateStockReportHandler(InventoryStore store) : ITaskHandler
{
    public const string Type = "GenerateStockReport";

    public string TaskType => Type;
    public string DisplayName => "Inventory: stock report";
    public string Description => "Records the total stock value and the number of low-stock products.";
    public string PayloadExample => "{}";

    public async Task<TaskResult> ExecuteAsync(ITaskContext context)
    {
        var products = await store.GetProductsAsync(context.CancellationToken);
        var report = await store.AddReportAsync(InventoryStore.ReportStock, products,
            new { products = products.Select(p => new { p.Sku, p.QuantityOnHand, value = p.StockValue }) }, context.CancellationToken);
        context.Logger.LogInformation("Stock report: {Count} products worth {Value:N2}, {Low} low.", report.ProductCount, report.TotalValue, report.LowStockCount);
        return TaskResult.Success($"{report.ProductCount} products, total value {report.TotalValue:N2}.",
            new { report.ProductCount, report.TotalValue, report.LowStockCount });
    }
}
