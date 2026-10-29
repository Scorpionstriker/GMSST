-- Creates the three local databases and their least-privilege login role (NFR-14, WP-1):
--   gmss              the scheduler's own database (tables created by EF Core migrations, FR-38);
--   inventory_demo    the Inventory Monitor module's database (schema "inventory", created by the module);
--   maintenance_demo  the separate Maintenance Agent application's database (schema "maintenance", created by the agent).
--
-- On Windows, run database\setup_windows.ps1, which runs this script and sets the environment variables.
-- Otherwise run it once as the PostgreSQL superuser, choosing the password on the command line:
--
--   psql -U postgres -v app_password='choose-a-strong-password' -f database/create_database.sql
--
-- Then set the same password in the GMSS_DB_PASSWORD environment variable of the user account that runs the
-- applications (NFR-15). The password is never stored in this repository.
-- The script can be run again safely: it only creates what is missing.

\set ON_ERROR_STOP on

\if :{?app_password}
\else
  \echo 'Set the password with: -v app_password=...'
  \quit
\endif

-- The login role used by the Admin application and the Maintenance Agent. It is not a superuser and
-- cannot create roles or other databases.
SELECT format('CREATE ROLE gmss_app LOGIN PASSWORD %L NOSUPERUSER NOCREATEDB NOCREATEROLE', :'app_password')
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'gmss_app')
\gexec

-- Each database is owned by gmss_app, so the applications can create their own schemas and tables without
-- superuser rights.
SELECT format('CREATE DATABASE %I OWNER gmss_app ENCODING ''UTF8'' TEMPLATE template0', name)
FROM (VALUES ('gmss'), ('inventory_demo'), ('maintenance_demo')) AS wanted(name)
WHERE NOT EXISTS (SELECT 1 FROM pg_database WHERE datname = wanted.name)
\gexec

-- Only gmss_app (and superusers) may connect to them.
REVOKE CONNECT ON DATABASE gmss FROM PUBLIC;
GRANT CONNECT ON DATABASE gmss TO gmss_app;
REVOKE CONNECT ON DATABASE inventory_demo FROM PUBLIC;
GRANT CONNECT ON DATABASE inventory_demo TO gmss_app;
REVOKE CONNECT ON DATABASE maintenance_demo FROM PUBLIC;
GRANT CONNECT ON DATABASE maintenance_demo TO gmss_app;

\echo 'Databases gmss, inventory_demo and maintenance_demo and role gmss_app are ready. Set GMSS_DB_PASSWORD and start the applications.'
