using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowHub.Traffic.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAgvTypeName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Name",
                schema: "traffic",
                table: "agv_types",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            // すでにある種類は、名前を ID と同じにする（あとから画面で変えられる）
            migrationBuilder.Sql("UPDATE traffic.agv_types SET \"Name\" = \"Id\";");

            migrationBuilder.CreateIndex(
                name: "IX_agv_types_Name",
                schema: "traffic",
                table: "agv_types",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_agv_types_Name",
                schema: "traffic",
                table: "agv_types");

            migrationBuilder.DropColumn(
                name: "Name",
                schema: "traffic",
                table: "agv_types");
        }
    }
}
