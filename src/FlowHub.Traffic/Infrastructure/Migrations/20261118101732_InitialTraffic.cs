using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowHub.Traffic.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialTraffic : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "traffic");

            migrationBuilder.CreateTable(
                name: "agv_types",
                schema: "traffic",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ForwardSpeedMmPerSecond = table.Column<double>(type: "double precision", nullable: false),
                    BackwardSpeedMmPerSecond = table.Column<double>(type: "double precision", nullable: false),
                    RotationSpeedDegreesPerSecond = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agv_types", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "command_numbers",
                schema: "traffic",
                columns: table => new
                {
                    AgvId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    NextNumber = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_command_numbers", x => x.AgvId);
                });

            migrationBuilder.CreateTable(
                name: "agvs",
                schema: "traffic",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TypeId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agvs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_agvs_agv_types_TypeId",
                        column: x => x.TypeId,
                        principalSchema: "traffic",
                        principalTable: "agv_types",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_agvs_TypeId",
                schema: "traffic",
                table: "agvs",
                column: "TypeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agvs",
                schema: "traffic");

            migrationBuilder.DropTable(
                name: "command_numbers",
                schema: "traffic");

            migrationBuilder.DropTable(
                name: "agv_types",
                schema: "traffic");
        }
    }
}
