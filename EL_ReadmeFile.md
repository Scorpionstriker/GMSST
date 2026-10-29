# Extensibility Layer

Projects: `Scheduler.Extensibility`, `Scheduler.Modules.Security`.

`Scheduler.Extensibility` contains the stable contracts developers build against: `ITaskHandler`,
`ITaskContext`, `TaskResult`, `ISchedulerModule`, `ISchedulerClient`, `ILoggingProvider`,
`IAuthenticationProvider`, `IAuditLog`, `IModuleDatabases`, `IRestoreGuard` and the scheduler events.
Modules reference only this project.

`Scheduler.Modules.Security` is the Login and Security module, built as an example of a module. It can
be removed by deleting its line in `src/Scheduler.Admin/ModuleCatalog.cs` and its project reference. Without
it the Admin application stays closed unless the developer setting `Security:AllowLoginBypass` is on.

The demo module `demos/DemoApps.InventoryMonitor` (own database `inventory_demo`) and the connector
`demos/DemoApps.MaintenanceAgentConnector` use the same contracts. The Maintenance Agent itself
(`demos/DemoApps.MaintenanceAgent`) is a separate program that the scheduler calls over the loopback address.
