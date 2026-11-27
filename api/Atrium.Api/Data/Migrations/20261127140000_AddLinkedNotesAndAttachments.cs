using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Atrium.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261127140000_AddLinkedNotesAndAttachments")]
public sealed class AddLinkedNotesAndAttachments : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.AddColumn<Guid>(
            name: "university_course_id",
            table: "notes",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "university_activity_item_id",
            table: "notes",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "calendar_event_id",
            table: "notes",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "calendar_occurrence_start_at",
            table: "notes",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "attachments",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                kind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                content_type = table.Column<string>(type: "character varying(127)", maxLength: 127, nullable: false),
                size_bytes = table.Column<long>(type: "bigint", nullable: false),
                sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                storage_key = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                note_id = table.Column<Guid>(type: "uuid", nullable: true),
                university_course_id = table.Column<Guid>(type: "uuid", nullable: true),
                university_activity_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                calendar_event_id = table.Column<Guid>(type: "uuid", nullable: true),
                calendar_occurrence_start_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                allow_agent_read = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_attachments", x => x.id);
                table.ForeignKey(
                    name: "FK_attachments_calendar_events_calendar_event_id",
                    column: x => x.calendar_event_id,
                    principalTable: "calendar_events",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_attachments_notes_note_id",
                    column: x => x.note_id,
                    principalTable: "notes",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_attachments_university_activity_items_university_activity_item_id",
                    column: x => x.university_activity_item_id,
                    principalTable: "university_activity_items",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_attachments_university_courses_university_course_id",
                    column: x => x.university_course_id,
                    principalTable: "university_courses",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_attachments_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_notes_user_uni_course",
            table: "notes",
            columns: ["user_id", "university_course_id", "deleted_at"]);

        migrationBuilder.CreateIndex(
            name: "ix_notes_user_calendar_event",
            table: "notes",
            columns: ["user_id", "calendar_event_id", "deleted_at"]);

        migrationBuilder.CreateIndex(
            name: "ix_notes_uni_item_unique",
            table: "notes",
            column: "university_activity_item_id",
            unique: true,
            filter: "\"university_activity_item_id\" IS NOT NULL AND \"deleted_at\" IS NULL");

        migrationBuilder.CreateIndex(
            name: "ix_notes_event_occurrence_unique",
            table: "notes",
            columns: ["calendar_event_id", "calendar_occurrence_start_at"],
            unique: true,
            filter: "\"calendar_event_id\" IS NOT NULL AND \"calendar_occurrence_start_at\" IS NOT NULL AND \"deleted_at\" IS NULL");

        migrationBuilder.CreateIndex(
            name: "ix_notes_event_shared_unique",
            table: "notes",
            column: "calendar_event_id",
            unique: true,
            filter: "\"calendar_event_id\" IS NOT NULL AND \"calendar_occurrence_start_at\" IS NULL AND \"deleted_at\" IS NULL");

        migrationBuilder.CreateIndex(
            name: "ix_attachments_user_note",
            table: "attachments",
            columns: ["user_id", "note_id", "deleted_at"]);

        migrationBuilder.CreateIndex(
            name: "ix_attachments_user_uni_course",
            table: "attachments",
            columns: ["user_id", "university_course_id", "deleted_at"]);

        migrationBuilder.CreateIndex(
            name: "ix_attachments_user_uni_item",
            table: "attachments",
            columns: ["user_id", "university_activity_item_id", "deleted_at"]);

        migrationBuilder.CreateIndex(
            name: "ix_attachments_user_calendar_event",
            table: "attachments",
            columns: ["user_id", "calendar_event_id", "deleted_at"]);

        migrationBuilder.CreateIndex(
            name: "IX_notes_university_course_id",
            table: "notes",
            column: "university_course_id");

        migrationBuilder.CreateIndex(
            name: "IX_attachments_calendar_event_id",
            table: "attachments",
            column: "calendar_event_id");

        migrationBuilder.CreateIndex(
            name: "IX_attachments_note_id",
            table: "attachments",
            column: "note_id");

        migrationBuilder.CreateIndex(
            name: "IX_attachments_university_activity_item_id",
            table: "attachments",
            column: "university_activity_item_id");

        migrationBuilder.CreateIndex(
            name: "IX_attachments_university_course_id",
            table: "attachments",
            column: "university_course_id");

        migrationBuilder.AddForeignKey(
            name: "FK_notes_calendar_events_calendar_event_id",
            table: "notes",
            column: "calendar_event_id",
            principalTable: "calendar_events",
            principalColumn: "id",
            onDelete: ReferentialAction.SetNull);

        migrationBuilder.AddForeignKey(
            name: "FK_notes_university_activity_items_university_activity_item_id",
            table: "notes",
            column: "university_activity_item_id",
            principalTable: "university_activity_items",
            principalColumn: "id",
            onDelete: ReferentialAction.SetNull);

        migrationBuilder.AddForeignKey(
            name: "FK_notes_university_courses_university_course_id",
            table: "notes",
            column: "university_course_id",
            principalTable: "university_courses",
            principalColumn: "id",
            onDelete: ReferentialAction.SetNull);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropForeignKey(
            name: "FK_notes_calendar_events_calendar_event_id",
            table: "notes");

        migrationBuilder.DropForeignKey(
            name: "FK_notes_university_activity_items_university_activity_item_id",
            table: "notes");

        migrationBuilder.DropForeignKey(
            name: "FK_notes_university_courses_university_course_id",
            table: "notes");

        migrationBuilder.DropTable(
            name: "attachments");

        migrationBuilder.DropIndex(
            name: "ix_notes_user_uni_course",
            table: "notes");

        migrationBuilder.DropIndex(
            name: "ix_notes_user_calendar_event",
            table: "notes");

        migrationBuilder.DropIndex(
            name: "ix_notes_uni_item_unique",
            table: "notes");

        migrationBuilder.DropIndex(
            name: "ix_notes_event_occurrence_unique",
            table: "notes");

        migrationBuilder.DropIndex(
            name: "ix_notes_event_shared_unique",
            table: "notes");

        migrationBuilder.DropIndex(
            name: "IX_notes_university_course_id",
            table: "notes");

        migrationBuilder.DropColumn(
            name: "university_course_id",
            table: "notes");

        migrationBuilder.DropColumn(
            name: "university_activity_item_id",
            table: "notes");

        migrationBuilder.DropColumn(
            name: "calendar_event_id",
            table: "notes");

        migrationBuilder.DropColumn(
            name: "calendar_occurrence_start_at",
            table: "notes");
    }
}

