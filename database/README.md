# Database

The schema itself is created by EF Core migrations in `src/Scheduler.Infrastructure/Persistence/Migrations`,
which the Admin application applies automatically at first start (FR-38).

This folder holds what migrations do not cover:

- `create_database.sql`: creates the `gmss` database and the least-privilege `gmss_app` role (NFR-14). Run it once
  as the PostgreSQL superuser: `psql -U postgres -v app_password='...' -f database/create_database.sql`;
- `schema.sql`: the full schema as SQL, generated from the migrations with `dotnet ef migrations script`, for
  reading and for the documentation (the application does not use it);
- `seed/`: notes on the default configuration values and logging providers seeded at first start.
