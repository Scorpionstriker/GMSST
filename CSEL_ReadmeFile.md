# Core Scheduling Engine Layer

Projects: `Scheduler.Domain`, `Scheduler.Application`, `Scheduler.Engine`.

- **Domain** holds the entities, status enums and rules. It references nothing.
- **Application** holds the use cases, validation, cron parser, schedule calculator, retry policy and
  dependency-cycle check.
- **Engine** runs the adaptive polling loop, the bounded worker pool, time-outs, retries, start-up
  recovery and controlled shutdown, and holds the four built-in task types. Only one engine runs per
  database; a second one stays stopped and says which computer holds the lock.

See README.md for the requirements each project serves.
