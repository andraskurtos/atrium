using Atrium.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Atrium.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<UserPreferences> UserPreferences => Set<UserPreferences>();
    public DbSet<RefreshSession> RefreshSessions => Set<RefreshSession>();
    public DbSet<Invite> Invites => Set<Invite>();
    public DbSet<AgentToken> AgentTokens => Set<AgentToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(u => u.Id);
            entity.Property(u => u.Id).HasColumnName("id");
            entity.Property(u => u.Username).HasColumnName("username").HasMaxLength(32).IsRequired();
            entity.Property(u => u.Email).HasColumnName("email").HasMaxLength(254).IsRequired();
            entity.Property(u => u.DisplayName).HasColumnName("display_name").HasMaxLength(50).IsRequired();
            entity.Property(u => u.Bio).HasColumnName("bio").HasMaxLength(120).IsRequired();
            entity.Property(u => u.Timezone).HasColumnName("timezone").HasMaxLength(64).IsRequired();
            entity.Property(u => u.AvatarDataUrl).HasColumnName("avatar_data_url");
            entity.Property(u => u.Role).HasColumnName("role").HasMaxLength(16).IsRequired();
            entity.Property(u => u.IsActive).HasColumnName("is_active").HasDefaultValue(true);
            entity.Property(u => u.PasswordHash).HasColumnName("password_hash").IsRequired();
            entity.Property(u => u.PasswordUpdatedAt).HasColumnName("password_updated_at");
            entity.Property(u => u.CreatedAt).HasColumnName("created_at");
            entity.Property(u => u.UpdatedAt).HasColumnName("updated_at");

            entity.HasIndex(u => u.Username).IsUnique();
            entity.HasIndex(u => u.Email).IsUnique();
            entity.HasIndex(u => u.Role)
                .IsUnique()
                .HasDatabaseName("ix_users_single_owner")
                .HasFilter("\"role\" = 'Owner'");
        });

        modelBuilder.Entity<UserPreferences>(entity =>
        {
            entity.ToTable("user_preferences");
            entity.HasKey(p => p.UserId);
            entity.Property(p => p.UserId).HasColumnName("user_id");
            entity.Property(p => p.Theme).HasColumnName("theme").HasMaxLength(16).IsRequired();
            entity.Property(p => p.WeekStartsOn).HasColumnName("week_starts_on").HasMaxLength(16).IsRequired();
            entity.Property(p => p.TimeFormat).HasColumnName("time_format").HasMaxLength(8).IsRequired();
            entity.Property(p => p.RequireAgentApproval).HasColumnName("require_agent_approval");
            entity.Property(p => p.MorningBrief).HasColumnName("morning_brief");
            entity.Property(p => p.EventReminders).HasColumnName("event_reminders");

            entity.HasOne(p => p.User)
                .WithOne(u => u.Preferences)
                .HasForeignKey<UserPreferences>(p => p.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RefreshSession>(entity =>
        {
            entity.ToTable("refresh_sessions");
            entity.HasKey(s => s.Id);
            entity.Property(s => s.Id).HasColumnName("id");
            entity.Property(s => s.UserId).HasColumnName("user_id");
            entity.Property(s => s.FamilyId).HasColumnName("family_id");
            entity.Property(s => s.FamilyCreatedAt).HasColumnName("family_created_at");
            entity.Property(s => s.TokenHash).HasColumnName("token_hash").HasMaxLength(64).IsRequired();
            entity.Property(s => s.DeviceLabel).HasColumnName("device_label").HasMaxLength(120).IsRequired();
            entity.Property(s => s.IpAddress).HasColumnName("ip_address").HasMaxLength(64).IsRequired();
            entity.Property(s => s.CreatedAt).HasColumnName("created_at");
            entity.Property(s => s.LastActiveAt).HasColumnName("last_active_at");
            entity.Property(s => s.ExpiresAt).HasColumnName("expires_at");
            entity.Property(s => s.RevokedAt).HasColumnName("revoked_at");
            entity.Property(s => s.ReplacedBySessionId).HasColumnName("replaced_by_session_id");

            entity.HasIndex(s => s.UserId);
            entity.HasIndex(s => s.FamilyId);
            entity.HasIndex(s => s.TokenHash).IsUnique();

            entity.HasOne(s => s.User)
                .WithMany(u => u.RefreshSessions)
                .HasForeignKey(s => s.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Invite>(entity =>
        {
            entity.ToTable("invites");
            entity.HasKey(i => i.Id);
            entity.Property(i => i.Id).HasColumnName("id");
            entity.Property(i => i.CreatedByUserId).HasColumnName("created_by_user_id");
            entity.Property(i => i.CodePrefix).HasColumnName("code_prefix").HasMaxLength(16).IsRequired();
            entity.Property(i => i.CodeHash).HasColumnName("code_hash").HasMaxLength(64).IsRequired();
            entity.Property(i => i.Label).HasColumnName("label").HasMaxLength(80);
            entity.Property(i => i.ExpiresAt).HasColumnName("expires_at");
            entity.Property(i => i.UsedAt).HasColumnName("used_at");
            entity.Property(i => i.UsedByUserId).HasColumnName("used_by_user_id");
            entity.Property(i => i.RevokedAt).HasColumnName("revoked_at");
            entity.Property(i => i.CreatedAt).HasColumnName("created_at");

            entity.HasIndex(i => i.CodeHash).IsUnique();

            entity.HasOne(i => i.CreatedByUser)
                .WithMany()
                .HasForeignKey(i => i.CreatedByUserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(i => i.UsedByUser)
                .WithMany()
                .HasForeignKey(i => i.UsedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<AgentToken>(entity =>
        {
            entity.ToTable("agent_tokens");
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Id).HasColumnName("id");
            entity.Property(t => t.UserId).HasColumnName("user_id");
            entity.Property(t => t.Name).HasColumnName("name").HasMaxLength(80).IsRequired();
            entity.Property(t => t.Prefix).HasColumnName("prefix").HasMaxLength(20).IsRequired();
            entity.Property(t => t.TokenHash).HasColumnName("token_hash").HasMaxLength(64).IsRequired();
            entity.Property(t => t.Scopes).HasColumnName("scopes").HasMaxLength(256).IsRequired();
            entity.Property(t => t.CreatedAt).HasColumnName("created_at");
            entity.Property(t => t.LastUsedAt).HasColumnName("last_used_at");
            entity.Property(t => t.RevokedAt).HasColumnName("revoked_at");

            entity.HasIndex(t => t.UserId);
            entity.HasIndex(t => t.TokenHash).IsUnique();

            entity.HasOne(t => t.User)
                .WithMany(u => u.AgentTokens)
                .HasForeignKey(t => t.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
