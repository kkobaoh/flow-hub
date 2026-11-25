using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowHub.Map.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCarrierHeightSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Source",
                schema: "map",
                table: "carrier_heights",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Measured");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Source",
                schema: "map",
                table: "carrier_heights");
        }
    }
}
