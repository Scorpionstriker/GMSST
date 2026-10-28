# Queue

`PostgresTaskQueue`: enqueue due tasks and claim them with `SELECT ... FOR UPDATE SKIP LOCKED` plus a time-limited lease, only for task types this host can handle (FR-19, FR-20, FR-25, NFR-03).
