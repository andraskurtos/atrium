namespace Atrium.Api.Data.Entities;

public sealed class Note
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? FolderId { get; set; }
    public Guid? UniversityCourseId { get; set; }
    public Guid? UniversityActivityItemId { get; set; }
    public Guid? CalendarEventId { get; set; }
    public DateTimeOffset? CalendarOccurrenceStartAt { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
    public bool AllowAgentRead { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    public User User { get; set; } = null!;
    public NoteFolder? Folder { get; set; }
    public UniversityCourse? UniversityCourse { get; set; }
    public UniversityActivityItem? UniversityActivityItem { get; set; }
    public CalendarEvent? CalendarEvent { get; set; }
    public ICollection<NoteTag> Tags { get; } = [];
}

