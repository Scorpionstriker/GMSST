# Seed data

At first start (and after an upgrade) the Admin application or the Maintenance Agent adds any setting that is
missing, with its default value. Values an administrator has changed are never overwritten (FR-38).

| Setting | Default | Allowed |
|---|---|---|
| MaxConcurrency | 4 tasks | 1–16 (applies after a restart) |
| MaxPollIntervalSeconds | 30 s | 5–300 |
| MaxRetryLimit | 3 | 0–10 (a task may override) |
| RetryBaseDelaySeconds | 30 s | 1–3600 (a task may override) |
| RetryMaxDelaySeconds | 900 s | 1–86400 (a task may override) |
| LeaseGraceSeconds | 60 s | 10–3600 |
| LogRetentionDays | 90 days | 1–3650 |
| AuditRetentionDays | 365 days | 30–3650 |
| BackupRetentionCount | 14 backups | 1–365 |
| SessionTimeoutMinutes | 20 min | 1–480 |
| MaxFailedLogins | 5 | 1–20 |
| LockoutMinutes | 15 min | 1–1440 |
| RunAllMisfireCap | 10 runs | 1–1000 |
| ShutdownGraceSeconds | 30 s | 0–600 |
| WorkerStaleSeconds | 120 s | 30–3600 |
| HealthCheckIntervalSeconds | 60 s | 10–3600 |
| HealthFailureThreshold | 3 failures | 2–100 |
| HealthTimeoutThreshold | 2 timeouts | 1–100 |
| HealthQueueBacklogThreshold | 100 waiting entries | 1–1,000,000 |
| HealthCpuPercentThreshold | 80% | 1–100 |
| HealthMemoryMegabytesThreshold | 1024 MB | 128–1,048,576 |

Logging providers: Database and File are enabled from level Info; Console is present but disabled.

Built-in tasks (Admin application only): Database Backup daily at 01:00 and Log Clean-up daily at 02:00, local time.

No user is seeded: the first-run flow creates the Developer super-admin. No password is seeded.
