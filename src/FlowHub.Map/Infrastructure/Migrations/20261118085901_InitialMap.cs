using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowHub.Map.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialMap : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "map");

            migrationBuilder.CreateTable(
                name: "carrier_heights",
                schema: "map",
                columns: table => new
                {
                    PanelId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    HeightMm = table.Column<int>(type: "integer", nullable: false),
                    SetAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_carrier_heights", x => x.PanelId);
                });

            migrationBuilder.CreateTable(
                name: "connections",
                schema: "map",
                columns: table => new
                {
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    FromPanelId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ToPanelId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DistanceMm = table.Column<int>(type: "integer", nullable: false),
                    HeadingDegrees = table.Column<int>(type: "integer", nullable: false),
                    OneWay = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_connections", x => new { x.Version, x.Order });
                });

            migrationBuilder.CreateTable(
                name: "panels",
                schema: "map",
                columns: table => new
                {
                    Version = table.Column<int>(type: "integer", nullable: false),
                    PanelId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    HasChargingPin = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_panels", x => new { x.Version, x.PanelId });
                });

            migrationBuilder.CreateTable(
                name: "versions",
                schema: "map",
                columns: table => new
                {
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_versions", x => x.Version);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "carrier_heights",
                schema: "map");

            migrationBuilder.DropTable(
                name: "connections",
                schema: "map");

            migrationBuilder.DropTable(
                name: "panels",
                schema: "map");

            migrationBuilder.DropTable(
                name: "versions",
                schema: "map");
        }
    }
}
