using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErsatzTV.Infrastructure.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class Add_CopyPrepQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CopyPrepQueueItem",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MediaItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    MediaVersionId = table.Column<int>(type: "INTEGER", nullable: false),
                    MediaFileId = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    SourcePath = table.Column<string>(type: "TEXT", nullable: false),
                    TargetPath = table.Column<string>(type: "TEXT", nullable: true),
                    ArchivePath = table.Column<string>(type: "TEXT", nullable: true),
                    WorkingPath = table.Column<string>(type: "TEXT", nullable: true),
                    LastLogPath = table.Column<string>(type: "TEXT", nullable: true),
                    LastCommand = table.Column<string>(type: "TEXT", nullable: true),
                    LastError = table.Column<string>(type: "TEXT", nullable: true),
                    LastExitCode = table.Column<int>(type: "INTEGER", nullable: true),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    QueuedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FailedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CanceledAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReplacedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CopyPrepQueueItem", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CopyPrepQueueItem_MediaFile_MediaFileId",
                        column: x => x.MediaFileId,
                        principalTable: "MediaFile",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CopyPrepQueueItem_MediaItem_MediaItemId",
                        column: x => x.MediaItemId,
                        principalTable: "MediaItem",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CopyPrepQueueItem_MediaVersion_MediaVersionId",
                        column: x => x.MediaVersionId,
                        principalTable: "MediaVersion",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CopyPrepQueueLogEntry",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CopyPrepQueueItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Level = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Event = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    Details = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CopyPrepQueueLogEntry", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CopyPrepQueueLogEntry_CopyPrepQueueItem_CopyPrepQueueItemId",
                        column: x => x.CopyPrepQueueItemId,
                        principalTable: "CopyPrepQueueItem",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CopyPrepQueueItem_MediaFileId",
                table: "CopyPrepQueueItem",
                column: "MediaFileId");

            migrationBuilder.CreateIndex(
                name: "IX_CopyPrepQueueItem_MediaItemId",
                table: "CopyPrepQueueItem",
                column: "MediaItemId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CopyPrepQueueItem_MediaVersionId",
                table: "CopyPrepQueueItem",
                column: "MediaVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_CopyPrepQueueItem_Status",
                table: "CopyPrepQueueItem",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_CopyPrepQueueLogEntry_CopyPrepQueueItemId",
                table: "CopyPrepQueueLogEntry",
                column: "CopyPrepQueueItemId");

            migrationBuilder.CreateIndex(
                name: "IX_CopyPrepQueueLogEntry_CreatedAt",
                table: "CopyPrepQueueLogEntry",
                column: "CreatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CopyPrepQueueLogEntry");

            migrationBuilder.DropTable(
                name: "CopyPrepQueueItem");
        }
    }
}
