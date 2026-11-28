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

        modelBuilder.Entity("Atrium.Api.Data.Entities.CalendarEvent", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("uuid").HasColumnName("id");
            b.Property<Guid>("CalendarId").HasColumnType("uuid").HasColumnName("calendar_id");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone").HasColumnName("created_at");
            b.Property<string>("Description").HasColumnType("text").HasColumnName("description");
            b.Property<DateTimeOffset>("EndAt").HasColumnType("timestamp with time zone").HasColumnName("end_at");
            b.Property<string>("ExternalUid").HasMaxLength(255).HasColumnType("character varying(255)").HasColumnName("external_uid");
            b.Property<bool>("IsAllDay").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(false).HasColumnName("is_all_day");
            b.Property<bool>("IsRecurrenceExceptionDeleted").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(false).HasColumnName("is_recurrence_exception_deleted");
            b.Property<string>("Location").HasMaxLength(280).HasColumnType("character varying(280)").HasColumnName("location");
            b.Property<Guid?>("RecurrenceMasterId").HasColumnType("uuid").HasColumnName("recurrence_master_id");
            b.Property<DateTimeOffset?>("RecurrenceOriginalStartAt").HasColumnType("timestamp with time zone").HasColumnName("recurrence_original_start_at");
            b.Property<string>("RecurrenceRule").HasMaxLength(255).HasColumnType("character varying(255)").HasColumnName("recurrence_rule");
            b.Property<string>("Source").HasMaxLength(80).HasColumnType("character varying(80)").HasColumnName("source");
            b.Property<DateTimeOffset>("StartAt").HasColumnType("timestamp with time zone").HasColumnName("start_at");
            b.Property<string>("Status").IsRequired().HasMaxLength(16).HasColumnType("character varying(16)").HasColumnName("status");
            b.Property<string>("Timezone").HasMaxLength(64).HasColumnType("character varying(64)").HasColumnName("timezone");
            b.Property<string>("Title").IsRequired().HasMaxLength(200).HasColumnType("character varying(200)").HasColumnName("title");
            b.Property<DateTimeOffset>("UpdatedAt").HasColumnType("timestamp with time zone").HasColumnName("updated_at");
            b.Property<Guid>("UserId").HasColumnType("uuid").HasColumnName("user_id");

            b.HasKey("Id");
            b.HasIndex("CalendarId", "ExternalUid").IsUnique().HasDatabaseName("ix_calendar_events_calendar_external_uid").HasFilter("\"external_uid\" IS NOT NULL");
            b.HasIndex("CalendarId", "StartAt");
            b.HasIndex("RecurrenceMasterId", "RecurrenceOriginalStartAt").IsUnique().HasDatabaseName("ix_calendar_events_master_original_start").HasFilter("\"recurrence_master_id\" IS NOT NULL");
            b.HasIndex("UserId", "StartAt", "EndAt");
            b.ToTable("calendar_events", (string?)null);
        });

        modelBuilder.Entity("Atrium.Api.Data.Entities.UniversityActivityItem", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("uuid").HasColumnName("id");
            b.Property<Guid>("ActivityId").HasColumnType("uuid").HasColumnName("activity_id");
            b.Property<string>("AttendanceStatus").IsRequired().ValueGeneratedOnAdd().HasMaxLength(16).HasColumnType("character varying(16)").HasDefaultValue("pending").HasColumnName("attendance_status");
            b.Property<Guid?>("CalendarEventId").HasColumnType("uuid").HasColumnName("calendar_event_id");
            b.Property<Guid>("CourseId").HasColumnType("uuid").HasColumnName("course_id");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone").HasColumnName("created_at");
            b.Property<string>("MetadataJson").HasColumnType("text").HasColumnName("metadata_json");
            b.Property<string>("Notes").HasColumnType("text").HasColumnName("notes");
            b.Property<int?>("SequenceNumber").HasColumnType("integer").HasColumnName("sequence_number");
            b.Property<string>("Theme").HasColumnType("text").HasColumnName("theme");
            b.Property<string>("Title").IsRequired().HasMaxLength(200).HasColumnType("character varying(200)").HasColumnName("title");
            b.Property<DateTimeOffset>("UpdatedAt").HasColumnType("timestamp with time zone").HasColumnName("updated_at");
            b.Property<Guid>("UserId").HasColumnType("uuid").HasColumnName("user_id");

            b.HasKey("Id");
            b.HasIndex("ActivityId", "SequenceNumber");
            b.HasIndex("CalendarEventId").IsUnique().HasDatabaseName("ix_university_activity_items_calendar_event").HasFilter("\"calendar_event_id\" IS NOT NULL");
            b.HasIndex("CourseId");
            b.HasIndex("UserId");
            b.ToTable("university_activity_items", (string?)null);
        });

        modelBuilder.Entity("Atrium.Api.Data.Entities.UniversityCourse", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("uuid").HasColumnName("id");
            b.Property<string>("Code").HasMaxLength(32).HasColumnType("character varying(32)").HasColumnName("code");
            b.Property<string>("Color").IsRequired().HasMaxLength(16).HasColumnType("character varying(16)").HasColumnName("color");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone").HasColumnName("created_at");
            b.Property<decimal?>("Credits").HasPrecision(5, 2).HasColumnType("numeric(5,2)").HasColumnName("credits");
            b.Property<string>("Description").HasColumnType("text").HasColumnName("description");
            b.Property<string>("Instructor").HasMaxLength(120).HasColumnType("character varying(120)").HasColumnName("instructor");
            b.Property<string>("MetadataJson").HasColumnType("text").HasColumnName("metadata_json");
            b.Property<string>("Name").IsRequired().HasMaxLength(140).HasColumnType("character varying(140)").HasColumnName("name");
            b.Property<string>("Semester").HasMaxLength(64).HasColumnType("character varying(64)").HasColumnName("semester");
            b.Property<string>("Status").IsRequired().ValueGeneratedOnAdd().HasMaxLength(16).HasColumnType("character varying(16)").HasDefaultValue("active").HasColumnName("status");
            b.Property<DateTimeOffset>("UpdatedAt").HasColumnType("timestamp with time zone").HasColumnName("updated_at");
            b.Property<Guid>("UserId").HasColumnType("uuid").HasColumnName("user_id");

            b.HasKey("Id");
            b.HasIndex("UserId", "Semester");
            b.HasIndex("UserId", "Status");
            b.ToTable("university_courses", (string?)null);
        });

        modelBuilder.Entity("Atrium.Api.Data.Entities.UniversityCourseActivity", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("uuid").HasColumnName("id");
            b.Property<string>("ActivityType").IsRequired().HasMaxLength(32).HasColumnType("character varying(32)").HasColumnName("activity_type");
            b.Property<Guid>("CourseId").HasColumnType("uuid").HasColumnName("course_id");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone").HasColumnName("created_at");
            b.Property<string>("DefaultLocation").HasMaxLength(280).HasColumnType("character varying(280)").HasColumnName("default_location");
            b.Property<string>("Description").HasColumnType("text").HasColumnName("description");
            b.Property<string>("MetadataJson").HasColumnType("text").HasColumnName("metadata_json");
            b.Property<int?>("MinAttendanceCount").HasColumnType("integer").HasColumnName("min_attendance_count");
            b.Property<decimal?>("MinAttendancePercent").HasPrecision(5, 2).HasColumnType("numeric(5,2)").HasColumnName("min_attendance_percent");
            b.Property<int?>("PlannedTotalCount").HasColumnType("integer").HasColumnName("planned_total_count");
            b.Property<int>("SortOrder").ValueGeneratedOnAdd().HasColumnType("integer").HasDefaultValue(0).HasColumnName("sort_order");
            b.Property<string>("Title").HasMaxLength(120).HasColumnType("character varying(120)").HasColumnName("title");
            b.Property<DateTimeOffset>("UpdatedAt").HasColumnType("timestamp with time zone").HasColumnName("updated_at");
            b.Property<Guid>("UserId").HasColumnType("uuid").HasColumnName("user_id");

            b.HasKey("Id");
            b.HasIndex("CourseId", "SortOrder");
            b.HasIndex("UserId");
            b.ToTable("university_course_activities", (string?)null);
        });

        modelBuilder.Entity("Atrium.Api.Data.Entities.UserCalendar", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("uuid").HasColumnName("id");
            b.Property<string>("Color").IsRequired().HasMaxLength(16).HasColumnType("character varying(16)").HasColumnName("color");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone").HasColumnName("created_at");
            b.Property<string>("Description").IsRequired().HasMaxLength(280).HasColumnType("character varying(280)").HasColumnName("description");
            b.Property<bool>("IsDefault").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(false).HasColumnName("is_default");
            b.Property<bool>("IsVisible").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(true).HasColumnName("is_visible");
            b.Property<string>("Module").HasMaxLength(32).HasColumnType("character varying(32)").HasColumnName("module");
            b.Property<string>("Name").IsRequired().HasMaxLength(80).HasColumnType("character varying(80)").HasColumnName("name");
            b.Property<string>("Timezone").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)").HasColumnName("timezone");
            b.Property<DateTimeOffset>("UpdatedAt").HasColumnType("timestamp with time zone").HasColumnName("updated_at");
            b.Property<Guid>("UserId").HasColumnType("uuid").HasColumnName("user_id");

            b.HasKey("Id");
            b.HasIndex("UserId");
            b.HasIndex("UserId", "Module").IsUnique().HasDatabaseName("ix_calendars_user_module").HasFilter("\"module\" IS NOT NULL");
            b.ToTable("calendars", (string?)null);
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

        modelBuilder.Entity("Atrium.Api.Data.Entities.DailyCheckItem", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("uuid").HasColumnName("id");
            b.Property<DateTimeOffset?>("CompletedAt").HasColumnType("timestamp with time zone").HasColumnName("completed_at");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone").HasColumnName("created_at");
            b.Property<DateOnly>("Date").HasColumnType("date").HasColumnName("date");
            b.Property<string>("DueTime").HasMaxLength(5).HasColumnType("character varying(5)").HasColumnName("due_time");
            b.Property<bool>("IsCompleted").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(false).HasColumnName("is_completed");
            b.Property<int>("SortOrder").ValueGeneratedOnAdd().HasColumnType("integer").HasDefaultValue(0).HasColumnName("sort_order");
            b.Property<string>("Title").IsRequired().HasMaxLength(280).HasColumnType("character varying(280)").HasColumnName("title");
            b.Property<DateTimeOffset>("UpdatedAt").HasColumnType("timestamp with time zone").HasColumnName("updated_at");
            b.Property<Guid>("UserId").HasColumnType("uuid").HasColumnName("user_id");

            b.HasKey("Id");
            b.HasIndex("UserId", "Date", "SortOrder", "CreatedAt").HasDatabaseName("ix_daily_check_items_user_date_sort");
            b.ToTable("daily_check_items", (string?)null);
        });

        modelBuilder.Entity("Atrium.Api.Data.Entities.DailyNote", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("uuid").HasColumnName("id");
            b.Property<string>("Content").IsRequired().HasMaxLength(10000).HasColumnType("character varying(10000)").HasColumnName("content");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone").HasColumnName("created_at");
            b.Property<DateOnly>("Date").HasColumnType("date").HasColumnName("date");
            b.Property<DateTimeOffset>("UpdatedAt").HasColumnType("timestamp with time zone").HasColumnName("updated_at");
            b.Property<Guid>("UserId").HasColumnType("uuid").HasColumnName("user_id");

            b.HasKey("Id");
            b.HasIndex("UserId", "Date").IsUnique().HasDatabaseName("ix_daily_notes_user_date");
            b.ToTable("daily_notes", (string?)null);
        });

        modelBuilder.Entity("Atrium.Api.Data.Entities.Attachment", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("uuid").HasColumnName("id");
            b.Property<bool>("AllowAgentRead").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(true).HasColumnName("allow_agent_read");
            b.Property<Guid?>("CalendarEventId").HasColumnType("uuid").HasColumnName("calendar_event_id");
            b.Property<DateTimeOffset?>("CalendarOccurrenceStartAt").HasColumnType("timestamp with time zone").HasColumnName("calendar_occurrence_start_at");
            b.Property<string>("ContentType").IsRequired().HasMaxLength(127).HasColumnType("character varying(127)").HasColumnName("content_type");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone").HasColumnName("created_at");
            b.Property<DateTimeOffset?>("DeletedAt").HasColumnType("timestamp with time zone").HasColumnName("deleted_at");
            b.Property<int?>("ExtractedPageCount").HasColumnType("integer").HasColumnName("extracted_page_count");
            b.Property<string>("FileName").IsRequired().HasMaxLength(255).HasColumnType("character varying(255)").HasColumnName("file_name");
            b.Property<string>("Kind").IsRequired().HasMaxLength(24).HasColumnType("character varying(24)").HasColumnName("kind");
            b.Property<Guid?>("NoteId").HasColumnType("uuid").HasColumnName("note_id");
            b.Property<string>("Sha256").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)").HasColumnName("sha256");
            b.Property<long>("SizeBytes").HasColumnType("bigint").HasColumnName("size_bytes");
            b.Property<string>("StorageKey").IsRequired().HasMaxLength(255).HasColumnType("character varying(255)").HasColumnName("storage_key");
            b.Property<DateTimeOffset?>("TextExtractedAt").HasColumnType("timestamp with time zone").HasColumnName("text_extracted_at");
            b.Property<string>("TextExtractionStatus").IsRequired().ValueGeneratedOnAdd().HasMaxLength(24).HasColumnType("character varying(24)").HasDefaultValue("pending").HasColumnName("text_extraction_status");
            b.Property<Guid?>("UniversityActivityItemId").HasColumnType("uuid").HasColumnName("university_activity_item_id");
            b.Property<Guid?>("UniversityCourseId").HasColumnType("uuid").HasColumnName("university_course_id");
            b.Property<Guid>("UserId").HasColumnType("uuid").HasColumnName("user_id");

            b.HasKey("Id");
            b.HasIndex("CalendarEventId");
            b.HasIndex("NoteId");
            b.HasIndex("UniversityActivityItemId");
            b.HasIndex("UniversityCourseId");
            b.HasIndex("UserId", "CalendarEventId", "DeletedAt").HasDatabaseName("ix_attachments_user_calendar_event");
            b.HasIndex("UserId", "NoteId", "DeletedAt").HasDatabaseName("ix_attachments_user_note");
            b.HasIndex("UserId", "UniversityActivityItemId", "DeletedAt").HasDatabaseName("ix_attachments_user_uni_item");
            b.HasIndex("UserId", "UniversityCourseId", "DeletedAt").HasDatabaseName("ix_attachments_user_uni_course");
            b.ToTable("attachments", (string?)null);
        });

        modelBuilder.Entity("Atrium.Api.Data.Entities.AttachmentTextChunk", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("uuid").HasColumnName("id");
            b.Property<Guid>("AttachmentId").HasColumnType("uuid").HasColumnName("attachment_id");
            b.Property<int>("CharCount").HasColumnType("integer").HasColumnName("char_count");
            b.Property<int>("ChunkIndex").HasColumnType("integer").HasColumnName("chunk_index");
            b.Property<string>("Content").IsRequired().HasColumnType("text").HasColumnName("content");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone").HasColumnName("created_at");
            b.Property<string>("PageLabel").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)").HasColumnName("page_label");
            b.Property<Guid>("UserId").HasColumnType("uuid").HasColumnName("user_id");

            b.HasKey("Id");
            b.HasIndex("AttachmentId", "ChunkIndex").IsUnique().HasDatabaseName("ix_attachment_text_chunks_attachment_chunk");
            b.HasIndex("UserId", "AttachmentId").HasDatabaseName("ix_attachment_text_chunks_user_attachment");
            b.ToTable("attachment_text_chunks", (string?)null);
        });

        modelBuilder.Entity("Atrium.Api.Data.Entities.Note", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("uuid").HasColumnName("id");
            b.Property<bool>("AllowAgentRead").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(true).HasColumnName("allow_agent_read");
            b.Property<Guid?>("CalendarEventId").HasColumnType("uuid").HasColumnName("calendar_event_id");
            b.Property<DateTimeOffset?>("CalendarOccurrenceStartAt").HasColumnType("timestamp with time zone").HasColumnName("calendar_occurrence_start_at");
            b.Property<string>("Content").IsRequired().HasColumnType("text").HasColumnName("content");
            b.Property<string>("ContentHash").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)").HasColumnName("content_hash");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone").HasColumnName("created_at");
            b.Property<DateOnly?>("DailyDate").HasColumnType("date").HasColumnName("daily_date");
            b.Property<DateTimeOffset?>("DeletedAt").HasColumnType("timestamp with time zone").HasColumnName("deleted_at");
            b.Property<Guid?>("FolderId").HasColumnType("uuid").HasColumnName("folder_id");
            b.Property<string>("Title").IsRequired().HasMaxLength(200).HasColumnType("character varying(200)").HasColumnName("title");
            b.Property<Guid?>("UniversityActivityItemId").HasColumnType("uuid").HasColumnName("university_activity_item_id");
            b.Property<Guid?>("UniversityCourseId").HasColumnType("uuid").HasColumnName("university_course_id");
            b.Property<DateTimeOffset>("UpdatedAt").HasColumnType("timestamp with time zone").HasColumnName("updated_at");
            b.Property<Guid>("UserId").HasColumnType("uuid").HasColumnName("user_id");
            b.Property<int>("Version").IsConcurrencyToken().ValueGeneratedOnAdd().HasColumnType("integer").HasDefaultValue(1).HasColumnName("version");

            b.HasKey("Id");
            b.HasIndex("CalendarEventId")
                .IsUnique()
                .HasDatabaseName("ix_notes_event_shared_unique")
                .HasFilter("\"calendar_event_id\" IS NOT NULL AND \"calendar_occurrence_start_at\" IS NULL AND \"deleted_at\" IS NULL");
            b.HasIndex("FolderId");
            b.HasIndex("UniversityActivityItemId")
                .IsUnique()
                .HasDatabaseName("ix_notes_uni_item_unique")
                .HasFilter("\"university_activity_item_id\" IS NOT NULL AND \"deleted_at\" IS NULL");
            b.HasIndex("UniversityCourseId");
            b.HasIndex("CalendarEventId", "CalendarOccurrenceStartAt")
                .IsUnique()
                .HasDatabaseName("ix_notes_event_occurrence_unique")
                .HasFilter("\"calendar_event_id\" IS NOT NULL AND \"calendar_occurrence_start_at\" IS NOT NULL AND \"deleted_at\" IS NULL");
            b.HasIndex("UserId", "UpdatedAt").HasDatabaseName("ix_notes_user_updated");
            b.HasIndex("UserId", "CalendarEventId", "DeletedAt").HasDatabaseName("ix_notes_user_calendar_event");
            b.HasIndex("UserId", "DailyDate")
                .IsUnique()
                .HasDatabaseName("ix_notes_daily_date_unique")
                .HasFilter("\"daily_date\" IS NOT NULL AND \"deleted_at\" IS NULL");
            b.HasIndex("UserId", "FolderId", "DeletedAt").HasDatabaseName("ix_notes_user_folder_deleted");
            b.HasIndex("UserId", "UniversityCourseId", "DeletedAt").HasDatabaseName("ix_notes_user_uni_course");
            b.ToTable("notes", (string?)null);
        });

        modelBuilder.Entity("Atrium.Api.Data.Entities.NoteFolder", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("uuid").HasColumnName("id");
            b.Property<bool>("AllowAgentRead").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(true).HasColumnName("allow_agent_read");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone").HasColumnName("created_at");
            b.Property<DateTimeOffset?>("DeletedAt").HasColumnType("timestamp with time zone").HasColumnName("deleted_at");
            b.Property<string>("Name").IsRequired().HasMaxLength(120).HasColumnType("character varying(120)").HasColumnName("name");
            b.Property<Guid?>("ParentFolderId").HasColumnType("uuid").HasColumnName("parent_folder_id");
            b.Property<int>("SortOrder").ValueGeneratedOnAdd().HasColumnType("integer").HasDefaultValue(0).HasColumnName("sort_order");
            b.Property<DateTimeOffset>("UpdatedAt").HasColumnType("timestamp with time zone").HasColumnName("updated_at");
            b.Property<Guid>("UserId").HasColumnType("uuid").HasColumnName("user_id");

            b.HasKey("Id");
            b.HasIndex("ParentFolderId");
            b.HasIndex("UserId", "ParentFolderId", "DeletedAt").HasDatabaseName("ix_note_folders_user_parent");
            b.ToTable("note_folders", (string?)null);
        });

        modelBuilder.Entity("Atrium.Api.Data.Entities.NoteTag", b =>
        {
            b.Property<Guid>("NoteId").HasColumnType("uuid").HasColumnName("note_id");
            b.Property<string>("Tag").HasMaxLength(40).HasColumnType("character varying(40)").HasColumnName("tag");
            b.Property<Guid>("UserId").HasColumnType("uuid").HasColumnName("user_id");

            b.HasKey("NoteId", "Tag");
            b.HasIndex("UserId", "Tag").HasDatabaseName("ix_note_tags_user_tag");
            b.ToTable("note_tags", (string?)null);
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

        modelBuilder.Entity("Atrium.Api.Data.Entities.CalendarEvent", b =>
        {
            b.HasOne("Atrium.Api.Data.Entities.UserCalendar", "Calendar")
                .WithMany("Events")
                .HasForeignKey("CalendarId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
            b.HasOne("Atrium.Api.Data.Entities.CalendarEvent", "RecurrenceMaster")
                .WithMany("RecurrenceOverrides")
                .HasForeignKey("RecurrenceMasterId")
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne("Atrium.Api.Data.Entities.User", "User")
                .WithMany()
                .HasForeignKey("UserId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
            b.Navigation("Calendar");
            b.Navigation("RecurrenceMaster");
            b.Navigation("User");
        });

        modelBuilder.Entity("Atrium.Api.Data.Entities.DailyCheckItem", b =>
        {
            b.HasOne("Atrium.Api.Data.Entities.User", "User")
                .WithMany()
                .HasForeignKey("UserId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
            b.Navigation("User");
        });

        modelBuilder.Entity("Atrium.Api.Data.Entities.DailyNote", b =>
        {
            b.HasOne("Atrium.Api.Data.Entities.User", "User")
                .WithMany()
                .HasForeignKey("UserId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
            b.Navigation("User");
        });

        modelBuilder.Entity("Atrium.Api.Data.Entities.Attachment", b =>
        {
            b.HasOne("Atrium.Api.Data.Entities.CalendarEvent", "CalendarEvent")
                .WithMany()
                .HasForeignKey("CalendarEventId")
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne("Atrium.Api.Data.Entities.Note", "Note")
                .WithMany()
                .HasForeignKey("NoteId")
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne("Atrium.Api.Data.Entities.UniversityActivityItem", "UniversityActivityItem")
                .WithMany()
                .HasForeignKey("UniversityActivityItemId")
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne("Atrium.Api.Data.Entities.UniversityCourse", "UniversityCourse")
                .WithMany()
                .HasForeignKey("UniversityCourseId")
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne("Atrium.Api.Data.Entities.User", "User")
                .WithMany()
                .HasForeignKey("UserId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
            b.Navigation("CalendarEvent");
            b.Navigation("Note");
            b.Navigation("UniversityActivityItem");
            b.Navigation("UniversityCourse");
            b.Navigation("User");
        });

        modelBuilder.Entity("Atrium.Api.Data.Entities.AttachmentTextChunk", b =>
        {
            b.HasOne("Atrium.Api.Data.Entities.Attachment", "Attachment")
                .WithMany("TextChunks")
                .HasForeignKey("AttachmentId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
            b.HasOne("Atrium.Api.Data.Entities.User", "User")
                .WithMany()
                .HasForeignKey("UserId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
            b.Navigation("Attachment");
            b.Navigation("User");
        });

        modelBuilder.Entity("Atrium.Api.Data.Entities.Note", b =>
        {
            b.HasOne("Atrium.Api.Data.Entities.CalendarEvent", "CalendarEvent")
                .WithMany()
                .HasForeignKey("CalendarEventId")
                .OnDelete(DeleteBehavior.SetNull);
            b.HasOne("Atrium.Api.Data.Entities.NoteFolder", "Folder")
                .WithMany("Notes")
                .HasForeignKey("FolderId")
                .OnDelete(DeleteBehavior.SetNull);
            b.HasOne("Atrium.Api.Data.Entities.UniversityActivityItem", "UniversityActivityItem")
                .WithMany()
                .HasForeignKey("UniversityActivityItemId")
                .OnDelete(DeleteBehavior.SetNull);
            b.HasOne("Atrium.Api.Data.Entities.UniversityCourse", "UniversityCourse")
                .WithMany()
                .HasForeignKey("UniversityCourseId")
                .OnDelete(DeleteBehavior.SetNull);
            b.HasOne("Atrium.Api.Data.Entities.User", "User")
                .WithMany()
                .HasForeignKey("UserId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
            b.Navigation("CalendarEvent");
            b.Navigation("Folder");
            b.Navigation("UniversityActivityItem");
            b.Navigation("UniversityCourse");
            b.Navigation("User");
        });

        modelBuilder.Entity("Atrium.Api.Data.Entities.NoteFolder", b =>
        {
            b.HasOne("Atrium.Api.Data.Entities.NoteFolder", "ParentFolder")
                .WithMany("ChildFolders")
                .HasForeignKey("ParentFolderId")
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne("Atrium.Api.Data.Entities.User", "User")
                .WithMany()
                .HasForeignKey("UserId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
            b.Navigation("ParentFolder");
            b.Navigation("User");
        });

        modelBuilder.Entity("Atrium.Api.Data.Entities.NoteTag", b =>
        {
            b.HasOne("Atrium.Api.Data.Entities.Note", "Note")
                .WithMany("Tags")
                .HasForeignKey("NoteId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
            b.HasOne("Atrium.Api.Data.Entities.User", "User")
                .WithMany()
                .HasForeignKey("UserId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
            b.Navigation("Note");
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

        modelBuilder.Entity("Atrium.Api.Data.Entities.UniversityActivityItem", b =>
        {
            b.HasOne("Atrium.Api.Data.Entities.UniversityCourseActivity", "Activity")
                .WithMany("Items")
                .HasForeignKey("ActivityId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
            b.HasOne("Atrium.Api.Data.Entities.CalendarEvent", "CalendarEvent")
                .WithMany()
                .HasForeignKey("CalendarEventId")
                .OnDelete(DeleteBehavior.SetNull);
            b.HasOne("Atrium.Api.Data.Entities.UniversityCourse", "Course")
                .WithMany()
                .HasForeignKey("CourseId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
            b.HasOne("Atrium.Api.Data.Entities.User", "User")
                .WithMany()
                .HasForeignKey("UserId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
            b.Navigation("Activity");
            b.Navigation("CalendarEvent");
            b.Navigation("Course");
            b.Navigation("User");
        });

        modelBuilder.Entity("Atrium.Api.Data.Entities.UniversityCourse", b =>
        {
            b.HasOne("Atrium.Api.Data.Entities.User", "User")
                .WithMany("UniversityCourses")
                .HasForeignKey("UserId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
            b.Navigation("User");
        });

        modelBuilder.Entity("Atrium.Api.Data.Entities.UniversityCourseActivity", b =>
        {
            b.HasOne("Atrium.Api.Data.Entities.UniversityCourse", "Course")
                .WithMany("Activities")
                .HasForeignKey("CourseId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
            b.HasOne("Atrium.Api.Data.Entities.User", "User")
                .WithMany()
                .HasForeignKey("UserId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
            b.Navigation("Course");
            b.Navigation("User");
        });

        modelBuilder.Entity("Atrium.Api.Data.Entities.UserCalendar", b =>
        {
            b.HasOne("Atrium.Api.Data.Entities.User", "User")
                .WithMany("Calendars")
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

        modelBuilder.Entity("Atrium.Api.Data.Entities.Attachment", b =>
        {
            b.Navigation("TextChunks");
        });

        modelBuilder.Entity("Atrium.Api.Data.Entities.CalendarEvent", b =>
        {
            b.Navigation("RecurrenceOverrides");
        });

        modelBuilder.Entity("Atrium.Api.Data.Entities.Note", b =>
        {
            b.Navigation("Tags");
        });

        modelBuilder.Entity("Atrium.Api.Data.Entities.NoteFolder", b =>
        {
            b.Navigation("ChildFolders");
            b.Navigation("Notes");
        });

        modelBuilder.Entity("Atrium.Api.Data.Entities.UniversityCourse", b =>
        {
            b.Navigation("Activities");
        });

        modelBuilder.Entity("Atrium.Api.Data.Entities.UniversityCourseActivity", b =>
        {
            b.Navigation("Items");
        });

        modelBuilder.Entity("Atrium.Api.Data.Entities.User", b =>
        {
            b.Navigation("AgentTokens");
            b.Navigation("Calendars");
            b.Navigation("Preferences");
            b.Navigation("RefreshSessions");
            b.Navigation("UniversityCourses");
        });

        modelBuilder.Entity("Atrium.Api.Data.Entities.UserCalendar", b =>
        {
            b.Navigation("Events");
        });
    }
}

