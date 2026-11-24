using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowHub.Map.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCarrierLoweringHeading : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CarrierLoweringHeading",
                schema: "map",
                table: "panels",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CarrierLoweringHeading",
                schema: "map",
                table: "panels");
        }
    }
}
