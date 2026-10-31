# Building and running on macOS

Windows is the main platform; this guide covers macOS 14 (Sonoma) or later (NFR-10). The Admin app runs as a
Mac Catalyst app (minimum Mac Catalyst 17.0). The core libraries, the tests and the Maintenance Agent run as on
Windows. Read the [Installation guide](Installation_Guide.md) first: the design, the databases and the settings are
the same, and only the steps below differ.

## 1. Prerequisites

| Item | Notes |
|---|---|
| macOS 14 or later, Apple silicon or Intel | |
| Xcode | The version the MAUI workload asks for (from the App Store); open it once to accept the licence and install components |
| .NET 10 SDK | From dot.net; `dotnet --version` must show 10.0.100 or later |
| MAUI workload | `sudo dotnet workload install maui` (or `maccatalyst` only), then check `dotnet workload list` shows maccatalyst |
| Editor | Visual Studio Code with the C# Dev Kit and .NET MAUI extensions, or JetBrains Rider |

`Directory.Packages.props` pins the MAUI packages to 10.0.110. If your installed workload is newer, change both
versions to match it.

## 2. PostgreSQL

Install PostgreSQL 18 with either:

- the EDB installer from postgresql.org (tools in `/Library/PostgreSQL/18/bin`), or
- Homebrew: `brew install postgresql@18` and `brew services start postgresql@18` (tools in
  `/opt/homebrew/opt/postgresql@18/bin`).

Apply the local-only settings from section 2.1 of the installation guide (`listen_addresses = 'localhost'`,
`scram-sha-256` lines for `127.0.0.1/32` and `::1/128` only), then restart the server
(`brew services restart postgresql@18`, or the EDB service).

Create the role and databases, choosing a password for `gmss_app`:

```
psql -U postgres -v app_password='choose-a-strong-password' -f database/create_database.sql
```

With Homebrew the superuser is your own macOS account, so use `-U $USER` instead of `-U postgres`.

## 3. Environment variables

Add the variables to `~/.zprofile`:

```
export GMSS_DB_PASSWORD='choose-a-strong-password'
export GSST_AGENT_KEY='paste-a-long-random-value'
```

Create a key with `openssl rand -base64 32`. Apps started from Finder or the Dock do not read `~/.zprofile`, so also
run, after every restart of the Mac:

```
launchctl setenv GMSS_DB_PASSWORD 'choose-a-strong-password'
launchctl setenv GSST_AGENT_KEY 'the-same-key'
```

Open a new terminal afterwards. The values are never written to a project file (NFR-15).

## 4. Build and run the Admin app

```
dotnet build src/Scheduler.Admin -f net10.0-maccatalyst
dotnet build -t:Run src/Scheduler.Admin -f net10.0-maccatalyst
```

At first start the app creates the tables and asks for the first administrator, as on Windows. If backups report
that `pg_dump` was not found, set **Configuration > This computer > PostgreSQL tools folder** to the folder above.
The user settings file is `~/Library/Application Support/GSST/appsettings.user.json`; backups and logs default to
`~/Library/Application Support/GMSS/Backups` and `.../Logs`.

**Quit with Command-Q** (or GSST > Quit) rather than leaving the app open in the background. When its window is
destroyed the app stops the engine and gives running tasks up to ShutdownGraceSeconds to finish. If macOS ends the
app before that, nothing is lost: at the next start, recovery queues interrupted runs again once their lease has
expired (at-least-once delivery, NFR-01).

## 5. Tests and the Maintenance Agent

```
dotnet test GSST.Core.slnf
dotnet run --project demos/DemoApps.MaintenanceAgent
```

The tests use an in-memory database, so they need no PostgreSQL.

## 6. Updating

1. Pull or copy the new source.
2. Run `dotnet workload update` if Visual Studio Code or the MAUI release notes ask for it, and update the two MAUI
   versions in `Directory.Packages.props` to the installed workload.
3. Build and run the tests. The Admin app applies new database migrations at its next start, after a verified
   safety backup of `gmss`.
