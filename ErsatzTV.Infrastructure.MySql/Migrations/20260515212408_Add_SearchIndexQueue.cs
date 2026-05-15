using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErsatzTV.Infrastructure.MySql.Migrations
{
    /// <inheritdoc />
    public partial class Add_SearchIndexQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SearchIndexQueue",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    MediaItemId = table.Column<int>(type: "int", nullable: false),
                    Operation = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Processed = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SearchIndexQueue", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_SearchIndexQueue_CreatedAt",
                table: "SearchIndexQueue",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SearchIndexQueue_MediaItemId",
                table: "SearchIndexQueue",
                column: "MediaItemId");

            migrationBuilder.CreateIndex(
                name: "IX_SearchIndexQueue_Processed",
                table: "SearchIndexQueue",
                column: "Processed");

            migrationBuilder.CreateIndex(
                name: "IX_SearchIndexQueue_ProcessedAt",
                table: "SearchIndexQueue",
                column: "ProcessedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SearchIndexQueue");
        }
    }
}
