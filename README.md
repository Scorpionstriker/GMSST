# Generic System Scheduler Template (GSST)

A locally hosted, modular task scheduler written in C# on .NET 10 with PostgreSQL. It is a starting
point: developers extend it through the extensibility layer, or edit the core projects directly.
Requirement IDs (FR-xx, NFR-xx, DR-xx) refer to the project's Requirements Specification.

## Solution layout

```
GSST/
├── GSST.slnx                  Full solution (open this in Visual Studio on Windows or macOS)
├── GSST.Core.slnf             Everything except the MAUI Admin app (builds on Linux too)
├── Directory.Build.props      Shared build settings
├── Directory.Packages.props   Every NuGet version, pinned in one place (NFR-13)
├── src/
│   ├── Scheduler.Domain/              Entities/, Enums/, Rules/
│   ├── Scheduler.Extensibility/       Contracts/, Events/
│   ├── Scheduler.Application/         Common/, Tasks/, Schedules/, Queue/, Executions/,
│   │                                  Dependencies/, Reports/, Configuration/, Cron/, Validation/
│   ├── Scheduler.Infrastructure/      Persistence/, Queue/, Logging/, Caching/, Configuration/, Backup/
│   ├── Scheduler.Engine/              SchedulerEngine.cs, TaskExecutor.cs, Handlers/, Hosting/
│   ├── Scheduler.Modules.Security/    Authentication/, Pages/
│   └── Scheduler.Admin/               Components/ (Layout, Pages), Platforms/, wwwroot/, MauiProgram.cs
├── demos/
│   ├── DemoApps.InventoryMonitor/     Handlers/, Data/, Pages/
│   └── DemoApps.MaintenanceAgent/     Program.cs, Handlers/, appsettings.json
├── tests/
│   ├── Scheduler.UnitTests/
│   ├── Scheduler.IntegrationTests/
│   └── Scheduler.ArchitectureTests/
├── database/                  Database set-up scripts and seed notes
└── docs/                      Technical documentation and user manual
```

## Projects

| Project | What it does | Requirements it serves |
|---|---|---|
| **Scheduler.Domain** | Entities (ScheduledTask, Schedule, QueueEntry, TaskExecution, AppUser ...), status enums and domain rules. References nothing. | FR-03, FR-04, FR-08, FR-19, FR-23, NFR-17 |
| **Scheduler.Extensibility** | The public contracts: `ITaskHandler`, `ITaskContext`, `TaskResult`, `ISchedulerModule`, `ILoggingProvider`, `IAuthenticationProvider` and scheduler events. Modules depend only on this. | FR-06, FR-40 to FR-43, NFR-07 |
| **Scheduler.Application** | Use cases (create/pause/run tasks, reports, configuration), validation, cron parser, schedule calculator, retry policy, dependency-cycle check. Handlers return a `Result` instead of throwing. | FR-01 to FR-17, FR-26, FR-27, FR-31, FR-36, FR-37, FR-64 to FR-66, FR-74 to FR-76 |
| **Scheduler.Infrastructure** | EF Core + Npgsql persistence and migrations, `SELECT ... FOR UPDATE SKIP LOCKED` claiming, Console/File/Database logging providers, `IMemoryCache`, backup and restore with pg_dump/pg_restore. | FR-20, FR-25, FR-28 to FR-30, FR-35, FR-38, FR-39, FR-67 to FR-73, NFR-03, NFR-14 to NFR-16 |
| **Scheduler.Engine** | The adaptive polling loop, enqueuing, bounded worker pool, time-outs, retries, start-up recovery, controlled shutdown, built-in task handlers. Hosted by the Admin app and by the Maintenance Agent. | FR-07, FR-18 to FR-24, FR-31 to FR-34, NFR-01, NFR-02, NFR-11 |
| **Scheduler.Modules.Security** | Login and Security module: local users, PBKDF2 password hashing, lockout, roles, Login and Users pages. Removable. | FR-44 to FR-53 |
| **Scheduler.Admin** | .NET MAUI Blazor Hybrid desktop app (Windows and macOS) and composition root. Menu, dashboard and every admin page. Lists loaded modules in `ModuleCatalog.cs`. | FR-54 to FR-63, NFR-04, NFR-05 |
| **DemoApps.InventoryMonitor** | Demo app 1: a module that adds an Inventory menu and the GenerateStockReport, LowStockCheck and WeeklySummary task types. | FR-41, FR-78 |
| **DemoApps.MaintenanceAgent** | Demo app 2: a headless console app that embeds the scheduler core, shares the database and runs HealthCheck and TempFileCleanup tasks. Runs on Linux too. | FR-25, FR-42, FR-79, NFR-10 |
| **Scheduler.UnitTests** | Fast tests with no database. | NFR-22 |
| **Scheduler.IntegrationTests** | Tests against a local PostgreSQL test database (claiming, migrations, backup). | NFR-03, NFR-22 |
| **Scheduler.ArchitectureTests** | Fail if a project uses a layer it may not depend on. | FR-77, NFR-09 |

## Allowed dependencies

Dependencies point inwards. Project references make the wrong direction hard, and
`Scheduler.ArchitectureTests` makes it fail the test run.

| Project | May depend on |
|---|---|
| Scheduler.Domain | nothing |
| Scheduler.Extensibility | Domain |
| Scheduler.Application | Domain, Extensibility |
| Scheduler.Infrastructure | Application, Domain, Extensibility |
| Scheduler.Engine | Application, Domain, Extensibility |
| Scheduler.Modules.Security | Extensibility |
| DemoApps.InventoryMonitor | Extensibility |
| DemoApps.MaintenanceAgent | Engine, Infrastructure, Extensibility |
| Scheduler.Admin | everything (composition root) |

## How to extend

- **New task type:** implement `ITaskHandler` with a unique `TaskType` name and register it from a module.
- **New module:** implement `ISchedulerModule` in a new class library, then add it to `ModuleCatalog.cs`
  in the Admin app and add a project reference. To remove a module, do the reverse.
- **Embed the scheduler in another app:** call `AddSchedulerApplication()`, `AddSchedulerInfrastructure(...)`
  and `AddSchedulerEngine()`, register your handlers and run `SchedulerEngine` as a hosted service, as
  `DemoApps.MaintenanceAgent/Program.cs` does.

## Building

- **Windows (Visual Studio 2026 with the .NET MAUI workload):** open `GSST.slnx`, set
  `Scheduler.Admin` as the start-up project and run it on "Windows Machine".
- **Any OS, without the Admin app:** `dotnet build GSST.Core.slnf` and `dotnet test GSST.Core.slnf`.
- **Database:** PostgreSQL 16 or later on localhost, database `gmss`, role `gmss_app`. The password is
  read from the `GMSS_DB_PASSWORD` user environment variable and is never stored in this repository
  (NFR-15).
