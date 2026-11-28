using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Atrium.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261128153000_AddChatHistory")]
public sealed class AddChatHistory : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.CreateTable(
            name: "chat_threads",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                course_id = table.Column<Guid>(type: "uuid", nullable: true),
                title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_chat_threads", x => x.id);
                table.ForeignKey(
                    name: "FK_chat_threads_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_chat_threads_university_courses_course_id",
                    column: x => x.course_id,
                    principalTable: "university_courses",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateIndex(
            name: "IX_chat_threads_course_id",
            table: "chat_threads",
            column: "course_id");

        migrationBuilder.CreateIndex(
            name: "ix_chat_threads_user_updated",
            table: "chat_threads",
            columns: ["user_id", "updated_at"]);

        migrationBuilder.CreateTable(
            name: "chat_messages",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                thread_id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                content = table.Column<string>(type: "text", nullable: false),
                reasoning = table.Column<string>(type: "text", nullable: true),
                citations_json = table.Column<string>(type: "text", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_chat_messages", x => x.id);
                table.ForeignKey(
                    name: "FK_chat_messages_chat_threads_thread_id",
                    column: x => x.thread_id,
                    principalTable: "chat_threads",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_chat_messages_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_chat_messages_user_id",
            table: "chat_messages",
            column: "user_id");

        migrationBuilder.CreateIndex(
            name: "ix_chat_messages_thread_created",
            table: "chat_messages",
            columns: ["thread_id", "created_at"]);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropTable(name: "chat_messages");
        migrationBuilder.DropTable(name: "chat_threads");
    }
}

