# Data Layer

Project: `Scheduler.Infrastructure`.

EF Core with Npgsql against a local PostgreSQL database (`gmss`, schema `scheduler`): migrations, the
`SELECT ... FOR UPDATE SKIP LOCKED` claim query, the single-engine lock, the Console, File and Database
logging providers, the in-memory configuration cache, and backup and restore of the scheduler and demo
databases (`gmss`, `inventory_demo`, `maintenance_demo`) with pg_dump/pg_restore.

The database password is read from the `GMSS_DB_PASSWORD` environment variable, never from a file.
