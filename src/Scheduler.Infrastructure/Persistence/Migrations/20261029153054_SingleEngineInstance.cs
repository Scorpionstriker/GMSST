using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scheduler.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SingleEngineInstance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "engine_instance",
                schema: "scheduler",
                columns: table => new
                {
                    instance_id = table.Column<short>(type: "smallint", nullable: false),
                    host_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    process_id = table.Column<int>(type: "integer", nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    heartbeat_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_engine_instance", x => x.instance_id);
                    table.CheckConstraint("ck_engine_instance_single_row", "instance_id = 1");
                });

            // WP-1: the Maintenance Agent no longer runs its own engine. Its old tasks (types HealthCheck and
            // TempFileCleanup) are renamed and deleted, so the connector module can create the new Local HTTP Request
            // tasks under the same names; their waiting runs are cancelled.
            migrationBuilder.Sql(
                """
                UPDATE scheduler.task_queue SET queue_status = 'Cancelled'
                WHERE queue_status = 'Waiting'
                  AND task_id IN (SELECT task_id FROM scheduler.task WHERE task_type IN ('HealthCheck', 'TempFileCleanup'));
                UPDATE scheduler.task SET name = left(name, 88) || ' (old agent)', is_deleted = true, updated_at = now()
                WHERE task_type IN ('HealthCheck', 'TempFileCleanup');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "engine_instance",
                schema: "scheduler");
        }
    }
}
