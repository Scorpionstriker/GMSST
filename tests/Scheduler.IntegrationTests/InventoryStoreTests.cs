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
        Assert.Equal(InventoryStore.AdministratorOnly, await store.SaveProductAsync(null, "NEW", "New product", 1, 1, 1));
        Assert.Equal(InventoryStore.AdministratorOnly, await store.AdjustStockAsync(products[0].Id, 1));
        Assert.Equal(InventoryStore.AdministratorOnly, await store.DeleteProductAsync(products[0].Id));

        var admin = CreateStore(new FixedUser(UserRole.Administrator));
        Assert.Null(await admin.AdjustStockAsync(products[0].Id, 1));
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

    private InventoryStore CreateStore(ICurrentUser user)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Inventory:Name"] = database.Options.Name })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSchedulerInfrastructure(new DatabaseConnection(database.Options, TestDatabase.Password), configuration);
        var provider = services.BuildServiceProvider();
        return new InventoryStore(provider.GetRequiredService<IModuleDatabases>(), user);
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
