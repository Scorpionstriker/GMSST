# Module Development Guide

Modules are the preferred way to extend GSST. They keep application-specific handlers, data, pages and event
mappings outside the scheduler core and use `Scheduler.Extensibility` to integrate. The Admin application discovers
pages from the module assembly and modules in `ModuleCatalog.cs`.

## Add a module project

1. Create a class library targeting the platform/framework used by the host. Reference `Scheduler.Extensibility`
   and only the extra libraries your module actually needs.
2. Implement one or more `ITaskHandler`s, each with a unique, stable `TaskType`. Validate payloads, honor
   `ITaskContext.CancellationToken`, return `TaskResult.Failure` for expected failures, and design for at-least-once
   delivery. Use `ExecutionId` as an idempotency key where appropriate.
3. Implement `ISchedulerModule`. Register handlers/services in `Register`, supply menu items, and use
   `InitializeAsync` for module-owned schema setup or stable task registration.
4. Add a project reference and a module instance to `src/Scheduler.Admin/ModuleCatalog.cs`. Admin pages in the
   module assembly use Razor `@page` routes; each menu route should point to a page in that assembly.
5. Add unit tests for handler behavior and integration tests for module-owned storage. Run the architecture tests
   to ensure no forbidden dependency from core to demo/module code was introduced.

Typical task handler:

```csharp
public sealed class ReconcileStockHandler(IStockReconciler reconciler) : ITaskHandler
{
    public string TaskType => "Contoso.Inventory.ReconcileStock";
    public string DisplayName => "Reconcile stock";
    public string? PayloadExample => """{ "warehouse": "north" }""";

    public async Task<TaskResult> ExecuteAsync(ITaskContext context)
    {
        var payload = context.GetPayload<ReconcilePayload>();
        if (payload is null || string.IsNullOrWhiteSpace(payload.Warehouse))
        {
            return TaskResult.Failure("A warehouse is required.");
        }

        await reconciler.ReconcileAsync(payload.Warehouse, context.CancellationToken);
        return TaskResult.Success("Stock reconciled.");
    }

    private sealed record ReconcilePayload(string Warehouse);
}

// IStockReconciler and its implementation belong to the module.
public interface IStockReconciler
{
    Task ReconcileAsync(string warehouse, CancellationToken cancellationToken);
}
```

Module registration and scheduled task setup:

```csharp
public sealed class InventoryModule : ISchedulerModule
{
    public string Name => "Contoso Inventory";
    public IReadOnlyList<ModuleMenuItem> MenuItems =>
        [new("Inventory", "/contoso/inventory", "Contoso")];

    public void Register(IServiceCollection services)
    {
        services.AddTransient<ITaskHandler, ReconcileStockHandler>();
        services.AddScoped<IStockReconciler, StockReconciler>();
    }

    public async Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var scheduler = services.GetRequiredService<ISchedulerClient>();
        await scheduler.EnsureTaskAsync(new TaskDefinition
        {
            Name = "Inventory: nightly reconciliation",
            SystemKey = "contoso.inventory.reconcile",
            TaskType = "Contoso.Inventory.ReconcileStock",
            CronExpression = "0 2 * * *",
            Payload = """{ "warehouse": "north" }"""
        }, cancellationToken);
    }
}
```

## Integration contracts

- **Business events:** keep the business-event-to-task mapping in the module. Call
  `ISchedulerClient.TriggerEventAsync` with the stable task system key, event name, source and a JSON object no larger
  than 64 KiB. The handler receives event data separately from its configured payload; retries retain the original
  event details.
- **Module-owned databases:** use `IModuleDatabases` and a module configuration section. Keep module schema,
  migrations and data access in the module/infrastructure project, not in `Scheduler.Domain`.
- **Audit:** use `IAuditLog.RecordAsync(action, message, application)` for successful user-initiated actions.
  Never include passwords, recovery codes, keys or other secrets.
- **Notifications:** use `ISchedulerNotifications` for immediate or one-time UTC notices. Scheduled-notice queries
  and cancellation are limited to the calling source; inbox access is limited to the signed-in user.
- **Host application:** use `IApplicationControl` to display the host's version and request a normal close from
  module UI. The host owns the platform-specific shutdown behavior.
- **Pages and menu:** add Razor pages to the module assembly and list routes through `MenuItems`. Set
  `AdministratorOnly` or `DeveloperOnly` for menu visibility; protect sensitive application operations through
  authorization as well.
- **Managed applications and restore:** implement `IManagedApplication` for Launch/Stop actions and `IRestoreGuard`
  when the module's process must stop before one of its databases can be restored.
- **Other providers:** `ILoggingProvider` adds a logging destination; `IAuthenticationProvider` and
  `IUserAccountStore` support alternate identity systems.

Modules are on by default and can be switched off unless `CanBeDisabled` is false. Use
`OnEnabledChangedAsync` to pause/resume module work the engine cannot associate with that module automatically.
Removing a module requires removing its project reference and catalog entry; existing tasks of its type are retained
but cannot run until a handler is available again.

The Inventory Monitor and Maintenance Agent Connector are in-repository examples. See
[How It Works](How_It_Works.md) for boundaries and [Technical Guide](Technical_Guide.md) for the full contracts and
integration tradeoffs.
