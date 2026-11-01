# How GSST works

GSST is a locally hosted, modular scheduler. It runs one scheduling engine inside the .NET MAUI Blazor Admin
application, uses PostgreSQL for durable scheduler state, and loads optional modules through explicit contracts.
The core scheduler does not reference the demonstration applications.

## Project boundaries

| Project | Responsibility |
|---|---|
| `Scheduler.Domain` | Scheduler entities, enums and state-transition rules; no project dependencies |
| `Scheduler.Extensibility` | Public contracts that modules and adopting applications implement or call |
| `Scheduler.Application` | Use cases, validation, authorization, report/log queries and application-owned persistence interfaces |
| `Scheduler.Engine` | Polling, enqueueing, claiming, worker execution, retry/recovery and resource-health checks |
| `Scheduler.Infrastructure` | PostgreSQL, EF Core, logging providers, machine settings, backup and concrete persistence |
| `Scheduler.Modules.Security` | Example authentication and user-management module |
| `Scheduler.Admin` | Desktop composition root, GUI, module catalog and the hosted scheduler engine |
| `demos/` | Removable Inventory Monitor module and separate Maintenance Agent application |

Dependencies point inward. The Admin project composes the layers; an engine, application or infrastructure project
does not depend on demo code. The Inventory Monitor owns its database and domain behavior. The Maintenance Agent
is a separate local application reached through the connector module and loopback HTTP calls.

## One work cycle

1. The Admin host loads modules listed in `src/Scheduler.Admin/ModuleCatalog.cs`, registers their services and pages,
   applies scheduler migrations and initializes module-owned storage/tasks.
2. The engine acquires a PostgreSQL advisory lock so only one scheduler engine runs against a scheduler database.
3. On each cycle it recovers expired worker leases, enqueues due schedules and claims eligible queue rows with
   PostgreSQL row locking. Priority, due time and queue time determine the claim order.
4. A bounded worker runs the registered `ITaskHandler` with an `ITaskContext`, including configured payload,
   execution ID and cancellation token. Event-triggered entries also pass their event name, source and JSON data.
5. The engine records success, failure, time-out, output and logs; retry policy can enqueue another attempt.
   Delivery is at-least-once, so handlers should be idempotent.
6. On shutdown the host stops claiming work, allows a configured grace period, cancels remaining work and recovers
   interrupted runs on the next start.

An optional per-task execution cap increments at every started run, including manual, event-triggered and retry
starts. At the cap, the task becomes Completed, waiting entries are cancelled, and already-running work is allowed
to finish.

## Data and history

The `gmss` database stores schedules, queue entries, executions, execution logs, errors, users, configuration,
notifications and audit events. The Inventory module owns `inventory_demo`; the Maintenance Agent owns
`maintenance_demo`. Local computer paths and secrets are not stored in the scheduler database. `GMSS_DB_PASSWORD`
and `GSST_AGENT_KEY` are supplied through environment variables.

Execution history is a record of task runs. Error log is a direct error view. Log History directly browses stored
audit events, scheduler logs and sign-in attempts. Reports are generated filtered summaries and are not required
for log browsing. Database and file logging providers are configurable. Audit records are retained according to
the audit-retention setting.

Health monitoring observes repeated failures/timeouts, stale worker heartbeats, queue backlog/saturation and
scheduler process CPU and memory. It logs and sends in-app notifications to active Administrators and Developers;
resource alerts do not pause scheduling. Per-task OS resource quotas are not implemented.

## Extension versus core change

Use the Extensibility contracts when adding task types, module-owned pages, business-event mappings, audit records,
module logging, authentication providers, notifications, or managed applications. This keeps module behavior
removable and independently owned.

Change core projects only for behavior that should apply to every task, module or host—for example a new queue
policy, a scheduler-wide execution rule or a new core persistence capability. Core changes must preserve project
dependency direction, update persistence and UI surfaces as relevant, and include tests. See
[Module Development](Module_Development.md), [Core Development](Core_Development.md) and the detailed
[Technical Guide](Technical_Guide.md).
