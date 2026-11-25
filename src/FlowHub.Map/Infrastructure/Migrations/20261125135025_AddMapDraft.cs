using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowHub.Map.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMapDraft : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "drafts",
                schema: "map",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    SavedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Content = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_drafts", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "drafts",
                schema: "map");
        }
    }
}
