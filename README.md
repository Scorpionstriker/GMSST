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
│   ├── Scheduler.Application/         Common/, Tasks/, Cron/, Queue/, Executions/, Dependencies/,
│   │                                  Reports/, Configuration/, Backup/
│   ├── Scheduler.Infrastructure/      Persistence/ (Configurations, Repositories, Queries, Migrations),
│   │                                  Logging/, Caching/, Configuration/, Backup/
│   ├── Scheduler.Engine/              SchedulerEngine.cs, Execution/, Events/, Handlers/, Hosting/
│   ├── Scheduler.Modules.Security/    Authentication/, Pages/
│   └── Scheduler.Admin/               Components/ (Layout, Pages, Shared), Services/, Platforms/, wwwroot/,
│                                      MauiProgram.cs, ModuleCatalog.cs, appsettings.json
├── demos/
│   ├── DemoApps.InventoryMonitor/     Handlers/, Data/, Pages/
│   └── DemoApps.MaintenanceAgent/     Program.cs, Handlers/, appsettings.json
├── tests/
│   ├── Scheduler.UnitTests/
│   ├── Scheduler.IntegrationTests/
│   └── Scheduler.ArchitectureTests/
├── database/                  create_database.sql, schema.sql (generated from the migrations), seed notes
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
| **Scheduler.UnitTests** | Fast tests with no database: cron parser, schedule calculator and misfire policies, retries, dependency cycles, CSV export, passwords, handler checks, use-case wiring. | NFR-22 |
| **Scheduler.IntegrationTests** | Tests against a throw-away PostgreSQL database: migrations, claiming by several hosts, time-outs, retries, recovery, dependencies, misfires, sign-in and lockout, backup and restore. Skipped when `GMSS_DB_PASSWORD` is not set. | NFR-03, NFR-22 |
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
| DemoApps.MaintenanceAgent | Engine, Infrastructure, Extensibility |
| Scheduler.Admin | everything (composition root) |

## How to extend

- **New task type:** implement `ITaskHandler` with a unique `TaskType` name and register it from a module.
- **New module:** implement `ISchedulerModule` in a new class library, then add it to `ModuleCatalog.cs`
  in the Admin app and add a project reference. To remove a module, do the reverse.
- **Embed the scheduler in another app:** call `AddSchedulerApplication()`, `AddSchedulerInfrastructure(...)`
  and `AddSchedulerEngine()`, register your handlers and run `SchedulerEngine` as a hosted service, as
  `DemoApps.MaintenanceAgent/Program.cs` does.

## Getting started

1. **Install** PostgreSQL 16 or later (on localhost, port 5432) and the .NET 10 SDK. For the Admin app on
   Windows, install Visual Studio 2026 with the .NET MAUI workload.
2. **Create the database and role** once, as the PostgreSQL superuser, choosing a password:

   ```
   psql -U postgres -v app_password='choose-a-strong-password' -f database/create_database.sql
   ```

   The PostgreSQL tools (`psql`, `pg_dump`, `pg_restore`) must be on the PATH, or set `Paths:PostgresBinFolder`
   in `appsettings.json` (for example `C:\Program Files\PostgreSQL\16\bin`). Backups need `pg_dump` and `pg_restore`.
3. **Set the password** for your user account, then sign out and in again (or restart Visual Studio) so
   programs see it:
   - Windows: `setx GMSS_DB_PASSWORD "choose-a-strong-password"`
   - macOS: add `export GMSS_DB_PASSWORD=...` to `~/.zprofile`; for apps started from Finder also run
     `launchctl setenv GMSS_DB_PASSWORD ...`

   The password is read only from this variable and is never stored in a file or in this repository (NFR-15).
4. **Run the Admin app:** open `GSST.slnx`, set `Scheduler.Admin` as the start-up project and run it on
   "Windows Machine" (or "Mac Catalyst"). At first start it creates the tables, seeds the default settings and
   asks you to create the first Developer (super-admin) account. Administrators retain scheduler administration
   access; Developers also have super-admin access to Developer accounts and Developer-only features.
   Viewers have read-only access. There is no default password.
5. **Optional, the Maintenance Agent:** `dotnet run --project demos/DemoApps.MaintenanceAgent`. It shares the
   same database, so its tasks appear in the Admin app and both hosts claim work without running a task twice.

## Building and testing

- **Any OS, without the Admin app:** `dotnet build GSST.Core.slnf` and `dotnet test GSST.Core.slnf`.
- The integration tests create and delete their own `gmss_test_*` databases, so the `gmss_app` role needs
  the `CREATEDB` right on a development machine: `ALTER ROLE gmss_app CREATEDB;` (not needed in normal use).
  Without `GMSS_DB_PASSWORD` they are skipped.
- Settings live in `appsettings.json` (embedded in the Admin app; next to the Maintenance Agent):
  `Database` (host, port, name, user), `Paths` (backup folder, log folder, PostgreSQL tools) and `Engine`
  (host name shown on the dashboard, built-in tasks on or off).

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
- **Credential store:** the database password comes only from the `GMSS_DB_PASSWORD` environment variable.
- **Local HTTP task type:** only addresses on the same computer (localhost) may be called, and redirects are
  not followed, because the system is fully local.
