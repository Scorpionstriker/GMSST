using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scheduler.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "scheduler");

            migrationBuilder.CreateTable(
                name: "app_user",
                schema: "scheduler",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    username = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    password_salt = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    failed_login_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    lockout_end = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_login_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_app_user", x => x.user_id);
                    table.CheckConstraint("ck_app_user_role", "role IN ('Administrator', 'Viewer')");
                });

            migrationBuilder.CreateTable(
                name: "logging_provider",
                schema: "scheduler",
                columns: table => new
                {
                    provider_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    provider_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    minimum_level = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    settings = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_logging_provider", x => x.provider_id);
                    table.CheckConstraint("ck_logging_provider_minimum_level", "minimum_level IN ('Info', 'Warning', 'Error')");
                    table.CheckConstraint("ck_logging_provider_provider_type", "provider_type IN ('Console', 'File', 'Database', 'Custom')");
                });

            migrationBuilder.CreateTable(
                name: "worker_thread",
                schema: "scheduler",
                columns: table => new
                {
                    thread_id = table.Column<Guid>(type: "uuid", nullable: false),
                    host_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    thread_pool_name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    current_execution_id = table.Column<Guid>(type: "uuid", nullable: true),
                    last_heartbeat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_worker_thread", x => x.thread_id);
                    table.CheckConstraint("ck_worker_thread_status", "status IN ('Idle', 'Busy', 'Stopped')");
                });

            migrationBuilder.CreateTable(
                name: "login_audit",
                schema: "scheduler",
                columns: table => new
                {
                    audit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    username_attempted = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    succeeded = table.Column<bool>(type: "boolean", nullable: false),
                    reason = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    attempted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_login_audit", x => x.audit_id);
                    table.ForeignKey(
                        name: "fk_login_audit_user",
                        column: x => x.user_id,
                        principalSchema: "scheduler",
                        principalTable: "app_user",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "task",
                schema: "scheduler",
                columns: table => new
                {
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    task_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    priority = table.Column<int>(type: "integer", nullable: false, defaultValue: 5),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    max_retries = table.Column<int>(type: "integer", nullable: true),
                    timeout_seconds = table.Column<int>(type: "integer", nullable: false, defaultValue: 300),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task", x => x.task_id);
                    table.CheckConstraint("ck_task_max_retries", "max_retries IS NULL OR (max_retries BETWEEN 0 AND 10)");
                    table.CheckConstraint("ck_task_priority", "priority BETWEEN 1 AND 10");
                    table.CheckConstraint("ck_task_status", "status IN ('Active', 'Paused', 'Disabled')");
                    table.CheckConstraint("ck_task_timeout_seconds", "timeout_seconds > 0");
                    table.ForeignKey(
                        name: "fk_task_created_by",
                        column: x => x.created_by,
                        principalSchema: "scheduler",
                        principalTable: "app_user",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_task_updated_by",
                        column: x => x.updated_by,
                        principalSchema: "scheduler",
                        principalTable: "app_user",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "configuration",
                schema: "scheduler",
                columns: table => new
                {
                    config_id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: true),
                    config_key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    config_value = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    scope = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_configuration", x => x.config_id);
                    table.CheckConstraint("ck_configuration_scope", "scope IN ('Global', 'Task')");
                    table.CheckConstraint("ck_configuration_scope_task", "(scope = 'Global') = (task_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_configuration_task",
                        column: x => x.task_id,
                        principalSchema: "scheduler",
                        principalTable: "task",
                        principalColumn: "task_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "schedule",
                schema: "scheduler",
                columns: table => new
                {
                    schedule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    schedule_type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    cron_expression = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    interval_seconds = table.Column<int>(type: "integer", nullable: true),
                    time_zone_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, defaultValue: "UTC"),
                    misfire_policy = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    start_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    end_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    next_run_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_run_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_schedule", x => x.schedule_id);
                    table.CheckConstraint("ck_schedule_cron_required", "schedule_type <> 'Cron' OR cron_expression IS NOT NULL");
                    table.CheckConstraint("ck_schedule_interval_positive", "interval_seconds IS NULL OR interval_seconds > 0");
                    table.CheckConstraint("ck_schedule_interval_required", "schedule_type <> 'Interval' OR interval_seconds IS NOT NULL");
                    table.CheckConstraint("ck_schedule_misfire_policy", "misfire_policy IN ('Skip', 'RunOnce', 'RunAll')");
                    table.CheckConstraint("ck_schedule_schedule_type", "schedule_type IN ('Cron', 'Interval', 'Once')");
                    table.CheckConstraint("ck_schedule_window", "end_time IS NULL OR end_time > start_time");
                    table.ForeignKey(
                        name: "fk_schedule_created_by",
                        column: x => x.created_by,
                        principalSchema: "scheduler",
                        principalTable: "app_user",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_schedule_task",
                        column: x => x.task_id,
                        principalSchema: "scheduler",
                        principalTable: "task",
                        principalColumn: "task_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_schedule_updated_by",
                        column: x => x.updated_by,
                        principalSchema: "scheduler",
                        principalTable: "app_user",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "task_dependency",
                schema: "scheduler",
                columns: table => new
                {
                    dependency_id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    depends_on_task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_dependency", x => x.dependency_id);
                    table.CheckConstraint("ck_task_dependency_not_self", "depends_on_task_id <> task_id");
                    table.ForeignKey(
                        name: "fk_task_dependency_depends_on",
                        column: x => x.depends_on_task_id,
                        principalSchema: "scheduler",
                        principalTable: "task",
                        principalColumn: "task_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_task_dependency_task",
                        column: x => x.task_id,
                        principalSchema: "scheduler",
                        principalTable: "task",
                        principalColumn: "task_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "task_queue",
                schema: "scheduler",
                columns: table => new
                {
                    queue_id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    schedule_id = table.Column<Guid>(type: "uuid", nullable: true),
                    trigger_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    scheduled_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    enqueued_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    queue_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    claimed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    claimed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    claimed_until = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    attempt = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_queue", x => x.queue_id);
                    table.CheckConstraint("ck_task_queue_attempt", "attempt >= 0");
                    table.CheckConstraint("ck_task_queue_lease", "claimed_until IS NULL OR claimed_until > claimed_at");
                    table.CheckConstraint("ck_task_queue_priority", "priority BETWEEN 1 AND 10");
                    table.CheckConstraint("ck_task_queue_queue_status", "queue_status IN ('Waiting', 'Claimed', 'Processed', 'Cancelled')");
                    table.CheckConstraint("ck_task_queue_trigger_type", "trigger_type IN ('Scheduled', 'Manual', 'Retry', 'Dependency', 'CatchUp')");
                    table.ForeignKey(
                        name: "fk_task_queue_schedule",
                        column: x => x.schedule_id,
                        principalSchema: "scheduler",
                        principalTable: "schedule",
                        principalColumn: "schedule_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_task_queue_task",
                        column: x => x.task_id,
                        principalSchema: "scheduler",
                        principalTable: "task",
                        principalColumn: "task_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_task_queue_worker",
                        column: x => x.claimed_by,
                        principalSchema: "scheduler",
                        principalTable: "worker_thread",
                        principalColumn: "thread_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "task_execution",
                schema: "scheduler",
                columns: table => new
                {
                    execution_id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    schedule_id = table.Column<Guid>(type: "uuid", nullable: true),
                    queue_id = table.Column<Guid>(type: "uuid", nullable: true),
                    thread_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trigger_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    scheduled_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    start_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    end_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    duration_ms = table.Column<int>(type: "integer", nullable: true),
                    execution_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    retry_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    output = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_execution", x => x.execution_id);
                    table.CheckConstraint("ck_task_execution_duration", "duration_ms IS NULL OR duration_ms >= 0");
                    table.CheckConstraint("ck_task_execution_end", "end_time IS NULL OR end_time >= start_time");
                    table.CheckConstraint("ck_task_execution_execution_status", "execution_status IN ('Running', 'Success', 'Failed', 'Retrying', 'TimedOut', 'Cancelled')");
                    table.CheckConstraint("ck_task_execution_retry_count", "retry_count >= 0");
                    table.CheckConstraint("ck_task_execution_trigger_type", "trigger_type IN ('Scheduled', 'Manual', 'Retry', 'Dependency', 'CatchUp')");
                    table.ForeignKey(
                        name: "fk_task_execution_queue",
                        column: x => x.queue_id,
                        principalSchema: "scheduler",
                        principalTable: "task_queue",
                        principalColumn: "queue_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_task_execution_schedule",
                        column: x => x.schedule_id,
                        principalSchema: "scheduler",
                        principalTable: "schedule",
                        principalColumn: "schedule_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_task_execution_task",
                        column: x => x.task_id,
                        principalSchema: "scheduler",
                        principalTable: "task",
                        principalColumn: "task_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_task_execution_worker",
                        column: x => x.thread_id,
                        principalSchema: "scheduler",
                        principalTable: "worker_thread",
                        principalColumn: "thread_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "error_log",
                schema: "scheduler",
                columns: table => new
                {
                    error_id = table.Column<Guid>(type: "uuid", nullable: false),
                    execution_id = table.Column<Guid>(type: "uuid", nullable: false),
                    error_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    error_message = table.Column<string>(type: "text", nullable: false),
                    stack_trace = table.Column<string>(type: "jsonb", nullable: true),
                    occurred_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_error_log", x => x.error_id);
                    table.ForeignKey(
                        name: "fk_error_log_execution",
                        column: x => x.execution_id,
                        principalSchema: "scheduler",
                        principalTable: "task_execution",
                        principalColumn: "execution_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "execution_log",
                schema: "scheduler",
                columns: table => new
                {
                    log_id = table.Column<Guid>(type: "uuid", nullable: false),
                    execution_id = table.Column<Guid>(type: "uuid", nullable: true),
                    provider_id = table.Column<Guid>(type: "uuid", nullable: false),
                    log_level = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    message = table.Column<string>(type: "jsonb", nullable: false),
                    logged_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_execution_log", x => x.log_id);
                    table.CheckConstraint("ck_execution_log_log_level", "log_level IN ('Info', 'Warning', 'Error')");
                    table.ForeignKey(
                        name: "fk_execution_log_execution",
                        column: x => x.execution_id,
                        principalSchema: "scheduler",
                        principalTable: "task_execution",
                        principalColumn: "execution_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_execution_log_provider",
                        column: x => x.provider_id,
                        principalSchema: "scheduler",
                        principalTable: "logging_provider",
                        principalColumn: "provider_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_configuration_task_key",
                schema: "scheduler",
                table: "configuration",
                columns: new[] { "task_id", "config_key" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_error_execution",
                schema: "scheduler",
                table: "error_log",
                column: "execution_id");

            migrationBuilder.CreateIndex(
                name: "ix_error_time",
                schema: "scheduler",
                table: "error_log",
                column: "occurred_at");

            migrationBuilder.CreateIndex(
                name: "IX_execution_log_provider_id",
                schema: "scheduler",
                table: "execution_log",
                column: "provider_id");

            migrationBuilder.CreateIndex(
                name: "ix_log_execution",
                schema: "scheduler",
                table: "execution_log",
                column: "execution_id");

            migrationBuilder.CreateIndex(
                name: "ix_log_time",
                schema: "scheduler",
                table: "execution_log",
                column: "logged_at");

            migrationBuilder.CreateIndex(
                name: "ux_logging_provider_name",
                schema: "scheduler",
                table: "logging_provider",
                column: "provider_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_login_audit_time",
                schema: "scheduler",
                table: "login_audit",
                column: "attempted_at");

            migrationBuilder.CreateIndex(
                name: "IX_login_audit_user_id",
                schema: "scheduler",
                table: "login_audit",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_schedule_created_by",
                schema: "scheduler",
                table: "schedule",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_schedule_next_run",
                schema: "scheduler",
                table: "schedule",
                column: "next_run_time",
                filter: "is_active AND NOT is_deleted");

            migrationBuilder.CreateIndex(
                name: "IX_schedule_task_id",
                schema: "scheduler",
                table: "schedule",
                column: "task_id");

            migrationBuilder.CreateIndex(
                name: "IX_schedule_updated_by",
                schema: "scheduler",
                table: "schedule",
                column: "updated_by");

            migrationBuilder.CreateIndex(
                name: "IX_task_created_by",
                schema: "scheduler",
                table: "task",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_task_status",
                schema: "scheduler",
                table: "task",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_task_updated_by",
                schema: "scheduler",
                table: "task",
                column: "updated_by");

            migrationBuilder.CreateIndex(
                name: "ix_task_dependency_depends_on",
                schema: "scheduler",
                table: "task_dependency",
                column: "depends_on_task_id");

            migrationBuilder.CreateIndex(
                name: "ux_task_dependency",
                schema: "scheduler",
                table: "task_dependency",
                columns: new[] { "task_id", "depends_on_task_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_execution_start_time",
                schema: "scheduler",
                table: "task_execution",
                column: "start_time");

            migrationBuilder.CreateIndex(
                name: "ix_execution_status_time",
                schema: "scheduler",
                table: "task_execution",
                columns: new[] { "execution_status", "start_time" });

            migrationBuilder.CreateIndex(
                name: "ix_execution_task_time",
                schema: "scheduler",
                table: "task_execution",
                columns: new[] { "task_id", "start_time" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_task_execution_schedule_id",
                schema: "scheduler",
                table: "task_execution",
                column: "schedule_id");

            migrationBuilder.CreateIndex(
                name: "IX_task_execution_thread_id",
                schema: "scheduler",
                table: "task_execution",
                column: "thread_id");

            migrationBuilder.CreateIndex(
                name: "ux_task_execution_queue",
                schema: "scheduler",
                table: "task_execution",
                column: "queue_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_queue_claim",
                schema: "scheduler",
                table: "task_queue",
                columns: new[] { "priority", "scheduled_time" },
                filter: "queue_status = 'Waiting'");

            migrationBuilder.CreateIndex(
                name: "ix_queue_lease",
                schema: "scheduler",
                table: "task_queue",
                column: "claimed_until",
                filter: "queue_status = 'Claimed'");

            migrationBuilder.CreateIndex(
                name: "ix_queue_task",
                schema: "scheduler",
                table: "task_queue",
                column: "task_id");

            migrationBuilder.CreateIndex(
                name: "IX_task_queue_claimed_by",
                schema: "scheduler",
                table: "task_queue",
                column: "claimed_by");

            migrationBuilder.CreateIndex(
                name: "IX_task_queue_schedule_id",
                schema: "scheduler",
                table: "task_queue",
                column: "schedule_id");

            migrationBuilder.CreateIndex(
                name: "ix_worker_host",
                schema: "scheduler",
                table: "worker_thread",
                column: "host_name");

            // Indexes EF Core cannot express: case-insensitive unique names (Phase 4, Table 5.15; FR-05, BR-11).
            migrationBuilder.Sql("CREATE UNIQUE INDEX ux_task_name ON scheduler.task (lower(name)) WHERE NOT is_deleted;");
            migrationBuilder.Sql("CREATE UNIQUE INDEX ux_user_username ON scheduler.app_user (lower(username));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS scheduler.ux_user_username;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS scheduler.ux_task_name;");

            migrationBuilder.DropTable(
                name: "configuration",
                schema: "scheduler");

            migrationBuilder.DropTable(
                name: "error_log",
                schema: "scheduler");

            migrationBuilder.DropTable(
                name: "execution_log",
                schema: "scheduler");

            migrationBuilder.DropTable(
                name: "login_audit",
                schema: "scheduler");

            migrationBuilder.DropTable(
                name: "task_dependency",
                schema: "scheduler");

            migrationBuilder.DropTable(
                name: "task_execution",
                schema: "scheduler");

            migrationBuilder.DropTable(
                name: "logging_provider",
                schema: "scheduler");

            migrationBuilder.DropTable(
                name: "task_queue",
                schema: "scheduler");

            migrationBuilder.DropTable(
                name: "schedule",
                schema: "scheduler");

            migrationBuilder.DropTable(
                name: "worker_thread",
                schema: "scheduler");

            migrationBuilder.DropTable(
                name: "task",
                schema: "scheduler");

            migrationBuilder.DropTable(
                name: "app_user",
                schema: "scheduler");
        }
    }
}
