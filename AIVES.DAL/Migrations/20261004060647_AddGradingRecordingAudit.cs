using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIVES.DAL.Migrations
{
    /// <inheritdoc />
    public partial class AddGradingRecordingAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DecisionLatencyMs",
                table: "ExamTurns",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RawAnswer",
                table: "ExamTurns",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ResponseDelayMs",
                table: "ExamTurns",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SpeakingMs",
                table: "ExamTurns",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RecordAudio",
                table: "Exams",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "RecordVideo",
                table: "Exams",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "RubricJson",
                table: "ExamCandidateQuestions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FinalScore",
                table: "ExamAttempts",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FinalizedAtUtc",
                table: "ExamAttempts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FinalizedById",
                table: "ExamAttempts",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "GradingClaimedAtUtc",
                table: "ExamAttempts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GradingError",
                table: "ExamAttempts",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GradingStatus",
                table: "ExamAttempts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "LecturerComment",
                table: "ExamAttempts",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RecordingConsentAtUtc",
                table: "ExamAttempts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AuditEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ActorEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Action = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ExamId = table.Column<int>(type: "int", nullable: true),
                    CandidateId = table.Column<int>(type: "int", nullable: true),
                    Details = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GlossaryTerms",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SubjectId = table.Column<int>(type: "int", nullable: false),
                    Term = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SpokenForms = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GlossaryTerms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GlossaryTerms_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QuestionGrades",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExamCandidateQuestionId = table.Column<int>(type: "int", nullable: false),
                    MaxScore = table.Column<decimal>(type: "decimal(6,2)", precision: 6, scale: 2, nullable: false),
                    AiScore = table.Column<decimal>(type: "decimal(6,2)", precision: 6, scale: 2, nullable: true),
                    AiCriteriaJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AiStrengths = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AiWeaknesses = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AiMissingPoints = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AiSummary = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AiModel = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    AiGradedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LecturerScore = table.Column<decimal>(type: "decimal(6,2)", precision: 6, scale: 2, nullable: true),
                    LecturerComment = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    UpdatedById = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestionGrades", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuestionGrades_ExamCandidateQuestions_ExamCandidateQuestionId",
                        column: x => x.ExamCandidateQuestionId,
                        principalTable: "ExamCandidateQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SubjectLecturers",
                columns: table => new
                {
                    SubjectId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubjectLecturers", x => new { x.SubjectId, x.UserId });
                    table.ForeignKey(
                        name: "FK_SubjectLecturers_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SubjectLecturers_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SystemSettings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemSettings", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "TurnRecordings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExamTurnId = table.Column<int>(type: "int", nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    HasVideo = table.Column<bool>(type: "bit", nullable: false),
                    StoragePath = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TurnRecordings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TurnRecordings_ExamTurns_ExamTurnId",
                        column: x => x.ExamTurnId,
                        principalTable: "ExamTurns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExamAttempts_Status_GradingStatus",
                table: "ExamAttempts",
                columns: new[] { "Status", "GradingStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_Action",
                table: "AuditEntries",
                column: "Action");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_AtUtc",
                table: "AuditEntries",
                column: "AtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_ExamId_AtUtc",
                table: "AuditEntries",
                columns: new[] { "ExamId", "AtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_GlossaryTerms_SubjectId_Term",
                table: "GlossaryTerms",
                columns: new[] { "SubjectId", "Term" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuestionGrades_ExamCandidateQuestionId",
                table: "QuestionGrades",
                column: "ExamCandidateQuestionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubjectLecturers_UserId",
                table: "SubjectLecturers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_TurnRecordings_CreatedAtUtc",
                table: "TurnRecordings",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_TurnRecordings_ExamTurnId",
                table: "TurnRecordings",
                column: "ExamTurnId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditEntries");

            migrationBuilder.DropTable(
                name: "GlossaryTerms");

            migrationBuilder.DropTable(
                name: "QuestionGrades");

            migrationBuilder.DropTable(
                name: "SubjectLecturers");

            migrationBuilder.DropTable(
                name: "SystemSettings");

            migrationBuilder.DropTable(
                name: "TurnRecordings");

            migrationBuilder.DropIndex(
                name: "IX_ExamAttempts_Status_GradingStatus",
                table: "ExamAttempts");

            migrationBuilder.DropColumn(
                name: "DecisionLatencyMs",
                table: "ExamTurns");

            migrationBuilder.DropColumn(
                name: "RawAnswer",
                table: "ExamTurns");

            migrationBuilder.DropColumn(
                name: "ResponseDelayMs",
                table: "ExamTurns");

            migrationBuilder.DropColumn(
                name: "SpeakingMs",
                table: "ExamTurns");

            migrationBuilder.DropColumn(
                name: "RecordAudio",
                table: "Exams");

            migrationBuilder.DropColumn(
                name: "RecordVideo",
                table: "Exams");

            migrationBuilder.DropColumn(
                name: "RubricJson",
                table: "ExamCandidateQuestions");

            migrationBuilder.DropColumn(
                name: "FinalScore",
                table: "ExamAttempts");

            migrationBuilder.DropColumn(
                name: "FinalizedAtUtc",
                table: "ExamAttempts");

            migrationBuilder.DropColumn(
                name: "FinalizedById",
                table: "ExamAttempts");

            migrationBuilder.DropColumn(
                name: "GradingClaimedAtUtc",
                table: "ExamAttempts");

            migrationBuilder.DropColumn(
                name: "GradingError",
                table: "ExamAttempts");

            migrationBuilder.DropColumn(
                name: "GradingStatus",
                table: "ExamAttempts");

            migrationBuilder.DropColumn(
                name: "LecturerComment",
                table: "ExamAttempts");

            migrationBuilder.DropColumn(
                name: "RecordingConsentAtUtc",
                table: "ExamAttempts");
        }
    }
}
