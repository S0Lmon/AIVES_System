using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIVES.DAL.Migrations
{
    /// <inheritdoc />
    public partial class AddInterviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AnswerTimeLimitSeconds",
                table: "Exams",
                type: "int",
                nullable: false,
                defaultValue: 120);

            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "Exams",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "vi-VN");

            migrationBuilder.AddColumn<int>(
                name: "MaxFollowUpsPerQuestion",
                table: "Exams",
                type: "int",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.CreateTable(
                name: "ExamAttempts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExamCandidateId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExamAttempts_ExamCandidates_ExamCandidateId",
                        column: x => x.ExamCandidateId,
                        principalTable: "ExamCandidates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExamTurns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExamAttemptId = table.Column<int>(type: "int", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    MainIndex = table.Column<int>(type: "int", nullable: false),
                    FollowUpIndex = table.Column<int>(type: "int", nullable: false),
                    QuestionText = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AskedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Answer = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    InputMode = table.Column<int>(type: "int", nullable: true),
                    AnsweredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TimedOut = table.Column<bool>(type: "bit", nullable: false),
                    Decision = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamTurns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExamTurns_ExamAttempts_ExamAttemptId",
                        column: x => x.ExamAttemptId,
                        principalTable: "ExamAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExamAttempts_ExamCandidateId",
                table: "ExamAttempts",
                column: "ExamCandidateId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExamTurns_ExamAttemptId_Order",
                table: "ExamTurns",
                columns: new[] { "ExamAttemptId", "Order" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExamTurns");

            migrationBuilder.DropTable(
                name: "ExamAttempts");

            migrationBuilder.DropColumn(
                name: "AnswerTimeLimitSeconds",
                table: "Exams");

            migrationBuilder.DropColumn(
                name: "Language",
                table: "Exams");

            migrationBuilder.DropColumn(
                name: "MaxFollowUpsPerQuestion",
                table: "Exams");
        }
    }
}
