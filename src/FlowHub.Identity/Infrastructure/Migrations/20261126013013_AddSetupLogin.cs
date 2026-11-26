using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowHub.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSetupLogin : Migration
    {
        private static readonly string[] LoginColumns = ["Id", "Failures", "LockedUntilTicks", "Version"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "setup_login",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    Failures = table.Column<int>(type: "integer", nullable: false),
                    LockedUntilTicks = table.Column<long>(type: "bigint", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_setup_login", x => x.Id);
                });

            migrationBuilder.InsertData(
                schema: "identity",
                table: "setup_login",
                columns: LoginColumns,
                values: new object[] { 1, 0, 0L, 0L });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "setup_login",
                schema: "identity");
        }
    }
}
