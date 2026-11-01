# Extensibility Layer README

Projects: `Scheduler.Extensibility`, `Scheduler.Modules.Security`.

`Scheduler.Extensibility` contains the public contracts used to add behavior without making modules depend on
scheduler implementations:

- `ITaskHandler`, `ITaskContext` and `TaskResult` define task types. Handlers receive the configured payload,
  cancellation token, execution ID, retry number, and (for event-triggered runs) separate event name, source and data.
- `ISchedulerModule` registers handlers and services and contributes pages and menu entries. `CanBeDisabled` and
  `OnEnabledChangedAsync` control runtime module switches.
- `ISchedulerClient` creates/looks up tasks by stable system key, runs or changes their active state, and queues
  event-triggered runs. Event-to-task mappings remain owned by the module.
- `ISchedulerNotifications` creates immediate or one-time scheduled, recipient-specific in-app notifications,
  manages a module's pending notices and reads the signed-in user's inbox.
- `IApplicationControl` exposes the host app version and lets UI modules request a normal application close; the
  host handles platform-specific shutdown.
- `IAuditLog` records user and module actions to the scheduler's audit log, with application attribution.
- `ILoggingProvider`, `IAuthenticationProvider`, `IUserAccountStore`, `IModuleDatabases`, `IRestoreGuard`,
  `IManagedApplication` and scheduler events provide logging, authentication, data, restore, managed-process and
  lifecycle integration points.

The Admin GUI itself is listed as the first module (`src/Scheduler.Admin/AdminGuiModule.cs`): it provides core
pages and menu entries and cannot be switched off. The Admin host composes modules listed in `ModuleCatalog.cs`.
The contracts are in-process APIs, not a network protocol; an adopting application can expose selected operations
over its own API and authentication boundary.

`Scheduler.Modules.Security` is the Login and Security module. It provides the Developer super-admin,
Administrator and Viewer roles; account registration and approval; password and username changes; lockout; and
one-time recovery codes. Developer-only login bypass is a local development feature. To remove or replace this
module, update `src/Scheduler.Admin/ModuleCatalog.cs` and its project reference, then supply the intended
authentication configuration. Consult [Core_Development.md](docs/Core_Development.md) before changing host
security behavior.

The demo module `demos/DemoApps.InventoryMonitor` (own database `inventory_demo`) and the connector
`demos/DemoApps.MaintenanceAgentConnector` use the same contracts. Inventory product and stock changes are audit
events; its scheduled report/low-stock handlers are separate task executions. The Maintenance Agent itself
(`demos/DemoApps.MaintenanceAgent`) is a separate program called through a loopback-only HTTP handler.

See [How_It_Works.md](docs/How_It_Works.md) for runtime flow and
[Module_Development.md](docs/Module_Development.md) for step-by-step extension guidance.
