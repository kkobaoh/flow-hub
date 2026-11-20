using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowHub.Traffic.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCanTravelSpecialTransfer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CanTravelSpecialTransfer",
                schema: "traffic",
                table: "agv_types",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CanTravelSpecialTransfer",
                schema: "traffic",
                table: "agv_types");
        }
    }
}
