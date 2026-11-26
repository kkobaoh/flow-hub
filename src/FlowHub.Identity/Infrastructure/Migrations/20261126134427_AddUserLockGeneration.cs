using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowHub.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserLockGeneration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "LockGeneration",
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
                name: "LockGeneration",
                schema: "identity",
                table: "users");
        }
    }
}
