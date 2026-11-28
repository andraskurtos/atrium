using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Atrium.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261128120000_AddArtifactTextChunks")]
public sealed class AddArtifactTextChunks : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.AddColumn<string>(
            name: "text_extraction_status",
            table: "attachments",
            type: "character varying(24)",
            maxLength: 24,
            nullable: false,
            defaultValue: "pending");

        migrationBuilder.AddColumn<int>(
            name: "extracted_page_count",
            table: "attachments",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "text_extracted_at",
            table: "attachments",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "attachment_text_chunks",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                attachment_id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                chunk_index = table.Column<int>(type: "integer", nullable: false),
                page_label = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                content = table.Column<string>(type: "text", nullable: false),
                char_count = table.Column<int>(type: "integer", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_attachment_text_chunks", x => x.id);
                table.ForeignKey(
                    name: "FK_attachment_text_chunks_attachments_attachment_id",
                    column: x => x.attachment_id,
                    principalTable: "attachments",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_attachment_text_chunks_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_attachment_text_chunks_attachment_chunk",
            table: "attachment_text_chunks",
            columns: ["attachment_id", "chunk_index"],
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_attachment_text_chunks_user_attachment",
            table: "attachment_text_chunks",
            columns: ["user_id", "attachment_id"]);

        if (string.Equals(ActiveProvider, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
        {
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS ix_attachment_text_chunks_fts ON attachment_text_chunks USING GIN (to_tsvector('english', content));");
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS ix_notes_fts ON notes USING GIN (to_tsvector('english', title || ' ' || content));");
        }
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        if (string.Equals(ActiveProvider, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_attachment_text_chunks_fts;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_notes_fts;");
        }

        migrationBuilder.DropTable(name: "attachment_text_chunks");

        migrationBuilder.DropColumn(name: "text_extraction_status", table: "attachments");
        migrationBuilder.DropColumn(name: "extracted_page_count", table: "attachments");
        migrationBuilder.DropColumn(name: "text_extracted_at", table: "attachments");
    }
}

