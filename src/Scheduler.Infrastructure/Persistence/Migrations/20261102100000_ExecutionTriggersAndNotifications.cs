using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Scheduler.Infrastructure.Persistence.Migrations;

[DbContext(typeof(SchedulerDbContext))]
[Migration("20261102100000_ExecutionTriggersAndNotifications")]
public sealed class ExecutionTriggersAndNotifications : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "max_executions",
            schema: "scheduler",
            table: "task",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "execution_count",
            schema: "scheduler",
            table: "task",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.DropCheckConstraint(
            name: "ck_task_status",
            schema: "scheduler",
            table: "task");

        migrationBuilder.AddCheckConstraint(
            name: "ck_task_status",
            schema: "scheduler",
            table: "task",
            sql: "status IN ('Active', 'Paused', 'Disabled', 'Completed')");

        migrationBuilder.AddCheckConstraint(
            name: "ck_task_max_executions",
            schema: "scheduler",
            table: "task",
            sql: "max_executions IS NULL OR (max_executions BETWEEN 1 AND 1000000)");

        migrationBuilder.AddCheckConstraint(
            name: "ck_task_execution_count",
            schema: "scheduler",
            table: "task",
            sql: "execution_count >= 0");

        migrationBuilder.AddColumn<string>(
            name: "event_name",
            schema: "scheduler",
            table: "task_queue",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "event_source",
            schema: "scheduler",
            table: "task_queue",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "event_data",
            schema: "scheduler",
            table: "task_queue",
            type: "character varying(65536)",
            maxLength: 65536,
            nullable: true);

        migrationBuilder.DropCheckConstraint(
            name: "ck_task_queue_trigger_type",
            schema: "scheduler",
            table: "task_queue");

        migrationBuilder.AddCheckConstraint(
            name: "ck_task_queue_trigger_type",
            schema: "scheduler",
            table: "task_queue",
            sql: "trigger_type IN ('Scheduled', 'Manual', 'Retry', 'Dependency', 'CatchUp', 'Event')");

        migrationBuilder.DropCheckConstraint(
            name: "ck_task_execution_trigger_type",
            schema: "scheduler",
            table: "task_execution");

        migrationBuilder.AddCheckConstraint(
            name: "ck_task_execution_trigger_type",
            schema: "scheduler",
            table: "task_execution",
            sql: "trigger_type IN ('Scheduled', 'Manual', 'Retry', 'Dependency', 'CatchUp', 'Event')");

        migrationBuilder.CreateTable(
            name: "in_app_notification",
            schema: "scheduler",
            columns: table => new
            {
                notification_id = table.Column<Guid>(type: "uuid", nullable: false),
                source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                recipient_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                message = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                deliver_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                read_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                cancelled_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_in_app_notification", x => x.notification_id);
                table.ForeignKey(
                    name: "fk_notification_recipient",
                    column: x => x.recipient_user_id,
                    principalSchema: "scheduler",
                    principalTable: "app_user",
                    principalColumn: "user_id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_notification_inbox",
            schema: "scheduler",
            table: "in_app_notification",
            columns: new[] { "recipient_user_id", "deliver_at" },
            filter: "cancelled_at IS NULL");

        migrationBuilder.CreateIndex(
            name: "ix_notification_source",
            schema: "scheduler",
            table: "in_app_notification",
            columns: new[] { "source", "deliver_at" },
            filter: "cancelled_at IS NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "in_app_notification", schema: "scheduler");

        migrationBuilder.DropCheckConstraint(name: "ck_task_queue_trigger_type", schema: "scheduler", table: "task_queue");
        migrationBuilder.AddCheckConstraint(
            name: "ck_task_queue_trigger_type",
            schema: "scheduler",
            table: "task_queue",
            sql: "trigger_type IN ('Scheduled', 'Manual', 'Retry', 'Dependency', 'CatchUp')");
        migrationBuilder.DropCheckConstraint(name: "ck_task_execution_trigger_type", schema: "scheduler", table: "task_execution");
        migrationBuilder.AddCheckConstraint(
            name: "ck_task_execution_trigger_type",
            schema: "scheduler",
            table: "task_execution",
            sql: "trigger_type IN ('Scheduled', 'Manual', 'Retry', 'Dependency', 'CatchUp')");
        migrationBuilder.DropColumn(name: "event_name", schema: "scheduler", table: "task_queue");
        migrationBuilder.DropColumn(name: "event_source", schema: "scheduler", table: "task_queue");
        migrationBuilder.DropColumn(name: "event_data", schema: "scheduler", table: "task_queue");

        migrationBuilder.DropCheckConstraint(name: "ck_task_status", schema: "scheduler", table: "task");
        migrationBuilder.AddCheckConstraint(
            name: "ck_task_status",
            schema: "scheduler",
            table: "task",
            sql: "status IN ('Active', 'Paused', 'Disabled')");
        migrationBuilder.DropCheckConstraint(name: "ck_task_max_executions", schema: "scheduler", table: "task");
        migrationBuilder.DropCheckConstraint(name: "ck_task_execution_count", schema: "scheduler", table: "task");
        migrationBuilder.DropColumn(name: "max_executions", schema: "scheduler", table: "task");
        migrationBuilder.DropColumn(name: "execution_count", schema: "scheduler", table: "task");
    }
}
