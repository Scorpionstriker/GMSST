# Database

The schema itself is created by EF Core migrations in `src/Scheduler.Infrastructure/Persistence/Migrations`,
which the Admin application applies automatically at first start (FR-38).

This folder holds what migrations do not cover:

- a script to create the `gmss` database and the least-privilege `gmss_app` role (NFR-14);
- `seed/`: notes on the default configuration values and logging providers seeded at first start.
