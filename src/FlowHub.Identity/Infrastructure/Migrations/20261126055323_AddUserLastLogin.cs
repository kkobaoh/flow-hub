using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowHub.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserLastLogin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "LastLoginTicks",
                schema: "identity",
                table: "users",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastLoginTicks",
                schema: "identity",
                table: "users");
        }
    }
}
