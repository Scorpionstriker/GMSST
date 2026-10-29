-- Creates the scheduler database and its least-privilege login role (NFR-14).
-- Run once, as the PostgreSQL superuser, choosing the password on the command line:
--
--   psql -U postgres -v app_password='choose-a-strong-password' -f database/create_database.sql
--
-- Then set the same password in the GMSS_DB_PASSWORD environment variable of the Windows or macOS user
-- account that runs the scheduler (NFR-15). The password is never stored in this repository.
-- The tables are created by the application itself at first start (EF Core migrations, FR-38).
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

-- The database is owned by gmss_app so that migrations can create the scheduler schema (and modules
-- their own schemas, such as inventory) without superuser rights.
SELECT 'CREATE DATABASE gmss OWNER gmss_app ENCODING ''UTF8'' TEMPLATE template0'
WHERE NOT EXISTS (SELECT 1 FROM pg_database WHERE datname = 'gmss')
\gexec

-- Only gmss_app (and superusers) may connect.
REVOKE CONNECT ON DATABASE gmss FROM PUBLIC;
GRANT CONNECT ON DATABASE gmss TO gmss_app;

\echo 'Database gmss and role gmss_app are ready. Set GMSS_DB_PASSWORD and start the Admin application.'
