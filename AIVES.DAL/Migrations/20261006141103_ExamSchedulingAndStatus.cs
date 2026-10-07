using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIVES.DAL.Migrations
{
    /// <inheritdoc />
    public partial class ExamSchedulingAndStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BreakEveryCount",
                table: "Exams",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BreakMinutes",
                table: "Exams",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BufferMinutes",
                table: "Exams",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "EndsAtUtc",
                table: "Exams",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExamType",
                table: "Exams",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Instructions",
                table: "Exams",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Strategy",
                table: "Exams",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Term",
                table: "Exams",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SlotStartsAtUtc",
                table: "ExamCandidates",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StatusOverride",
                table: "ExamCandidates",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BreakEveryCount",
                table: "Exams");

            migrationBuilder.DropColumn(
                name: "BreakMinutes",
                table: "Exams");

            migrationBuilder.DropColumn(
                name: "BufferMinutes",
                table: "Exams");

            migrationBuilder.DropColumn(
                name: "EndsAtUtc",
                table: "Exams");

            migrationBuilder.DropColumn(
                name: "ExamType",
                table: "Exams");

            migrationBuilder.DropColumn(
                name: "Instructions",
                table: "Exams");

            migrationBuilder.DropColumn(
                name: "Strategy",
                table: "Exams");

            migrationBuilder.DropColumn(
                name: "Term",
                table: "Exams");

            migrationBuilder.DropColumn(
                name: "SlotStartsAtUtc",
                table: "ExamCandidates");

            migrationBuilder.DropColumn(
                name: "StatusOverride",
                table: "ExamCandidates");
        }
    }
}
