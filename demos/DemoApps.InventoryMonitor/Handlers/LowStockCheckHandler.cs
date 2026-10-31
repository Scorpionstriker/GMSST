using DemoApps.InventoryMonitor.Data;
using Microsoft.Extensions.Logging;
using Scheduler.Extensibility.Contracts;

namespace DemoApps.InventoryMonitor.Handlers;

/// <summary>
/// Produces the low-stock list: every product at or below its reorder level. With "failWhenLow": true in the payload
/// the run fails while products are low, which shows the retry and failure handling in a demonstration.
/// </summary>
public sealed class LowStockCheckHandler(InventoryStore store) : ITaskHandler
{
    public const string Type = "LowStockCheck";

    public string TaskType => Type;
    public string DisplayName => "Inventory: low-stock check";
    public string Description => "Lists products at or below their reorder level; the list appears on the Low-stock page.";
    public string PayloadExample => """{ "failWhenLow": false }""";

    public async Task<TaskResult> ExecuteAsync(ITaskContext context)
    {
        var products = await store.GetProductsAsync(context.CancellationToken);
        var low = products.Where(p => p.IsLow).Select(p => new LowStockLine(p.Sku, p.Name, p.QuantityOnHand, p.ReorderLevel)).ToList();
        await store.AddReportAsync(InventoryStore.ReportLowStock, products, new { items = low }, context.ExecutionId, context.CancellationToken);

        foreach (var line in low)
        {
            context.Logger.LogWarning("Low stock: {Sku} has {Quantity} (reorder at {Reorder}).", line.Sku, line.QuantityOnHand, line.ReorderLevel);
        }

        var message = low.Count == 0 ? "All products are above their reorder level." : $"{low.Count} product(s) need reordering.";
        var failWhenLow = context.Payload.ValueKind == System.Text.Json.JsonValueKind.Object
                          && context.Payload.TryGetProperty("failWhenLow", out var flag) && flag.ValueKind == System.Text.Json.JsonValueKind.True;
        return failWhenLow && low.Count > 0 ? TaskResult.Failure(message, new { low = low.Count }) : TaskResult.Success(message, new { low = low.Count });
    }
}
