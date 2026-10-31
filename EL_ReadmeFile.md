# Extensibility Layer

Projects: `Scheduler.Extensibility`, `Scheduler.Modules.Security`.

`Scheduler.Extensibility` contains the stable contracts developers build against: `ITaskHandler`,
`ITaskContext`, `TaskResult`, `ISchedulerModule`, `ISchedulerClient`, `ILoggingProvider`,
`IAuthenticationProvider`, `IAuditLog`, `IModuleDatabases`, `IRestoreGuard`, `IManagedApplication` (Launch and
Stop on the Modules page) and the scheduler events. A module can be switched off and on at run time on the Modules
page (`ISchedulerModule.CanBeDisabled`, `OnEnabledChangedAsync`); every module is on by default.
Modules reference only this project. The Admin GUI itself is listed as the first module (`src/Scheduler.Admin/AdminGuiModule.cs`):
it provides the core pages and menu entries and cannot be switched off.

`Scheduler.Modules.Security` is the Login and Security module, built as an example of a module. It can
be removed by deleting its line in `src/Scheduler.Admin/ModuleCatalog.cs` and its project reference. Without
it the Admin application stays closed unless the developer setting `Security:AllowLoginBypass` is on.

The demo module `demos/DemoApps.InventoryMonitor` (own database `inventory_demo`) and the connector
`demos/DemoApps.MaintenanceAgentConnector` use the same contracts. The Maintenance Agent itself
(`demos/DemoApps.MaintenanceAgent`) is a separate program that the scheduler calls over the loopback address.
