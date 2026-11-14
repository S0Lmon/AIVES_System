using AIVES.DAL.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIVES.DAL.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261113190000_AdminInterviewTestCandidates")]
public partial class AdminInterviewTestCandidates : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "AdminTestOwnerId",
            table: "ExamCandidates",
            type: "nvarchar(450)",
            maxLength: 450,
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "IsAdminTest",
            table: "ExamCandidates",
            type: "bit",
            nullable: false,
            defaultValue: false);

        migrationBuilder.CreateIndex(
            name: "IX_ExamCandidates_IsAdminTest_AdminTestOwnerId",
            table: "ExamCandidates",
            columns: new[] { "IsAdminTest", "AdminTestOwnerId" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_ExamCandidates_IsAdminTest_AdminTestOwnerId",
            table: "ExamCandidates");

        migrationBuilder.DropColumn(
            name: "AdminTestOwnerId",
            table: "ExamCandidates");

        migrationBuilder.DropColumn(
            name: "IsAdminTest",
            table: "ExamCandidates");
    }
}
