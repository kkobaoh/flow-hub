using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowHub.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserLoginState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Failures",
                schema: "identity",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "LockedUntilTicks",
                schema: "identity",
                table: "users",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "Version",
                schema: "identity",
                table: "users",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Failures",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "LockedUntilTicks",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "Version",
                schema: "identity",
                table: "users");
        }
    }
}
