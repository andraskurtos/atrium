using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Atrium.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261128140000_AddUserAiSettings")]
public sealed class AddUserAiSettings : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.AddColumn<string>(
            name: "ai_inference_url",
            table: "user_preferences",
            type: "character varying(500)",
            maxLength: 500,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ai_model",
            table: "user_preferences",
            type: "character varying(120)",
            maxLength: 120,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ai_api_key_encrypted",
            table: "user_preferences",
            type: "text",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropColumn(name: "ai_inference_url", table: "user_preferences");
        migrationBuilder.DropColumn(name: "ai_model", table: "user_preferences");
        migrationBuilder.DropColumn(name: "ai_api_key_encrypted", table: "user_preferences");
    }
}

