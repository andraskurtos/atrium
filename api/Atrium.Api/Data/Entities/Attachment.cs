namespace Atrium.Api.Data.Entities;

public sealed class Attachment
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Kind { get; set; } = "artifact";
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public string StorageKey { get; set; } = string.Empty;
    public Guid? NoteId { get; set; }
    public Guid? UniversityCourseId { get; set; }
    public Guid? UniversityActivityItemId { get; set; }
    public Guid? CalendarEventId { get; set; }
    public DateTimeOffset? CalendarOccurrenceStartAt { get; set; }
    public bool AllowAgentRead { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    public User User { get; set; } = null!;
    public Note? Note { get; set; }
    public UniversityCourse? UniversityCourse { get; set; }
    public UniversityActivityItem? UniversityActivityItem { get; set; }
    public CalendarEvent? CalendarEvent { get; set; }
}

