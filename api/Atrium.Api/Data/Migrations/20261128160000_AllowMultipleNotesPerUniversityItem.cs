using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Atrium.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261128160000_AllowMultipleNotesPerUniversityItem")]
public sealed class AllowMultipleNotesPerUniversityItem : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropIndex(
            name: "ix_notes_uni_item_unique",
            table: "notes");

        migrationBuilder.CreateIndex(
            name: "ix_notes_user_uni_item",
            table: "notes",
            columns: ["user_id", "university_activity_item_id", "deleted_at"]);

        migrationBuilder.CreateIndex(
            name: "IX_notes_university_activity_item_id",
            table: "notes",
            column: "university_activity_item_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropIndex(
            name: "ix_notes_user_uni_item",
            table: "notes");

        migrationBuilder.DropIndex(
            name: "IX_notes_university_activity_item_id",
            table: "notes");

        migrationBuilder.CreateIndex(
            name: "ix_notes_uni_item_unique",
            table: "notes",
            column: "university_activity_item_id",
            unique: true,
            filter: "\"university_activity_item_id\" IS NOT NULL AND \"deleted_at\" IS NULL");
    }
}

