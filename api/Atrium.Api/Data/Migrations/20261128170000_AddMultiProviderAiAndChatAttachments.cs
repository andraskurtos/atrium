using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Atrium.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261128170000_AddMultiProviderAiAndChatAttachments")]
public sealed class AddMultiProviderAiAndChatAttachments : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.CreateTable(
            name: "ai_providers",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                base_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                api_key_encrypted = table.Column<string>(type: "text", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ai_providers", x => x.id);
                table.ForeignKey(
                    name: "FK_ai_providers_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ai_models",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                provider_id = table.Column<Guid>(type: "uuid", nullable: false),
                model_id = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                display_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                supports_vision = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                sort_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ai_models", x => x.id);
                table.ForeignKey(
                    name: "FK_ai_models_ai_providers_provider_id",
                    column: x => x.provider_id,
                    principalTable: "ai_providers",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_ai_models_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "chat_attachments",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                thread_id = table.Column<Guid>(type: "uuid", nullable: true),
                message_id = table.Column<Guid>(type: "uuid", nullable: true),
                file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                content_type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                kind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                size_bytes = table.Column<long>(type: "bigint", nullable: false),
                storage_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                extracted_text = table.Column<string>(type: "text", nullable: true),
                metadata_json = table.Column<string>(type: "text", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_chat_attachments", x => x.id);
                table.ForeignKey(
                    name: "FK_chat_attachments_chat_messages_message_id",
                    column: x => x.message_id,
                    principalTable: "chat_messages",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_chat_attachments_chat_threads_thread_id",
                    column: x => x.thread_id,
                    principalTable: "chat_threads",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_chat_attachments_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.AddColumn<Guid>(
            name: "default_ai_model_id",
            table: "user_preferences",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "default_vision_model_id",
            table: "user_preferences",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "web_search_provider",
            table: "user_preferences",
            type: "character varying(32)",
            maxLength: 32,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "web_search_url",
            table: "user_preferences",
            type: "character varying(500)",
            maxLength: 500,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "web_search_api_key_encrypted",
            table: "user_preferences",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "last_model_id",
            table: "chat_threads",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "ix_ai_providers_user",
            table: "ai_providers",
            column: "user_id");

        migrationBuilder.CreateIndex(
            name: "ix_ai_models_user_provider",
            table: "ai_models",
            columns: ["user_id", "provider_id"]);

        migrationBuilder.CreateIndex(
            name: "IX_ai_models_provider_id",
            table: "ai_models",
            column: "provider_id");

        migrationBuilder.CreateIndex(
            name: "ix_chat_attachments_user_thread",
            table: "chat_attachments",
            columns: ["user_id", "thread_id"]);

        migrationBuilder.CreateIndex(
            name: "ix_chat_attachments_message",
            table: "chat_attachments",
            column: "message_id");

        migrationBuilder.CreateIndex(
            name: "IX_chat_attachments_thread_id",
            table: "chat_attachments",
            column: "thread_id");

        migrationBuilder.CreateIndex(
            name: "IX_chat_threads_last_model_id",
            table: "chat_threads",
            column: "last_model_id");

        migrationBuilder.CreateIndex(
            name: "IX_user_preferences_default_ai_model_id",
            table: "user_preferences",
            column: "default_ai_model_id");

        migrationBuilder.CreateIndex(
            name: "IX_user_preferences_default_vision_model_id",
            table: "user_preferences",
            column: "default_vision_model_id");

        migrationBuilder.AddForeignKey(
            name: "FK_chat_threads_ai_models_last_model_id",
            table: "chat_threads",
            column: "last_model_id",
            principalTable: "ai_models",
            principalColumn: "id",
            onDelete: ReferentialAction.SetNull);

        migrationBuilder.AddForeignKey(
            name: "FK_user_preferences_ai_models_default_ai_model_id",
            table: "user_preferences",
            column: "default_ai_model_id",
            principalTable: "ai_models",
            principalColumn: "id",
            onDelete: ReferentialAction.SetNull);

        migrationBuilder.AddForeignKey(
            name: "FK_user_preferences_ai_models_default_vision_model_id",
            table: "user_preferences",
            column: "default_vision_model_id",
            principalTable: "ai_models",
            principalColumn: "id",
            onDelete: ReferentialAction.SetNull);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropForeignKey(name: "FK_chat_threads_ai_models_last_model_id", table: "chat_threads");
        migrationBuilder.DropForeignKey(name: "FK_user_preferences_ai_models_default_ai_model_id", table: "user_preferences");
        migrationBuilder.DropForeignKey(name: "FK_user_preferences_ai_models_default_vision_model_id", table: "user_preferences");

        migrationBuilder.DropIndex(name: "IX_chat_threads_last_model_id", table: "chat_threads");
        migrationBuilder.DropIndex(name: "IX_user_preferences_default_ai_model_id", table: "user_preferences");
        migrationBuilder.DropIndex(name: "IX_user_preferences_default_vision_model_id", table: "user_preferences");

        migrationBuilder.DropColumn(name: "last_model_id", table: "chat_threads");
        migrationBuilder.DropColumn(name: "default_ai_model_id", table: "user_preferences");
        migrationBuilder.DropColumn(name: "default_vision_model_id", table: "user_preferences");
        migrationBuilder.DropColumn(name: "web_search_provider", table: "user_preferences");
        migrationBuilder.DropColumn(name: "web_search_url", table: "user_preferences");
        migrationBuilder.DropColumn(name: "web_search_api_key_encrypted", table: "user_preferences");

        migrationBuilder.DropTable(name: "chat_attachments");
        migrationBuilder.DropTable(name: "ai_models");
        migrationBuilder.DropTable(name: "ai_providers");
    }
}

