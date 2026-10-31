using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scheduler.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Register and Forgot password: each account can hold one hashed recovery code, used once to set a new password
    /// without an administrator. Existing accounts start without a code and create one on the Change password page.
    /// </summary>
    public partial class AccountRecovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "recovery_code_hash",
                schema: "scheduler",
                table: "app_user",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "recovery_code_salt",
                schema: "scheduler",
                table: "app_user",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "recovery_code_hash",
                schema: "scheduler",
                table: "app_user");

            migrationBuilder.DropColumn(
                name: "recovery_code_salt",
                schema: "scheduler",
                table: "app_user");
        }
    }
}
