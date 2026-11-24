using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowHub.Map.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMapDrawing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PanelSizeMm",
                schema: "map",
                table: "versions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Column",
                schema: "map",
                table: "panels",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "CrossOffsetEastMm",
                schema: "map",
                table: "panels",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "CrossOffsetSouthMm",
                schema: "map",
                table: "panels",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "OutlineHeightMm",
                schema: "map",
                table: "panels",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OutlineWidthMm",
                schema: "map",
                table: "panels",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OutlineXMm",
                schema: "map",
                table: "panels",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OutlineYMm",
                schema: "map",
                table: "panels",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Row",
                schema: "map",
                table: "panels",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PanelSizeMm",
                schema: "map",
                table: "versions");

            migrationBuilder.DropColumn(
                name: "Column",
                schema: "map",
                table: "panels");

            migrationBuilder.DropColumn(
                name: "CrossOffsetEastMm",
                schema: "map",
                table: "panels");

            migrationBuilder.DropColumn(
                name: "CrossOffsetSouthMm",
                schema: "map",
                table: "panels");

            migrationBuilder.DropColumn(
                name: "OutlineHeightMm",
                schema: "map",
                table: "panels");

            migrationBuilder.DropColumn(
                name: "OutlineWidthMm",
                schema: "map",
                table: "panels");

            migrationBuilder.DropColumn(
                name: "OutlineXMm",
                schema: "map",
                table: "panels");

            migrationBuilder.DropColumn(
                name: "OutlineYMm",
                schema: "map",
                table: "panels");

            migrationBuilder.DropColumn(
                name: "Row",
                schema: "map",
                table: "panels");
        }
    }
}
