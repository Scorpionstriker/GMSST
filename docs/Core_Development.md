# Core Development Guide

Use core changes only when behavior must be shared by every module or host. Application-specific workflows usually
belong in a module. The core must remain independent of demonstration projects and module implementations.

## Choose the right layer

| Change | Usual location |
|---|---|
| Entity invariant or state transition | `Scheduler.Domain` |
| Public module/host integration capability | `Scheduler.Extensibility/Contracts` |
| Validated and authorized use case | `Scheduler.Application` |
| Queue execution, worker, retry or recovery behavior | `Scheduler.Engine` |
| Database mapping, migration, query or logging provider | `Scheduler.Infrastructure` |
| Desktop composition, navigation or shared Admin experience | `Scheduler.Admin` |
| Inventory or Maintenance business behavior | Its project under `demos/`, not core |

Project references enforce dependency direction: Domain has no dependencies; Extensibility can reference Domain;
Application and Engine use Domain/Extensibility; Infrastructure implements Application interfaces; Admin composes
the projects. Architecture tests guard these boundaries.

## Implement a cross-cutting feature

1. Trace the relevant contract, use case, handler, domain entity, persistence, host wiring and UI. Add or extend a
   contract only when modules/hosts need that capability.
2. Define behavior for permissions, retries, cancellation, shutdown, concurrency, missing modules, and recovery
   before changing the implementation.
3. Put validation and role authorization in Application use cases so every entry point follows the same rules.
   Do not rely on hiding GUI controls for security.
4. Keep host-specific behavior behind an interface. The Engine must not take a project dependency on Infrastructure,
   Admin UI, Security module or demo projects.
5. If persistent state changes, update the entity, EF configuration, forward migration, model snapshot and
   `database/schema.sql`. Migrations run at startup; schema reference SQL is not executed by the application.
6. Update all affected UI surfaces and user/developer documentation. Add unit tests for rules and wiring and
   PostgreSQL integration tests for constraints, query semantics and migration behavior.
7. Run focused tests/builds, then the core solution build/tests and architecture tests where applicable. Validate
   migrations against PostgreSQL; a compile-only test does not verify SQL behavior.

The scheduler is at-least-once. A handler can repeat after a process crash or failed result persistence; use stable
idempotency keys and transactions where appropriate. Task timeouts and shutdown cancellation are delivered through
`ITaskContext.CancellationToken`. Avoid blocking the fixed worker pool on long synchronous work.

## Adapt GSST to another application or platform

The current MAUI Blazor Admin host is supported on Windows and macOS. `GSST.Core.slnf` builds core projects and the
Maintenance Agent without the MAUI Admin app on Linux; the complete desktop GUI is not a Linux target.

To embed scheduling in another host, compose the Application, Infrastructure and Engine projects in that host,
register the scheduler services and modules, provide database credentials through the supported environment
configuration, and control engine startup/shutdown with the host lifecycle. Modules and external in-process callers
can use `ISchedulerClient`; it is not itself an HTTP or network API. If another application needs remote scheduling,
build a separate authenticated API adapter around the contract and explicitly define authorization, validation,
rate limits, observability and deployment ownership. Do not expose PostgreSQL or an internal scheduler API directly.

For a UI tailored to a different application, retain the shared application/use-case contracts and add an adapter
or host-specific UI. Keep product-specific workflows in modules and preserve database ownership boundaries. A module
can be replaced or removed without rewriting the engine; core modifications are appropriate only for scheduler-wide
requirements.

See [How It Works](How_It_Works.md), [Module Development](Module_Development.md), and the detailed
[Technical Guide](Technical_Guide.md).
