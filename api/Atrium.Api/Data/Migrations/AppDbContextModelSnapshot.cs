using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace Atrium.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
public sealed class AppDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder
            .HasAnnotation("ProductVersion", "10.0.0")
            .HasAnnotation("Relational:MaxIdentifierLength", 63);

        NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

        modelBuilder.Entity("Atrium.Api.Data.Entities.AgentToken", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("uuid").HasColumnName("id");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone").HasColumnName("created_at");
            b.Property<DateTimeOffset?>("LastUsedAt").HasColumnType("timestamp with time zone").HasColumnName("last_used_at");
            b.Property<string>("Name").IsRequired().HasMaxLength(80).HasColumnType("character varying(80)").HasColumnName("name");
            b.Property<string>("Prefix").IsRequired().HasMaxLength(20).HasColumnType("character varying(20)").HasColumnName("prefix");
            b.Property<DateTimeOffset?>("RevokedAt").HasColumnType("timestamp with time zone").HasColumnName("revoked_at");
            b.Property<string>("Scopes").IsRequired().HasMaxLength(256).HasColumnType("character varying(256)").HasColumnName("scopes");
            b.Property<string>("TokenHash").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)").HasColumnName("token_hash");
            b.Property<Guid>("UserId").HasColumnType("uuid").HasColumnName("user_id");

            b.HasKey("Id");
            b.HasIndex("TokenHash").IsUnique();
            b.HasIndex("UserId");
            b.ToTable("agent_tokens", (string?)null);
        });

        modelBuilder.Entity("Atrium.Api.Data.Entities.Invite", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("uuid").HasColumnName("id");
            b.Property<string>("CodeHash").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)").HasColumnName("code_hash");
            b.Property<string>("CodePrefix").IsRequired().HasMaxLength(16).HasColumnType("character varying(16)").HasColumnName("code_prefix");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone").HasColumnName("created_at");
            b.Property<Guid>("CreatedByUserId").HasColumnType("uuid").HasColumnName("created_by_user_id");
            b.Property<DateTimeOffset>("ExpiresAt").HasColumnType("timestamp with time zone").HasColumnName("expires_at");
            b.Property<string>("Label").HasMaxLength(80).HasColumnType("character varying(80)").HasColumnName("label");
            b.Property<DateTimeOffset?>("RevokedAt").HasColumnType("timestamp with time zone").HasColumnName("revoked_at");
            b.Property<DateTimeOffset?>("UsedAt").HasColumnType("timestamp with time zone").HasColumnName("used_at");
            b.Property<Guid?>("UsedByUserId").HasColumnType("uuid").HasColumnName("used_by_user_id");

            b.HasKey("Id");
            b.HasIndex("CodeHash").IsUnique();
            b.HasIndex("CreatedByUserId");
            b.HasIndex("UsedByUserId");
            b.ToTable("invites", (string?)null);
        });

        modelBuilder.Entity("Atrium.Api.Data.Entities.RefreshSession", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("uuid").HasColumnName("id");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone").HasColumnName("created_at");
            b.Property<string>("DeviceLabel").IsRequired().HasMaxLength(120).HasColumnType("character varying(120)").HasColumnName("device_label");
            b.Property<DateTimeOffset>("ExpiresAt").HasColumnType("timestamp with time zone").HasColumnName("expires_at");
            b.Property<DateTimeOffset>("FamilyCreatedAt").HasColumnType("timestamp with time zone").HasColumnName("family_created_at");
            b.Property<Guid>("FamilyId").HasColumnType("uuid").HasColumnName("family_id");
            b.Property<string>("IpAddress").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)").HasColumnName("ip_address");
            b.Property<DateTimeOffset>("LastActiveAt").HasColumnType("timestamp with time zone").HasColumnName("last_active_at");
            b.Property<Guid?>("ReplacedBySessionId").HasColumnType("uuid").HasColumnName("replaced_by_session_id");
            b.Property<DateTimeOffset?>("RevokedAt").HasColumnType("timestamp with time zone").HasColumnName("revoked_at");
            b.Property<string>("TokenHash").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)").HasColumnName("token_hash");
            b.Property<Guid>("UserId").HasColumnType("uuid").HasColumnName("user_id");

            b.HasKey("Id");
            b.HasIndex("FamilyId");
            b.HasIndex("TokenHash").IsUnique();
            b.HasIndex("UserId");
            b.ToTable("refresh_sessions", (string?)null);
        });

        modelBuilder.Entity("Atrium.Api.Data.Entities.User", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("uuid").HasColumnName("id");
            b.Property<string>("AvatarDataUrl").HasColumnType("text").HasColumnName("avatar_data_url");
            b.Property<string>("Bio").IsRequired().HasMaxLength(120).HasColumnType("character varying(120)").HasColumnName("bio");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone").HasColumnName("created_at");
            b.Property<string>("DisplayName").IsRequired().HasMaxLength(50).HasColumnType("character varying(50)").HasColumnName("display_name");
            b.Property<string>("Email").IsRequired().HasMaxLength(254).HasColumnType("character varying(254)").HasColumnName("email");
            b.Property<bool>("IsActive").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(true).HasColumnName("is_active");
            b.Property<string>("PasswordHash").IsRequired().HasColumnType("text").HasColumnName("password_hash");
            b.Property<DateTimeOffset>("PasswordUpdatedAt").HasColumnType("timestamp with time zone").HasColumnName("password_updated_at");
            b.Property<string>("Role").IsRequired().HasMaxLength(16).HasColumnType("character varying(16)").HasColumnName("role");
            b.Property<string>("Timezone").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)").HasColumnName("timezone");
            b.Property<DateTimeOffset>("UpdatedAt").HasColumnType("timestamp with time zone").HasColumnName("updated_at");
            b.Property<string>("Username").IsRequired().HasMaxLength(32).HasColumnType("character varying(32)").HasColumnName("username");

            b.HasKey("Id");
            b.HasIndex("Email").IsUnique();
            b.HasIndex("Role").IsUnique().HasDatabaseName("ix_users_single_owner").HasFilter("\"role\" = 'Owner'");
            b.HasIndex("Username").IsUnique();
            b.ToTable("users", (string?)null);
        });

        modelBuilder.Entity("Atrium.Api.Data.Entities.UserPreferences", b =>
        {
            b.Property<Guid>("UserId").HasColumnType("uuid").HasColumnName("user_id");
            b.Property<bool>("EventReminders").HasColumnType("boolean").HasColumnName("event_reminders");
            b.Property<bool>("MorningBrief").HasColumnType("boolean").HasColumnName("morning_brief");
            b.Property<bool>("RequireAgentApproval").HasColumnType("boolean").HasColumnName("require_agent_approval");
            b.Property<string>("Theme").IsRequired().HasMaxLength(16).HasColumnType("character varying(16)").HasColumnName("theme");
            b.Property<string>("TimeFormat").IsRequired().HasMaxLength(8).HasColumnType("character varying(8)").HasColumnName("time_format");
            b.Property<string>("WeekStartsOn").IsRequired().HasMaxLength(16).HasColumnType("character varying(16)").HasColumnName("week_starts_on");

            b.HasKey("UserId");
            b.ToTable("user_preferences", (string?)null);
        });

        modelBuilder.Entity("Atrium.Api.Data.Entities.AgentToken", b =>
        {
            b.HasOne("Atrium.Api.Data.Entities.User", "User")
                .WithMany("AgentTokens")
                .HasForeignKey("UserId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
            b.Navigation("User");
        });

        modelBuilder.Entity("Atrium.Api.Data.Entities.Invite", b =>
        {
            b.HasOne("Atrium.Api.Data.Entities.User", "CreatedByUser")
                .WithMany()
                .HasForeignKey("CreatedByUserId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
            b.HasOne("Atrium.Api.Data.Entities.User", "UsedByUser")
                .WithMany()
                .HasForeignKey("UsedByUserId")
                .OnDelete(DeleteBehavior.SetNull);
            b.Navigation("CreatedByUser");
            b.Navigation("UsedByUser");
        });

        modelBuilder.Entity("Atrium.Api.Data.Entities.RefreshSession", b =>
        {
            b.HasOne("Atrium.Api.Data.Entities.User", "User")
                .WithMany("RefreshSessions")
                .HasForeignKey("UserId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
            b.Navigation("User");
        });

        modelBuilder.Entity("Atrium.Api.Data.Entities.UserPreferences", b =>
        {
            b.HasOne("Atrium.Api.Data.Entities.User", "User")
                .WithOne("Preferences")
                .HasForeignKey("Atrium.Api.Data.Entities.UserPreferences", "UserId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
            b.Navigation("User");
        });

        modelBuilder.Entity("Atrium.Api.Data.Entities.User", b =>
        {
            b.Navigation("AgentTokens");
            b.Navigation("Preferences");
            b.Navigation("RefreshSessions");
        });
    }
}

