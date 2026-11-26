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
    public DbSet<UserCalendar> Calendars => Set<UserCalendar>();
    public DbSet<CalendarEvent> CalendarEvents => Set<CalendarEvent>();
    public DbSet<UniversityCourse> UniversityCourses => Set<UniversityCourse>();
    public DbSet<UniversityCourseActivity> UniversityCourseActivities => Set<UniversityCourseActivity>();
    public DbSet<UniversityActivityItem> UniversityActivityItems => Set<UniversityActivityItem>();
    public DbSet<DailyCheckItem> DailyCheckItems => Set<DailyCheckItem>();
    public DbSet<DailyNote> DailyNotes => Set<DailyNote>();

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

        modelBuilder.Entity<UserCalendar>(entity =>
        {
            entity.ToTable("calendars");
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Id).HasColumnName("id");
            entity.Property(c => c.UserId).HasColumnName("user_id");
            entity.Property(c => c.Name).HasColumnName("name").HasMaxLength(80).IsRequired();
            entity.Property(c => c.Description).HasColumnName("description").HasMaxLength(280).IsRequired();
            entity.Property(c => c.Color).HasColumnName("color").HasMaxLength(16).IsRequired();
            entity.Property(c => c.Timezone).HasColumnName("timezone").HasMaxLength(64).IsRequired();
            entity.Property(c => c.IsDefault).HasColumnName("is_default").HasDefaultValue(false);
            entity.Property(c => c.IsVisible).HasColumnName("is_visible").HasDefaultValue(true);
            entity.Property(c => c.Module).HasColumnName("module").HasMaxLength(32);
            entity.Property(c => c.CreatedAt).HasColumnName("created_at");
            entity.Property(c => c.UpdatedAt).HasColumnName("updated_at");

            entity.HasIndex(c => c.UserId);
            entity.HasIndex(c => new { c.UserId, c.Module })
                .IsUnique()
                .HasDatabaseName("ix_calendars_user_module")
                .HasFilter("\"module\" IS NOT NULL");

            entity.HasOne(c => c.User)
                .WithMany(u => u.Calendars)
                .HasForeignKey(c => c.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CalendarEvent>(entity =>
        {
            entity.ToTable("calendar_events");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CalendarId).HasColumnName("calendar_id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.Location).HasColumnName("location").HasMaxLength(280);
            entity.Property(e => e.StartAt).HasColumnName("start_at");
            entity.Property(e => e.EndAt).HasColumnName("end_at");
            entity.Property(e => e.IsAllDay).HasColumnName("is_all_day").HasDefaultValue(false);
            entity.Property(e => e.Timezone).HasColumnName("timezone").HasMaxLength(64);
            entity.Property(e => e.Status).HasColumnName("status").HasMaxLength(16).IsRequired();
            entity.Property(e => e.ExternalUid).HasColumnName("external_uid").HasMaxLength(255);
            entity.Property(e => e.RecurrenceRule).HasColumnName("recurrence_rule").HasMaxLength(255);
            entity.Property(e => e.RecurrenceMasterId).HasColumnName("recurrence_master_id");
            entity.Property(e => e.RecurrenceOriginalStartAt).HasColumnName("recurrence_original_start_at");
            entity.Property(e => e.IsRecurrenceExceptionDeleted).HasColumnName("is_recurrence_exception_deleted").HasDefaultValue(false);
            entity.Property(e => e.Source).HasColumnName("source").HasMaxLength(80);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

            entity.HasIndex(e => new { e.UserId, e.StartAt, e.EndAt });
            entity.HasIndex(e => new { e.CalendarId, e.StartAt });
            entity.HasIndex(e => new { e.CalendarId, e.ExternalUid })
                .IsUnique()
                .HasDatabaseName("ix_calendar_events_calendar_external_uid")
                .HasFilter("\"external_uid\" IS NOT NULL");
            entity.HasIndex(e => new { e.RecurrenceMasterId, e.RecurrenceOriginalStartAt })
                .IsUnique()
                .HasDatabaseName("ix_calendar_events_master_original_start")
                .HasFilter("\"recurrence_master_id\" IS NOT NULL");

            entity.HasOne(e => e.Calendar)
                .WithMany(c => c.Events)
                .HasForeignKey(e => e.CalendarId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.RecurrenceMaster)
                .WithMany(m => m.RecurrenceOverrides)
                .HasForeignKey(e => e.RecurrenceMasterId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UniversityCourse>(entity =>
        {
            entity.ToTable("university_courses");
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Id).HasColumnName("id");
            entity.Property(c => c.UserId).HasColumnName("user_id");
            entity.Property(c => c.Name).HasColumnName("name").HasMaxLength(140).IsRequired();
            entity.Property(c => c.Code).HasColumnName("code").HasMaxLength(32);
            entity.Property(c => c.Semester).HasColumnName("semester").HasMaxLength(64);
            entity.Property(c => c.Description).HasColumnName("description");
            entity.Property(c => c.Instructor).HasColumnName("instructor").HasMaxLength(120);
            entity.Property(c => c.Credits).HasColumnName("credits").HasPrecision(5, 2);
            entity.Property(c => c.Color).HasColumnName("color").HasMaxLength(16).IsRequired();
            entity.Property(c => c.Status).HasColumnName("status").HasMaxLength(16).HasDefaultValue("active").IsRequired();
            entity.Property(c => c.MetadataJson).HasColumnName("metadata_json");
            entity.Property(c => c.CreatedAt).HasColumnName("created_at");
            entity.Property(c => c.UpdatedAt).HasColumnName("updated_at");

            entity.HasIndex(c => new { c.UserId, c.Semester });
            entity.HasIndex(c => new { c.UserId, c.Status });

            entity.HasOne(c => c.User)
                .WithMany(u => u.UniversityCourses)
                .HasForeignKey(c => c.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UniversityCourseActivity>(entity =>
        {
            entity.ToTable("university_course_activities");
            entity.HasKey(a => a.Id);
            entity.Property(a => a.Id).HasColumnName("id");
            entity.Property(a => a.CourseId).HasColumnName("course_id");
            entity.Property(a => a.UserId).HasColumnName("user_id");
            entity.Property(a => a.ActivityType).HasColumnName("activity_type").HasMaxLength(32).IsRequired();
            entity.Property(a => a.Title).HasColumnName("title").HasMaxLength(120);
            entity.Property(a => a.Description).HasColumnName("description");
            entity.Property(a => a.DefaultLocation).HasColumnName("default_location").HasMaxLength(280);
            entity.Property(a => a.MinAttendanceCount).HasColumnName("min_attendance_count");
            entity.Property(a => a.MinAttendancePercent).HasColumnName("min_attendance_percent").HasPrecision(5, 2);
            entity.Property(a => a.PlannedTotalCount).HasColumnName("planned_total_count");
            entity.Property(a => a.SortOrder).HasColumnName("sort_order").HasDefaultValue(0);
            entity.Property(a => a.MetadataJson).HasColumnName("metadata_json");
            entity.Property(a => a.CreatedAt).HasColumnName("created_at");
            entity.Property(a => a.UpdatedAt).HasColumnName("updated_at");

            entity.HasIndex(a => new { a.CourseId, a.SortOrder });
            entity.HasIndex(a => a.UserId);

            entity.HasOne(a => a.Course)
                .WithMany(c => c.Activities)
                .HasForeignKey(a => a.CourseId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(a => a.User)
                .WithMany()
                .HasForeignKey(a => a.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UniversityActivityItem>(entity =>
        {
            entity.ToTable("university_activity_items");
            entity.HasKey(i => i.Id);
            entity.Property(i => i.Id).HasColumnName("id");
            entity.Property(i => i.ActivityId).HasColumnName("activity_id");
            entity.Property(i => i.CourseId).HasColumnName("course_id");
            entity.Property(i => i.UserId).HasColumnName("user_id");
            entity.Property(i => i.CalendarEventId).HasColumnName("calendar_event_id");
            entity.Property(i => i.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
            entity.Property(i => i.Theme).HasColumnName("theme");
            entity.Property(i => i.Notes).HasColumnName("notes");
            entity.Property(i => i.SequenceNumber).HasColumnName("sequence_number");
            entity.Property(i => i.AttendanceStatus).HasColumnName("attendance_status").HasMaxLength(16).HasDefaultValue("pending").IsRequired();
            entity.Property(i => i.MetadataJson).HasColumnName("metadata_json");
            entity.Property(i => i.CreatedAt).HasColumnName("created_at");
            entity.Property(i => i.UpdatedAt).HasColumnName("updated_at");

            entity.HasIndex(i => new { i.ActivityId, i.SequenceNumber });
            entity.HasIndex(i => i.CourseId);
            entity.HasIndex(i => i.UserId);
            entity.HasIndex(i => i.CalendarEventId)
                .IsUnique()
                .HasDatabaseName("ix_university_activity_items_calendar_event")
                .HasFilter("\"calendar_event_id\" IS NOT NULL");

            entity.HasOne(i => i.Activity)
                .WithMany(a => a.Items)
                .HasForeignKey(i => i.ActivityId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(i => i.Course)
                .WithMany()
                .HasForeignKey(i => i.CourseId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(i => i.User)
                .WithMany()
                .HasForeignKey(i => i.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(i => i.CalendarEvent)
                .WithMany()
                .HasForeignKey(i => i.CalendarEventId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<DailyCheckItem>(entity =>
        {
            entity.ToTable("daily_check_items");
            entity.HasKey(i => i.Id);
            entity.Property(i => i.Id).HasColumnName("id");
            entity.Property(i => i.UserId).HasColumnName("user_id");
            entity.Property(i => i.Date).HasColumnName("date").HasColumnType("date");
            entity.Property(i => i.Title).HasColumnName("title").HasMaxLength(280).IsRequired();
            entity.Property(i => i.IsCompleted).HasColumnName("is_completed").HasDefaultValue(false);
            entity.Property(i => i.CompletedAt).HasColumnName("completed_at");
            entity.Property(i => i.DueTime).HasColumnName("due_time").HasMaxLength(5);
            entity.Property(i => i.SortOrder).HasColumnName("sort_order").HasDefaultValue(0);
            entity.Property(i => i.CreatedAt).HasColumnName("created_at");
            entity.Property(i => i.UpdatedAt).HasColumnName("updated_at");

            entity.HasIndex(i => new { i.UserId, i.Date, i.SortOrder, i.CreatedAt })
                .HasDatabaseName("ix_daily_check_items_user_date_sort");

            entity.HasOne(i => i.User)
                .WithMany()
                .HasForeignKey(i => i.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DailyNote>(entity =>
        {
            entity.ToTable("daily_notes");
            entity.HasKey(n => n.Id);
            entity.Property(n => n.Id).HasColumnName("id");
            entity.Property(n => n.UserId).HasColumnName("user_id");
            entity.Property(n => n.Date).HasColumnName("date").HasColumnType("date");
            entity.Property(n => n.Content).HasColumnName("content").HasMaxLength(10000).IsRequired();
            entity.Property(n => n.CreatedAt).HasColumnName("created_at");
            entity.Property(n => n.UpdatedAt).HasColumnName("updated_at");

            entity.HasIndex(n => new { n.UserId, n.Date })
                .IsUnique()
                .HasDatabaseName("ix_daily_notes_user_date");

            entity.HasOne(n => n.User)
                .WithMany()
                .HasForeignKey(n => n.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        if (string.Equals(Database.ProviderName, "Microsoft.EntityFrameworkCore.Sqlite", StringComparison.Ordinal))
        {
            modelBuilder.Entity<CalendarEvent>(entity =>
            {
                entity.Property(e => e.StartAt)
                    .HasConversion(
                        v => v.UtcTicks,
                        ticks => new DateTimeOffset(ticks, TimeSpan.Zero));
                entity.Property(e => e.EndAt)
                    .HasConversion(
                        v => v.UtcTicks,
                        ticks => new DateTimeOffset(ticks, TimeSpan.Zero));
                entity.Property(e => e.RecurrenceOriginalStartAt)
                    .HasConversion(
                        v => v.HasValue ? v.Value.UtcTicks : (long?)null,
                        ticks => ticks.HasValue ? new DateTimeOffset(ticks.Value, TimeSpan.Zero) : (DateTimeOffset?)null);
            });
        }
    }
}
