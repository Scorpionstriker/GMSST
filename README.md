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
├── Directory.Packages.props   Every NuGet version, pinned in one place, MAUI included (NFR-13)
├── global.json                Pins the .NET SDK (10.0.100, later feature bands allowed)
├── src/
│   ├── Scheduler.Domain/              Entities/, Enums/, Rules/
│   ├── Scheduler.Extensibility/       Contracts/, Events/
│   ├── Scheduler.Application/         Common/, Tasks/, Cron/, Queue/, Executions/, Dependencies/,
│   │                                  Reports/, Configuration/, Backup/
│   ├── Scheduler.Infrastructure/      Persistence/ (Configurations, Repositories, Queries, Migrations),
│   │                                  Logging/, Caching/, Configuration/, Backup/
│   ├── Scheduler.Engine/              SchedulerEngine.cs, Execution/, Events/, Handlers/, Hosting/
│   ├── Scheduler.Modules.Security/    Authentication/, Pages/
│   └── Scheduler.Admin/               Components/ (Layout, Pages, Shared), Services/, Platforms/, wwwroot/,
│                                      MauiProgram.cs, ModuleCatalog.cs, appsettings.json
├── demos/
│   ├── DemoApps.InventoryMonitor/            Handlers/, Data/, Pages/ (module, own database inventory_demo)
│   ├── DemoApps.MaintenanceAgentConnector/   AgentClient.cs, Pages/ (module that manages the agent)
│   └── DemoApps.MaintenanceAgent/            Program.cs, Services/, Data/ (separate app, own database maintenance_demo)
├── tests/
│   ├── Scheduler.UnitTests/
│   └── Scheduler.ArchitectureTests/
├── database/                  create_database.sql, setup_windows.ps1, schema.sql (generated), seed notes
└── docs/                      Guides, test cases, test results and UAT documents
```

## Projects

| Project | What it does | Requirements it serves |
|---|---|---|
| **Scheduler.Domain** | Entities (ScheduledTask, Schedule, QueueEntry, TaskExecution, AppUser ...), status enums and domain rules. References nothing. | FR-03, FR-04, FR-08, FR-19, FR-23, NFR-17 |
| **Scheduler.Extensibility** | The public contracts: `ITaskHandler`, `ITaskContext`, `TaskResult`, `ISchedulerModule`, `ILoggingProvider`, `IAuthenticationProvider` and scheduler events. Modules depend only on this. | FR-06, FR-40 to FR-43, NFR-07 |
| **Scheduler.Application** | Use cases (create/pause/run tasks, reports, configuration), validation, cron parser, schedule calculator, retry policy, dependency-cycle check. Handlers return a `Result` instead of throwing. | FR-01 to FR-17, FR-26, FR-27, FR-31, FR-36, FR-37, FR-64 to FR-66, FR-74 to FR-76 |
| **Scheduler.Infrastructure** | EF Core + Npgsql persistence and migrations, `SELECT ... FOR UPDATE SKIP LOCKED` claiming, Console/File/Database logging providers, `IMemoryCache`, backup and restore with pg_dump/pg_restore. | FR-20, FR-25, FR-28 to FR-30, FR-35, FR-38, FR-39, FR-67 to FR-73, NFR-03, NFR-14 to NFR-16 |
| **Scheduler.Engine** | The adaptive polling loop, enqueuing, bounded worker pool, time-outs, retries, start-up recovery, controlled shutdown, built-in task handlers. Hosted only by the Admin app: one engine per database. | FR-07, FR-18 to FR-25, FR-31 to FR-34, NFR-01, NFR-02, NFR-11 |
| **Scheduler.Modules.Security** | Login and Security module: local users, PBKDF2 password hashing, lockout, roles, Login and Users pages. Removable. | FR-44 to FR-53 |
| **Scheduler.Admin** | .NET MAUI Blazor Hybrid desktop app (Windows and macOS) and composition root. Menu, dashboard and every admin page. Lists loaded modules in `ModuleCatalog.cs`. | FR-54 to FR-63, NFR-04, NFR-05 |
| **DemoApps.InventoryMonitor** | Demo app 1 (integration method 1): a module that adds an Inventory menu and the GenerateStockReport, LowStockCheck and WeeklySummary task types, with its data in its own database `inventory_demo`. | FR-41, FR-42, FR-78 |
| **DemoApps.MaintenanceAgentConnector** | Integration method 2: a module that creates the Local HTTP Request tasks that manage the Maintenance Agent, and its status page. | FR-42, FR-79 |
| **DemoApps.MaintenanceAgent** | Demo app 2: a separate local web app with its own database `maintenance_demo` and no scheduler code. It listens on the loopback address only, needs the shared key, and offers `/health`, `/cleanup` and `/status`. Runs on Linux too. | FR-79, NFR-10, NFR-14 |
| **Scheduler.UnitTests** | Fast tests with no database: cron parser, schedule calculator and misfire policies, retries, dependency cycles, CSV export, passwords, handler checks, login bypass, task states, navigation, and the use cases, engine, delivery and security module on an in-memory database. | FR-55, NFR-01, NFR-03, NFR-22 |
| **Scheduler.ArchitectureTests** | Fail if a project uses a layer it may not depend on, or if a layer other than Infrastructure uses EF Core or Npgsql. Positive-control tests prove the rules really detect dependencies. | FR-77, NFR-09 |

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
| DemoApps.MaintenanceAgentConnector | Extensibility |
| DemoApps.MaintenanceAgent | none of the scheduler projects |
| Scheduler.Admin | everything (composition root) |

## How to extend

- **New task type:** implement `ITaskHandler` with a unique `TaskType` name and register it from a module.
- **New module:** implement `ISchedulerModule` in a new class library, then add it to `ModuleCatalog.cs`
  in the Admin app and add a project reference. To remove a module, do the reverse.
- **Manage a separate application:** give it a local endpoint and create Local HTTP Request tasks for it from a
  module, as `DemoApps.MaintenanceAgentConnector` does for the Maintenance Agent. Secrets in request headers are
  written as `env:VARIABLE_NAME` and read from the environment when the task runs.

See [docs/Technical_Guide.md](docs/Technical_Guide.md) for a worked example.

## Getting started

The full steps are in [docs/Installation_Guide.md](docs/Installation_Guide.md) (Windows), with
[docs/Build_macOS.md](docs/Build_macOS.md) and [docs/Build_Linux.md](docs/Build_Linux.md) for the other platforms.
In short, on Windows:

1. Install PostgreSQL 18, the .NET 10 SDK and Visual Studio 2026 with the .NET MAUI workload.
2. Run `powershell -ExecutionPolicy Bypass -File database\setup_windows.ps1`. It creates the `gmss_app` role and
   the databases `gmss`, `inventory_demo` and `maintenance_demo`, and sets the user environment variables
   `GMSS_DB_PASSWORD` and `GSST_AGENT_KEY`. Passwords are never stored in a file (NFR-15). Reopen Visual Studio.
3. Open `GSST.slnx`, start `Scheduler.Admin` on "Windows Machine" and create the first administrator.
   There is no default password.
4. Start the Maintenance Agent: `dotnet run --project demos\DemoApps.MaintenanceAgent`.

## Building and testing

- **Any OS, without the Admin app:** `dotnet build GSST.Core.slnf` and `dotnet test GSST.Core.slnf`.
- The tests need no database: the use cases, the engine and the security module run on an in-memory database
  (`tests/Scheduler.UnitTests/InMemory`). PostgreSQL-only behaviour is checked by hand (docs/Test_Cases.md, MC-17 to MC-22).
- Defaults live in `appsettings.json` (embedded in the Admin app; next to the Maintenance Agent). Settings for one
  computer (folders, PostgreSQL tools, login bypass, agent address) are kept in the user settings file
  `%LOCALAPPDATA%\GSST\appsettings.user.json` and edited on Configuration > This computer.
- Test cases for every requirement are in [docs/Test_Cases.md](docs/Test_Cases.md) and the latest results in
  [docs/Test_Results.md](docs/Test_Results.md).

## Design notes

These points refine the Phase 4 design while it was being built:

- **Missed runs:** a run that is more than 60 seconds late is treated as missed and handled by the schedule's
  misfire policy (Skip, Run once, Run all up to `RunAllMisfireCap`). A run that is only slightly late, for
  example because the engine was busy, simply runs.
- **History is kept when the queue is cleaned up:** `task_execution.queue_id` may be null (ON DELETE SET NULL),
  so the Log Clean-up task can remove old queue entries while their execution history stays.
- **Two extra settings:** `ShutdownGraceSeconds` (how long running tasks get to finish when the engine stops,
  default 30) and `WorkerStaleSeconds` (when a worker without a heartbeat is marked Stopped, default 120).
- **Start time:** for cron and interval schedules the start time is optional and defaults to now; only a
  one-off (Once) schedule needs a date and time.
- **Interrupted runs are not lost:** when the app closes or the engine is paused, a running task is cancelled
  and queued again with the same attempt number. A run whose host crashed is recovered when its lease expires.
- **Credential store:** the database password comes only from the `GMSS_DB_PASSWORD` environment variable, and
  the Maintenance Agent key only from `GSST_AGENT_KEY`.
- **One engine per database:** a PostgreSQL advisory lock lets only one scheduling engine run per database; a
  second Admin app keeps its engine stopped and names the running host.
- **At-least-once delivery:** work is never lost, but a task may run again after a crash, so handlers are written
  to be safe to run twice (`ITaskContext.ExecutionId` serves as an idempotency key).
- **Local HTTP task type:** only addresses on the same computer (localhost) may be called, and redirects are
  not followed, because the system is fully local.
