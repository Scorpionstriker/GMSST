# Core Scheduling Engine Layer README

Projects: `Scheduler.Domain`, `Scheduler.Application`, `Scheduler.Engine`.

- **Domain** holds the entities, status enums and rules. It references nothing.
- **Application** holds the use cases, validation, cron parser, schedule calculator, retry policy and
  dependency-cycle check.
- **Engine** runs the adaptive polling loop, bounded worker pool, time-outs, retries, start-up recovery and
  controlled shutdown. Execution starts—including retries, manual and event-triggered runs—increment a task's
  optional maximum-execution counter. Reaching the cap completes the task, cancels waiting entries and lets
  already-running work finish.
- **Engine health monitoring** checks repeated failures and timeouts, stale worker heartbeats, queue pressure and
  scheduler-process CPU/working-set memory. It logs and notifies without pausing scheduling; resource quotas are
  not enforced because task-specific OS process isolation is not implemented.

Only one engine runs per scheduler database; another host stays stopped and identifies the host holding the lock.
The Inventory Monitor and Maintenance Agent remain separate modules/applications, not built-in core handlers.
Modules can trigger a task by stable system key with separate event data; the engine stores event metadata in the
queue and passes it to the handler separately from its configured payload.

The Application layer also supplies durable in-app notifications and direct log-history queries. Contracts exposed
to modules remain in `Scheduler.Extensibility`; the engine does not reference demo projects.

See [How_It_Works.md](docs/How_It_Works.md), [Core_Development.md](docs/Core_Development.md) and
[Technical_Guide.md](docs/Technical_Guide.md) for the execution flow and implementation details.
