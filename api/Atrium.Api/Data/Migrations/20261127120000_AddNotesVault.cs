using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Atrium.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261127120000_AddNotesVault")]
public sealed class AddNotesVault : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.CreateTable(
            name: "note_folders",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                parent_folder_id = table.Column<Guid>(type: "uuid", nullable: true),
                name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                allow_agent_read = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                sort_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_note_folders", x => x.id);
                table.ForeignKey(
                    name: "FK_note_folders_note_folders_parent_folder_id",
                    column: x => x.parent_folder_id,
                    principalTable: "note_folders",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_note_folders_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "notes",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                folder_id = table.Column<Guid>(type: "uuid", nullable: true),
                title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                content = table.Column<string>(type: "text", nullable: false),
                content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                allow_agent_read = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_notes", x => x.id);
                table.ForeignKey(
                    name: "FK_notes_note_folders_folder_id",
                    column: x => x.folder_id,
                    principalTable: "note_folders",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_notes_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "note_tags",
            columns: table => new
            {
                note_id = table.Column<Guid>(type: "uuid", nullable: false),
                tag = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_note_tags", x => new { x.note_id, x.tag });
                table.ForeignKey(
                    name: "FK_note_tags_notes_note_id",
                    column: x => x.note_id,
                    principalTable: "notes",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_note_tags_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_note_folders_parent_folder_id",
            table: "note_folders",
            column: "parent_folder_id");

        migrationBuilder.CreateIndex(
            name: "ix_note_folders_user_parent",
            table: "note_folders",
            columns: ["user_id", "parent_folder_id", "deleted_at"]);

        migrationBuilder.CreateIndex(
            name: "IX_notes_folder_id",
            table: "notes",
            column: "folder_id");

        migrationBuilder.CreateIndex(
            name: "ix_notes_user_folder_deleted",
            table: "notes",
            columns: ["user_id", "folder_id", "deleted_at"]);

        migrationBuilder.CreateIndex(
            name: "ix_notes_user_updated",
            table: "notes",
            columns: ["user_id", "updated_at"]);

        migrationBuilder.CreateIndex(
            name: "ix_note_tags_user_tag",
            table: "note_tags",
            columns: ["user_id", "tag"]);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropTable("note_tags");
        migrationBuilder.DropTable("notes");
        migrationBuilder.DropTable("note_folders");
    }
}

