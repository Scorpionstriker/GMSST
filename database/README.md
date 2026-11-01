# Database

The schema itself is created by EF Core migrations in `src/Scheduler.Infrastructure/Persistence/Migrations`,
which the Admin application applies automatically at first start (FR-38).

This folder holds what migrations do not cover:

- `create_database.sql`: creates the least-privilege `gmss_app` role and the `gmss`, `inventory_demo` and
  `maintenance_demo` databases (NFR-14). Run it as the PostgreSQL superuser using the setup instructions in
  [Installation_Guide.md](../docs/Installation_Guide.md);
- `schema.sql`: a checked-in SQL reference for the current schema, including tables, indexes and constraints; the
  application applies the EF Core migrations at start-up and does not execute this file;
- `seed/`: notes on the default configuration values and logging providers seeded at first start.

When adding a persistent feature, update the EF entity configuration and model snapshot, add a forward migration,
and keep `schema.sql` in sync. Verify migrations against PostgreSQL as well as running the unit tests; do not edit
an already-applied migration to represent a new schema change.
