using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Atrium.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261126180000_AddUniversityModule")]
public sealed class AddUniversityModule : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.AddColumn<string>(
            name: "module",
            table: "calendars",
            type: "character varying(32)",
            maxLength: 32,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "ix_calendars_user_module",
            table: "calendars",
            columns: ["user_id", "module"],
            unique: true,
            filter: "\"module\" IS NOT NULL");

        migrationBuilder.CreateTable(
            name: "university_courses",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                semester = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                description = table.Column<string>(type: "text", nullable: true),
                instructor = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                credits = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                color = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "active"),
                metadata_json = table.Column<string>(type: "text", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_university_courses", x => x.id);
                table.ForeignKey(
                    name: "FK_university_courses_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "university_course_activities",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                course_id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                activity_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                description = table.Column<string>(type: "text", nullable: true),
                default_location = table.Column<string>(type: "character varying(280)", maxLength: 280, nullable: true),
                min_attendance_count = table.Column<int>(type: "integer", nullable: true),
                min_attendance_percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                planned_total_count = table.Column<int>(type: "integer", nullable: true),
                sort_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                metadata_json = table.Column<string>(type: "text", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_university_course_activities", x => x.id);
                table.ForeignKey(
                    name: "FK_university_course_activities_university_courses_course_id",
                    column: x => x.course_id,
                    principalTable: "university_courses",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_university_course_activities_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "university_activity_items",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                activity_id = table.Column<Guid>(type: "uuid", nullable: false),
                course_id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                calendar_event_id = table.Column<Guid>(type: "uuid", nullable: true),
                title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                theme = table.Column<string>(type: "text", nullable: true),
                notes = table.Column<string>(type: "text", nullable: true),
                sequence_number = table.Column<int>(type: "integer", nullable: true),
                attendance_status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "pending"),
                metadata_json = table.Column<string>(type: "text", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_university_activity_items", x => x.id);
                table.ForeignKey(
                    name: "FK_university_activity_items_calendar_events_calendar_event_id",
                    column: x => x.calendar_event_id,
                    principalTable: "calendar_events",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_university_activity_items_university_course_activities_acti~",
                    column: x => x.activity_id,
                    principalTable: "university_course_activities",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_university_activity_items_university_courses_course_id",
                    column: x => x.course_id,
                    principalTable: "university_courses",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_university_activity_items_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex("IX_university_courses_user_id_semester", "university_courses", ["user_id", "semester"]);
        migrationBuilder.CreateIndex("IX_university_courses_user_id_status", "university_courses", ["user_id", "status"]);
        migrationBuilder.CreateIndex("IX_university_course_activities_course_id_sort_order", "university_course_activities", ["course_id", "sort_order"]);
        migrationBuilder.CreateIndex("IX_university_course_activities_user_id", "university_course_activities", "user_id");
        migrationBuilder.CreateIndex("IX_university_activity_items_activity_id_sequence_number", "university_activity_items", ["activity_id", "sequence_number"]);
        migrationBuilder.CreateIndex("IX_university_activity_items_course_id", "university_activity_items", "course_id");
        migrationBuilder.CreateIndex("IX_university_activity_items_user_id", "university_activity_items", "user_id");
        migrationBuilder.CreateIndex(
            name: "ix_university_activity_items_calendar_event",
            table: "university_activity_items",
            column: "calendar_event_id",
            unique: true,
            filter: "\"calendar_event_id\" IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropTable("university_activity_items");
        migrationBuilder.DropTable("university_course_activities");
        migrationBuilder.DropTable("university_courses");

        migrationBuilder.DropIndex(
            name: "ix_calendars_user_module",
            table: "calendars");

        migrationBuilder.DropColumn(
            name: "module",
            table: "calendars");
    }
}
