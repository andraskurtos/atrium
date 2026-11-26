using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Atrium.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261126150000_AddRecurrenceOverrides")]
public sealed class AddRecurrenceOverrides : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.AddColumn<bool>(
            name: "is_recurrence_exception_deleted",
            table: "calendar_events",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<Guid>(
            name: "recurrence_master_id",
            table: "calendar_events",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "recurrence_original_start_at",
            table: "calendar_events",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "ix_calendar_events_master_original_start",
            table: "calendar_events",
            columns: ["recurrence_master_id", "recurrence_original_start_at"],
            unique: true,
            filter: "\"recurrence_master_id\" IS NOT NULL");

        migrationBuilder.AddForeignKey(
            name: "FK_calendar_events_calendar_events_recurrence_master_id",
            table: "calendar_events",
            column: "recurrence_master_id",
            principalTable: "calendar_events",
            principalColumn: "id",
            onDelete: ReferentialAction.Cascade);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropForeignKey(
            name: "FK_calendar_events_calendar_events_recurrence_master_id",
            table: "calendar_events");

        migrationBuilder.DropIndex(
            name: "ix_calendar_events_master_original_start",
            table: "calendar_events");

        migrationBuilder.DropColumn(
            name: "is_recurrence_exception_deleted",
            table: "calendar_events");

        migrationBuilder.DropColumn(
            name: "recurrence_master_id",
            table: "calendar_events");

        migrationBuilder.DropColumn(
            name: "recurrence_original_start_at",
            table: "calendar_events");
    }
}
