using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Atrium.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261125222000_InitialAuthAndAccounts")]
public sealed class InitialAuthAndAccounts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.CreateTable(
            name: "users",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                username = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                display_name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                bio = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                timezone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                avatar_data_url = table.Column<string>(type: "text", nullable: true),
                role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                password_hash = table.Column<string>(type: "text", nullable: false),
                password_updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_users", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "user_preferences",
            columns: table => new
            {
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                theme = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                week_starts_on = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                time_format = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                require_agent_approval = table.Column<bool>(type: "boolean", nullable: false),
                morning_brief = table.Column<bool>(type: "boolean", nullable: false),
                event_reminders = table.Column<bool>(type: "boolean", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_user_preferences", x => x.user_id);
                table.ForeignKey(
                    name: "FK_user_preferences_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "refresh_sessions",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                family_id = table.Column<Guid>(type: "uuid", nullable: false),
                family_created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                device_label = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                ip_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                last_active_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                replaced_by_session_id = table.Column<Guid>(type: "uuid", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_refresh_sessions", x => x.id);
                table.ForeignKey(
                    name: "FK_refresh_sessions_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "invites",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                code_prefix = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                code_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                label = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                used_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_invites", x => x.id);
                table.ForeignKey(
                    name: "FK_invites_users_created_by_user_id",
                    column: x => x.created_by_user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_invites_users_used_by_user_id",
                    column: x => x.used_by_user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateTable(
            name: "agent_tokens",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                prefix = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                scopes = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_agent_tokens", x => x.id);
                table.ForeignKey(
                    name: "FK_agent_tokens_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex("IX_users_username", "users", "username", unique: true);
        migrationBuilder.CreateIndex("IX_users_email", "users", "email", unique: true);
        migrationBuilder.CreateIndex("ix_users_single_owner", "users", "role", unique: true, filter: "\"role\" = 'Owner'");
        migrationBuilder.CreateIndex("IX_refresh_sessions_user_id", "refresh_sessions", "user_id");
        migrationBuilder.CreateIndex("IX_refresh_sessions_family_id", "refresh_sessions", "family_id");
        migrationBuilder.CreateIndex("IX_refresh_sessions_token_hash", "refresh_sessions", "token_hash", unique: true);
        migrationBuilder.CreateIndex("IX_invites_code_hash", "invites", "code_hash", unique: true);
        migrationBuilder.CreateIndex("IX_agent_tokens_user_id", "agent_tokens", "user_id");
        migrationBuilder.CreateIndex("IX_agent_tokens_token_hash", "agent_tokens", "token_hash", unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropTable("agent_tokens");
        migrationBuilder.DropTable("invites");
        migrationBuilder.DropTable("refresh_sessions");
        migrationBuilder.DropTable("user_preferences");
        migrationBuilder.DropTable("users");
    }
}
