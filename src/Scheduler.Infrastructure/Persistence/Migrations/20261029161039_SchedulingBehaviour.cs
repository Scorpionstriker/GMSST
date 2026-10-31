using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scheduler.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// WP-6: remembers whether a queue entry comes from Run now, so the retries of a manual run of a paused task still
    /// run, and adds the queue time to the claim index (priority, due time, then the entry queued first).
    /// </summary>
    public partial class SchedulingBehaviour : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_queue_claim",
                schema: "scheduler",
                table: "task_queue");

            migrationBuilder.AddColumn<bool>(
                name: "is_manual_run",
                schema: "scheduler",
                table: "task_queue",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("UPDATE scheduler.task_queue SET is_manual_run = true WHERE trigger_type = 'Manual';");

            migrationBuilder.CreateIndex(
                name: "ix_queue_claim",
                schema: "scheduler",
                table: "task_queue",
                columns: new[] { "priority", "scheduled_time", "enqueued_at" },
                filter: "queue_status = 'Waiting'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_queue_claim",
                schema: "scheduler",
                table: "task_queue");

            migrationBuilder.DropColumn(
                name: "is_manual_run",
                schema: "scheduler",
                table: "task_queue");

            migrationBuilder.CreateIndex(
                name: "ix_queue_claim",
                schema: "scheduler",
                table: "task_queue",
                columns: new[] { "priority", "scheduled_time" },
                filter: "queue_status = 'Waiting'");
        }
    }
}
