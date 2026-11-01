# Data Layer README

Project: `Scheduler.Infrastructure`.

EF Core with Npgsql against a local PostgreSQL database (`gmss`, schema `scheduler`): migrations, the
`SELECT ... FOR UPDATE SKIP LOCKED` claim query, the single-engine lock, the Console, File and Database
logging providers, the in-memory configuration cache, and backup and restore of the scheduler and demo
databases (`gmss`, `inventory_demo`, `maintenance_demo`) with pg_dump/pg_restore.

The database password is read from the `GMSS_DB_PASSWORD` environment variable, never from a file.

Infrastructure implements application-owned persistence/query contracts. Module databases remain separate:
the Inventory Monitor owns `inventory_demo`, and the Maintenance Agent owns `maintenance_demo`. The scheduler
stores task/event queue data, execution/error/log history, recipient-specific notifications, users, configuration
and audit records in `gmss`. `LogHistoryQueries` supports direct filtered GUI browsing; report queries remain
separate.

Computer-specific paths (backup, backup copies, logs and CSV exports) are kept in the user's local settings file,
not the scheduler database. `database/schema.sql` is a reference file; the Admin app applies forward EF migrations.
When changing persistence, update the entity, configuration, migration, model snapshot, schema reference and
integration tests together. Migration behavior requires PostgreSQL validation.

See [How_It_Works.md](docs/How_It_Works.md) and [Core_Development.md](docs/Core_Development.md).
