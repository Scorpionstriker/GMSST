# Installation and configuration guide

This guide sets up the Generic Moduler System Scheduler Template (GSST) on one Windows computer, which is the main
platform (NFR-10). Everything runs locally: PostgreSQL, the Admin application (which hosts the only scheduling
engine) and the Maintenance Agent. For macOS see [Build_macOS.md](Build_macOS.md); for Linux, which runs the core
libraries and the Maintenance Agent only, see [Build_Linux.md](Build_Linux.md).

## 1. What you need

| Item | Version used | Notes |
|---|---|---|
| Windows | 10 (19041) or 11, 64-bit | |
| .NET SDK | 10.0.100 or a later 10.0 feature band | `global.json` pins 10.0.100 with `rollForward: latestFeature`, so 10.0.3xx and 10.0.4xx also work |
| Visual Studio | 2026 (18.x) with the **.NET Multi-platform App UI development** workload | Needed for the Admin app only |
| .NET MAUI workload | `maui-windows` 10.0.110 | `Directory.Packages.props` pins `Microsoft.Maui.Controls` and `Microsoft.AspNetCore.Components.WebView.Maui` to 10.0.110 (NFR-13). Check yours with `dotnet workload list` |
| PostgreSQL | 18 (16 or later works) | Server and command-line tools (`psql`, `pg_dump`, `pg_restore`) |

The workload versions on the reference laptop (`dotnet workload list`, workload set 10.0.400) were: maui-windows
10.0.110, android 36.1.118, ios 26.5.10315 and maccatalyst 26.5.10315. Only `maui-windows` is needed on Windows.
When you update Visual Studio and the MAUI workload changes, update the two MAUI versions in
`Directory.Packages.props` to the new `maui-windows` version, then rebuild and run the tests.

## 2. Install PostgreSQL

1. Download the Windows installer for PostgreSQL 18 from postgresql.org and run it.
2. Keep the default port **5432**. Choose a strong password for the `postgres` superuser and keep it safe.
   GSST never stores or needs it after set-up.
3. Untick Stack Builder at the end; nothing from it is needed.

### 2.1 Keep PostgreSQL local only

GSST is fully local, so the database server should accept connections from this computer only (NFR-14).

1. Open `postgresql.conf` in the data folder (for example `C:\Program Files\PostgreSQL\18\data`) and check:

   ```
   listen_addresses = 'localhost'
   password_encryption = scram-sha-256
   ```

2. Open `pg_hba.conf` in the same folder. Keep only local lines and make every `host` line use
   `scram-sha-256`:

   ```
   # TYPE  DATABASE  USER  ADDRESS        METHOD
   host    all       all   127.0.0.1/32   scram-sha-256
   host    all       all   ::1/128        scram-sha-256
   ```

   Remove or comment out any line with `0.0.0.0/0`, `::/0` or a network address, and any line that uses `trust`.

3. Restart the service so the changes apply: open **Services**, find **postgresql-x64-18** and choose **Restart**,
   or run `Restart-Service postgresql-x64-18` in an administrator PowerShell window.

## 3. Create the databases and environment variables

Run the set-up script once from the `GSST` folder in PowerShell:

```
powershell -ExecutionPolicy Bypass -File database\setup_windows.ps1
```

The script:

1. finds `psql.exe` (on the PATH, then `C:\PostgreSQL 18`, then `C:\Program Files\PostgreSQL\18`, 17 and 16; pass
   `-PostgresBinFolder "C:\path\to\bin"` if yours is elsewhere);
2. asks you to choose a password for the `gmss_app` role (at least 12 characters), unless `GMSS_DB_PASSWORD` is
   already set, in which case that value is used;
3. asks for the `postgres` superuser password and runs `database\create_database.sql`, which creates the
   least-privilege role `gmss_app` and three databases owned by it: `gmss` (the scheduler), `inventory_demo`
   (demo app 1) and `maintenance_demo` (demo app 2). Other roles cannot connect to them;
4. sets two **user** environment variables: `GMSS_DB_PASSWORD` (the `gmss_app` password) and `GSST_AGENT_KEY`
   (a new random key shared by the Admin app and the Maintenance Agent).

Nothing is written to a file. The passwords are read only from these variables (NFR-15). You can run the script
again at any time: it creates only what is missing. `-NewAgentKey` replaces the agent key.

**Close and reopen Visual Studio, terminals and the applications** afterwards so they see the new variables.

## 4. First start of the Admin application

1. Open `GSST.slnx` in Visual Studio.
2. Set **Scheduler.Admin** as the start-up project, choose **Windows Machine** and press F5.
3. At first start the app creates the tables in `gmss` (migrations), seeds the default settings and logging
   providers, creates the built-in tasks (Database Backup at 01:00 and Log Clean-up at 02:00, local time) and the
   demo tasks, and creates the Inventory tables and sample products in `inventory_demo`.
4. The sign-in screen asks you to **create the first Developer super-admin**. There is no default account or password.

Only one scheduling engine runs per database. If a second copy of the Admin app is started against the same
database, its engine stays stopped and a banner names the computer whose engine is running.

## 5. Start the Maintenance Agent (demo app 2)

The Maintenance Agent is a separate application with its own database (`maintenance_demo`). The scheduler manages
it by calling its local web interface on `http://127.0.0.1:5087` with the shared key.

The easiest way is **Launch** on the Modules page (or **Launch Maintenance Agent** on Managed apps > Maintenance
Agent). The Admin application finds `demos\DemoApps.MaintenanceAgent` in the folders above it, runs it in the
background with `dotnet run` and stops it again with **Stop** or when the Admin application closes. If the Admin
application runs from somewhere else, set `MaintenanceAgent:LaunchPath` in the user settings file to the agent's
`.csproj`, its built `.exe` or their folder. To start it by hand instead:

```
dotnet run --project demos\DemoApps.MaintenanceAgent
```

Leave that window open; stop it with Ctrl+C. It listens on the loopback address only, so nothing outside this
computer can reach it, and it refuses every request without the `X-GSST-Key` header that matches
`GSST_AGENT_KEY`. Its settings are in `demos\DemoApps.MaintenanceAgent\appsettings.json` (port, minimum free disk
space, memory limit, and `Maintenance:AllowedCleanupRoots`).

In the Admin app, **Managed apps > Maintenance Agent** shows its status. When the agent is not running, the
"Maintenance: health check" task fails with "The Maintenance Agent is not running on port 5087." and the failure
appears in the Error log, which is expected.

## 6. Settings for this computer

Settings that belong to the computer rather than the database are kept in the **user settings file**
`%LOCALAPPDATA%\GSST\appsettings.user.json` (macOS: `~/Library/Application Support/GSST/appsettings.user.json`).
The file is created at first start. An administrator edits it on **Configuration > This computer**:

| Setting | Default when empty | Meaning |
|---|---|---|
| `Paths:PostgresBinFolder` | PATH, then the usual install folders | Folder holding `pg_dump.exe` and `pg_restore.exe` |
| `Paths:BackupFolder` | `C:\GMSS\Backups` | Where backup sets are written |
| `Paths:BackupCopyFolder` | off | A second folder, for example on an external drive, that every verified backup set is copied to |
| `Paths:LogFolder` | `C:\GMSS\Logs` | Folder of the daily JSON-lines log files |
| `Paths:ExportFolder` | Documents/GMSS Exports | Default destination for report and execution-history CSV files; each export can choose another folder |
| `History:LastExportFolder` | none | Last folder used to export a CSV, remembered for convenience |
| `History:LastBackupFolder` | none | Last folder used to create or restore a backup set, remembered for convenience |
| `Security:AllowLoginBypass` | `false` | Developer login bypass, see section 8 |
| `MaintenanceAgent:Url` | `http://127.0.0.1:5087` | Address of the Maintenance Agent |

Values can also come from environment variables with the prefix `GSST_`, for example
`GSST_Paths__BackupFolder`. Scheduler settings such as retries, concurrency and retention are stored in the
database and changed on the Configuration page (see the [User manual](User_Manual.md)).

## 7. Moving to another computer

A backup set holds one `pg_dump` file per database and a `manifest.json` with SHA-256 checksums. To move GSST to
another computer:

1. On the old computer, open **Backup and restore**, tick all three databases and choose **Back up now**. Copy
   the newest backup set folder (for example to a USB drive).
2. On the new computer, follow sections 2 to 4 of this guide. The set-up script creates empty databases.
3. Start the Admin app, create a temporary administrator, open **Backup and restore**, choose the copied folder,
   select the set and **Restore**, typing `RESTORE` to confirm. The scheduler database is restored, so you are
   signed out and sign in again with your old account.
4. Check **Configuration > This computer**: folders are not part of the backup, so set them for the new computer.
5. Stop the Maintenance Agent before restoring `maintenance_demo`; the restore refuses while it is running.

## 8. Login bypass for developers

The Login and Security module can be removed from the solution (FR-44). Without it, the Admin app shows only
"No login module is installed. Install the Login and Security module, or turn on Security:AllowLoginBypass for
development."

Only a signed-in Developer super-admin can enable login bypass from **Configuration > This computer**. The bypass is
for development only; it is not available to Administrators or Viewers. Every page then shows a
red banner: "Login bypassed (developer mode). Anyone using this computer has full access." Turning the bypass on or
off is recorded in the Security and audit report. Never leave it on for a computer other people use.

## 9. Troubleshooting

| Problem | Cause and fix |
|---|---|
| "The database password is not set" or the app cannot connect | `GMSS_DB_PASSWORD` is missing. Run `setup_windows.ps1` again, then close and reopen Visual Studio |
| `28P01 password authentication failed` | The variable and the role password differ. Run `ALTER ROLE gmss_app PASSWORD '...'` as `postgres`, or delete the variable and rerun the script |
| "pg_dump was not found" on the Backup page | Set the PostgreSQL tools folder on Configuration > This computer, for example `C:\Program Files\PostgreSQL\18\bin` |
| "Backup made but could not be verified" | A file failed `pg_restore --list`. Check free disk space and the tools version (it must be the server's version or newer) |
| Banner: "The scheduler engine is already running on …" | Another Admin app uses the same database. Only one scheduler may run per database. Close the other one, then restart this one |
| Maintenance tasks fail with "not running on port 5087" | Start the Maintenance Agent, or change its port in both `appsettings.json` files |
| The agent answers 401 | `GSST_AGENT_KEY` differs between the two programs. Reopen both after running the script |
| Tasks show a "No handler" badge | No installed module provides that task type. Add the module back, or delete or disable the task |
| Restore says "Stop the Maintenance Agent first, then restore." | Stop the agent with Ctrl+C, restore, then start it again |
| Upgrade stopped: "safety backup failed" | Pending migrations are only applied after a verified backup of `gmss` (FR-73). Fix the backup problem shown and start again |
| A `demo` schema is left in `gmss` | Earlier versions kept the Inventory tables there. It is no longer used and can be removed with `DROP SCHEMA demo CASCADE;` in `gmss` after a backup |
