using DemoApps.InventoryMonitor.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Scheduler.Domain.Enums;
using Scheduler.Extensibility.Contracts;
using Scheduler.Infrastructure;
using Scheduler.Infrastructure.Persistence;

namespace Scheduler.IntegrationTests;

/// <summary>
/// The Inventory Monitor keeps its data in its own database through IModuleDatabases (WP-1), writes need the
/// Administrator role (WP-4) and a report written twice for one execution is stored once (WP-2). The test database
/// stands in for inventory_demo through the "Inventory" configuration section.
/// </summary>
public sealed class InventoryStoreTests(TestDatabase database) : IClassFixture<TestDatabase>
{
    [SkippableFact]
    public async Task Store_UsesItsOwnSchema_AndOnlyAdministratorsChangeProducts()
    {
        RequiresDatabase.Check();
        var store = CreateStore(new FixedUser(UserRole.Viewer));
        await store.EnsureSchemaAsync();
        await store.SeedAsync();

        var products = await store.GetProductsAsync();
        Assert.Equal(8, products.Count);
        const string AdministratorOnly = "Only an administrator can change products and stock.";
        Assert.Equal(AdministratorOnly, await store.SaveProductAsync(null, "NEW", "New product", 1, 1, 1));
        Assert.Equal(AdministratorOnly, await store.AdjustStockAsync(products[0].Id, 1));
        Assert.Equal(AdministratorOnly, await store.DeleteProductAsync(products[0].Id));

        var audit = new CapturingAuditLog();
        var admin = CreateStore(new FixedUser(UserRole.Administrator), audit);
        var sku = "AUDIT-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        Assert.Null(await admin.SaveProductAsync(null, sku, "Audit test product", 1, 2, 1));
        var created = (await admin.GetProductsAsync()).Single(product => product.Sku == sku);
        Assert.Null(await admin.SaveProductAsync(created.Id, sku + "-EDIT", "Updated audit test product", 2, 3, 1));
        Assert.Null(await admin.AdjustStockAsync(created.Id, 1));
        Assert.Null(await admin.DeleteProductAsync(created.Id));
        Assert.All(audit.Entries, entry => Assert.Equal("Inventory Monitor", entry.Application));
        Assert.Contains(audit.Entries, entry => entry.Action == "InventoryProductCreated");
        Assert.Contains(audit.Entries, entry => entry.Action == "InventoryProductUpdated");
        Assert.Contains(audit.Entries, entry => entry.Action == "InventoryStockAdjusted");
        Assert.Contains(audit.Entries, entry => entry.Action == "InventoryProductDeleted");
    }

    [SkippableFact]
    public async Task Report_ForTheSameExecution_IsWrittenOnce()
    {
        RequiresDatabase.Check();
        var store = CreateStore(new FixedUser(UserRole.Viewer));
        await store.EnsureSchemaAsync();
        var products = await store.GetProductsAsync();
        var executionId = Guid.NewGuid();

        var first = await store.AddReportAsync(InventoryStore.ReportStock, products, new { }, executionId);
        var second = await store.AddReportAsync(InventoryStore.ReportStock, products, new { }, executionId);

        Assert.Equal(first.Id, second.Id);
        var reports = await store.GetReportsAsync(InventoryStore.ReportStock, DateTime.UnixEpoch, 100);
        Assert.Single(reports, r => r.Id == first.Id);
    }

    private InventoryStore CreateStore(ICurrentUser user, IAuditLog? audit = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Inventory:Name"] = database.Options.Name })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSchedulerInfrastructure(new DatabaseConnection(database.Options, TestDatabase.Password), configuration);
        services.AddSingleton<IAuditLog>(audit ?? new CapturingAuditLog());
        var provider = services.BuildServiceProvider();
        return new InventoryStore(provider.GetRequiredService<IModuleDatabases>(), user, provider.GetRequiredService<IAuditLog>());
    }

    private sealed class CapturingAuditLog : IAuditLog
    {
        public List<(string Action, string Message, string Application)> Entries { get; } = [];

        public Task RecordAsync(string action, string message, CancellationToken cancellationToken = default)
        {
            Entries.Add((action, message, "Unknown"));
            return Task.CompletedTask;
        }

        public Task RecordAsync(string action, string message, string application, CancellationToken cancellationToken = default)
        {
            Entries.Add((action, message, application));
            return Task.CompletedTask;
        }
    }

    private sealed class FixedUser(UserRole role) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public Guid? UserId { get; } = Guid.NewGuid();
        public string? Username => "test";
        public UserRole? Role => role;

        public event EventHandler? Changed
        {
            add { }
            remove { }
        }
    }
}
