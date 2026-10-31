using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Scheduler.Infrastructure.Persistence.Migrations;

[DbContext(typeof(SchedulerDbContext))]
[Migration("20261031101500_AddDeveloperRole")]
public sealed class AddDeveloperRole : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_app_user_role",
            schema: "scheduler",
            table: "app_user");

        migrationBuilder.Sql("""
            UPDATE scheduler.app_user
            SET role = 'Developer'
            WHERE user_id = (
                SELECT user_id
                FROM scheduler.app_user
                WHERE role = 'Administrator' AND is_active = TRUE
                ORDER BY created_at, user_id
                LIMIT 1
            );
            """);

        migrationBuilder.AddCheckConstraint(
            name: "ck_app_user_role",
            schema: "scheduler",
            table: "app_user",
            sql: "role IN ('Administrator', 'Viewer', 'Developer')");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_app_user_role",
            schema: "scheduler",
            table: "app_user");

        migrationBuilder.Sql("UPDATE scheduler.app_user SET role = 'Administrator' WHERE role = 'Developer';");

        migrationBuilder.AddCheckConstraint(
            name: "ck_app_user_role",
            schema: "scheduler",
            table: "app_user",
            sql: "role IN ('Administrator', 'Viewer')");
    }
}
