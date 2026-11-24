using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowHub.Traffic.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddControlStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ControlStatus",
                schema: "traffic",
                table: "agvs",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Detached"); // すでに登録されている AGV は、切り離しから始める
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ControlStatus",
                schema: "traffic",
                table: "agvs");
        }
    }
}
