# Technical guide

This guide is for developers who maintain or extend the Generic System Scheduler Template (GSST). It explains the
architecture, how the engine works, the database design, the two ways other applications integrate with the
scheduler, and how to add a module. Requirement IDs (FR, NFR, BR, DR) refer to the Requirements Specification.
Installation is covered in the [Installation guide](Installation_Guide.md).

## 1. Architecture

GSST is a layered, modular monolith that runs on one computer (NFR-07, NFR-09). Dependencies point inwards:

| Layer (solution folder) | Project | Responsibility | May depend on |
|---|---|---|---|
| Core Scheduling Engine | Scheduler.Domain | Entities, status enums, state transition rules | nothing |
| Core Scheduling Engine | Scheduler.Application | Use cases with validation, authorisation and logging decorators, cron parser, schedule calculator, retry policy, reports, backup and configuration use cases | Domain, Extensibility |
| Core Scheduling Engine | Scheduler.Engine | Scheduling loop, worker pool, task executor, recovery, built-in task types | Application, Domain, Extensibility |
| Data | Scheduler.Infrastructure | EF Core and Npgsql persistence, migrations, queue claiming, logging providers, cache, backup with pg_dump and pg_restore, engine lock | Application, Domain, Extensibility |
| Extensibility | Scheduler.Extensibility | Public contracts for modules: `ITaskHandler`, `ISchedulerModule`, `ILoggingProvider`, `IAuthenticationProvider`, `ISchedulerClient` and others | Domain |
| Extensibility | Scheduler.Modules.Security | Login and Security module: local users, PBKDF2 hashing, lockout, roles, self-registration with administrator approval, password recovery with a one-time recovery code (no email needed), Login, Register, Forgot password, Users and Change password pages | Extensibility |
| User interface | Scheduler.Admin | .NET MAUI Blazor Hybrid desktop app and composition root; hosts the engine | everything |
| Demo apps | DemoApps.InventoryMonitor | Integration method 1: a module inside the scheduler | Extensibility |
| Demo apps | DemoApps.MaintenanceAgentConnector | Integration method 2: a module that manages the separate Maintenance Agent | Extensibility |
| Demo apps | DemoApps.MaintenanceAgent | A separate local web application with its own database | none of the scheduler projects |

`Scheduler.ArchitectureTests` (NetArchTest) fails the build pipeline when a project uses a layer it may not, or
when any layer other than Infrastructure uses EF Core or Npgsql (FR-77). Positive-control tests prove the rules
really detect a dependency, so they cannot pass by accident.

Inside a layer the code is organised by feature (`Tasks/`, `Queue/`, `Reports/`, `Backup/` …). Each use case is
a command or query record with one handler, found by assembly scanning and wrapped in decorators for validation,
the Administrator role check (`IRequireAdministrator`) and logging. Handlers return `Result` or `Result<T>` with an
error kind (Validation, NotFound, Forbidden, Conflict, Failure) and per-field messages instead of throwing.

## 2. One engine per database

Exactly one scheduling engine runs per scheduler database, inside the Admin app. The Maintenance Agent and the
Inventory module contain no engine. `PostgresEngineInstanceLock` takes a session-level advisory lock
(`pg_try_advisory_lock`) on a dedicated connection that stays open while the engine runs. The lock ends with the
connection, so a crashed engine never blocks the next start. A second Admin app against the same database keeps
its engine stopped and shows "The scheduler engine is already running on <host>. Only one scheduler may run per
database." The `engine_instance` table (one row) records which host holds the lock and since when.

Because there is only one engine, Run now, pause and resume in the Admin app wake the engine directly through
`IEngineSignal`; there is no cross-process signalling.

## 3. The engine cycle

`SchedulerEngine` runs this loop (FR-18 to FR-21, FR-32 to FR-34):

1. **Heartbeat** the engine lock and the worker rows; take the lock again if its connection was lost.
2. **Recover**: mark workers without a heartbeat for `WorkerStaleSeconds` as Stopped, and handle claimed entries
   whose lease expired. A run that had started is recorded as failed and retried under the normal rules; an
   entry that never started is released back to Waiting.
3. **Enqueue**: for each Active task whose next run is due, add a Waiting queue entry. Runs more than 60 seconds
   late follow the schedule's misfire policy (Skip, Run once, Run all up to `RunAllMisfireCap`). Tasks whose type
   has no registered handler are not queued and show a "No handler" badge.
4. **Claim** while a worker is free: `SELECT … FOR UPDATE SKIP LOCKED`, ordered by priority (1 is highest),
   scheduled time, enqueue time and id. The claim sets a lease of the task's time-out plus `LeaseGraceSeconds`.
5. **Execute** on a worker of the fixed pool (`MaxConcurrency`, default 4). `TaskExecutor` creates the execution
   row, calls the handler with a cancellation token that fires at the time-out or at shutdown, and records the
   result, output and error.
6. **Retry** a failed or timed-out run with exponential back-off from `RetryBaseDelaySeconds` up to
   `RetryMaxDelaySeconds`, at most `MaxRetryLimit` times (a task may override all three).
7. **Release dependants**: when a run succeeds, tasks that depend on it are queued once all their prerequisites'
   latest final runs since the last dependency trigger succeeded.
8. **Sleep** until the next run or waiting entry is due, at most `MaxPollIntervalSeconds`, at least 0.5 s. A wake
   signal ends the sleep early. Only entries this host can claim count, so a busy pool does not cause a busy loop.

Stopping (window closed, or pause) stops claiming, gives running tasks `ShutdownGraceSeconds`, then cancels them;
cancelled runs are queued again with the same attempt number. The app waits at most `ShutdownGraceSeconds` + 15 s.

### 3.1 Task states

| Status | Scheduled runs | Run now | Retries of an earlier run | Dependency triggers |
|---|---|---|---|---|
| Active | yes | yes | yes | yes |
| Paused | no | yes (the manual run and its own retries) | no | no |
| Disabled | no | no | no | no |

Paused means "temporarily stopped"; Disabled means "switched off until enabled". Status changes follow the
transition table in `StatusTransitions`.

### 3.2 Delivery guarantee

Delivery is **at-least-once** (NFR-01): work is never lost, but after a crash, or when the result of a finished run
could not be saved, the same work may run again. `TaskExecutor` retries saving a result three times (after 1, 2 and
4 seconds) before giving up; the lease then expires and recovery runs the task again. Handlers must therefore be
idempotent. `ITaskContext.ExecutionId` identifies one attempt and can be stored as an idempotency key, as
`GenerateStockReportHandler` does with a unique `execution_id` on `inventory.stock_report`. Work that must never
repeat should also check its own data. When a worker is free, a due task starts within 5 seconds of its due time
(tested in `InMemoryDeliveryTests`).

## 4. Database design

There are three PostgreSQL databases, all owned by the least-privilege role `gmss_app` (NFR-14) and created by
`database/create_database.sql`:

| Database | Owner of the schema | Tables |
|---|---|---|
| `gmss` | Scheduler (EF Core migrations) | `task`, `schedule`, `task_dependency`, `task_queue`, `task_execution`, `worker_thread`, `error_log`, `execution_log`, `configuration`, `logging_provider`, `app_user`, `login_audit`, `engine_instance` |
| `inventory_demo` | Inventory Monitor module | schema `inventory`: `product`, `stock_report` |
| `maintenance_demo` | Maintenance Agent | `health_check`, `cleanup_run` |

Main relationships in `gmss`: a `task` has one `schedule` (cron, interval or once, with a time zone), many
`task_dependency` rows (no cycles, checked on save), many `task_queue` entries and many `task_execution` rows. An
execution may point to its queue entry (`queue_id`, `ON DELETE SET NULL`, so purging old queue entries keeps the
history) and has `error_log` and `execution_log` rows. All times are stored in UTC. Status columns have check
constraints. `task.system_key` (unique, nullable) marks built-in tasks. `task_queue.is_manual_run` lets a paused
task's manual run and its retries go ahead. `app_user`, `configuration` and `logging_provider` use the PostgreSQL
`xmin` column as a concurrency token, so a stale edit is refused with a "changed by someone else" message.

The full schema as SQL is in `database/schema.sql` (generated with `dotnet ef migrations script --idempotent`).
Migrations are applied at start-up under an advisory lock, and only after a verified safety backup of `gmss`
when migrations are pending (FR-73).

## 5. Integration methods

### 5.1 Method 1: a module inside the scheduler (Inventory Monitor)

`InventoryModule` implements `ISchedulerModule`. It registers three task handlers (GenerateStockReport,
LowStockCheck, WeeklySummary), adds the Inventory menu with three pages, connects to its own database through
`IModuleDatabases.Get("Inventory", "inventory_demo")`, creates its schema and sample data in `InitializeAsync`,
and creates its scheduled tasks through `ISchedulerClient.EnsureTaskAsync`. Write operations require the
Administrator role.

### 5.2 Method 2: managing a separate application (Maintenance Agent)

The Maintenance Agent is an ASP.NET Core minimal API with no scheduler code. It listens only on
`127.0.0.1:5087` and `[::1]:5087` and has three endpoints:

| Endpoint | What it does |
|---|---|
| `GET /health` | Checks its database, free disk space and memory, records the result, returns 200 (healthy) or 503 |
| `POST /cleanup` | Deletes old files from allowed temporary folders, records the run, returns counts |
| `GET /status` | Name, version, uptime, last health check and last clean-up |

Every request needs the header `X-GSST-Key` equal to `GSST_AGENT_KEY`; otherwise the answer is 401. The
`MaintenanceAgentConnector` module creates two tasks of the built-in **Local HTTP Request** type
("Maintenance: health check" every 5 minutes, and "Maintenance: temporary-file clean-up" daily at 03:00). Their
payload names the header value as `env:GSST_AGENT_KEY`, which the handler reads from the environment at run time,
so the key is never stored in the database. The Local HTTP Request type only calls loopback addresses and does not
follow redirects. The connector also adds the Managed apps > Maintenance Agent page and an `IRestoreGuard` that
refuses to restore `maintenance_demo` while the agent is running.

## 6. Extending the scheduler

### 6.1 Worked example: a new module

1. Create a class library that references only `Scheduler.Extensibility` (and `Microsoft.AspNetCore.Components.Web`
   if it has pages).
2. Write a handler:

   ```csharp
   public sealed class ArchiveFilesHandler : ITaskHandler
   {
       public const string Type = "ArchiveFiles";
       public string TaskType => Type;
       public string DisplayName => "Archive files";
       public string? PayloadExample => """{ "folder": "C:\\Data\\Out" }""";

       public async Task<TaskResult> ExecuteAsync(ITaskContext context)
       {
           var payload = context.GetPayload<ArchivePayload>();
           if (string.IsNullOrWhiteSpace(payload?.Folder))
           {
               return TaskResult.Failure("The payload needs a folder.");
           }

           // Do the work; honour context.CancellationToken and make it safe to run twice.
           return TaskResult.Success("Archived.");
       }
   }
   ```

3. Write the module:

   ```csharp
   public sealed class ArchiveModule : ISchedulerModule
   {
       public string Name => "File archive";
       public IReadOnlyList<ModuleMenuItem> MenuItems { get; } = [];

       public void Register(IServiceCollection services) =>
           services.AddTransient<ITaskHandler, ArchiveFilesHandler>();

       public Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken) =>
           services.GetRequiredService<ISchedulerClient>().EnsureTaskAsync(new TaskDefinition
           {
               Name = "Archive: nightly",
               SystemKey = "archive.nightly",
               TaskType = ArchiveFilesHandler.Type,
               CronExpression = "30 23 * * *",
               Payload = """{ "folder": "C:\\Data\\Out" }"""
           }, cancellationToken);
   }
   ```

4. Add a project reference from `Scheduler.Admin` and one line to `ModuleCatalog.cs`. To remove the module, do the
   reverse; its tasks then show "No handler" and are not queued.
5. Pages: add Razor components with `@page` routes in the module and list them in `MenuItems`. The Admin app
   discovers routes from the module's assembly. `NavigationTests` checks that every menu entry has a page.

### 6.2 Other extension points

| Contract | Use |
|---|---|
| `ILoggingProvider` | A new log destination. Registered providers are added to the Logging providers list at start-up and can be switched on and given a minimum level on the Configuration page |
| `IAuthenticationProvider`, `IUserAccountStore` | Replace the local sign-in, for example with a directory service |
| `IModuleDatabases` | A module's own database connection from a configuration section, using `GMSS_DB_PASSWORD` |
| `IRestoreGuard` | Refuse a restore while something depends on a database |
| `IAuditLog` | Write security audit entries that appear in the Security and audit report |
| `ISchedulerClient` | Create tasks by system key or name, and run them now |

## 7. Configuration

Scheduler settings live in the `configuration` table, are checked against `SettingDefinitions` before they are
stored, and are cached (FR-36, FR-37). Defaults are seeded at first start and never overwrite a changed value.

| Setting | Default | Allowed | Task override |
|---|---|---|---|
| MaxConcurrency | 4 | 1–16 (after restart) | |
| MaxPollIntervalSeconds | 30 | 5–300 | |
| MaxRetryLimit | 3 | 0–10 | yes |
| RetryBaseDelaySeconds | 30 | 1–3600 | yes |
| RetryMaxDelaySeconds | 900 | 1–86400 | yes |
| LeaseGraceSeconds | 60 | 10–3600 | |
| LogRetentionDays | 90 | 1–3650 | |
| AuditRetentionDays | 365 | 30–3650 | |
| BackupRetentionCount | 14 | 1–365 | |
| SessionTimeoutMinutes | 20 | 1–480 | |
| MaxFailedLogins | 5 | 1–20 | |
| LockoutMinutes | 15 | 1–1440 | |
| RunAllMisfireCap | 10 | 1–1000 | |
| ShutdownGraceSeconds | 30 | 0–600 | |
| WorkerStaleSeconds | 120 | 30–3600 | |

Computer-specific settings (folders, login bypass, agent address) are in the user settings file, described in the
installation guide. The embedded `appsettings.json` holds the `Database`, `Inventory`, `MaintenanceAgent` and
`Engine` defaults. Passwords and keys come only from the environment variables `GMSS_DB_PASSWORD` and
`GSST_AGENT_KEY` (NFR-15).

## 8. Logging

Every run writes structured entries through the enabled logging providers (FR-28 to FR-30):

| Provider | Destination | Default |
|---|---|---|
| Database | `execution_log` and `error_log` tables, shown on the Execution history and Error log pages | on, from Info |
| File | One JSON-lines file per day, `gmssYYYYMMDD.log`, in the log folder | on, from Info |
| Console | Standard output, for development | off |

Levels are Info, Warning and Error. Security events (sign-ins, user changes, login bypass changes) are Info entries
with source `Security`. The Log Clean-up task removes executions, errors, log entries and log files older than
`LogRetentionDays`, and sign-in and audit records older than `AuditRetentionDays`.

## 9. Backup set format

A backup is a **set**: a folder named after its kind and local time (`gsst_backup_20261029_143000`, or
`gsst_prerestore_…` and `gsst_premigration_…` for the safety backups taken before a restore and before an upgrade)
holding one `pg_dump -Fc` file per selected database and a `manifest.json` with the time, kind (Manual, Scheduled,
PreRestore or PreMigration),
databases, file sizes, the `pg_restore --list` verification result and a SHA-256 checksum per file. Sets are
written to the backup folder and, when set, copied to the copy folder, where the checksums are checked again. The
newest `BackupRetentionCount` normal sets and safety sets younger than seven days are kept in both folders.

A restore checks the checksums, asks every `IRestoreGuard`, pauses the engine, and restores each chosen database
with `--clean --if-exists --single-transaction --no-owner --exit-on-error`, so a failed restore leaves that
database unchanged. Restoring `gmss` signs the user out.

## 10. Design decisions and the tests that check them

The automated tests need no database. `tests/Scheduler.UnitTests/InMemory` holds an in-memory implementation of the
application layer's persistence interfaces (`IUnitOfWorkFactory` and its repositories, `ISettingsProvider` and
`IUserAccountStore`). It keeps the rules the tests depend on: unique task names ignoring case, row versions for
optimistic concurrency, rollback of unsaved changes, and the claim order with one claim per entry. `InMemoryHost`
composes the use cases, the engine and the security module on it exactly as the Admin application does, so the
engine, delivery, retries, recovery, dependencies and security are tested end to end in seconds. What only
PostgreSQL does (SKIP LOCKED between connections, the advisory lock, constraints, migrations, `pg_dump` and bulk
deletes) is checked by the manual procedures MC-17 to MC-22 in [Test_Cases.md](Test_Cases.md).

| Decision | Checked by |
|---|---|
| Cron times are wall-clock times in the schedule's time zone; a time skipped by daylight saving runs at the first valid minute; day-of-month and weekday both starting with `*` follow the AND rule | `CronExpressionTests` |
| A cron step of 0 is rejected instead of looping | `CronExpressionTests` |
| The engine does not spin while its only worker is busy | `InMemorySchedulingTests` |
| Missed runs follow Skip, Run once or Run all (capped) | `ScheduleCalculatorTests`, `InMemorySchedulingTests` |
| No shell task type; the HTTP task type calls only localhost | `HandlerTests` |
| Validators are registered by assembly scanning and run before every use case | `ApplicationWiringTests` |
| A claim lasts the time-out plus `LeaseGraceSeconds`; recovery handles expired leases | `InMemorySchedulingTests`, `InMemoryDeliveryTests` |
| Bounded worker pool, time-outs, controlled shutdown, retries with back-off | `InMemoryEngineTests`, `InMemoryDeliveryTests`, `RetryPolicyTests` (in `RulesTests.cs`) |
| End times apply to every schedule type | `ScheduleCalculatorTests` |
| Run now queues a separate Manual entry and leaves the task's status unchanged | `InMemoryEngineTests`, `InMemorySchedulingTests` |
| Reports are aggregated in PostgreSQL; CSV export is newest first, capped and safe against formula injection | `CsvWriterTests`; manual MC-13 |
| Only migrations create the schema, under an advisory lock, after a safety backup | Manual MC-18, MC-21 |
| One engine per database | Manual MC-17 |
| Built-in tasks have stable system keys and cannot be deleted | `InMemoryUseCaseTests`, `InMemorySchedulingTests` |
| Every menu entry has a page and every page is reachable | `NavigationTests` |
