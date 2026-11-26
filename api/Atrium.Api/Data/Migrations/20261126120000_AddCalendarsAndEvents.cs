using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Atrium.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261126120000_AddCalendarsAndEvents")]
public sealed class AddCalendarsAndEvents : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.CreateTable(
            name: "calendars",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                description = table.Column<string>(type: "character varying(280)", maxLength: 280, nullable: false),
                color = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                timezone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                is_default = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                is_visible = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_calendars", x => x.id);
                table.ForeignKey(
                    name: "FK_calendars_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "calendar_events",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                calendar_id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                description = table.Column<string>(type: "text", nullable: true),
                location = table.Column<string>(type: "character varying(280)", maxLength: 280, nullable: true),
                start_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                end_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                is_all_day = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                timezone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                external_uid = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                recurrence_rule = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                source = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_calendar_events", x => x.id);
                table.ForeignKey(
                    name: "FK_calendar_events_calendars_calendar_id",
                    column: x => x.calendar_id,
                    principalTable: "calendars",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_calendar_events_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex("IX_calendars_user_id", "calendars", "user_id");
        migrationBuilder.CreateIndex("IX_calendar_events_calendar_id_start_at", "calendar_events", ["calendar_id", "start_at"]);
        migrationBuilder.CreateIndex("IX_calendar_events_user_id_start_at_end_at", "calendar_events", ["user_id", "start_at", "end_at"]);
        migrationBuilder.CreateIndex(
            name: "ix_calendar_events_calendar_external_uid",
            table: "calendar_events",
            columns: ["calendar_id", "external_uid"],
            unique: true,
            filter: "\"external_uid\" IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropTable("calendar_events");
        migrationBuilder.DropTable("calendars");
    }
}
