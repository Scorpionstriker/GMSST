# Building and running on Linux

The Admin app is a .NET MAUI app, and MAUI has no Linux desktop target, so on Linux you can build and test the
core libraries and run the Maintenance Agent only. Scheduling itself needs the Admin app, which hosts the engine, on
Windows or macOS. This guide was checked on Ubuntu 24.04; other distributions need the matching package names.

## 1. Prerequisites

```
sudo apt update
sudo apt install -y dotnet-sdk-10.0 postgresql postgresql-client
```

If your distribution has no .NET 10 package, use Microsoft's install script from dot.net. Check with
`dotnet --version` (10.0.100 or later) and `psql --version` (16 or later).

## 2. PostgreSQL

1. In `/etc/postgresql/<version>/main/postgresql.conf` check `listen_addresses = 'localhost'` and
   `password_encryption = scram-sha-256`.
2. In `/etc/postgresql/<version>/main/pg_hba.conf` keep only local `host` lines with `scram-sha-256`:

   ```
   host    all   all   127.0.0.1/32   scram-sha-256
   host    all   all   ::1/128        scram-sha-256
   ```

3. Restart: `sudo systemctl restart postgresql`.
4. Create the role and databases:

   ```
   sudo -u postgres psql -v app_password='choose-a-strong-password' -f database/create_database.sql
   ```

## 3. Environment variables

Add to `~/.profile` (or `~/.bashrc`) and open a new terminal:

```
export GMSS_DB_PASSWORD='choose-a-strong-password'
export GSST_AGENT_KEY='the same key as on the computer running the Admin app'
```

## 4. Build and test

```
dotnet build GSST.Core.slnf
dotnet test GSST.Core.slnf
```

`GSST.Core.slnf` is the solution without the Admin app. The tests use an in-memory database, so they need no PostgreSQL.

## 5. Run the Maintenance Agent as a user service

```
dotnet publish demos/DemoApps.MaintenanceAgent -c Release -o ~/gsst-agent
```

Create `~/.config/systemd/user/gsst-agent.service`:

```
[Unit]
Description=GSST Maintenance Agent
After=network.target

[Service]
WorkingDirectory=%h/gsst-agent
ExecStart=%h/gsst-agent/DemoApps.MaintenanceAgent
EnvironmentFile=%h/.config/gsst/agent.env
Restart=on-failure

[Install]
WantedBy=default.target
```

Put the two variables in `~/.config/gsst/agent.env` (`GMSS_DB_PASSWORD=...` and `GSST_AGENT_KEY=...`, one per
line, no `export`) and make it private with `chmod 600 ~/.config/gsst/agent.env`. Then:

```
systemctl --user daemon-reload
systemctl --user enable --now gsst-agent
journalctl --user -u gsst-agent -f
```

The agent listens on `127.0.0.1:5087` and `[::1]:5087` only. Its temporary folder is
`GMSS` inside the system temporary folder (usually `/tmp/GMSS`). Allowed clean-up folders are set in `appsettings.json` under
`Maintenance:AllowedCleanupRoots`.

## 6. Updating

Stop the service (`systemctl --user stop gsst-agent`), publish again to the same folder, and start it
(`systemctl --user start gsst-agent`). The agent updates its own tables in `maintenance_demo` at start-up.
