# Extensibility Layer

Projects: `Scheduler.Extensibility`, `Scheduler.Modules.Security`.

`Scheduler.Extensibility` contains the stable contracts developers build against: `ITaskHandler`,
`ITaskContext`, `TaskResult`, `ISchedulerModule`, `ILoggingProvider`, `IAuthenticationProvider` and the
scheduler events. Modules reference only this project.

`Scheduler.Modules.Security` is the Login and Security module, built as an example of a module. It can
be removed by deleting its line in `src/Scheduler.Admin/ModuleCatalog.cs` and its project reference.

The demo module `demos/DemoApps.InventoryMonitor` uses the same contracts.
