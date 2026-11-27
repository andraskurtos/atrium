using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Atrium.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261127160000_AddDailyNoteLink")]
public sealed class AddDailyNoteLink : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.AddColumn<DateOnly>(
            name: "daily_date",
            table: "notes",
            type: "date",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "ix_notes_daily_date_unique",
            table: "notes",
            columns: ["user_id", "daily_date"],
            unique: true,
            filter: "\"daily_date\" IS NOT NULL AND \"deleted_at\" IS NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropIndex(
            name: "ix_notes_daily_date_unique",
            table: "notes");

        migrationBuilder.DropColumn(
            name: "daily_date",
            table: "notes");
    }
}

