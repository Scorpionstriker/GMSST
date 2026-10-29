DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'scheduler') THEN
        CREATE SCHEMA scheduler;
    END IF;
END $EF$;
CREATE TABLE IF NOT EXISTS scheduler.__ef_migrations_history (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___ef_migrations_history" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM scheduler.__ef_migrations_history WHERE "MigrationId" = '20261028124304_InitialCreate') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'scheduler') THEN
            CREATE SCHEMA scheduler;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM scheduler.__ef_migrations_history WHERE "MigrationId" = '20261028124304_InitialCreate') THEN
    CREATE TABLE scheduler.app_user (
        user_id uuid NOT NULL,
        username character varying(50) NOT NULL,
        password_hash character varying(256) NOT NULL,
        password_salt character varying(64) NOT NULL,
        role character varying(20) NOT NULL,
        failed_login_count integer NOT NULL DEFAULT 0,
        lockout_end timestamp with time zone,
        last_login_at timestamp with time zone,
        is_active boolean NOT NULL DEFAULT TRUE,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        updated_at timestamp with time zone,
        CONSTRAINT "PK_app_user" PRIMARY KEY (user_id),
        CONSTRAINT ck_app_user_role CHECK (role IN ('Administrator', 'Viewer'))
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM scheduler.__ef_migrations_history WHERE "MigrationId" = '20261028124304_InitialCreate') THEN
    CREATE TABLE scheduler.logging_provider (
        provider_id uuid NOT NULL,
        provider_name character varying(50) NOT NULL,
        provider_type character varying(20) NOT NULL,
        minimum_level character varying(10) NOT NULL,
        settings jsonb NOT NULL DEFAULT ('{}'::jsonb),
        is_active boolean NOT NULL DEFAULT TRUE,
        CONSTRAINT "PK_logging_provider" PRIMARY KEY (provider_id),
        CONSTRAINT ck_logging_provider_minimum_level CHECK (minimum_level IN ('Info', 'Warning', 'Error')),
        CONSTRAINT ck_logging_provider_provider_type CHECK (provider_type IN ('Console', 'File', 'Database', 'Custom'))
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM scheduler.__ef_migrations_history WHERE "MigrationId" = '20261028124304_InitialCreate') THEN
    CREATE TABLE scheduler.worker_thread (
        thread_id uuid NOT NULL,
        host_name character varying(100) NOT NULL,
        thread_pool_name character varying(50) NOT NULL,
        status character varying(10) NOT NULL,
        current_execution_id uuid,
        last_heartbeat timestamp with time zone NOT NULL,
        started_at timestamp with time zone NOT NULL,
        CONSTRAINT "PK_worker_thread" PRIMARY KEY (thread_id),
        CONSTRAINT ck_worker_thread_status CHECK (status IN ('Idle', 'Busy', 'Stopped'))
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM scheduler.__ef_migrations_history WHERE "MigrationId" = '20261028124304_InitialCreate') THEN
    CREATE TABLE scheduler.login_audit (
        audit_id uuid NOT NULL,
        user_id uuid,
        username_attempted character varying(50) NOT NULL,
        succeeded boolean NOT NULL,
        reason character varying(100),
        attempted_at timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT "PK_login_audit" PRIMARY KEY (audit_id),
        CONSTRAINT fk_login_audit_user FOREIGN KEY (user_id) REFERENCES scheduler.app_user (user_id) ON DELETE SET NULL
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM scheduler.__ef_migrations_history WHERE "MigrationId" = '20261028124304_InitialCreate') THEN
    CREATE TABLE scheduler.task (
        task_id uuid NOT NULL,
        name character varying(100) NOT NULL,
        description text,
        task_type character varying(100) NOT NULL,
        payload jsonb NOT NULL DEFAULT ('{}'::jsonb),
        priority integer NOT NULL DEFAULT 5,
        status character varying(20) NOT NULL,
        max_retries integer,
        timeout_seconds integer NOT NULL DEFAULT 300,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        is_deleted boolean NOT NULL DEFAULT FALSE,
        CONSTRAINT "PK_task" PRIMARY KEY (task_id),
        CONSTRAINT ck_task_max_retries CHECK (max_retries IS NULL OR (max_retries BETWEEN 0 AND 10)),
        CONSTRAINT ck_task_priority CHECK (priority BETWEEN 1 AND 10),
        CONSTRAINT ck_task_status CHECK (status IN ('Active', 'Paused', 'Disabled')),
        CONSTRAINT ck_task_timeout_seconds CHECK (timeout_seconds > 0),
        CONSTRAINT fk_task_created_by FOREIGN KEY (created_by) REFERENCES scheduler.app_user (user_id) ON DELETE SET NULL,
        CONSTRAINT fk_task_updated_by FOREIGN KEY (updated_by) REFERENCES scheduler.app_user (user_id) ON DELETE SET NULL
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM scheduler.__ef_migrations_history WHERE "MigrationId" = '20261028124304_InitialCreate') THEN
    CREATE TABLE scheduler.configuration (
        config_id uuid NOT NULL,
        task_id uuid,
        config_key character varying(50) NOT NULL,
        config_value character varying(255) NOT NULL,
        scope character varying(10) NOT NULL,
        updated_at timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT "PK_configuration" PRIMARY KEY (config_id),
        CONSTRAINT ck_configuration_scope CHECK (scope IN ('Global', 'Task')),
        CONSTRAINT ck_configuration_scope_task CHECK ((scope = 'Global') = (task_id IS NULL)),
        CONSTRAINT fk_configuration_task FOREIGN KEY (task_id) REFERENCES scheduler.task (task_id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM scheduler.__ef_migrations_history WHERE "MigrationId" = '20261028124304_InitialCreate') THEN
    CREATE TABLE scheduler.schedule (
        schedule_id uuid NOT NULL,
        task_id uuid NOT NULL,
        schedule_type character varying(10) NOT NULL,
        cron_expression character varying(100),
        interval_seconds integer,
        time_zone_id character varying(64) NOT NULL DEFAULT 'UTC',
        misfire_policy character varying(20) NOT NULL,
        start_time timestamp with time zone NOT NULL,
        end_time timestamp with time zone,
        next_run_time timestamp with time zone,
        last_run_time timestamp with time zone,
        is_active boolean NOT NULL DEFAULT TRUE,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        is_deleted boolean NOT NULL DEFAULT FALSE,
        CONSTRAINT "PK_schedule" PRIMARY KEY (schedule_id),
        CONSTRAINT ck_schedule_cron_required CHECK (schedule_type <> 'Cron' OR cron_expression IS NOT NULL),
        CONSTRAINT ck_schedule_interval_positive CHECK (interval_seconds IS NULL OR interval_seconds > 0),
        CONSTRAINT ck_schedule_interval_required CHECK (schedule_type <> 'Interval' OR interval_seconds IS NOT NULL),
        CONSTRAINT ck_schedule_misfire_policy CHECK (misfire_policy IN ('Skip', 'RunOnce', 'RunAll')),
        CONSTRAINT ck_schedule_schedule_type CHECK (schedule_type IN ('Cron', 'Interval', 'Once')),
        CONSTRAINT ck_schedule_window CHECK (end_time IS NULL OR end_time > start_time),
        CONSTRAINT fk_schedule_created_by FOREIGN KEY (created_by) REFERENCES scheduler.app_user (user_id) ON DELETE SET NULL,
        CONSTRAINT fk_schedule_task FOREIGN KEY (task_id) REFERENCES scheduler.task (task_id) ON DELETE CASCADE,
        CONSTRAINT fk_schedule_updated_by FOREIGN KEY (updated_by) REFERENCES scheduler.app_user (user_id) ON DELETE SET NULL
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM scheduler.__ef_migrations_history WHERE "MigrationId" = '20261028124304_InitialCreate') THEN
    CREATE TABLE scheduler.task_dependency (
        dependency_id uuid NOT NULL,
        task_id uuid NOT NULL,
        depends_on_task_id uuid NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT "PK_task_dependency" PRIMARY KEY (dependency_id),
        CONSTRAINT ck_task_dependency_not_self CHECK (depends_on_task_id <> task_id),
        CONSTRAINT fk_task_dependency_depends_on FOREIGN KEY (depends_on_task_id) REFERENCES scheduler.task (task_id) ON DELETE CASCADE,
        CONSTRAINT fk_task_dependency_task FOREIGN KEY (task_id) REFERENCES scheduler.task (task_id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM scheduler.__ef_migrations_history WHERE "MigrationId" = '20261028124304_InitialCreate') THEN
    CREATE TABLE scheduler.task_queue (
        queue_id uuid NOT NULL,
        task_id uuid NOT NULL,
        schedule_id uuid,
        trigger_type character varying(20) NOT NULL,
        priority integer NOT NULL,
        scheduled_time timestamp with time zone NOT NULL,
        enqueued_at timestamp with time zone NOT NULL DEFAULT (now()),
        queue_status character varying(20) NOT NULL,
        claimed_by uuid,
        claimed_at timestamp with time zone,
        claimed_until timestamp with time zone,
        attempt integer NOT NULL DEFAULT 0,
        CONSTRAINT "PK_task_queue" PRIMARY KEY (queue_id),
        CONSTRAINT ck_task_queue_attempt CHECK (attempt >= 0),
        CONSTRAINT ck_task_queue_lease CHECK (claimed_until IS NULL OR claimed_until > claimed_at),
        CONSTRAINT ck_task_queue_priority CHECK (priority BETWEEN 1 AND 10),
        CONSTRAINT ck_task_queue_queue_status CHECK (queue_status IN ('Waiting', 'Claimed', 'Processed', 'Cancelled')),
        CONSTRAINT ck_task_queue_trigger_type CHECK (trigger_type IN ('Scheduled', 'Manual', 'Retry', 'Dependency', 'CatchUp')),
        CONSTRAINT fk_task_queue_schedule FOREIGN KEY (schedule_id) REFERENCES scheduler.schedule (schedule_id) ON DELETE SET NULL,
        CONSTRAINT fk_task_queue_task FOREIGN KEY (task_id) REFERENCES scheduler.task (task_id) ON DELETE RESTRICT,
        CONSTRAINT fk_task_queue_worker FOREIGN KEY (claimed_by) REFERENCES scheduler.worker_thread (thread_id) ON DELETE SET NULL
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM scheduler.__ef_migrations_history WHERE "MigrationId" = '20261028124304_InitialCreate') THEN
    CREATE TABLE scheduler.task_execution (
        execution_id uuid NOT NULL,
        task_id uuid NOT NULL,
        schedule_id uuid,
        queue_id uuid,
        thread_id uuid NOT NULL,
        trigger_type character varying(20) NOT NULL,
        scheduled_time timestamp with time zone NOT NULL,
        start_time timestamp with time zone NOT NULL,
        end_time timestamp with time zone,
        duration_ms integer,
        execution_status character varying(20) NOT NULL,
        retry_count integer NOT NULL DEFAULT 0,
        output jsonb,
        CONSTRAINT "PK_task_execution" PRIMARY KEY (execution_id),
        CONSTRAINT ck_task_execution_duration CHECK (duration_ms IS NULL OR duration_ms >= 0),
        CONSTRAINT ck_task_execution_end CHECK (end_time IS NULL OR end_time >= start_time),
        CONSTRAINT ck_task_execution_execution_status CHECK (execution_status IN ('Running', 'Success', 'Failed', 'Retrying', 'TimedOut', 'Cancelled')),
        CONSTRAINT ck_task_execution_retry_count CHECK (retry_count >= 0),
        CONSTRAINT ck_task_execution_trigger_type CHECK (trigger_type IN ('Scheduled', 'Manual', 'Retry', 'Dependency', 'CatchUp')),
        CONSTRAINT fk_task_execution_queue FOREIGN KEY (queue_id) REFERENCES scheduler.task_queue (queue_id) ON DELETE SET NULL,
        CONSTRAINT fk_task_execution_schedule FOREIGN KEY (schedule_id) REFERENCES scheduler.schedule (schedule_id) ON DELETE SET NULL,
        CONSTRAINT fk_task_execution_task FOREIGN KEY (task_id) REFERENCES scheduler.task (task_id) ON DELETE RESTRICT,
        CONSTRAINT fk_task_execution_worker FOREIGN KEY (thread_id) REFERENCES scheduler.worker_thread (thread_id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM scheduler.__ef_migrations_history WHERE "MigrationId" = '20261028124304_InitialCreate') THEN
    CREATE TABLE scheduler.error_log (
        error_id uuid NOT NULL,
        execution_id uuid NOT NULL,
        error_type character varying(100) NOT NULL,
        error_message text NOT NULL,
        stack_trace jsonb,
        occurred_at timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT "PK_error_log" PRIMARY KEY (error_id),
        CONSTRAINT fk_error_log_execution FOREIGN KEY (execution_id) REFERENCES scheduler.task_execution (execution_id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM scheduler.__ef_migrations_history WHERE "MigrationId" = '20261028124304_InitialCreate') THEN
    CREATE TABLE scheduler.execution_log (
        log_id uuid NOT NULL,
        execution_id uuid,
        provider_id uuid NOT NULL,
        log_level character varying(10) NOT NULL,
        source character varying(50) NOT NULL,
        message jsonb NOT NULL,
        logged_at timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT "PK_execution_log" PRIMARY KEY (log_id),
        CONSTRAINT ck_execution_log_log_level CHECK (log_level IN ('Info', 'Warning', 'Error')),
        CONSTRAINT fk_execution_log_execution FOREIGN KEY (execution_id) REFERENCES scheduler.task_execution (execution_id) ON DELETE CASCADE,
        CONSTRAINT fk_execution_log_provider FOREIGN KEY (provider_id) REFERENCES scheduler.logging_provider (provider_id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM scheduler.__ef_migrations_history WHERE "MigrationId" = '20261028124304_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_configuration_task_key ON scheduler.configuration (task_id, config_key) NULLS NOT DISTINCT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM scheduler.__ef_migrations_history WHERE "MigrationId" = '20261028124304_InitialCreate') THEN
    CREATE INDEX ix_error_execution ON scheduler.error_log (execution_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM scheduler.__ef_migrations_history WHERE "MigrationId" = '20261028124304_InitialCreate') THEN
    CREATE INDEX ix_error_time ON scheduler.error_log (occurred_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM scheduler.__ef_migrations_history WHERE "MigrationId" = '20261028124304_InitialCreate') THEN
    CREATE INDEX "IX_execution_log_provider_id" ON scheduler.execution_log (provider_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM scheduler.__ef_migrations_history WHERE "MigrationId" = '20261028124304_InitialCreate') THEN
    CREATE INDEX ix_log_execution ON scheduler.execution_log (execution_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM scheduler.__ef_migrations_history WHERE "MigrationId" = '20261028124304_InitialCreate') THEN
    CREATE INDEX ix_log_time ON scheduler.execution_log (logged_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM scheduler.__ef_migrations_history WHERE "MigrationId" = '20261028124304_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_logging_provider_name ON scheduler.logging_provider (provider_name);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM scheduler.__ef_migrations_history WHERE "MigrationId" = '20261028124304_InitialCreate') THEN
    CREATE INDEX ix_login_audit_time ON scheduler.login_audit (attempted_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM scheduler.__ef_migrations_history WHERE "MigrationId" = '20261028124304_InitialCreate') THEN
    CREATE INDEX "IX_login_audit_user_id" ON scheduler.login_audit (user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM scheduler.__ef_migrations_history WHERE "MigrationId" = '20261028124304_InitialCreate') THEN
    CREATE INDEX "IX_schedule_created_by" ON scheduler.schedule (created_by);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM scheduler.__ef_migrations_history WHERE "MigrationId" = '20261028124304_InitialCreate') THEN
    CREATE INDEX ix_schedule_next_run ON scheduler.schedule (next_run_time) WHERE is_active AND NOT is_deleted;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM scheduler.__ef_migrations_history WHERE "MigrationId" = '20261028124304_InitialCreate') THEN
    CREATE INDEX "IX_schedule_task_id" ON scheduler.schedule (task_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM scheduler.__ef_migrations_history WHERE "MigrationId" = '20261028124304_InitialCreate') THEN
    CREATE INDEX "IX_schedule_updated_by" ON scheduler.schedule (updated_by);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM scheduler.__ef_migrations_history WHERE "MigrationId" = '20261028124304_InitialCreate') THEN
    CREATE INDEX "IX_task_created_by" ON scheduler.task (created_by);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM scheduler.__ef_migrations_history WHERE "MigrationId" = '20261028124304_InitialCreate') THEN
    CREATE INDEX ix_task_status ON scheduler.task (status);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM scheduler.__ef_migrations_history WHERE "MigrationId" = '20261028124304_InitialCreate') THEN
    CREATE INDEX "IX_task_updated_by" ON scheduler.task (updated_by);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM scheduler.__ef_migrations_history WHERE "MigrationId" = '20261028124304_InitialCreate') THEN
    CREATE INDEX ix_task_dependency_depends_on ON scheduler.task_dependency (depends_on_task_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM scheduler.__ef_migrations_history WHERE "MigrationId" = '20261028124304_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_task_dependency ON scheduler.task_dependency (task_id, depends_on_task_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM scheduler.__ef_migrations_history WHERE "MigrationId" = '20261028124304_InitialCreate') THEN
    CREATE INDEX ix_execution_start_time ON scheduler.task_execution (start_time);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM scheduler.__ef_migrations_history WHERE "MigrationId" = '20261028124304_InitialCreate') THEN
    CREATE INDEX ix_execution_status_time ON scheduler.task_execution (execution_status, start_time);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM scheduler.__ef_migrations_history WHERE "MigrationId" = '20261028124304_InitialCreate') THEN
    CREATE INDEX ix_execution_task_time ON scheduler.task_execution (task_id, start_time DESC);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM scheduler.__ef_migrations_history WHERE "MigrationId" = '20261028124304_InitialCreate') THEN
    CREATE INDEX "IX_task_execution_schedule_id" ON scheduler.task_execution (schedule_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM scheduler.__ef_migrations_history WHERE "MigrationId" = '20261028124304_InitialCreate') THEN
    CREATE INDEX "IX_task_execution_thread_id" ON scheduler.task_execution (thread_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM scheduler.__ef_migrations_history WHERE "MigrationId" = '20261028124304_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_task_execution_queue ON scheduler.task_execution (queue_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM scheduler.__ef_migrations_history WHERE "MigrationId" = '20261028124304_InitialCreate') THEN
    CREATE INDEX ix_queue_claim ON scheduler.task_queue (priority, scheduled_time) WHERE queue_status = 'Waiting';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM scheduler.__ef_migrations_history WHERE "MigrationId" = '20261028124304_InitialCreate') THEN
    CREATE INDEX ix_queue_lease ON scheduler.task_queue (claimed_until) WHERE queue_status = 'Claimed';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM scheduler.__ef_migrations_history WHERE "MigrationId" = '20261028124304_InitialCreate') THEN
    CREATE INDEX ix_queue_task ON scheduler.task_queue (task_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM scheduler.__ef_migrations_history WHERE "MigrationId" = '20261028124304_InitialCreate') THEN
    CREATE INDEX "IX_task_queue_claimed_by" ON scheduler.task_queue (claimed_by);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM scheduler.__ef_migrations_history WHERE "MigrationId" = '20261028124304_InitialCreate') THEN
    CREATE INDEX "IX_task_queue_schedule_id" ON scheduler.task_queue (schedule_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM scheduler.__ef_migrations_history WHERE "MigrationId" = '20261028124304_InitialCreate') THEN
    CREATE INDEX ix_worker_host ON scheduler.worker_thread (host_name);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM scheduler.__ef_migrations_history WHERE "MigrationId" = '20261028124304_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_task_name ON scheduler.task (lower(name)) WHERE NOT is_deleted;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM scheduler.__ef_migrations_history WHERE "MigrationId" = '20261028124304_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_user_username ON scheduler.app_user (lower(username));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM scheduler.__ef_migrations_history WHERE "MigrationId" = '20261028124304_InitialCreate') THEN
    INSERT INTO scheduler.__ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20261028124304_InitialCreate', '10.0.14');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM scheduler.__ef_migrations_history WHERE "MigrationId" = '20261029153054_SingleEngineInstance') THEN
    CREATE TABLE scheduler.engine_instance (
        instance_id smallint NOT NULL,
        host_name character varying(100) NOT NULL,
        process_id integer NOT NULL,
        started_at timestamp with time zone NOT NULL,
        heartbeat_at timestamp with time zone NOT NULL,
        CONSTRAINT "PK_engine_instance" PRIMARY KEY (instance_id),
        CONSTRAINT ck_engine_instance_single_row CHECK (instance_id = 1)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM scheduler.__ef_migrations_history WHERE "MigrationId" = '20261029153054_SingleEngineInstance') THEN
    UPDATE scheduler.task_queue SET queue_status = 'Cancelled'
    WHERE queue_status = 'Waiting'
      AND task_id IN (SELECT task_id FROM scheduler.task WHERE task_type IN ('HealthCheck', 'TempFileCleanup'));
    UPDATE scheduler.task SET name = left(name, 88) || ' (old agent)', is_deleted = true, updated_at = now()
    WHERE task_type IN ('HealthCheck', 'TempFileCleanup');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM scheduler.__ef_migrations_history WHERE "MigrationId" = '20261029153054_SingleEngineInstance') THEN
    INSERT INTO scheduler.__ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20261029153054_SingleEngineInstance', '10.0.14');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM scheduler.__ef_migrations_history WHERE "MigrationId" = '20261029161039_SchedulingBehaviour') THEN
    DROP INDEX scheduler.ix_queue_claim;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM scheduler.__ef_migrations_history WHERE "MigrationId" = '20261029161039_SchedulingBehaviour') THEN
    ALTER TABLE scheduler.task_queue ADD is_manual_run boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM scheduler.__ef_migrations_history WHERE "MigrationId" = '20261029161039_SchedulingBehaviour') THEN
    UPDATE scheduler.task_queue SET is_manual_run = true WHERE trigger_type = 'Manual';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM scheduler.__ef_migrations_history WHERE "MigrationId" = '20261029161039_SchedulingBehaviour') THEN
    CREATE INDEX ix_queue_claim ON scheduler.task_queue (priority, scheduled_time, enqueued_at) WHERE queue_status = 'Waiting';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM scheduler.__ef_migrations_history WHERE "MigrationId" = '20261029161039_SchedulingBehaviour') THEN
    INSERT INTO scheduler.__ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20261029161039_SchedulingBehaviour', '10.0.14');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM scheduler.__ef_migrations_history WHERE "MigrationId" = '20261029185513_ConcurrencyTokens') THEN
    INSERT INTO scheduler.__ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20261029185513_ConcurrencyTokens', '10.0.14');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM scheduler.__ef_migrations_history WHERE "MigrationId" = '20261029185937_BuiltInTaskKeys') THEN
    ALTER TABLE scheduler.task ADD system_key character varying(100);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM scheduler.__ef_migrations_history WHERE "MigrationId" = '20261029185937_BuiltInTaskKeys') THEN
    CREATE UNIQUE INDEX ux_task_system_key ON scheduler.task (system_key) WHERE system_key IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM scheduler.__ef_migrations_history WHERE "MigrationId" = '20261029185937_BuiltInTaskKeys') THEN
    INSERT INTO scheduler.__ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20261029185937_BuiltInTaskKeys', '10.0.14');
    END IF;
END $EF$;
COMMIT;

