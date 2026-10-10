using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIVES.DAL.Migrations
{
    /// <inheritdoc />
    public partial class AddExams : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Exams",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SubjectId = table.Column<int>(type: "int", nullable: true),
                    SubjectName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TopicId = table.Column<int>(type: "int", nullable: true),
                    TopicName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    StartsAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SlotMinutes = table.Column<int>(type: "int", nullable: false),
                    MainQuestionCount = table.Column<int>(type: "int", nullable: false),
                    MaxFollowUpQuestions = table.Column<int>(type: "int", nullable: false),
                    CreatedById = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Exams", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Exams_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Exams_Topics_TopicId",
                        column: x => x.TopicId,
                        principalTable: "Topics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ExamCandidates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExamId = table.Column<int>(type: "int", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamCandidates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExamCandidates_Exams_ExamId",
                        column: x => x.ExamId,
                        principalTable: "Exams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExamCandidateQuestions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExamCandidateId = table.Column<int>(type: "int", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    QuestionId = table.Column<int>(type: "int", nullable: true),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExpectedAnswer = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BloomLevelName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamCandidateQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExamCandidateQuestions_ExamCandidates_ExamCandidateId",
                        column: x => x.ExamCandidateId,
                        principalTable: "ExamCandidates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExamCandidateQuestions_Questions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "Questions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExamCandidateQuestions_ExamCandidateId_Order",
                table: "ExamCandidateQuestions",
                columns: new[] { "ExamCandidateId", "Order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExamCandidateQuestions_QuestionId",
                table: "ExamCandidateQuestions",
                column: "QuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamCandidates_Email",
                table: "ExamCandidates",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_ExamCandidates_ExamId_Email",
                table: "ExamCandidates",
                columns: new[] { "ExamId", "Email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExamCandidates_ExamId_Order",
                table: "ExamCandidates",
                columns: new[] { "ExamId", "Order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Exams_CreatedById",
                table: "Exams",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_Exams_StartsAtUtc",
                table: "Exams",
                column: "StartsAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Exams_SubjectId",
                table: "Exams",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Exams_TopicId",
                table: "Exams",
                column: "TopicId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExamCandidateQuestions");

            migrationBuilder.DropTable(
                name: "ExamCandidates");

            migrationBuilder.DropTable(
                name: "Exams");
        }
    }
}
