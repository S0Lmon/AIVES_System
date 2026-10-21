using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIVES.DAL.Migrations
{
    /// <inheritdoc />
    public partial class AddExamQuestionPool : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RubricId",
                table: "Exams",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ExamQuestionPools",
                columns: table => new
                {
                    ExamId = table.Column<int>(type: "int", nullable: false),
                    QuestionId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamQuestionPools", x => new { x.ExamId, x.QuestionId });
                    table.ForeignKey(
                        name: "FK_ExamQuestionPools_Exams_ExamId",
                        column: x => x.ExamId,
                        principalTable: "Exams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExamQuestionPools_Questions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "Questions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Exams_RubricId",
                table: "Exams",
                column: "RubricId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamQuestionPools_QuestionId",
                table: "ExamQuestionPools",
                column: "QuestionId");

            migrationBuilder.AddForeignKey(
                name: "FK_Exams_Rubrics_RubricId",
                table: "Exams",
                column: "RubricId",
                principalTable: "Rubrics",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Exams_Rubrics_RubricId",
                table: "Exams");

            migrationBuilder.DropTable(
                name: "ExamQuestionPools");

            migrationBuilder.DropIndex(
                name: "IX_Exams_RubricId",
                table: "Exams");

            migrationBuilder.DropColumn(
                name: "RubricId",
                table: "Exams");
        }
    }
}
