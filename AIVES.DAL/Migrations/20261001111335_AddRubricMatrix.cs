using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIVES.DAL.Migrations
{
    /// <inheritdoc />
    public partial class AddRubricMatrix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RubricLevels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RubricId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Points = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RubricLevels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RubricLevels_Rubrics_RubricId",
                        column: x => x.RubricId,
                        principalTable: "Rubrics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RubricCriterionLevels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RubricCriterionId = table.Column<int>(type: "int", nullable: false),
                    RubricLevelId = table.Column<int>(type: "int", nullable: false),
                    Descriptor = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Points = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RubricCriterionLevels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RubricCriterionLevels_RubricCriteria_RubricCriterionId",
                        column: x => x.RubricCriterionId,
                        principalTable: "RubricCriteria",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RubricCriterionLevels_RubricLevels_RubricLevelId",
                        column: x => x.RubricLevelId,
                        principalTable: "RubricLevels",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_RubricCriterionLevels_RubricCriterionId",
                table: "RubricCriterionLevels",
                column: "RubricCriterionId");

            migrationBuilder.CreateIndex(
                name: "IX_RubricCriterionLevels_RubricLevelId",
                table: "RubricCriterionLevels",
                column: "RubricLevelId");

            migrationBuilder.CreateIndex(
                name: "IX_RubricLevels_RubricId",
                table: "RubricLevels",
                column: "RubricId");

            // Rubrics that predate the matrix only had criteria carrying a max point. Give them a
            // readable four column scale and one cell per criterion so no rubric is left without
            // columns and every existing criterion keeps its total.
            migrationBuilder.Sql("""
                INSERT INTO [RubricLevels] ([RubricId], [Name], [Points], [Description], [Order])
                SELECT r.[Id], s.[Name],
                       CAST(FLOOR(ISNULL((SELECT MAX(c.[MaxPoints]) FROM [RubricCriteria] c WHERE c.[RubricId] = r.[Id]), 0) * s.[Scale] / 4.0) AS int),
                       N'', s.[Scale]
                FROM [Rubrics] r
                CROSS JOIN (VALUES (1, N'Below'), (2, N'Approaching'), (3, N'Meets'), (4, N'Exceeds')) AS s([Scale], [Name]);
                """);

            migrationBuilder.Sql("""
                INSERT INTO [RubricCriterionLevels] ([RubricCriterionId], [RubricLevelId], [Descriptor], [Points])
                SELECT c.[Id], l.[Id], N'', l.[Points]
                FROM [RubricCriteria] c
                INNER JOIN [RubricLevels] l ON l.[RubricId] = c.[RubricId];
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RubricCriterionLevels");

            migrationBuilder.DropTable(
                name: "RubricLevels");
        }
    }
}
