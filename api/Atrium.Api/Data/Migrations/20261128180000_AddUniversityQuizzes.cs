using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Atrium.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261128180000_AddUniversityQuizzes")]
public sealed class AddUniversityQuizzes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.CreateTable(
            name: "university_quizzes",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                course_id = table.Column<Guid>(type: "uuid", nullable: false),
                university_activity_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                questions_json = table.Column<string>(type: "text", nullable: false),
                question_count = table.Column<int>(type: "integer", nullable: false),
                attempt_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                last_score = table.Column<int>(type: "integer", nullable: true),
                best_score = table.Column<int>(type: "integer", nullable: true),
                last_played_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_university_quizzes", x => x.id);
                table.ForeignKey(
                    name: "FK_university_quizzes_university_activity_items_university_activity_item_id",
                    column: x => x.university_activity_item_id,
                    principalTable: "university_activity_items",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_university_quizzes_university_courses_course_id",
                    column: x => x.course_id,
                    principalTable: "university_courses",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_university_quizzes_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_university_quizzes_user_course_item",
            table: "university_quizzes",
            columns: ["user_id", "course_id", "university_activity_item_id"]);

        migrationBuilder.CreateIndex(
            name: "IX_university_quizzes_course_id",
            table: "university_quizzes",
            column: "course_id");

        migrationBuilder.CreateIndex(
            name: "IX_university_quizzes_university_activity_item_id",
            table: "university_quizzes",
            column: "university_activity_item_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropTable(name: "university_quizzes");
    }
}

