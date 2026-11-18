using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowHub.Consumables.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialConsumables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "consumables");

            migrationBuilder.CreateTable(
                name: "devices",
                schema: "consumables",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TransferPanel = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    HasStockerSetting = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_devices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "process_types",
                schema: "consumables",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CarrierType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CarrierCapacity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_process_types", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "stations",
                schema: "consumables",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TransferPanel = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "device_stockers",
                schema: "consumables",
                columns: table => new
                {
                    DeviceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    StockerNumber = table.Column<int>(type: "integer", nullable: false),
                    SlotCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_stockers", x => new { x.DeviceId, x.StockerNumber });
                    table.ForeignKey(
                        name: "FK_device_stockers_devices_DeviceId",
                        column: x => x.DeviceId,
                        principalSchema: "consumables",
                        principalTable: "devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "varieties",
                schema: "consumables",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ProcessTypeId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_varieties", x => x.Id);
                    table.ForeignKey(
                        name: "FK_varieties_process_types_ProcessTypeId",
                        column: x => x.ProcessTypeId,
                        principalSchema: "consumables",
                        principalTable: "process_types",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "expected_arrivals",
                schema: "consumables",
                columns: table => new
                {
                    ConsumableId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    VarietyId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_expected_arrivals", x => x.ConsumableId);
                    table.ForeignKey(
                        name: "FK_expected_arrivals_varieties_VarietyId",
                        column: x => x.VarietyId,
                        principalSchema: "consumables",
                        principalTable: "varieties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stocker_settings",
                schema: "consumables",
                columns: table => new
                {
                    DeviceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    VarietyId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stocker_settings", x => new { x.DeviceId, x.VarietyId });
                    table.ForeignKey(
                        name: "FK_stocker_settings_devices_DeviceId",
                        column: x => x.DeviceId,
                        principalSchema: "consumables",
                        principalTable: "devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_stocker_settings_varieties_VarietyId",
                        column: x => x.VarietyId,
                        principalSchema: "consumables",
                        principalTable: "varieties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_expected_arrivals_VarietyId",
                schema: "consumables",
                table: "expected_arrivals",
                column: "VarietyId");

            migrationBuilder.CreateIndex(
                name: "IX_stocker_settings_VarietyId",
                schema: "consumables",
                table: "stocker_settings",
                column: "VarietyId");

            migrationBuilder.CreateIndex(
                name: "IX_varieties_ProcessTypeId",
                schema: "consumables",
                table: "varieties",
                column: "ProcessTypeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "device_stockers",
                schema: "consumables");

            migrationBuilder.DropTable(
                name: "expected_arrivals",
                schema: "consumables");

            migrationBuilder.DropTable(
                name: "stations",
                schema: "consumables");

            migrationBuilder.DropTable(
                name: "stocker_settings",
                schema: "consumables");

            migrationBuilder.DropTable(
                name: "devices",
                schema: "consumables");

            migrationBuilder.DropTable(
                name: "varieties",
                schema: "consumables");

            migrationBuilder.DropTable(
                name: "process_types",
                schema: "consumables");
        }
    }
}
