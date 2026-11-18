using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowHub.Dispatching.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialDispatching : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "dispatching");

            migrationBuilder.CreateTable(
                name: "agv_types",
                schema: "dispatching",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    GoChargeBelowPercent = table.Column<int>(type: "integer", nullable: false),
                    ResumeAtPercent = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agv_types", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "charging_defaults",
                schema: "dispatching",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    GoChargeBelowPercent = table.Column<int>(type: "integer", nullable: false),
                    ResumeAtPercent = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_charging_defaults", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "agvs",
                schema: "dispatching",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TypeId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CarrierType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agvs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_agvs_agv_types_TypeId",
                        column: x => x.TypeId,
                        principalSchema: "dispatching",
                        principalTable: "agv_types",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_agvs_TypeId",
                schema: "dispatching",
                table: "agvs",
                column: "TypeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agvs",
                schema: "dispatching");

            migrationBuilder.DropTable(
                name: "charging_defaults",
                schema: "dispatching");

            migrationBuilder.DropTable(
                name: "agv_types",
                schema: "dispatching");
        }
    }
}
