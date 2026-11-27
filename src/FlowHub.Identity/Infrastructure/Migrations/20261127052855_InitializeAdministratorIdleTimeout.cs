using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowHub.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitializeAdministratorIdleTimeout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IdleTimeoutInitialized",
                schema: "identity",
                table: "roles",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IdleTimeoutInitialized",
                schema: "identity",
                table: "roles");
        }
    }
}
