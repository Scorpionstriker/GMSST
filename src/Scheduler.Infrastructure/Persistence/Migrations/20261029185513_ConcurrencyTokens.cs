using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scheduler.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// WP-7 (F-27): configuration, logging_provider and app_user use PostgreSQL's xmin system column as a concurrency
    /// token. xmin exists on every table, so this migration changes only the model; it creates no SQL.
    /// </summary>
    public partial class ConcurrencyTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "scheduler",
                table: "logging_provider",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "scheduler",
                table: "configuration",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "scheduler",
                table: "app_user",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "scheduler",
                table: "logging_provider");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "scheduler",
                table: "configuration");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "scheduler",
                table: "app_user");
        }
    }
}
