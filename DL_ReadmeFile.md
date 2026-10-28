# Data Layer

Project: `Scheduler.Infrastructure`.

EF Core with Npgsql against a local PostgreSQL database (`gmss`, schema `scheduler`): migrations, the
`SELECT ... FOR UPDATE SKIP LOCKED` claim query, the Console, File and Database logging providers, the
in-memory configuration cache, and backup and restore with pg_dump/pg_restore.

The database password is read from the `GMSS_DB_PASSWORD` environment variable, never from a file.
