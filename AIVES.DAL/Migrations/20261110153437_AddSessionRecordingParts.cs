using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIVES.DAL.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionRecordingParts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SessionRecordingParts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExamAttemptId = table.Column<int>(type: "int", nullable: false),
                    Segment = table.Column<long>(type: "bigint", nullable: false),
                    PartIndex = table.Column<int>(type: "int", nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    HasVideo = table.Column<bool>(type: "bit", nullable: false),
                    StoragePath = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionRecordingParts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SessionRecordingParts_ExamAttempts_ExamAttemptId",
                        column: x => x.ExamAttemptId,
                        principalTable: "ExamAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SessionRecordingParts_CreatedAtUtc",
                table: "SessionRecordingParts",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SessionRecordingParts_ExamAttemptId_Segment_PartIndex",
                table: "SessionRecordingParts",
                columns: new[] { "ExamAttemptId", "Segment", "PartIndex" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SessionRecordingParts");
        }
    }
}
