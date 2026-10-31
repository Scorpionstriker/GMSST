using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scheduler.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// WP-8h (F-12): a stable, unique key for the tasks the scheduler and its modules create, so they can be renamed and
    /// are found again by key. Existing built-in tasks are given their key at the next start (EnsureTaskAsync).
    /// </summary>
    public partial class BuiltInTaskKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "system_key",
                schema: "scheduler",
                table: "task",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_task_system_key",
                schema: "scheduler",
                table: "task",
                column: "system_key",
                unique: true,
                filter: "system_key IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_task_system_key",
                schema: "scheduler",
                table: "task");

            migrationBuilder.DropColumn(
                name: "system_key",
                schema: "scheduler",
                table: "task");
        }
    }
}
