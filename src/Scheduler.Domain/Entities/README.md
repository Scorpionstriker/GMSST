# Entities

Domain classes from the Phase 4 domain model (Figure 3.4): `ScheduledTask`, `Schedule`, `TaskDependency`,
`QueueEntry`, `TaskExecution`, `WorkerThread`, `ErrorLogEntry`, `ExecutionLogEntry`, `ConfigurationSetting`,
`LoggingProvider`, `AppUser` and `LoginAudit`. Each entity enforces its own valid state changes
(for example `ScheduledTask.Pause()` and `Resume()`).
